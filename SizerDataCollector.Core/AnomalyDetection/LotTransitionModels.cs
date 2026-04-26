using System;
using System.Collections.Generic;

namespace SizerDataCollector.Core.AnomalyDetection
{
	public sealed class FpmBatchPoint
	{
		public DateTimeOffset Ts { get; set; }
		public double Fpm { get; set; }
		public long BatchRecordId { get; set; }
	}

	public sealed class LotTransitionBatchInfo
	{
		public long BatchRecordId { get; set; }
		public string GrowerCode { get; set; }
		public string Label { get; set; }
	}

	public sealed class LotTransitionEvent
	{
		public DateTimeOffset TransitionTs { get; set; }
		public string SerialNo { get; set; }
		public long OutgoingBatchRecordId { get; set; }
		public long IncomingBatchRecordId { get; set; }
		public string OutgoingGrowerCode { get; set; }
		public string IncomingGrowerCode { get; set; }
		public string OutgoingLabel { get; set; }
		public string IncomingLabel { get; set; }
		public DateTimeOffset DisruptionStartTs { get; set; }
		public DateTimeOffset TroughTs { get; set; }
		public DateTimeOffset StableRecoveryTs { get; set; }
		public double DisruptionDurationMinutes { get; set; }
		public double PreStableFpm { get; set; }
		public double TroughFpm { get; set; }
		public double PostStableFpm { get; set; }
		public double PrePeakFpm { get; set; }
		public double PostPeakFpm { get; set; }
		public DateTimeOffset OpportunityWindowStartTs { get; set; }
		public DateTimeOffset OpportunityWindowEndTs { get; set; }
		public double OpportunityWindowMinutes { get; set; }
		public double IntegratedFpmMinutes { get; set; }
		public double CounterfactualFpmMinutes { get; set; }
		public double FruitOpportunityShortfall { get; set; }
		public double? AvailabilityAvgDuringDisruption { get; set; }
		public double? AvailabilityAvgOpportunityWindow { get; set; }
		public string ExplanationJson { get; set; }
		public string ModelVersion { get; set; }
		public string DeliveredTo { get; set; }
	}

	public sealed class LotTransitionReport
	{
		public string SerialNo { get; set; }
		public DateTimeOffset FromTs { get; set; }
		public DateTimeOffset ToTs { get; set; }
		public List<LotTransitionEvent> Events { get; set; } = new List<LotTransitionEvent>();
		public int TransitionCandidates { get; set; }
	}
}
