using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SizerDataCollector.Core.AnomalyDetection;

namespace SizerDataCollector.Tests
{
	[TestClass]
	public class AnomalyDetectorTests
	{
		[TestMethod]
		public void Update_DetectsLaneWithLane32StylePeerSkew()
		{
			var detector = new AnomalyDetector(CreateConfig(minConsecutiveWindows: 2));
			var matrix = CreateMatrix(
				new[] { "PEDDLER", "D/S", "CULL", "E4", "GREEN", "E3" },
				new[]
				{
					new[] { 1794.0, 1218.0, 1715.0, 1662.0, 1189.0, 743.0 },
					new[] { 1794.0, 1218.0, 1715.0, 1662.0, 1189.0, 743.0 },
					new[] { 1794.0, 1218.0, 1715.0, 1662.0, 1189.0, 743.0 },
					new[] { 1794.0, 1218.0, 1715.0, 1662.0, 1189.0, 743.0 },
					new[] { 6277.0, 3332.0, 390.0, 0.0, 2.0, 0.0 }
				});

			var firstPass = detector.Update(matrix, DateTimeOffset.Parse("2026-04-24T05:39:00Z"), "140578", 123);
			var secondPass = detector.Update(matrix, DateTimeOffset.Parse("2026-04-24T05:40:00Z"), "140578", 123);

			Assert.AreEqual(0, firstPass.Count, "Detector should wait for the configured consecutive windows.");
			Assert.AreEqual(1, secondPass.Count, "Detector should emit one lane-level composition event.");

			var evt = secondPass[0];
			Assert.AreEqual(5, evt.LaneNo);
			Assert.AreEqual("PEDDLER", evt.GradeKey);
			Assert.AreEqual("High", evt.Severity);
			Assert.IsTrue(evt.Pct > 40.0, "Dominant point delta should reflect the lane-vs-peer share gap.");
			StringAssert.Contains(evt.AlarmTitle, "Lane 5");
			StringAssert.Contains(evt.AlarmTitle, "PEDDLER");
			StringAssert.Contains(evt.AlarmDetails, "grading differently from the rest of the machine");
			StringAssert.Contains(evt.AlarmDetails, "typical");
			StringAssert.Contains(evt.ExplanationJson, "\"type\":\"lane_composition_skew\"");
		}

		[TestMethod]
		public void Update_SuppressesLowVolumeLane()
		{
			var detector = new AnomalyDetector(CreateConfig(minConsecutiveWindows: 1, minLaneFpm: 150.0));
			var matrix = CreateMatrix(
				new[] { "PEDDLER", "D/S", "CULL" },
				new[]
				{
					new[] { 600.0, 300.0, 100.0 },
					new[] { 600.0, 300.0, 100.0 },
					new[] { 600.0, 300.0, 100.0 },
					new[] { 600.0, 300.0, 100.0 },
					new[] { 10.0, 10.0, 80.0 }
				});

			var events = detector.Update(matrix, DateTimeOffset.UtcNow, "140578", 123);

			Assert.AreEqual(0, events.Count);
		}

		[TestMethod]
		public void Update_SuppressesWhenTooFewPeerLanes()
		{
			var detector = new AnomalyDetector(CreateConfig(minConsecutiveWindows: 1, minActivePeerLanes: 4));
			var matrix = CreateMatrix(
				new[] { "PEDDLER", "D/S", "CULL" },
				new[]
				{
					new[] { 600.0, 300.0, 100.0 },
					new[] { 600.0, 300.0, 100.0 },
					new[] { 600.0, 300.0, 100.0 },
					new[] { 10.0, 10.0, 80.0 }
				});

			var events = detector.Update(matrix, DateTimeOffset.UtcNow, "140578", 123);

			Assert.AreEqual(0, events.Count);
		}

