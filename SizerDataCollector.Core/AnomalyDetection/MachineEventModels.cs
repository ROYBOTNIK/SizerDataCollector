using System;
using System.Collections.Generic;

namespace SizerDataCollector.Core.AnomalyDetection
{
	public sealed class MachineEvent
	{
		public string EventType { get; set; }
		public DateTimeOffset StartTs { get; set; }
		public DateTimeOffset EndTs { get; set; }
		public double DurationMinutes { get; set; }
		public string SerialNo { get; set; }
		public long? BatchRecordId { get; set; }
		public string Lot { get; set; }
		public string Variety { get; set; }
		public double? AvgAvailabilityRatio { get; set; }
		public double? MinAvailabilityRatio { get; set; }
		public double? AvgThroughputRatio { get; set; }
		public double? MinThroughputRatio { get; set; }
		public double? AvgTotalFpm { get; set; }
		public double? MinTotalFpm { get; set; }
		public double? AvgOeeScore { get; set; }
		public string Reason { get; set; }
		public bool OverlapsLotTransition { get; set; }
		public string ExplanationJson { get; set; }
		public string ModelVersion { get; set; }
		public string DeliveredTo { get; set; }
	}

	public sealed class MachineEventReport
	{
		public string SerialNo { get; set; }
		public DateTimeOffset FromTs { get; set; }
		public DateTimeOffset ToTs { get; set; }
		public int MinuteCount { get; set; }
		public int DowntimeCandidates { get; set; }
		public int SlowdownCandidates { get; set; }
		public List<MachineEvent> DowntimeEvents { get; set; } = new List<MachineEvent>();
		public List<MachineEvent> SlowdownEvents { get; set; } = new List<MachineEvent>();
	}
}
