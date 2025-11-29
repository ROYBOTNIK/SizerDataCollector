using System;
using System.Configuration;

namespace SizerDataCollector
{
	internal sealed class CollectorConfig
	{
		public string SizerHost { get; }
		public int SizerPort { get; }
		public int OpenTimeoutSec { get; }
		public int SendTimeoutSec { get; }
		public int ReceiveTimeoutSec { get; }

		public string TimescaleConnectionString { get; }

		public CollectorConfig()
		{
			SizerHost = GetString("SizerHost", "10.155.155.10");
			SizerPort = GetInt("SizerPort", 8001);
			OpenTimeoutSec = GetInt("OpenTimeoutSec", 5);
			SendTimeoutSec = GetInt("SendTimeoutSec", 5);
			ReceiveTimeoutSec = GetInt("ReceiveTimeoutSec", 5);

			TimescaleConnectionString = GetConnectionString("TimescaleDb");
		}

		private static string GetString(string key, string defaultValue)
		{
			var value = ConfigurationManager.AppSettings[key];
			return string.IsNullOrWhiteSpace(value) ? defaultValue : value;
		}

		private static int GetInt(string key, int defaultValue)
		{
			var value = ConfigurationManager.AppSettings[key];
			if (int.TryParse(value, out int result))
				return result;

			return defaultValue;
		}

		private static string GetConnectionString(string name)
		{
			var cs = ConfigurationManager.ConnectionStrings[name];
			return cs?.ConnectionString ?? string.Empty;
		}
	}
}

