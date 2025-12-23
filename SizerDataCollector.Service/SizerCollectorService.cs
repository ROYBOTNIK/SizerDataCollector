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

