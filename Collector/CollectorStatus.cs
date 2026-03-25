using System;

namespace SizerDataCollector.Core.Collector
{
	public sealed class CollectorStatus
	{
		private readonly object _syncRoot = new object();

		public DateTime? LastPollStartUtc { get; set; }
		public DateTime? LastPollEndUtc { get; set; }
		public string LastPollError { get; set; }
		public long TotalPollsStarted { get; set; }
		public long TotalPollsSucceeded { get; set; }
		public long TotalPollsFailed { get; set; }
		public DateTime? LastPollStartedUtc { get; set; }
		public DateTime? LastPollCompletedUtc { get; set; }
		public DateTime? LastSuccessUtc { get; set; }
		public DateTime? LastErrorUtc { get; set; }
		public string LastErrorMessage { get; set; }
		public string LastRunId { get; set; }
		public string MachineSerial { get; set; }
		public string MachineName { get; set; }
		public bool CommissioningIngestionEnabled { get; set; }
		public string CommissioningSerial { get; set; }
		public System.Collections.Generic.List<SizerDataCollector.Core.Commissioning.CommissioningReason> CommissioningBlockingReasons { get; set; } = new System.Collections.Generic.List<SizerDataCollector.Core.Commissioning.CommissioningReason>();
		public string ServiceState { get; set; }
		public string ServiceStateReason { get; set; }

		internal object SyncRoot => _syncRoot;

		public CollectorStatusSnapshot CreateSnapshot()
		{
			lock (_syncRoot)
			{
				return new CollectorStatusSnapshot
				{
					LastPollStartUtc = LastPollStartUtc,
					LastPollEndUtc = LastPollEndUtc,
					LastPollError = LastPollError,
					TotalPollsStarted = TotalPollsStarted,
					TotalPollsSucceeded = TotalPollsSucceeded,
					TotalPollsFailed = TotalPollsFailed,
					LastPollStartedUtc = LastPollStartedUtc,
					LastPollCompletedUtc = LastPollCompletedUtc,
					LastSuccessUtc = LastSuccessUtc,
					LastErrorUtc = LastErrorUtc,
					LastErrorMessage = LastErrorMessage,
					LastRunId = LastRunId,
					MachineSerial = MachineSerial,
					MachineName = MachineName,
					CommissioningIngestionEnabled = CommissioningIngestionEnabled,
					CommissioningSerial = CommissioningSerial,
					CommissioningBlockingReasons = CommissioningBlockingReasons == null ? null : new System.Collections.Generic.List<SizerDataCollector.Core.Commissioning.CommissioningReason>(CommissioningBlockingReasons),
					ServiceState = ServiceState,
					ServiceStateReason = ServiceStateReason
				};
			}
		}
	}

	public sealed class CollectorStatusSnapshot
	{
		public DateTime? LastPollStartUtc { get; set; }
		public DateTime? LastPollEndUtc { get; set; }
		public string LastPollError { get; set; }
		public long TotalPollsStarted { get; set; }
		public long TotalPollsSucceeded { get; set; }
		public long TotalPollsFailed { get; set; }
		public DateTime? LastPollStartedUtc { get; set; }
		public DateTime? LastPollCompletedUtc { get; set; }
		public DateTime? LastSuccessUtc { get; set; }
		public DateTime? LastErrorUtc { get; set; }
		public string LastErrorMessage { get; set; }
		public string LastRunId { get; set; }
		public string MachineSerial { get; set; }
		public string MachineName { get; set; }
		public bool CommissioningIngestionEnabled { get; set; }
		public System.Collections.Generic.List<SizerDataCollector.Core.Commissioning.CommissioningReason> CommissioningBlockingReasons { get; set; }
		public string CommissioningSerial { get; set; }
		public string ServiceState { get; set; }
		public string ServiceStateReason { get; set; }
	}
}

