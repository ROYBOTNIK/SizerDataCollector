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

		public bool EnableAnomalyDetection { get; set; }
		public int AnomalyWindowMinutes { get; set; } = 60;
		public double AnomalyZGate { get; set; } = 2.0;
		public double BandLowMin { get; set; } = 5.0;
		public double BandLowMax { get; set; } = 10.0;
		public double BandMediumMax { get; set; } = 20.0;
		public int AlarmCooldownSeconds { get; set; } = 300;
		public string RecycleGradeKey { get; set; } = "RCY";
		public bool EnableSizerAlarm { get; set; } = true;
		public bool EnableLlmEnrichment { get; set; }
		public string LlmEndpoint { get; set; } = string.Empty;

		public bool EnableSizeAnomalyDetection { get; set; }
		public bool EnableSizerSizeAlarm { get; set; }
		public int SizeEvalIntervalMinutes { get; set; } = 30;
		public int SizeWindowHours { get; set; } = 24;
		public double SizeZGate { get; set; } = 2.0;
		public double SizePctDevMin { get; set; } = 3.0;
		public int SizeCooldownMinutes { get; set; } = 240;
	}
}

