using System;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using SizerDataCollector.Core.Collector;
using SizerDataCollector.Core.Config;
using SizerDataCollector.Core.Db;
using SizerDataCollector.Core.Logging;
using SizerDataCollector.Core.Sizer;
using SizerDataCollector.Core.Commissioning;

namespace SizerDataCollector.Service
{
	public class SizerCollectorService : ServiceBase
	{
		private CancellationTokenSource _cts;
		private Task _runnerTask;
		private CollectorStatus _status;
		private HeartbeatWriter _heartbeatWriter;

		public SizerCollectorService()
		{
			ServiceName = "SizerDataCollectorService";
		}

		protected override void OnStart(string[] args)
		{
			Logger.Log("Service starting...");

			try
			{
				var settingsProvider = new CollectorSettingsProvider();
				var runtimeSettings = settingsProvider.Load();
				var config = new CollectorConfig(runtimeSettings);

				_status = new CollectorStatus();
				var dataRoot = NormalizeDataRoot(runtimeSettings.SharedDataDirectory);
				if (!string.IsNullOrWhiteSpace(dataRoot))
				{
					Directory.CreateDirectory(dataRoot);
				}
				var heartbeatPath = string.IsNullOrWhiteSpace(dataRoot)
					? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "heartbeat.json")
					: Path.Combine(dataRoot, "heartbeat.json");
				_heartbeatWriter = new HeartbeatWriter(heartbeatPath);
				_cts = new CancellationTokenSource();

				if (!IsIngestionEnabled(runtimeSettings, config, _status, _heartbeatWriter))
				{
					Logger.Log("Commissioning incomplete; ingestion disabled; service idle.");
					return;
				}

				_runnerTask = Task.Run(async () =>
				{
					try
					{
						DatabaseTester.TestAndInitialize(config);
						var repository = new TimescaleRepository(config.TimescaleConnectionString);

						using (var sizerClient = new SizerClient(config))
						{
							var engine = new CollectorEngine(config, repository, sizerClient);
							var runner = new CollectorRunner(config, engine, _status, _heartbeatWriter);
							await runner.RunAsync(_cts.Token).ConfigureAwait(false);
						}
					}
					catch (OperationCanceledException) when (_cts?.IsCancellationRequested == true)
					{
						// Expected during shutdown.
					}
					catch (Exception ex)
					{
						Logger.Log("Service runner encountered an unexpected error.", ex);
						throw;
					}
				});

				Logger.Log("Service started.");
			}
			catch (Exception ex)
			{
				Logger.Log("Service failed to start.", ex);
				_cts?.Cancel();
				_cts?.Dispose();
				_cts = null;
				_runnerTask = null;
				throw;
			}
		}

		protected override void OnStop()
		{
			Logger.Log("Service stopping...");

			try
			{
				_cts?.Cancel();

				if (_runnerTask != null)
				{
					var timeout = TimeSpan.FromSeconds(30);
					if (!_runnerTask.Wait(timeout))
					{
						Logger.Log($"Service runner did not stop within {timeout.TotalSeconds} seconds.");
					}
				}
			}
			catch (Exception ex)
			{
				Logger.Log("Error while stopping service.", ex);
			}
			finally
			{
				_runnerTask = null;

				_cts?.Dispose();
				_cts = null;

				Logger.Log("Service stopped.");
			}
		}

		private static bool IsIngestionEnabled(CollectorRuntimeSettings runtimeSettings, CollectorConfig config, CollectorStatus status, HeartbeatWriter heartbeatWriter)
		{
			status.CommissioningSerial = string.Empty;
			status.CommissioningBlockingReasons.Clear();

			if (runtimeSettings?.EnableIngestion != true)
			{
				status.CommissioningIngestionEnabled = false;
				status.CommissioningBlockingReasons.Add(new CommissioningReason { Code = "INGESTION_DISABLED", Message = "Runtime setting EnableIngestion is false." });
				WriteHeartbeat(status, heartbeatWriter);
				return false;
			}

			try
			{
				using (var sizerClient = new SizerClient(config))
				{
					var serial = sizerClient.GetSerialNoAsync(CancellationToken.None).GetAwaiter().GetResult();
					if (string.IsNullOrWhiteSpace(serial))
					{
						Logger.Log("Commissioning check: Sizer serial number unavailable; disabling ingestion.");
						status.CommissioningBlockingReasons.Add(new CommissioningReason { Code = "SIZER_UNAVAILABLE", Message = "Sizer serial number unavailable." });
						status.CommissioningIngestionEnabled = false;
						WriteHeartbeat(status, heartbeatWriter);
						return false;
					}

					var repository = new CommissioningRepository(config.TimescaleConnectionString);
					var row = repository.GetAsync(serial).GetAwaiter().GetResult();
					if (row?.IngestionEnabledAt == null)
					{
						Logger.Log($"Commissioning check: ingestion not enabled for serial '{serial}'.");
						status.CommissioningBlockingReasons.Add(new CommissioningReason { Code = "INGESTION_DISABLED", Message = "Ingestion not enabled in commissioning status." });
						status.CommissioningSerial = serial;
						status.CommissioningIngestionEnabled = false;
						WriteHeartbeat(status, heartbeatWriter);
						return false;
					}

					status.CommissioningSerial = serial;
					status.CommissioningIngestionEnabled = true;
				}

				return true;
			}
			catch (Exception ex)
			{
				Logger.Log("Commissioning check failed; disabling ingestion.", ex);
				status.CommissioningIngestionEnabled = false;
				status.CommissioningBlockingReasons.Add(new CommissioningReason { Code = "COMMISSIONING_CHECK_FAILED", Message = "Commissioning check failed (see logs)." });
				WriteHeartbeat(status, heartbeatWriter);
				return false;
			}
		}

		private static void WriteHeartbeat(CollectorStatus status, HeartbeatWriter heartbeatWriter)
		{
			if (heartbeatWriter == null || status == null)
			{
				return;
			}

			var snapshot = status.CreateSnapshot();
			var payload = new HeartbeatPayload
			{
				MachineSerial = snapshot.MachineSerial,
				MachineName = snapshot.MachineName,
				LastPollUtc = snapshot.LastPollCompletedUtc ?? snapshot.LastPollStartedUtc,
				LastSuccessUtc = snapshot.LastSuccessUtc,
				LastErrorUtc = snapshot.LastErrorUtc,
				LastErrorMessage = snapshot.LastErrorMessage,
				LastRunId = snapshot.LastRunId,
				CommissioningIngestionEnabled = snapshot.CommissioningIngestionEnabled,
				CommissioningSerial = snapshot.CommissioningSerial,
				CommissioningBlockingReasons = snapshot.CommissioningBlockingReasons
			};

			heartbeatWriter.Write(payload);
		}

		private static string NormalizeDataRoot(string candidate)
		{
			if (string.IsNullOrWhiteSpace(candidate))
			{
				return string.Empty;
			}

			// If a file path was stored by mistake (e.g., ends with .json), use its directory
			if (candidate.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
			{
				return Path.GetDirectoryName(candidate);
			}

			// If a file already exists at that path, fall back to its directory
			if (File.Exists(candidate))
			{
				return Path.GetDirectoryName(candidate);
			}

			return candidate;
		}
	}
}

