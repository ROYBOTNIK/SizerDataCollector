using System;

namespace SizerDataCollector
{
    internal class Program
    {
        static void Main(string[] args)
        {
            Logger.Log("Sizer API Test Client (SETUP PHASE) starting up...");

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
            Logger.Log("Sizer API Test Client (SETUP PHASE)");
            Logger.Log("=========================================");
            Logger.Log($"Configured Sizer endpoint (from App.config):");
            Logger.Log($"  Host: {cfg.SizerHost}");
            Logger.Log($"  Port: {cfg.SizerPort}");
            Logger.Log($"  URL:  {serviceUrl}");
            Logger.Log("");
            Logger.Log("Timeouts (seconds):");
            Logger.Log($"  Open:    {cfg.OpenTimeoutSec}");
            Logger.Log($"  Send:    {cfg.SendTimeoutSec}");
            Logger.Log($"  Receive: {cfg.ReceiveTimeoutSec}");
            Logger.Log("");
            Logger.Log("Database config (not used yet):");
            Logger.Log(string.IsNullOrWhiteSpace(cfg.TimescaleConnectionString)
                ? "  TimescaleDb connection string: (not set)"
                : "  TimescaleDb connection string: (present)");
            Logger.Log("");
            Logger.Log("NOTE:");
            Logger.Log("  The WCF Service Reference has NOT been added yet,");
            Logger.Log("  so this app does not attempt to call GetSerialNo() or GetMachineName().");
            Logger.Log("");
            Logger.Log("NEXT STEPS (when you are on the Sizer network):");
            Logger.Log("  1) Right-click the project → Add → Service Reference...");
            Logger.Log("  2) Use address: http://<sizer-ip>:8001/SizerService/");
            Logger.Log("  3) Namespace: SizerServiceReference");
            Logger.Log("  4) Replace Program.Main with the WCF test client code to call the API.");
            Logger.Log("");

            Logger.Log("Press any key to exit...");
            Console.ReadKey();
        }
    }
}
