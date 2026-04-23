using SizerDataCollector.Core.Config;

namespace SizerDataCollector.Core.AnomalyDetection
{
	public sealed class AnomalyDetectorConfig
	{
		public int WindowMinutes { get; }
		public double ZGate { get; }
		public double BandLowMin { get; }
		public double BandLowMax { get; }
		public double BandMediumMax { get; }
		public int CooldownSeconds { get; }
		public string RecycleGradeKey { get; }
		public double MinLaneFpm { get; }
		public double MinPeerLaneFpm { get; }
		public int MinActivePeerLanes { get; }
		public int MinConsecutiveWindows { get; }

		public AnomalyDetectorConfig(CollectorConfig config)
		{
			WindowMinutes = config.AnomalyWindowMinutes;
			ZGate = config.AnomalyZGate;
			BandLowMin = config.BandLowMin;
			BandLowMax = config.BandLowMax;
			BandMediumMax = config.BandMediumMax;
			CooldownSeconds = config.AlarmCooldownSeconds;
			RecycleGradeKey = config.RecycleGradeKey;
			MinLaneFpm = config.AnomalyMinLaneFpm;
			MinPeerLaneFpm = config.AnomalyMinPeerLaneFpm;
			MinActivePeerLanes = config.AnomalyMinActivePeerLanes;
			MinConsecutiveWindows = config.AnomalyMinConsecutiveWindows;
		}

		public AnomalyDetectorConfig(
			int windowMinutes,
			double zGate,
			double bandLowMin,
			double bandLowMax,
			double bandMediumMax,
			int cooldownSeconds,
			string recycleGradeKey,
			double minLaneFpm = 150.0,
			double minPeerLaneFpm = 150.0,
			int minActivePeerLanes = 4,
			int minConsecutiveWindows = 2)
		{
			WindowMinutes = windowMinutes;
			ZGate = zGate;
			BandLowMin = bandLowMin;
			BandLowMax = bandLowMax;
			BandMediumMax = bandMediumMax;
			CooldownSeconds = cooldownSeconds;
			RecycleGradeKey = recycleGradeKey;
			MinLaneFpm = minLaneFpm;
			MinPeerLaneFpm = minPeerLaneFpm;
			MinActivePeerLanes = minActivePeerLanes;
			MinConsecutiveWindows = minConsecutiveWindows;
		}
	}
}
