using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SizerDataCollector.Core.AnomalyDetection;

namespace SizerDataCollector.Tests
{
	[TestClass]
	public class LotTransitionAnalyzerTests
	{
		[TestMethod]
		public void AnalyzePoints_DetectsSlowdownRecoveryAndOpportunityLoss()
		{
			var start = DateTimeOffset.Parse("2026-04-23T10:00:00Z");
			var points = new List<FpmBatchPoint>();

			Add(points, start, 0, 101, 41000);
			Add(points, start, 1, 101, 41200);
			Add(points, start, 2, 101, 40900);
			Add(points, start, 3, 101, 41100);
			Add(points, start, 4, 101, 41000);
			Add(points, start, 5, 101, 41300);
			Add(points, start, 6, 101, 42000);
			Add(points, start, 7, 101, 41000);
			Add(points, start, 8, 101, 40600);
			Add(points, start, 9, 101, 26000);
			Add(points, start, 10, 202, 12800);
			Add(points, start, 11, 202, 13500);
			Add(points, start, 12, 202, 25000);
			Add(points, start, 13, 202, 32000);
			Add(points, start, 14, 202, 38200);
			Add(points, start, 15, 202, 40000);
			Add(points, start, 16, 202, 40500);
			Add(points, start, 17, 202, 40200);

			var analyzer = new LotTransitionAnalyzer(CreateConfig(), string.Empty);
			var report = analyzer.AnalyzePoints("140578", start, start.AddMinutes(20), points, BatchInfos());

			Assert.AreEqual(1, report.TransitionCandidates);
			Assert.AreEqual(1, report.Events.Count);

			var evt = report.Events[0];
			Assert.AreEqual(101, evt.OutgoingBatchRecordId);
			Assert.AreEqual(202, evt.IncomingBatchRecordId);
			Assert.AreEqual(start.AddMinutes(10), evt.TransitionTs);
			Assert.AreEqual(start.AddMinutes(9), evt.DisruptionStartTs);
			Assert.AreEqual(start.AddMinutes(10), evt.TroughTs);
			Assert.AreEqual(start.AddMinutes(14), evt.StableRecoveryTs);
			Assert.AreEqual(5.0, evt.DisruptionDurationMinutes, 0.001);
			Assert.AreEqual(42000, evt.PrePeakFpm, 0.001);
			Assert.AreEqual(40500, evt.PostPeakFpm, 0.001);
			Assert.IsTrue(evt.OpportunityWindowMinutes > evt.DisruptionDurationMinutes);
			Assert.IsTrue(evt.FruitOpportunityShortfall > 0);
			Assert.IsTrue(evt.StableFruitOpportunityShortfall > 0);
			Assert.IsTrue(evt.StableFruitOpportunityShortfall < evt.FruitOpportunityShortfall);
			Assert.AreEqual(evt.FruitOpportunityShortfall / evt.CounterfactualFpmMinutes, evt.PeakThroughputLossRatio, 0.001);
			Assert.AreEqual(evt.FruitOpportunityShortfall / evt.PrePeakFpm, evt.PeakEquivalentLostMinutes, 0.001);
			Assert.AreEqual(evt.StableFruitOpportunityShortfall / evt.StableCounterfactualFpmMinutes, evt.StableThroughputLossRatio, 0.001);
			Assert.AreEqual(evt.StableFruitOpportunityShortfall / evt.PreStableFpm, evt.StableEquivalentLostMinutes, 0.001);
			Assert.IsFalse(evt.BreakOverlapDetected);
			Assert.AreEqual(evt.StableFruitOpportunityShortfall, evt.BreakAdjustedStableFruitOpportunityShortfall, 0.001);
			Assert.AreEqual(evt.StableEquivalentLostMinutes, evt.BreakAdjustedStableEquivalentLostMinutes, 0.001);
			Assert.IsFalse(evt.TargetThroughput.HasValue);
			Assert.IsFalse(evt.TargetFruitOpportunityShortfall.HasValue);
			StringAssert.Contains(evt.ExplanationJson, "slowdown_threshold_fpm");
		}

		[TestMethod]
		public void AnalyzePoints_AdjustsOpportunityLossWhenBreakLikeStopOverlapsTransitionWindow()
		{
			var start = DateTimeOffset.Parse("2026-04-23T10:00:00Z");
			var points = new List<FpmBatchPoint>();

			for (var minute = 0; minute < 25; minute++)
				Add(points, start, minute, 101, minute == 22 ? 45000 : 40000);
			for (var minute = 25; minute <= 40; minute++)
				Add(points, start, minute, 101, 0);
			for (var minute = 41; minute < 50; minute++)
				Add(points, start, minute, 101, 40000);
			Add(points, start, 50, 202, 10000);
			Add(points, start, 51, 202, 18000);
			Add(points, start, 52, 202, 38000);
			Add(points, start, 53, 202, 43000);
			Add(points, start, 54, 202, 42000);
			Add(points, start, 55, 202, 41000);

			var analyzer = new LotTransitionAnalyzer(CreateConfig(), string.Empty);
			var report = analyzer.AnalyzePoints("140578", start, start.AddMinutes(60), points, BatchInfos());

			Assert.AreEqual(1, report.TransitionCandidates);
			Assert.AreEqual(1, report.Events.Count);

			var evt = report.Events[0];
			Assert.IsTrue(evt.BreakOverlapDetected);
			Assert.AreEqual(15.0, evt.BreakOverlapMinutes, 0.001);
			Assert.AreEqual(evt.OpportunityWindowMinutes - evt.BreakOverlapMinutes, evt.BreakAdjustedOpportunityWindowMinutes, 0.001);
			Assert.IsTrue(evt.BreakAdjustedStableFruitOpportunityShortfall < evt.StableFruitOpportunityShortfall);
			Assert.IsTrue(evt.BreakAdjustedStableEquivalentLostMinutes < evt.StableEquivalentLostMinutes);
			StringAssert.Contains(evt.ExplanationJson, "break_overlap_minutes");
		}

