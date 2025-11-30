using System;
using System.Threading;
using System.Threading.Tasks;
using SizerDataCollector.Collector;
using SizerDataCollector.Config;
using SizerDataCollector.Db;
using SizerDataCollector.Sizer;

namespace SizerDataCollector
{
	internal class Program
	{
		static void Main(string[] args)
		{
			Logger.Log("SizerDataCollector starting up...");

			CollectorRuntimeSettings settings;
			CollectorConfig cfg;
			CollectorStatus status;
			try
			{
				var settingsProvider = new CollectorSettingsProvider();
				settings = settingsProvider.Load();
				cfg = new CollectorConfig(settings);
				status = new CollectorStatus();
				LogEffectiveSettings(cfg);
			}
			catch (Exception ex)
			{
				Logger.Log("Failed to load configuration.", ex);
				Logger.Log("Press any key to exit...");
				Console.ReadKey();
				return;
			}

			using (var cts = new CancellationTokenSource())
			{
				ConsoleCancelEventHandler cancelHandler = (sender, eventArgs) =>
				{
					Logger.Log("Ctrl+C detected. Requesting shutdown...");
					eventArgs.Cancel = true;
					cts.Cancel();
				};

				Console.CancelKeyPress += cancelHandler;

				try
				{
					DatabaseTester.TestAndInitialize(cfg);

					if (!cfg.EnableIngestion)
					{
						Logger.Log("Running in probe-only mode (EnableIngestion=false).");
						SizerClientTester.TestSizerConnection(cfg);
						Logger.Log("Probe-only mode completed. Press any key to exit...");
						Console.ReadKey();
						return;
					}

					Logger.Log($"EnableIngestion=true; starting continuous ingestion loop (poll interval = {cfg.PollIntervalSeconds}s).");

					var repository = new TimescaleRepository(cfg.TimescaleConnectionString);

					try
					{
						using (var sizerClient = new SizerClient(cfg))
						{
							var engine = new CollectorEngine(cfg, repository, sizerClient);
							var runner = new CollectorRunner(cfg, engine, status);
							RunContinuousAsync(runner, cts.Token);
						}
					}
					catch (OperationCanceledException) when (cts.IsCancellationRequested)
					{
						// Graceful shutdown
					}
					catch (Exception ex)
					{
						Logger.Log("Continuous ingestion loop terminated due to an unexpected error.", ex);
					}

					if (cts.IsCancellationRequested)
					{
						Logger.Log("Cancellation requested; SizerDataCollector is shutting down.");
					}
					else
					{
						Logger.Log("Continuous ingestion loop completed.");
					}
				}
				finally
				{
					Console.CancelKeyPress -= cancelHandler;
					Logger.Log("Press any key to exit...");
					Console.ReadKey();
				}
			}
		}

		private static void RunContinuousAsync(CollectorRunner runner, CancellationToken token)
		{
			var task = runner.RunAsync(token);
			try
			{
				task.GetAwaiter().GetResult();
			}
			catch (OperationCanceledException) when (token.IsCancellationRequested)
			{
				throw;
			}
		}

		private static void LogEffectiveSettings(CollectorConfig cfg)
		{
			var timescaleStatus = string.IsNullOrWhiteSpace(cfg.TimescaleConnectionString)
				? "Timescale connection string not provided."
				: "Timescale connection string provided.";

			Logger.Log($"Runtime settings: host={cfg.SizerHost}:{cfg.SizerPort}, pollInterval={cfg.PollIntervalSeconds}s, initialBackoff={cfg.InitialBackoffSeconds}s, maxBackoff={cfg.MaxBackoffSeconds}s, enableIngestion={cfg.EnableIngestion}.");
			Logger.Log(timescaleStatus);
		}
	}
}
