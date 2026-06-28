using SizerDataCollector.Core.Config;

namespace SizerDataCollector.Core.AnomalyDetection
{
	public sealed class SizeAnomalyConfig
	{
		public int EvalIntervalMinutes { get; }
		public int WindowHours { get; }
		public double ZGate { get; }
		public double PctDevMin { get; }
		public int CooldownMinutes { get; }

		public SizeAnomalyConfig(CollectorConfig config)
		{
			EvalIntervalMinutes = config.SizeEvalIntervalMinutes;
			WindowHours = config.SizeWindowHours;
			ZGate = config.SizeZGate;
			PctDevMin = config.SizePctDevMin;
			CooldownMinutes = config.SizeCooldownMinutes;
		}

		public SizeAnomalyConfig(
			int evalIntervalMinutes,
			int windowHours,
			double zGate,
			double pctDevMin,
			int cooldownMinutes)
		{
			EvalIntervalMinutes = evalIntervalMinutes;
			WindowHours = windowHours;
			ZGate = zGate;
			PctDevMin = pctDevMin;
			CooldownMinutes = cooldownMinutes;
		}
	}
}
