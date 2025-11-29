using System;

namespace SizerDataCollector
{
    internal class Program
    {
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

            // 1. DB connection + schema check (this only ensures tables exist; it does NOT change any data)
            DatabaseTester.TestAndInitialize(cfg);

            // 2. Read-only test of Sizer WCF API (no DB writes)
            SizerClientTester.TestSizerConnection(cfg);

            Logger.Log("Done. Press any key to exit...");
            Console.ReadKey();
        }
    }
}
