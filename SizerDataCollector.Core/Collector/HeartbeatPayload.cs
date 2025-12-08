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
	}
}