		[TestMethod]
		public void Update_DoesNotTriggerForStablePeerMix()
		{
			var detector = new AnomalyDetector(CreateConfig(minConsecutiveWindows: 1));
			var matrix = CreateMatrix(
				new[] { "PEDDLER", "D/S", "CULL" },
				new[]
				{
					new[] { 600.0, 300.0, 100.0 },
					new[] { 600.0, 300.0, 100.0 },
					new[] { 600.0, 300.0, 100.0 },
					new[] { 600.0, 300.0, 100.0 },
					new[] { 600.0, 300.0, 100.0 }
				});

			var events = detector.Update(matrix, DateTimeOffset.UtcNow, "140578", 123);

			Assert.AreEqual(0, events.Count);
		}

		[TestMethod]
		public void Update_TriggersOnExtremeDeltaEvenWhenScoreIsBelowGate()
		{
			var detector = new AnomalyDetector(CreateConfig(minConsecutiveWindows: 1));
			var matrix = CreateMatrix(
				new[] { "PEDDLER", "D/S", "CULL" },
				new[]
				{
					new[] { 0.0, 1000.0, 0.0 },     // 0%
					new[] { 200.0, 800.0, 0.0 },    // 20%
					new[] { 400.0, 600.0, 0.0 },    // 40%
					new[] { 600.0, 400.0, 0.0 },    // 60%
					new[] { 800.0, 200.0, 0.0 }     // 80% (dominant lane)
				});

			var events = detector.Update(matrix, DateTimeOffset.UtcNow, "140578", 123);

			Assert.IsTrue(events.Count >= 1, "Extreme share delta should trigger even when robust score is damped by broad peer spread.");
			Assert.IsTrue(events.Exists(e => e.GradeKey == "PEDDLER" && Math.Abs(e.Pct) >= 20.0));
		}

		[TestMethod]
		public void Update_UsesLaneLevelCompositionSkewFallback()
		{
			var detector = new AnomalyDetector(CreateConfig(minConsecutiveWindows: 1));
			var matrix = CreateMatrix(
				new[] { "G1", "G2", "G3", "G4", "G5", "G6", "G7", "G8", "G9", "G10" },
				new[]
				{
					new[] { 140.0, 130.0, 120.0, 110.0, 100.0, 90.0, 80.0, 70.0, 60.0, 50.0 },
					new[] { 145.0, 125.0, 118.0, 112.0,  98.0, 92.0, 82.0, 72.0, 62.0, 48.0 },
					new[] { 142.0, 128.0, 119.0, 111.0, 101.0, 89.0, 81.0, 69.0, 61.0, 49.0 },
					new[] { 139.0, 131.0, 121.0, 109.0,  99.0, 91.0, 79.0, 71.0, 59.0, 51.0 },
					new[] { 190.0, 180.0, 150.0,  95.0,  70.0, 65.0, 55.0, 45.0, 35.0, 25.0 }
				});

			var events = detector.Update(matrix, DateTimeOffset.UtcNow, "140578", 123);

			Assert.IsTrue(events.Count >= 1, "Lane-level composition distance should trigger even when no single grade dominates by itself.");
			StringAssert.Contains(events[0].ExplanationJson, "\"compositionSkewPts\":");
		}