		[TestMethod]
		public void DatabaseSink_InsertSql_IncludesThroughputImpactColumns()
		{
			var field = typeof(LotTransitionDatabaseSink).GetField("InsertSql", BindingFlags.NonPublic | BindingFlags.Static);
			var sql = field.GetValue(null) as string;

			StringAssert.Contains(sql, "stable_counterfactual_fpm_minutes");
			StringAssert.Contains(sql, "stable_fruit_opportunity_shortfall");
			StringAssert.Contains(sql, "stable_throughput_loss_ratio");
			StringAssert.Contains(sql, "stable_equivalent_lost_minutes");
			StringAssert.Contains(sql, "peak_throughput_loss_ratio");
			StringAssert.Contains(sql, "peak_equivalent_lost_minutes");
			StringAssert.Contains(sql, "target_throughput");
			StringAssert.Contains(sql, "target_counterfactual_fpm_minutes");
			StringAssert.Contains(sql, "target_fruit_opportunity_shortfall");
			StringAssert.Contains(sql, "target_equivalent_lost_minutes");
			StringAssert.Contains(sql, "break_overlap_detected");
			StringAssert.Contains(sql, "break_adjusted_stable_fruit_opportunity_shortfall");
			StringAssert.Contains(sql, "DO UPDATE SET");
		}

		[TestMethod]
		public void SqlView_ExposesPrimaryStableImpactFields()
		{
			var viewsPath = Path.Combine(
				AppDomain.CurrentDomain.BaseDirectory,
				"..",
				"..",
				"..",
				"..",
				"SizerDataCollector.Service",
				"sql",
				"definitions",
				"views.sql");
			var sql = File.ReadAllText(Path.GetFullPath(viewsPath));

			StringAssert.Contains(sql, "primary_fruit_opportunity_shortfall");
			StringAssert.Contains(sql, "primary_throughput_loss_ratio");
			StringAssert.Contains(sql, "primary_equivalent_lost_minutes");
			StringAssert.Contains(sql, "'break_adjusted_stable_pre_transition_fpm'::text AS primary_baseline_label");
			StringAssert.Contains(sql, "break_overlap_detected");
			StringAssert.Contains(sql, "break_adjusted_stable_equivalent_lost_minutes");
			StringAssert.Contains(sql, "fruit_opportunity_shortfall");
		}

		[TestMethod]
		public void AnalyzePoints_SkipsFlatlineWithoutRecovery()
		{
			var start = DateTimeOffset.Parse("2026-04-23T10:00:00Z");
			var points = new List<FpmBatchPoint>();
			for (var minute = 0; minute < 10; minute++)
				Add(points, start, minute, 101, 40000);
			for (var minute = 10; minute < 20; minute++)
				Add(points, start, minute, 202, 0);

			var analyzer = new LotTransitionAnalyzer(CreateConfig(), string.Empty);
			var report = analyzer.AnalyzePoints("140578", start, start.AddMinutes(20), points, BatchInfos());

			Assert.AreEqual(1, report.TransitionCandidates);
			Assert.AreEqual(0, report.Events.Count);
		}

		[TestMethod]
		public void AnalyzePoints_SkipsWhenStableContextIsTooShort()
		{
			var start = DateTimeOffset.Parse("2026-04-23T10:00:00Z");
			var points = new List<FpmBatchPoint>();
			Add(points, start, 0, 101, 40000);
			Add(points, start, 1, 101, 26000);
			Add(points, start, 2, 202, 12000);
			Add(points, start, 3, 202, 38000);
			Add(points, start, 4, 202, 40000);

			var analyzer = new LotTransitionAnalyzer(CreateConfig(), string.Empty);
			var report = analyzer.AnalyzePoints("140578", start, start.AddMinutes(5), points, BatchInfos());

			Assert.AreEqual(1, report.TransitionCandidates);
			Assert.AreEqual(0, report.Events.Count);
		}

		private static LotTransitionConfig CreateConfig()
		{
			return new LotTransitionConfig(
				evalIntervalMinutes: 30,
				scanWindowHours: 72,
				stableWindowMinutes: 10,
				peakSearchMinutes: 30,
				slowdownFraction: 0.15,
				recoveryFraction: 0.10,
				consecutiveSamplesForSlowdown: 1,
				recoveryConsecutiveSamples: 2,
				minPreStableSamples: 3,
				minPostStableSamples: 3,
				minFpmForBaseline: 100);
		}

		private static Dictionary<long, LotTransitionBatchInfo> BatchInfos()
		{
			return new Dictionary<long, LotTransitionBatchInfo>
			{
				{ 101, new LotTransitionBatchInfo { BatchRecordId = 101, GrowerCode = "920", Label = "920 CL" } },
				{ 202, new LotTransitionBatchInfo { BatchRecordId = 202, GrowerCode = "309", Label = "309 CL" } }
			};
		}

		private static void Add(List<FpmBatchPoint> points, DateTimeOffset start, int minute, long batchRecordId, double fpm)
		{
			points.Add(new FpmBatchPoint
			{
				Ts = start.AddMinutes(minute),
				BatchRecordId = batchRecordId,
				Fpm = fpm
			});
		}
	}
}
