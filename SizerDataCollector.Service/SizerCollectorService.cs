using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using SizerDataCollector.Core.AnomalyDetection;
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
		private const string EventSourceName = "SizerDataCollectorService";
		private const string EventLogName = "Application";
		private CancellationTokenSource _cts;
		private Task _runnerTask;
		private CollectorStatus _status;
		private HeartbeatWriter _heartbeatWriter;

		public SizerCollectorService()
		{
			ServiceName = "SizerDataCollectorService";
		}


		public void StartAsConsole(string[] args = null)
		{
			OnStart(args ?? new string[0]);
		}

		public void StopAsConsole()
		{
			OnStop();
		}

		protected override void OnStart(string[] args)
		{
			Logger.Log("Service starting...");

			try
			{
				var settingsProvider = new CollectorSettingsProvider();
				var runtimeSettings = settingsProvider.Load();
				Logger.Configure(runtimeSettings);
				var config = new CollectorConfig(runtimeSettings);

				_status = new CollectorStatus();
				SetServiceState("starting", "Service initialization in progress.", false);

				// 1) Normalize any configured SharedDataDirectory
				var dataRoot = NormalizeDataRoot(runtimeSettings?.SharedDataDirectory);

				// 2) If nothing configured, default to ProgramData (recommended for services)
				if (string.IsNullOrWhiteSpace(dataRoot))
				{
					dataRoot = Path.Combine(
						Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
						"Opti-Fresh",
						"SizerDataCollector");
				}

				// 3) Ensure directory exists
				Directory.CreateDirectory(dataRoot);

				// 4) Heartbeat file lives in data root
				var heartbeatPath = Path.Combine(dataRoot, "heartbeat.json");

				_heartbeatWriter = new HeartbeatWriter(heartbeatPath);
				_cts = new CancellationTokenSource();

				if (runtimeSettings?.EnableIngestion != true)
				{
					_status.CommissioningIngestionEnabled = false;
					_status.CommissioningBlockingReasons.Add(
						new CommissioningReason("INGESTION_DISABLED",
							"Runtime setting EnableIngestion is false. Use 'set-ingestion --enabled true' to enable."));
					WriteHeartbeat(_status, _heartbeatWriter);
					SetServiceState("blocked", "Ingestion disabled in runtime settings.", true);
					Logger.Log("Ingestion disabled in settings; service idle. Use 'set-ingestion --enabled true' then restart.");
					return;
				}

				_status.CommissioningIngestionEnabled = true;

				_runnerTask = Task.Run(() => RunSupervisedLoopAsync(config, _cts.Token));

				Logger.Log("Service started. Ingestion enabled; runner will connect to Sizer when available.");
				SetServiceState("running", "Collector service running.", true);
			}
			catch (Exception ex)
			{
				Logger.Log("Service failed to start.", ex);
				TryWriteEventLog("Service failed to start: " + ex.Message, EventLogEntryType.Error, 1001);
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
			SetServiceState("stopping", "Service shutdown requested.", true);

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
				SetServiceState("stopped", "Service stopped.", true);
			}
		}

		private async Task RunSupervisedLoopAsync(CollectorConfig config, CancellationToken cancellationToken)
		{
			var restartDelay = TimeSpan.FromSeconds(15);
			while (!cancellationToken.IsCancellationRequested)
			{
				try
				{
					SetServiceState("running", "Collector loop active.", true);
					DatabaseTester.TestAndInitialize(config);
					var repository = new TimescaleRepository(config.TimescaleConnectionString);

					using (var sizerClient = new SizerClient(config))
					{
						AnomalyDetector anomalyDetector = null;
						IAlarmSink alarmSink = null;

						if (config.EnableAnomalyDetection)
						{
							var detectorConfig = new AnomalyDetectorConfig(config);
							anomalyDetector = new AnomalyDetector(detectorConfig);

							var sinks = new List<IAlarmSink>();
							sinks.Add(new LogAlarmSink());

							if (!string.IsNullOrWhiteSpace(config.TimescaleConnectionString))
								sinks.Add(new DatabaseAlarmSink(config.TimescaleConnectionString));

							if (config.EnableSizerAlarm)
								sinks.Add(new SizerAlarmSink(config.SizerHost, config.SizerPort, config.SendTimeoutSec));

							IAlarmSink compositeSink = new CompositeAlarmSink(sinks);

							if (config.EnableLlmEnrichment && !string.IsNullOrWhiteSpace(config.LlmEndpoint))
								compositeSink = new LlmEnricher(compositeSink, config.LlmEndpoint);

							alarmSink = compositeSink;
							Logger.Log(config.EnableSizerAlarm
								? "Anomaly detection enabled (Sizer alarm delivery ON)."
								: "Anomaly detection enabled (Sizer alarm delivery OFF).");
						}

						var engine = new CollectorEngine(config, repository, sizerClient, anomalyDetector, alarmSink);
						var runner = new CollectorRunner(config, engine, _status, _heartbeatWriter);

						Task sizeTask = Task.CompletedTask;
						if (config.EnableSizeAnomalyDetection && !string.IsNullOrWhiteSpace(config.TimescaleConnectionString))
						{
							var sizeConfig = new SizeAnomalyConfig(config);
							var sizeEvaluator = new SizeAnomalyEvaluator(sizeConfig, config.TimescaleConnectionString);
							var sizeDbSink = new SizeDatabaseAlarmSink(config.TimescaleConnectionString);
							SizerAlarmSink sizeSizerSink = null;
							if (config.EnableSizerSizeAlarm)
								sizeSizerSink = new SizerAlarmSink(config.SizerHost, config.SizerPort, config.SendTimeoutSec);

							sizeTask = RunSizeEvaluatorLoopAsync(
								sizeEvaluator, sizeDbSink, sizeSizerSink,
								sizeConfig, _status, cancellationToken);
							Logger.Log(string.Format(
								"Size anomaly detection enabled (interval={0}min, window={1}h, z-gate={2}, pctDevMin={3}%, Sizer alarm {4}).",
								sizeConfig.EvalIntervalMinutes, sizeConfig.WindowHours,
								sizeConfig.ZGate, sizeConfig.PctDevMin,
								config.EnableSizerSizeAlarm ? "ON" : "OFF"));
						}

						await runner.RunAsync(cancellationToken).ConfigureAwait(false);
						await sizeTask.ConfigureAwait(false);
					}

					if (!cancellationToken.IsCancellationRequested)
					{
						Logger.Log("Collector runner exited unexpectedly; restarting supervised loop.");
						SetServiceState("degraded", "Collector loop exited unexpectedly; retrying.", true);
					}
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					break;
				}
				catch (Exception ex)
				{
					Logger.Log("Service runner faulted; entering degraded retry mode.", ex, LogLevel.Error);
					RecordServiceFault(ex);
					SetServiceState("degraded", "Service runner faulted; retrying.", true);
					TryWriteEventLog("Service runner faulted; retrying. " + ex.Message, EventLogEntryType.Warning, 1002);
					WriteHeartbeat(_status, _heartbeatWriter);

					try
					{
						await Task.Delay(restartDelay, cancellationToken).ConfigureAwait(false);
					}
					catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
					{
						break;
					}
				}
			}
		}

		private static async Task RunSizeEvaluatorLoopAsync(
			SizeAnomalyEvaluator evaluator,
			SizeDatabaseAlarmSink dbSink,
			SizerAlarmSink sizerSink,
			SizeAnomalyConfig sizeConfig,
			CollectorStatus status,
			CancellationToken cancellationToken)
		{
			var delay = TimeSpan.FromMinutes(sizeConfig.EvalIntervalMinutes);
			while (!cancellationToken.IsCancellationRequested)
			{
				try
				{
					await Task.Delay(delay, cancellationToken).ConfigureAwait(false);

					var serialNo = status?.MachineSerial;
					if (string.IsNullOrWhiteSpace(serialNo))
					{
						Logger.Log("Size evaluator skipped: machine serial not yet known.", level: LogLevel.Debug);
						continue;
					}

					var now = DateTimeOffset.UtcNow;
					var events = await evaluator.EvaluateAsync(serialNo, now, cancellationToken).ConfigureAwait(false);

					foreach (var evt in events)
					{
						Logger.Log(string.Format("[{0,7}] {1}", evt.Severity, evt.AlarmDetails));

						var sinks = new List<string> { "log" };

						await dbSink.DeliverAsync(evt, cancellationToken).ConfigureAwait(false);
						sinks.Add("db");

						if (sizerSink != null)
						{
							try
							{
								await sizerSink.DeliverAsync(evt.AlarmTitle, evt.AlarmDetails, evt.Severity, cancellationToken).ConfigureAwait(false);
								sinks.Add("sizer");
							}
							catch (Exception ex)
							{
								Logger.Log("Size alarm delivery to Sizer failed.", ex, LogLevel.Warn);
							}
						}

						evt.DeliveredTo = string.Join(",", sinks);
					}

					if (events.Count > 0)
						Logger.Log(string.Format("Size evaluation complete: {0} alarm(s) fired.", events.Count), level: LogLevel.Info);
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					break;
				}
				catch (Exception ex)
				{
					Logger.Log("Size evaluator cycle failed.", ex, LogLevel.Warn);
				}
			}
		}

		private void RecordServiceFault(Exception ex)
		{
			if (_status == null)
			{
				return;
			}

			_status.LastErrorUtc = DateTime.UtcNow;
			_status.LastErrorMessage = ex?.Message ?? "Unknown service fault";
		}

		private void SetServiceState(string state, string reason, bool emitHeartbeat)
		{
			if (_status == null)
			{
				return;
			}

			_status.ServiceState = state;
			_status.ServiceStateReason = reason;
			if (emitHeartbeat && _heartbeatWriter != null)
			{
				WriteHeartbeat(_status, _heartbeatWriter);
			}
		}

		private static void TryWriteEventLog(string message, EventLogEntryType entryType, int eventId)
		{
			try
			{
				if (!EventLog.SourceExists(EventSourceName))
				{
					EventLog.CreateEventSource(EventSourceName, EventLogName);
				}

				EventLog.WriteEntry(EventSourceName, message, entryType, eventId);
			}
			catch
			{
				// Avoid failing service path if EventLog registration/write fails.
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
				CommissioningBlockingReasons = snapshot.CommissioningBlockingReasons,
				ServiceState = snapshot.ServiceState,
				ServiceStateReason = snapshot.ServiceStateReason
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
