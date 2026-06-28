using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SizerDataCollector.Core.Db
{
	public sealed class ThroughputBandTuningCalculator
	{
		public const int DefaultMinObservedMinutes = 240;

		private static readonly BandSpec[] Specs =
		{
			new BandSpec("very_low", 0.00m, 0.50m),
			new BandSpec("low", 0.50m, 0.70m),
			new BandSpec("close", 0.70m, 0.85m),
			new BandSpec("on_target", 0.85m, 0.95m),
			new BandSpec("surpassing_target", 0.95m, 1.00m)
		};

		public ThroughputBandTuningResult Tune(IEnumerable<double> throughputRatios, int minObservedMinutes)
		{
			if (throughputRatios == null) throw new ArgumentNullException(nameof(throughputRatios));
			if (minObservedMinutes <= 0) minObservedMinutes = DefaultMinObservedMinutes;

			var values = throughputRatios
				.Where(v => !double.IsNaN(v) && !double.IsInfinity(v))
				.Select(v => Math.Max(0.0, Math.Min(1.0, v)))
				.OrderBy(v => v)
				.ToList();

			if (values.Count < minObservedMinutes)
			{
				return ThroughputBandTuningResult.InsufficientData(values.Count, minObservedMinutes);
			}

			var b1 = Clamp(Quantile(values, 0.20), 0.45, 0.60);
			var b2 = Clamp(Quantile(values, 0.45), 0.65, 0.80);
			var b3 = Clamp(Quantile(values, 0.70), 0.80, 0.92);
			var b4 = Clamp(Quantile(values, 0.90), 0.90, 0.98);
			var boundaries = EnforceSpacing(new[] { b1, b2, b3, b4 }, 0.05);

			var bands = new List<TunedBand>();
			bands.Add(new TunedBand(Specs[0].BandName, Specs[0].LowerBound, Decimal(boundaries[0])));
			bands.Add(new TunedBand(Specs[1].BandName, Decimal(boundaries[0]), Decimal(boundaries[1])));
			bands.Add(new TunedBand(Specs[2].BandName, Decimal(boundaries[1]), Decimal(boundaries[2])));
			bands.Add(new TunedBand(Specs[3].BandName, Decimal(boundaries[2]), Decimal(boundaries[3])));
			bands.Add(new TunedBand(Specs[4].BandName, Decimal(boundaries[3]), Specs[4].UpperBound));

			return ThroughputBandTuningResult.Success(values.Count, minObservedMinutes, Confidence(values.Count), bands);
		}

		private static double Quantile(IReadOnlyList<double> sortedValues, double q)
		{
			if (sortedValues.Count == 0) return 0;
			if (sortedValues.Count == 1) return sortedValues[0];

			var position = (sortedValues.Count - 1) * q;
			var lower = (int)Math.Floor(position);
			var upper = (int)Math.Ceiling(position);
			if (lower == upper) return sortedValues[lower];

			var weight = position - lower;
			return sortedValues[lower] + ((sortedValues[upper] - sortedValues[lower]) * weight);
		}

		private static double Clamp(double value, double min, double max)
		{
			return Math.Max(min, Math.Min(max, value));
		}

		private static double[] EnforceSpacing(double[] boundaries, double minSpacing)
		{
			for (var i = 1; i < boundaries.Length; i++)
			{
				boundaries[i] = Math.Max(boundaries[i], boundaries[i - 1] + minSpacing);
			}

			for (var i = boundaries.Length - 2; i >= 0; i--)
			{
				boundaries[i] = Math.Min(boundaries[i], boundaries[i + 1] - minSpacing);
			}

			return boundaries.Select(v => Clamp(v, 0.0, 1.0)).ToArray();
		}

		private static decimal Confidence(int observedMinutes)
		{
			var confidence = Math.Min(1.0m, observedMinutes / 1440.0m);
			return Math.Round(confidence, 4);
		}

		private static decimal Decimal(double value)
		{
			return Math.Round(Convert.ToDecimal(value, CultureInfo.InvariantCulture), 4);
		}

		private sealed class BandSpec
		{
			public BandSpec(string bandName, decimal lowerBound, decimal upperBound)
			{
				BandName = bandName;
				LowerBound = lowerBound;
				UpperBound = upperBound;
			}

			public string BandName { get; }
			public decimal LowerBound { get; }
			public decimal UpperBound { get; }
		}
	}

	public sealed class ThroughputBandTuningResult
	{
		private ThroughputBandTuningResult(bool canApply, int observedMinutes, int minObservedMinutes, decimal confidence, IReadOnlyList<TunedBand> bands)
		{
			CanApply = canApply;
			ObservedMinutes = observedMinutes;
			MinObservedMinutes = minObservedMinutes;
			Confidence = confidence;
			Bands = bands ?? new List<TunedBand>();
		}

		public bool CanApply { get; }
		public int ObservedMinutes { get; }
		public int MinObservedMinutes { get; }
		public decimal Confidence { get; }
		public IReadOnlyList<TunedBand> Bands { get; }

		public static ThroughputBandTuningResult Success(int observedMinutes, int minObservedMinutes, decimal confidence, IReadOnlyList<TunedBand> bands)
		{
			return new ThroughputBandTuningResult(true, observedMinutes, minObservedMinutes, confidence, bands);
		}

		public static ThroughputBandTuningResult InsufficientData(int observedMinutes, int minObservedMinutes)
		{
			return new ThroughputBandTuningResult(false, observedMinutes, minObservedMinutes, 0m, new List<TunedBand>());
		}

		private const int DefaultMinObservedMinutes = ThroughputBandTuningCalculator.DefaultMinObservedMinutes;
	}

	public sealed class TunedBand
	{
		public TunedBand(string bandName, decimal lowerBound, decimal upperBound)
		{
			BandName = bandName;
			LowerBound = lowerBound;
			UpperBound = upperBound;
		}

		public string BandName { get; }
		public decimal LowerBound { get; }
		public decimal UpperBound { get; }
	}
}