		[TestMethod]
		public void Update_PreservesRollingWindow_WhenGradeSetFluctuates()
		{
			// Live machines frequently omit a grade one minute and re-emit it the next.
			// The detector must NOT wipe the rolling window when this happens - it should
			// project the missing grade as zero and keep accumulating samples.
			var detector = new AnomalyDetector(CreateConfig(minConsecutiveWindows: 1));

			var fullKeys = new[] { "PEDDLER", "D/S", "CULL" };
			var reducedKeys = new[] { "PEDDLER", "D/S" };
			var growingKeys = new[] { "PEDDLER", "D/S", "CULL", "GREEN" };

			detector.Update(BuildSnapshot(fullKeys,    1.0), DateTimeOffset.Parse("2026-04-24T05:00:00Z"), "140578", 123);
			Assert.AreEqual(1, detector.WindowSampleCount);

			detector.Update(BuildSnapshot(reducedKeys, 1.0), DateTimeOffset.Parse("2026-04-24T05:01:00Z"), "140578", 123);
			Assert.AreEqual(2, detector.WindowSampleCount, "A snapshot that omits a previously-seen grade must not reset the window.");

			detector.Update(BuildSnapshot(growingKeys, 1.0), DateTimeOffset.Parse("2026-04-24T05:02:00Z"), "140578", 123);
			Assert.AreEqual(3, detector.WindowSampleCount, "A snapshot that introduces a new grade must not reset the window.");

			// Canonical grade set should be the union of all observed keys, in first-seen order.
			var grades = detector.GradeKeys;
			Assert.AreEqual(4, grades.Count);
			CollectionAssert.AreEqual(
				new[] { "PEDDLER", "D/S", "CULL", "GREEN" },
				new System.Collections.Generic.List<string>(grades));
		}

		[TestMethod]
		public void Update_PreservesRollingWindow_WhenLaneCountGrows()
		{
			var detector = new AnomalyDetector(CreateConfig(minConsecutiveWindows: 1));

			var fourLanes = CreateMatrix(
				new[] { "PEDDLER", "D/S", "CULL" },
				new[]
				{
					new[] { 600.0, 300.0, 100.0 },
					new[] { 600.0, 300.0, 100.0 },
					new[] { 600.0, 300.0, 100.0 },
					new[] { 600.0, 300.0, 100.0 }
				});
			var sixLanes = CreateMatrix(
				new[] { "PEDDLER", "D/S", "CULL" },
				new[]
				{
					new[] { 600.0, 300.0, 100.0 },
					new[] { 600.0, 300.0, 100.0 },
					new[] { 600.0, 300.0, 100.0 },
					new[] { 600.0, 300.0, 100.0 },
					new[] { 600.0, 300.0, 100.0 },
					new[] { 600.0, 300.0, 100.0 }
				});

			detector.Update(fourLanes, DateTimeOffset.Parse("2026-04-24T05:00:00Z"), "140578", 123);
			Assert.AreEqual(4, detector.LaneCount);
			Assert.AreEqual(1, detector.WindowSampleCount);

			detector.Update(sixLanes, DateTimeOffset.Parse("2026-04-24T05:01:00Z"), "140578", 123);
			Assert.AreEqual(6, detector.LaneCount, "Lane dimension should grow to cover new lanes.");
			Assert.AreEqual(2, detector.WindowSampleCount, "Adding lanes must not reset the window.");

			// Lane count must not shrink when a later snapshot reports fewer lanes.
			detector.Update(fourLanes, DateTimeOffset.Parse("2026-04-24T05:02:00Z"), "140578", 123);
			Assert.AreEqual(6, detector.LaneCount, "Canonical lane count must be monotonically non-decreasing.");
			Assert.AreEqual(3, detector.WindowSampleCount);
		}

