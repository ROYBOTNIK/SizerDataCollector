using SizerDataCollector.Core.Config;

namespace SizerDataCollector.Core.AnomalyDetection
{
	public sealed class LotTransitionConfig
	{
		public int EvalIntervalMinutes { get; }
		public int ScanWindowHours { get; }
		public int StableWindowMinutes { get; }
		public int PeakSearchMinutes { get; }
		public double SlowdownFraction { get; }
		public double RecoveryFraction { get; }
		public int ConsecutiveSamplesForSlowdown { get; }
		public int RecoveryConsecutiveSamples { get; }
		public int MinPreStableSamples { get; }
		public int MinPostStableSamples { get; }
		public double MinFpmForBaseline { get; }
		public string ModelVersion { get; }

		public LotTransitionConfig(CollectorConfig config)
		{
			EvalIntervalMinutes = config.LotTransitionEvalIntervalMinutes;
			ScanWindowHours = config.LotTransitionScanWindowHours;
			StableWindowMinutes = config.LotTransitionStableWindowMinutes;
			PeakSearchMinutes = config.LotTransitionPeakSearchMinutes;
			SlowdownFraction = config.LotTransitionSlowdownFraction;
			RecoveryFraction = config.LotTransitionRecoveryFraction;
			ConsecutiveSamplesForSlowdown = config.LotTransitionConsecutiveSamplesForSlowdown;
			RecoveryConsecutiveSamples = config.LotTransitionRecoveryConsecutiveSamples;
			MinPreStableSamples = config.LotTransitionMinPreStableSamples;
			MinPostStableSamples = config.LotTransitionMinPostStableSamples;
			MinFpmForBaseline = config.LotTransitionMinFpmForBaseline;
			ModelVersion = "lot-transition-v1";
		}

		public LotTransitionConfig(
			int evalIntervalMinutes,
			int scanWindowHours,
			int stableWindowMinutes,
			int peakSearchMinutes,
			double slowdownFraction,
			double recoveryFraction,
			int consecutiveSamplesForSlowdown,
			int recoveryConsecutiveSamples,
			int minPreStableSamples,
			int minPostStableSamples,
			double minFpmForBaseline,
			string modelVersion = "lot-transition-v1")
		{
			EvalIntervalMinutes = evalIntervalMinutes;
			ScanWindowHours = scanWindowHours;
			StableWindowMinutes = stableWindowMinutes;
			PeakSearchMinutes = peakSearchMinutes;
			SlowdownFraction = slowdownFraction;
			RecoveryFraction = recoveryFraction;
			ConsecutiveSamplesForSlowdown = consecutiveSamplesForSlowdown;
			RecoveryConsecutiveSamples = recoveryConsecutiveSamples;
			MinPreStableSamples = minPreStableSamples;
			MinPostStableSamples = minPostStableSamples;
			MinFpmForBaseline = minFpmForBaseline;
			ModelVersion = string.IsNullOrWhiteSpace(modelVersion) ? "lot-transition-v1" : modelVersion;
		}
	}
}
