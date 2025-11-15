using System;

namespace SizerDataCollector
{
    internal class Program
    {
        static void Main(string[] args)
        {
            Logger.Log("SizerDataCollector (SETUP PHASE) starting up...");

            // 1. Load configuration (Sizer endpoint + DB connection string)
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

            string serviceUrl = $"http://{cfg.SizerHost}:{cfg.SizerPort}/SizerService/";

            Logger.Log("=========================================");
            Logger.Log("SizerDataCollector – Setup");
            Logger.Log("=========================================");
            Logger.Log("Sizer endpoint configuration:");
            Logger.Log($"  Host: {cfg.SizerHost}");
            Logger.Log($"  Port: {cfg.SizerPort}");
            Logger.Log($"  URL:  {serviceUrl}");
            Logger.Log("");
            Logger.Log("Timeouts (seconds):");
            Logger.Log($"  Open:    {cfg.OpenTimeoutSec}");
            Logger.Log($"  Send:    {cfg.SendTimeoutSec}");
            Logger.Log($"  Receive: {cfg.ReceiveTimeoutSec}");
            Logger.Log("");

            // 2. Test and initialize the Timescale/Postgres database
            DatabaseTester.TestAndInitialize(cfg);
            Logger.Log("");

            // 3. Reminder about Sizer WCF part (not wired yet)
            Logger.Log("NOTE (Sizer API):");
            Logger.Log("  The WCF Service Reference for Sizer has NOT been added yet,");
            Logger.Log("  so this app does not attempt to call GetSerialNo() or GetMachineName() yet.");
            Logger.Log("");
            Logger.Log("NEXT STEPS (when you are on the Sizer network):");
            Logger.Log("  1) Right-click the project → Add → Service Reference...");
            Logger.Log("  2) Use address: http://<sizer-ip>:8001/SizerService/");
            Logger.Log("  3) Namespace: SizerServiceReference");
            Logger.Log("  4) Update Program.Main to create a SizerServiceClient and call GetSerialNo()/GetMachineName().");
            Logger.Log("");

            Logger.Log("Press any key to exit...");
            Console.ReadKey();
        }
    }
}
