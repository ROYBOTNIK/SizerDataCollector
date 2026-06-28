using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SizerDataCollector.Core.AnomalyDetection;

namespace SizerDataCollector.Tests
{
	[TestClass]
	public class MachineEventAnalyzerTests
	{
		[TestMethod]
		public void AnalyzeMinutes_BuildsDowntimeAndSlowdownEventsOutsideLotTransitions()
		{
			var config = new MachineEventConfig(
				evalIntervalMinutes: 15,
				scanWindowHours: 24,
				downtimeMaxAvailabilityRatio: 0.0,
				slowdownMaxThroughputRatio: 0.75,
				slowdownMinAvailabilityRatio: 0.5,
				slowdownMinTotalFpm: 100.0,
				minDurationMinutes: 2,
				mergeGapMinutes: 0,
				excludeLotTransitions: true);
			var analyzer = new MachineEventAnalyzer(config, "Host=localhost");
			var start = new DateTimeOffset(2026, 4, 23, 10, 0, 0, TimeSpan.Zero);
			var minutes = new List<MachineEventAnalyzer.OperationalMinute>
			{
				Minute(start, availability: 0.0, throughput: 0.0, fpm: 0),
				Minute(start.AddMinutes(1), availability: 0.0, throughput: 0.0, fpm: 0),
				Minute(start.AddMinutes(2), availability: 1.0, throughput: 0.70, fpm: 300),
				Minute(start.AddMinutes(3), availability: 1.0, throughput: 0.72, fpm: 310),
				Minute(start.AddMinutes(4), availability: 1.0, throughput: 0.70, fpm: 305)
			};

			var report = analyzer.AnalyzeMinutes("140578", start, start.AddMinutes(5), minutes, new List<MachineEventAnalyzer.TimeWindow>());

			Assert.AreEqual(2, report.DowntimeCandidates);
			Assert.AreEqual(3, report.SlowdownCandidates);
			Assert.AreEqual(1, report.DowntimeEvents.Count);
			Assert.AreEqual(1, report.SlowdownEvents.Count);
			Assert.AreEqual(2.0, report.DowntimeEvents[0].DurationMinutes);
			Assert.AreEqual(3.0, report.SlowdownEvents[0].DurationMinutes);
		}

		[TestMethod]
		public void AnalyzeMinutes_ExcludesLotTransitionWindowsWhenConfigured()
		{
			var config = new MachineEventConfig(15, 24, 0.0, 0.75, 0.5, 100.0, 2, 0, true);
			var analyzer = new MachineEventAnalyzer(config, "Host=localhost");
			var start = new DateTimeOffset(2026, 4, 23, 10, 0, 0, TimeSpan.Zero);
			var minutes = new List<MachineEventAnalyzer.OperationalMinute>
			{
				Minute(start, availability: 1.0, throughput: 0.70, fpm: 300),
				Minute(start.AddMinutes(1), availability: 1.0, throughput: 0.70, fpm: 300),
				Minute(start.AddMinutes(2), availability: 1.0, throughput: 0.70, fpm: 300)
			};
			var windows = new List<MachineEventAnalyzer.TimeWindow>
			{
				new MachineEventAnalyzer.TimeWindow { StartTs = start, EndTs = start.AddMinutes(3) }
			};

			var report = analyzer.AnalyzeMinutes("140578", start, start.AddMinutes(3), minutes, windows);

			Assert.AreEqual(0, report.SlowdownCandidates);
			Assert.AreEqual(0, report.SlowdownEvents.Count);
		}

		[TestMethod]
		public void DatabaseSink_ReplacesShorterEventsWithSameStart()
		{
			var downtimeSql = ReadPrivateSql("DowntimeInsertSql");
			var slowdownSql = ReadPrivateSql("SlowdownInsertSql");

			StringAssert.Contains(downtimeSql, "existing_longer");
			StringAssert.Contains(downtimeSql, "DELETE FROM oee.downtime_events");
			StringAssert.Contains(downtimeSql, "end_ts < @end_ts");
			StringAssert.Contains(downtimeSql, "WHERE NOT EXISTS (SELECT 1 FROM existing_longer)");

			StringAssert.Contains(slowdownSql, "existing_longer");
			StringAssert.Contains(slowdownSql, "DELETE FROM oee.slowdown_events");
			StringAssert.Contains(slowdownSql, "end_ts < @end_ts");
			StringAssert.Contains(slowdownSql, "WHERE NOT EXISTS (SELECT 1 FROM existing_longer)");
		}

		private static MachineEventAnalyzer.OperationalMinute Minute(DateTimeOffset ts, double availability, double throughput, double fpm)
		{
			return new MachineEventAnalyzer.OperationalMinute
			{
				MinuteTs = ts,
				SerialNo = "140578",
				BatchRecordId = 123,
				Lot = "LOT",
				Variety = "VAR",
				AvailabilityRatio = availability,
				ThroughputRatio = throughput,
				OeeScore = availability * throughput,
				TotalFpm = fpm
			};
		}

		private static string ReadPrivateSql(string fieldName)
		{
			var field = typeof(MachineEventDatabaseSink).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Static);
			return field.GetValue(null) as string;
		}
	}
}
