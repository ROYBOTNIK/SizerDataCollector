using System;

namespace SizerDataCollector.Core.AnomalyDetection
{
	public static class PriorityClassifier
	{
		/// <summary>
		/// Maps |delta_pct| to a severity string: null (no alarm), "Low", "Medium", or "High".
		/// </summary>
		public static string Classify(double absDeltaPct, AnomalyDetectorConfig config)
		{
			if (absDeltaPct < config.BandLowMin)
				return null;
			if (absDeltaPct < config.BandLowMax)
				return "Low";
			if (absDeltaPct < config.BandMediumMax)
				return "Medium";
			return "High";
		}

		/// <summary>
		/// Maps severity string to the WCF AlarmPriority enum value.
		/// </summary>
		public static SizerServiceReference.AlarmPriority ToAlarmPriority(string severity)
		{
			if (string.Equals(severity, "High", StringComparison.OrdinalIgnoreCase))
				return SizerServiceReference.AlarmPriority.High;
			if (string.Equals(severity, "Medium", StringComparison.OrdinalIgnoreCase))
				return SizerServiceReference.AlarmPriority.Medium;
			return SizerServiceReference.AlarmPriority.Low;
		}
	}
}
