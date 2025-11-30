using System.Collections.Generic;

namespace SizerDataCollector.Core.Config
{
	public class CollectorRuntimeSettings
	{
		public string SizerHost { get; set; } = string.Empty;
		public int SizerPort { get; set; }
		public int OpenTimeoutSec { get; set; }
		public int SendTimeoutSec { get; set; }
		public int ReceiveTimeoutSec { get; set; }
		public string TimescaleConnectionString { get; set; } = string.Empty;
		public List<string> EnabledMetrics { get; set; } = new List<string>();
		public bool EnableIngestion { get; set; }
		public int PollIntervalSeconds { get; set; }
		public int InitialBackoffSeconds { get; set; }
		public int MaxBackoffSeconds { get; set; }
	}
}

