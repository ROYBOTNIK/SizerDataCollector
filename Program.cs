using System;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using SizerDataCollector.Core.Collector;
using SizerDataCollector.Core.Config;
using SizerDataCollector.Core.Db;
using SizerDataCollector.Core.Logging;
using SizerDataCollector.Core.Sizer;

namespace SizerDataCollector
{
	internal class Program
	{
		static int Main(string[] args)
		{
			try
			{
				var mode = ParseMode(args);
				Logger.Log($"SizerDataCollector test harness starting in '{mode}' mode...");

				var settingsProvider = new CollectorSettingsProvider();
				var runtimeSettings = settingsProvider.Load();
				var config = new CollectorConfig(runtimeSettings);
				LogEffectiveSettings(runtimeSettings);

				switch (mode)
				{
					case HarnessMode.Probe:
						RunProbe(config);
						break;
					case HarnessMode.SinglePoll:
						RunSinglePoll(config, runtimeSettings).GetAwaiter().GetResult();
						break;
					default:
						ShowUsage();
						return 1;
				}

				Logger.Log("Operation completed successfully.");
				return 0;
			}
			catch (Exception ex)
			{
				Logger.Log("SizerDataCollector test harness failed.", ex);
				return 1;
			}
		}

		private static HarnessMode ParseMode(string[] args)
		{
			if (args == null || args.Length == 0)
			{
				return HarnessMode.Unknown;
			}

			var mode = args[0]?.Trim().ToLowerInvariant();

			if (mode == "probe")
			{
				return HarnessMode.Probe;
			}

			if (mode == "single-poll")
			{
				return HarnessMode.SinglePoll;
			}

			return HarnessMode.Unknown;
		}

		private static void ShowUsage()
		{
			Logger.Log("Usage: SizerDataCollector.exe [probe|single-poll]");
		}

		private static void RunProbe(CollectorConfig config)
		{
			Logger.Log("Running schema and Sizer API probe...");
			DatabaseTester.TestAndInitialize(config);
			SizerClientTester.TestSizerConnection(config);
		}

		private static async Task RunSinglePoll(CollectorConfig config, CollectorRuntimeSettings runtimeSettings)
		{
			Logger.Log("Running collector for a single poll...");

			var status = new CollectorStatus();
			var dataRoot = runtimeSettings?.SharedDataDirectory ?? string.Empty;
			if (!string.IsNullOrWhiteSpace(dataRoot))
			{
				Directory.CreateDirectory(dataRoot);
			}
			var heartbeatPath = Path.Combine(dataRoot, "heartbeat.json");
			var heartbeatWriter = new HeartbeatWriter(heartbeatPath);
			var repository = new TimescaleRepository(config.TimescaleConnectionString);

			using (var sizerClient = new SizerClient(config))
			using (var cts = new CancellationTokenSource())
			{
				var engine = new CollectorEngine(config, repository, sizerClient);
				var runner = new CollectorRunner(config, engine, status, heartbeatWriter);
				await runner.RunAsync(cts.Token).ConfigureAwait(false);
			}
		}

		private enum HarnessMode
		{
			Unknown,
			Probe,
			SinglePoll
		}

		private static void LogEffectiveSettings(CollectorRuntimeSettings settings)
		{
			if (settings == null)
			{
				Logger.Log("Runtime settings: <null>");
				return;
			}

			Logger.Log($"Runtime settings:");
			Logger.Log($"  Sizer host: {settings.SizerHost}:{settings.SizerPort}");
			Logger.Log($"  Open timeout: {settings.OpenTimeoutSec}s");
			Logger.Log($"  Send timeout: {settings.SendTimeoutSec}s");
			Logger.Log($"  Receive timeout: {settings.ReceiveTimeoutSec}s");
			Logger.Log($"  Timescale connection string configured: {!string.IsNullOrWhiteSpace(settings.TimescaleConnectionString)}");
			Logger.Log($"  Poll interval: {settings.PollIntervalSeconds}s");
			Logger.Log($"  Initial backoff: {settings.InitialBackoffSeconds}s");
			Logger.Log($"  Max backoff: {settings.MaxBackoffSeconds}s");
			Logger.Log($"  Enable ingestion: {settings.EnableIngestion}");
			Logger.Log($"  Enabled metrics: {(settings.EnabledMetrics == null ? 0 : settings.EnabledMetrics.Count)}");
		}
	}
}
