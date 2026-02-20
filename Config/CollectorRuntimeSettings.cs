using System;
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
		public string SharedDataDirectory { get; set; } = string.Empty;
		public string LogLevel { get; set; } = "Info";
		public bool DiagnosticMode { get; set; }
		public DateTimeOffset? DiagnosticUntilUtc { get; set; }
		public bool LogAsJson { get; set; }
		public long LogMaxFileBytes { get; set; } = 10485760;
		public int LogRetentionDays { get; set; } = 14;
		public int LogMaxFiles { get; set; } = 100;
	}
}

