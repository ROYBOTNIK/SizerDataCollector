using System;
using System.ServiceModel;
using SizerDataCollector.Config;
using SizerDataCollector.SizerServiceReference;  // Adjust if your project name differs

namespace SizerDataCollector
{
	internal static class SizerClientTester
	{
		/// <summary>
		/// Connects to the Sizer WCF service, calls GetSerialNo and GetMachineName,
		/// and logs the results. Does NOT write anything to the database.
		/// </summary>
		public static void TestSizerConnection(CollectorConfig cfg)
		{
			string serviceUrl = $"http://{cfg.SizerHost}:{cfg.SizerPort}/SizerService/";

			Logger.Log("Testing Sizer WCF connection (READ-ONLY)...");
			Logger.Log($"  Endpoint: {serviceUrl}");

			var binding = new WSHttpBinding(SecurityMode.None)
			{
				OpenTimeout = TimeSpan.FromSeconds(cfg.OpenTimeoutSec),
				SendTimeout = TimeSpan.FromSeconds(cfg.SendTimeoutSec),
				ReceiveTimeout = TimeSpan.FromSeconds(cfg.ReceiveTimeoutSec),
				MaxReceivedMessageSize = 10 * 1024 * 1024L
			};

			var endpointAddress = new EndpointAddress(serviceUrl);
			var client = new SizerServiceClient(binding, endpointAddress);

			try
			{
				Logger.Log("Opening Sizer WCF client...");
				client.Open();
				Logger.Log("Sizer WCF client opened successfully.");

				Logger.Log("Calling GetSerialNo()...");
				string serialNo = client.GetSerialNo();
				Logger.Log($"GetSerialNo result: '{serialNo}'");

				Logger.Log("Calling GetMachineName()...");
				string machineName = client.GetMachineName();
				Logger.Log($"GetMachineName result: '{machineName}'");

				Logger.Log("READ-ONLY TEST: No changes have been made to the database.");
			}
			catch (Exception ex)
			{
				Logger.Log("ERROR while communicating with Sizer WCF service.", ex);
			}
			finally
			{
				try
				{
					if (client.State != CommunicationState.Faulted &&
						client.State != CommunicationState.Closed)
					{
						client.Close();
					}
					else
					{
						client.Abort();
					}
				}
				catch
				{
					client.Abort();
				}
			}

			Logger.Log("Sizer WCF read-only test completed.");
		}
	}
}

