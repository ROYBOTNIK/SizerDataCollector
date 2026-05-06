using SizerDataCollector.Core.Config;

namespace SizerDataCollector.Core.AnomalyDetection
{
	public sealed class MachineEventConfig
	{
		public int EvalIntervalMinutes { get; }
		public int ScanWindowHours { get; }
		public double DowntimeMaxAvailabilityRatio { get; }
		public double SlowdownMaxThroughputRatio { get; }
		public double SlowdownMinAvailabilityRatio { get; }
		public double SlowdownMinTotalFpm { get; }
		public int MinDurationMinutes { get; }
		public int MergeGapMinutes { get; }
		public bool ExcludeLotTransitions { get; }
		public string ModelVersion { get; }

		public MachineEventConfig(CollectorConfig config)
		{
			EvalIntervalMinutes = config.MachineEventEvalIntervalMinutes;
			ScanWindowHours = config.MachineEventScanWindowHours;
			DowntimeMaxAvailabilityRatio = config.MachineEventDowntimeMaxAvailabilityRatio;
			SlowdownMaxThroughputRatio = config.MachineEventSlowdownMaxThroughputRatio;
			SlowdownMinAvailabilityRatio = config.MachineEventSlowdownMinAvailabilityRatio;
			SlowdownMinTotalFpm = config.MachineEventSlowdownMinTotalFpm;
			MinDurationMinutes = config.MachineEventMinDurationMinutes;
			MergeGapMinutes = config.MachineEventMergeGapMinutes;
			ExcludeLotTransitions = config.MachineEventExcludeLotTransitions;
			ModelVersion = "machine-events-v1";
		}

		public MachineEventConfig(
			int evalIntervalMinutes,
			int scanWindowHours,
			double downtimeMaxAvailabilityRatio,
			double slowdownMaxThroughputRatio,
			double slowdownMinAvailabilityRatio,
			double slowdownMinTotalFpm,
			int minDurationMinutes,
			int mergeGapMinutes,
			bool excludeLotTransitions,
			string modelVersion = "machine-events-v1")
		{
			EvalIntervalMinutes = evalIntervalMinutes;
			ScanWindowHours = scanWindowHours;
			DowntimeMaxAvailabilityRatio = downtimeMaxAvailabilityRatio;
			SlowdownMaxThroughputRatio = slowdownMaxThroughputRatio;
			SlowdownMinAvailabilityRatio = slowdownMinAvailabilityRatio;
			SlowdownMinTotalFpm = slowdownMinTotalFpm;
			MinDurationMinutes = minDurationMinutes;
			MergeGapMinutes = mergeGapMinutes;
			ExcludeLotTransitions = excludeLotTransitions;
			ModelVersion = string.IsNullOrWhiteSpace(modelVersion) ? "machine-events-v1" : modelVersion;
		}
	}
}
