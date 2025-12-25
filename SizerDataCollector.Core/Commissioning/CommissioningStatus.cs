using System.Collections.Generic;
using SizerDataCollector.Core.Db;

namespace SizerDataCollector.Core.Commissioning
{
	public sealed class CommissioningStatus
	{
		public string SerialNo { get; set; }
		public bool DbBootstrapped { get; set; }
		public bool SizerConnected { get; set; }
		public bool ThresholdsSet { get; set; }
		public bool MachineDiscovered { get; set; }
		public bool GradeMappingCompleted { get; set; }
		public bool CanEnableIngestion { get; set; }
		public List<CommissioningReason> BlockingReasons { get; } = new List<CommissioningReason>();

		// Snapshot of stored commissioning row (timestamps/notes) for callers that need it.
		public CommissioningRow StoredRow { get; set; }
	}
}

