using System;
using System.Configuration;

namespace SizerCollectorTest
{
    internal class Program
    {
        static void Main(string[] args)
        {
            // Read configuration values from App.config
            string host = ConfigurationManager.AppSettings["SizerHost"] ?? "10.155.155.10";
            string portStr = ConfigurationManager.AppSettings["SizerPort"] ?? "8001";

            if (!int.TryParse(portStr, out int port))
            {
                Console.WriteLine("Invalid SizerPort in App.config, defaulting to 8001.");
                port = 8001;
            }

            int openTimeoutSec = GetIntSetting("OpenTimeoutSec", 5);
            int sendTimeoutSec = GetIntSetting("SendTimeoutSec", 5);
            int receiveTimeoutSec = GetIntSetting("ReceiveTimeoutSec", 5);

            string serviceUrl = $"http://{host}:{port}/SizerService/";

            Console.WriteLine("=========================================");
            Console.WriteLine("Sizer API Test Client (SETUP PHASE)");
            Console.WriteLine("=========================================");
            Console.WriteLine();
            Console.WriteLine("Configured Sizer endpoint (from App.config):");
            Console.WriteLine($"  Host: {host}");
            Console.WriteLine($"  Port: {port}");
            Console.WriteLine($"  URL:  {serviceUrl}");
            Console.WriteLine();
            Console.WriteLine("Timeouts (seconds):");
            Console.WriteLine($"  Open:    {openTimeoutSec}");
            Console.WriteLine($"  Send:    {sendTimeoutSec}");
            Console.WriteLine($"  Receive: {receiveTimeoutSec}");
            Console.WriteLine();
            Console.WriteLine("NOTE:");
            Console.WriteLine("  The WCF Service Reference has NOT been added yet,");
            Console.WriteLine("  so this app does not attempt to call GetSerialNo() or GetMachineName().");
            Console.WriteLine();
            Console.WriteLine("NEXT STEPS (when you are on the Sizer network):");
            Console.WriteLine("  1) Right-click the project → Add → Service Reference...");
            Console.WriteLine("  2) Use address: http://<sizer-ip>:8001/SizerService/");
            Console.WriteLine("  3) Namespace: SizerServiceReference");
            Console.WriteLine("  4) Then update Program.cs to use the generated SizerServiceClient to make test calls.");
            Console.WriteLine();
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
        }

        /// <summary>
        /// Helper to read an int from appSettings with a default.
        /// </summary>
        private static int GetIntSetting(string key, int defaultValue)
        {
            var value = ConfigurationManager.AppSettings[key];
            if (int.TryParse(value, out int result))
                return result;

            return defaultValue;
        }
    }
}
