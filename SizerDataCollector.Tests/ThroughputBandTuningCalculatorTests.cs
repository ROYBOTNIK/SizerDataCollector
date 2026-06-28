using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SizerDataCollector.Core.Db;

namespace SizerDataCollector.Tests
{
	[TestClass]
	public class ThroughputBandTuningCalculatorTests
	{
		[TestMethod]
		public void TuneClampsStrongMachineWithoutExceedingLimits()
		{
			var values = Repeat(0.92, 80)
				.Concat(Repeat(0.96, 80))
				.Concat(Repeat(0.99, 80))
				.ToList();

			var result = new ThroughputBandTuningCalculator().Tune(values, 10);

			Assert.IsTrue(result.CanApply);
			Assert.AreEqual(5, result.Bands.Count);
			Assert.AreEqual(0.6000m, result.Bands[0].UpperBound);
			Assert.AreEqual(0.8000m, result.Bands[1].UpperBound);
			Assert.AreEqual(0.9200m, result.Bands[2].UpperBound);
			Assert.IsTrue(result.Bands[3].UpperBound <= 0.9800m);
		}

		[TestMethod]
		public void TuneClampsWeakMachineSoOnTargetDoesNotBecomeTooEasy()
		{
			var values = Repeat(0.25, 80)
				.Concat(Repeat(0.38, 80))
				.Concat(Repeat(0.52, 80))
				.ToList();

			var result = new ThroughputBandTuningCalculator().Tune(values, 10);

			Assert.IsTrue(result.CanApply);
			Assert.AreEqual(0.4500m, result.Bands[0].UpperBound);
			Assert.AreEqual(0.6500m, result.Bands[1].UpperBound);
			Assert.AreEqual(0.8000m, result.Bands[2].UpperBound);
			Assert.AreEqual(0.9000m, result.Bands[3].UpperBound);
		}

		[TestMethod]
		public void TuneMaintainsMinimumSpacing()
		{
			var values = Repeat(0.78, 300).ToList();

			var result = new ThroughputBandTuningCalculator().Tune(values, 10);

			Assert.IsTrue(result.CanApply);
			for (var i = 1; i < result.Bands.Count; i++)
			{
				Assert.IsTrue(result.Bands[i].LowerBound - result.Bands[i - 1].LowerBound >= 0.0500m);
			}

			foreach (var band in result.Bands)
			{
				Assert.IsTrue(band.UpperBound - band.LowerBound >= 0.0500m);
			}
		}

		[TestMethod]
		public void TuneRejectsLowDataWindow()
		{
			var result = new ThroughputBandTuningCalculator().Tune(Repeat(0.8, 20), 240);

			Assert.IsFalse(result.CanApply);
			Assert.AreEqual(20, result.ObservedMinutes);
			Assert.AreEqual(240, result.MinObservedMinutes);
		}

		[TestMethod]
		public void TuneCanUseLowerMinimumForOverride()
		{
			var result = new ThroughputBandTuningCalculator().Tune(Repeat(0.8, 20), 10);

			Assert.IsTrue(result.CanApply);
			Assert.AreEqual(20, result.ObservedMinutes);
			Assert.AreEqual(10, result.MinObservedMinutes);
		}

		private static IEnumerable<double> Repeat(double value, int count)
		{
			return Enumerable.Repeat(value, count);
		}
	}
}
