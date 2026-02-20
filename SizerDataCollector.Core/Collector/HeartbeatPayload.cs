using System;

namespace SizerDataCollector.Core.Collector
{
	public sealed class HeartbeatPayload
	{
		public string MachineSerial { get; set; }
		public string MachineName { get; set; }

		public DateTime? LastPollUtc { get; set; }
		public DateTime? LastSuccessUtc { get; set; }
		public DateTime? LastErrorUtc { get; set; }

		public string LastErrorMessage { get; set; }
		public string LastRunId { get; set; }

		public bool? CommissioningIngestionEnabled { get; set; }
		public string CommissioningSerial { get; set; }
		public System.Collections.Generic.List<SizerDataCollector.Core.Commissioning.CommissioningReason> CommissioningBlockingReasons { get; set; }
		public string ServiceState { get; set; }
		public string ServiceStateReason { get; set; }
	}
}

