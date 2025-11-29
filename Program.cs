using System;
using System.Threading;
using SizerDataCollector.Collector;
using SizerDataCollector.Config;
using SizerDataCollector.Db;
using SizerDataCollector.Sizer;

namespace SizerDataCollector
{
	internal class Program
	{
		private const int IngestionTimeoutSeconds = 30;

		static void Main(string[] args)
		{
			Logger.Log("SizerDataCollector starting up...");

			CollectorConfig cfg;
			try
			{
				cfg = new CollectorConfig();
			}
			catch (Exception ex)
			{
				Logger.Log("Failed to load configuration.", ex);
				Logger.Log("Press any key to exit...");
				Console.ReadKey();
				return;
			}

			DatabaseTester.TestAndInitialize(cfg);

			if (!cfg.EnableIngestion)
			{
				Logger.Log("Running in probe-only mode (EnableIngestion=false).");
				SizerClientTester.TestSizerConnection(cfg);
				Logger.Log("Probe-only mode completed. Press any key to exit...");
				Console.ReadKey();
				return;
			}

			Logger.Log("Running single ingestion poll (EnableIngestion=true).");

			var repository = new TimescaleRepository(cfg.TimescaleConnectionString);

			try
			{
				using (var sizerClient = new SizerClient(cfg))
				using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(IngestionTimeoutSeconds)))
				{
					var engine = new CollectorEngine(cfg, repository, sizerClient);
					engine.RunSinglePollAsync(cts.Token).GetAwaiter().GetResult();
				}

				Logger.Log("Single ingestion poll completed.");
			}
			catch (Exception ex)
			{
				Logger.Log("Single ingestion poll failed.", ex);
			}

			Logger.Log("Press any key to exit...");
			Console.ReadKey();
		}
	}
}