		[TestMethod]
		public void Update_DetectsLane32SkewAcrossFluctuatingGradeSets()
		{
			// Build a realistic sequence where the grade set changes across snapshots but
			// lane 5's skew (heavy PEDDLER / D/S) remains obvious. This exercises the
			// canonical-dimensions path end-to-end.
			var detector = new AnomalyDetector(CreateConfig(minConsecutiveWindows: 2));

			var keysA = new[] { "PEDDLER", "D/S", "CULL", "E4", "GREEN", "E3" };
			var keysB = new[] { "PEDDLER", "D/S", "CULL", "E4", "GREEN" };       // E3 drops out
			var keysC = new[] { "PEDDLER", "D/S", "CULL", "E4", "GREEN", "E3" }; // E3 returns

			double[][] BalancedLanes(int gradeCount)
			{
				// 4 peer lanes with identical balanced distribution, 1 skewed lane (index 4).
				double[] peerShareByGrade = new[] { 1794.0, 1218.0, 1715.0, 1662.0, 1189.0, 743.0 };
				double[] skewByGrade      = new[] { 6277.0, 3332.0,  390.0,    0.0,    2.0,   0.0 };
				var rows = new double[5][];
				for (int lane = 0; lane < 4; lane++)
				{
					rows[lane] = new double[gradeCount];
					for (int g = 0; g < gradeCount; g++) rows[lane][g] = peerShareByGrade[g];
				}
				rows[4] = new double[gradeCount];
				for (int g = 0; g < gradeCount; g++) rows[4][g] = skewByGrade[g];
				return rows;
			}

			detector.Update(CreateMatrix(keysA, BalancedLanes(keysA.Length)), DateTimeOffset.Parse("2026-04-24T05:00:00Z"), "140578", 123);
			detector.Update(CreateMatrix(keysB, BalancedLanes(keysB.Length)), DateTimeOffset.Parse("2026-04-24T05:01:00Z"), "140578", 123);
			var events = detector.Update(CreateMatrix(keysC, BalancedLanes(keysC.Length)), DateTimeOffset.Parse("2026-04-24T05:02:00Z"), "140578", 123);

			Assert.AreEqual(3, detector.WindowSampleCount, "All three snapshots should be in the rolling window despite grade-set fluctuation.");
			Assert.IsTrue(events.Count >= 1, "Lane 5 skew should still trigger after grade-set fluctuation.");
			Assert.IsTrue(events.Exists(e => e.LaneNo == 5 && e.GradeKey == "PEDDLER"));
		}

		[TestMethod]
		public void Reset_ClearsCanonicalGradeState()
		{
			var detector = new AnomalyDetector(CreateConfig(minConsecutiveWindows: 1));
			detector.Update(BuildSnapshot(new[] { "PEDDLER", "D/S" }, 1.0), DateTimeOffset.UtcNow, "140578", 123);
			Assert.IsTrue(detector.GradeCount > 0);

			detector.Reset();

			Assert.AreEqual(0, detector.GradeCount);
			Assert.AreEqual(0, detector.LaneCount);
			Assert.AreEqual(0, detector.WindowSampleCount);
			Assert.AreEqual(0, detector.GradeKeys.Count);
		}

		private static GradeMatrix BuildSnapshot(string[] gradeKeys, double multiplier)
		{
			// 5 lanes: 4 balanced peers and 1 slightly different lane. Values are arbitrary -
			// these tests only care about window/canonical-state bookkeeping, not triggers.
			var peer = new double[gradeKeys.Length];
			var odd = new double[gradeKeys.Length];
			for (int g = 0; g < gradeKeys.Length; g++)
			{
				peer[g] = (500.0 + g * 50) * multiplier;
				odd[g] = (100.0 + g * 10) * multiplier;
			}
			var rows = new double[5][];
			for (int lane = 0; lane < 4; lane++) rows[lane] = (double[])peer.Clone();
			rows[4] = odd;
			return CreateMatrix(gradeKeys, rows);
		}

		private static AnomalyDetectorConfig CreateConfig(
			int minConsecutiveWindows,
			double minLaneFpm = 150.0,
			double minPeerLaneFpm = 150.0,
			int minActivePeerLanes = 4)
		{
			return new AnomalyDetectorConfig(
				windowMinutes: 5,
				zGate: 2.0,
				bandLowMin: 5.0,
				bandLowMax: 10.0,
				bandMediumMax: 20.0,
				cooldownSeconds: 0,
				recycleGradeKey: "RCY",
				minLaneFpm: minLaneFpm,
				minPeerLaneFpm: minPeerLaneFpm,
				minActivePeerLanes: minActivePeerLanes,
				minConsecutiveWindows: minConsecutiveWindows);
		}

		private static GradeMatrix CreateMatrix(string[] gradeKeys, double[][] lanes)
		{
			return new GradeMatrix(lanes, Array.AsReadOnly(gradeKeys));
		}
	}
}
