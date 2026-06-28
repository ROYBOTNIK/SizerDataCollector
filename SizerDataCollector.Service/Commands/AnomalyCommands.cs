using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using SizerDataCollector.Core.Config;
using SizerDataCollector.Core.Db;

namespace SizerDataCollector.Service.Commands
{
	internal static class AnomalyCommands
	{
		public static int Run(string[] args, Dictionary<string, string> options)
		{
			var subCommand = args.Length > 0
				? (args[0] ?? string.Empty).Trim().ToLowerInvariant()
				: string.Empty;

			switch (subCommand)
			{
				case "offenders":
					return Offenders(options);
				case "impact":
					return Impact(options);
				case "impact-summary":
					return ImpactSummary(options);
				case "tuning-compare":
					return TuningCompare(options);
				default:
					ShowUsage();
					return 1;
			}
		}

		private static int Offenders(Dictionary<string, string> options)
		{
			if (!RequireSerial(options, out var serialNo)) return 1;
			if (!TryResolveWindow(options, out var fromTs, out var toTs, out var error))
			{
				Console.WriteLine(error);
				return 1;
			}

			var config = LoadConfig();
			if (config == null) return 1;

			var repo = new AnomalyReportingRepository(config.TimescaleConnectionString);
			var rows = repo.GetOffendersAsync(
				NormalizeType(options),
				serialNo,
				fromTs,
				toTs,
				ParseLimit(options, 10),
				CancellationToken.None).GetAwaiter().GetResult();

			if (rows.Count == 0)
			{
				Console.WriteLine("No anomaly offenders found for the requested window.");
				return 0;
			}

			Console.WriteLine($"Anomaly offenders for '{serialNo}' from {fromTs:u} to {toTs:u}");
			Console.WriteLine();
			Console.WriteLine("Type  Lane Batch Scope                    Repeats High Med Low MaxPct  MaxZ   First Seen           Last Seen");
			Console.WriteLine("----  ---- ----- ------------------------ ------- ---- --- --- ------- ------ ------------------- -------------------");

			foreach (var row in rows)
			{
				var scope = row.AnomalyType == "grade"
					? (row.GradeKey ?? string.Empty)
					: string.Format("{0}h window", row.WindowHours ?? 0);
				Console.WriteLine(string.Format(
					"{0,-4}  {1,4} {2,5} {3,-24} {4,7} {5,4} {6,3} {7,3} {8,7} {9,6} {10:u} {11:u}",
					row.AnomalyType,
					row.LaneNo,
					FormatBatch(row.BatchRecordId),
					Trim(scope, 24),
					row.RepeatCount,
					row.HighCount,
					row.MediumCount,
					row.LowCount,
					FormatDouble(row.MaxAbsPct, 1),
					FormatDouble(row.MaxAbsZ, 1),
					row.FirstEventTs.UtcDateTime,
					row.LastEventTs.UtcDateTime));
				Console.WriteLine(string.Format(
					"                 cluster dir={0} avgPct={1} activeMin={2} spanMin={3} batches={4} lots={5} runtime={6}",
					row.DirectionLabel ?? "balanced",
					FormatSignedDouble(row.AvgScorePct, 2, true),
					row.ActiveMinutes,
					FormatDouble(row.SpanMinutes, 1),
					row.AffectedBatches,
					row.AffectedLots,
					FormatSignedDouble(row.RuntimeSharePct, 2, true)));
			}

			return 0;
		}

		private static int ImpactSummary(Dictionary<string, string> options)
		{
			if (!RequireSerial(options, out var serialNo)) return 1;
			if (!TryResolveWindow(options, out var fromTs, out var toTs, out var error))
			{
				Console.WriteLine(error);
				return 1;
			}

			var config = LoadConfig();
			if (config == null) return 1;

			var repo = new AnomalyReportingRepository(config.TimescaleConnectionString);
			var rows = repo.GetImpactFamilySummaryAsync(
				NormalizeType(options),
				serialNo,
				fromTs,
				toTs,
				ParseLimit(options, 25),
				CancellationToken.None).GetAwaiter().GetResult();

			if (rows.Count == 0)
			{
				Console.WriteLine("No aggregate impact families found for the requested window.");
				return 0;
			}

			Console.WriteLine($"Anomaly impact summary for '{serialNo}' from {fromTs:u} to {toTs:u}");
			Console.WriteLine();
			Console.WriteLine("Top anomaly families by average post-event OEE drop:");
			PrintImpactFamilyRows(rows
				.OrderBy(r => r.AvgDeltaOeePostVsPre ?? double.MaxValue)
				.ThenBy(r => r.AvgDeltaThroughputPostVsPre ?? double.MaxValue)
				.ThenByDescending(r => r.EventCount)
				.Take(ParseLimit(options, 10)));
			Console.WriteLine();
			Console.WriteLine("Top anomaly families by average post-event throughput drop:");
			PrintImpactFamilyRows(rows
				.OrderBy(r => r.AvgDeltaThroughputPostVsPre ?? double.MaxValue)
				.ThenBy(r => r.AvgDeltaOeePostVsPre ?? double.MaxValue)
				.ThenByDescending(r => r.EventCount)
				.Take(ParseLimit(options, 10)));
			Console.WriteLine();
			Console.WriteLine("Most frequent high-severity families with negative post impact:");
			PrintImpactFamilyRows(rows
				.OrderByDescending(r => r.HighSeverityNegativeCount)
				.ThenBy(r => r.AvgDeltaOeePostVsPre ?? double.MaxValue)
				.ThenByDescending(r => r.HighCount)
				.ThenByDescending(r => r.EventCount)
				.Take(ParseLimit(options, 10)));
			return 0;
		}

		private static int Impact(Dictionary<string, string> options)
		{
			if (!RequireSerial(options, out var serialNo)) return 1;
			if (!TryResolveWindow(options, out var fromTs, out var toTs, out var error))
			{
				Console.WriteLine(error);
				return 1;
			}

			var config = LoadConfig();
			if (config == null) return 1;

			var repo = new AnomalyReportingRepository(config.TimescaleConnectionString);
			var rows = repo.GetImpactAsync(
				NormalizeType(options),
				serialNo,
				fromTs,
				toTs,
				ParseLimit(options, 10),
				CancellationToken.None).GetAwaiter().GetResult();

			if (rows.Count == 0)
			{
				Console.WriteLine("No anomaly impact rows found for the requested window.");
				return 0;
			}

			Console.WriteLine($"Anomaly impact for '{serialNo}' from {fromTs:u} to {toTs:u}");
			Console.WriteLine();

			foreach (var row in rows)
			{
				var scope = row.AnomalyType == "grade"
					? row.GradeKey
					: string.Format("{0}h size", row.WindowHours ?? 0);
				Console.WriteLine(string.Format(
					"{0:u} [{1}/{2}] lane {3} {4} score={5} z={6}",
					row.EventTs.UtcDateTime,
					row.AnomalyType,
					row.Severity,
					row.LaneNo,
					scope ?? string.Empty,
					FormatSignedDouble(row.ScorePct, 1, true),
					FormatSignedDouble(row.ScoreZ, 1, false)));
				Console.WriteLine(string.Format(
					"  OEE {0} -> {1} -> {2}  delta(pre)={3} delta(post)={4}",
					FormatDouble(row.PreOeeScore, 3),
					FormatDouble(row.EventOeeScore, 3),
					FormatDouble(row.PostOeeScore, 3),
					FormatSignedDouble(row.DeltaOeeFromPre, 3, false),
					FormatSignedDouble(row.DeltaOeePostVsPre, 3, false)));
				Console.WriteLine(string.Format(
					"  Throughput {0} -> {1} -> {2}  Quality {3} -> {4} -> {5}",
					FormatDouble(row.PreThroughputRatio, 3),
					FormatDouble(row.EventThroughputRatio, 3),
					FormatDouble(row.PostThroughputRatio, 3),
					FormatDouble(row.PreQualityRatio, 3),
					FormatDouble(row.EventQualityRatio, 3),
					FormatDouble(row.PostQualityRatio, 3)));
				Console.WriteLine(string.Format(
					"  FPM={0} recycle={1} cupfill={2} tph={3} batch={4} lot={5} variety={6}",
					FormatDouble(row.EventTotalFpm, 1),
					FormatDouble(row.EventCombinedRecycleFpm, 1),
					FormatDouble(row.EventCupfillPct, 1),
					FormatDouble(row.EventTph, 1),
					row.BatchRecordId.HasValue ? row.BatchRecordId.Value.ToString(CultureInfo.InvariantCulture) : "-",
					row.Lot ?? "-",
					row.Variety ?? "-"));
				Console.WriteLine();
			}

			return 0;
		}

		private static int TuningCompare(Dictionary<string, string> options)
		{
			if (!RequireSerial(options, out var serialNo)) return 1;
			if (!TryResolveRange(options, "baseline-from", "baseline-to", out var baselineFromTs, out var baselineToTs, out var baselineError))
			{
				Console.WriteLine(baselineError);
				return 1;
			}
			if (!TryResolveRange(options, "candidate-from", "candidate-to", out var candidateFromTs, out var candidateToTs, out var candidateError))
			{
				Console.WriteLine(candidateError);
				return 1;
			}

			var config = LoadConfig();
			if (config == null) return 1;

			var repo = new AnomalyReportingRepository(config.TimescaleConnectionString);
			var report = repo.GetTuningComparisonAsync(
				NormalizeType(options),
				serialNo,
				baselineFromTs,
				baselineToTs,
				candidateFromTs,
				candidateToTs,
				ParseLimit(options, 5),
				CancellationToken.None).GetAwaiter().GetResult();

			Console.WriteLine($"Tuning comparison for '{serialNo}' ({DisplayType(NormalizeType(options))})");
			Console.WriteLine();
			PrintWindowSummary(report.Baseline);
			PrintWindowSummary(report.Candidate);
			Console.WriteLine(string.Format(
				"Delta events: {0:+#;-#;0}  Delta high: {1:+#;-#;0}  Delta max score: {2}",
				report.Candidate.TotalEvents - report.Baseline.TotalEvents,
				report.Candidate.HighCount - report.Baseline.HighCount,
				FormatSignedDouble(
					(report.Candidate.MaxAbsPct ?? 0) - (report.Baseline.MaxAbsPct ?? 0),
					1,
					true)));
			Console.WriteLine();
			Console.WriteLine("Baseline top offenders:");
			PrintTopOffenders(report.BaselineTopOffenders);
			Console.WriteLine();
			Console.WriteLine("Candidate top offenders:");
			PrintTopOffenders(report.CandidateTopOffenders);
			return 0;
		}

		private static void PrintWindowSummary(AnomalyWindowSummary summary)
		{
			Console.WriteLine(string.Format(
				"{0}: {1:u} -> {2:u}",
				summary.Label,
				summary.FromTs.UtcDateTime,
				summary.ToTs.UtcDateTime));
			Console.WriteLine(string.Format(
				"  events={0} high={1} medium={2} low={3} maxScore={4} maxZ={5}",
				summary.TotalEvents,
				summary.HighCount,
				summary.MediumCount,
				summary.LowCount,
				FormatDouble(summary.MaxAbsPct, 1),
				FormatDouble(summary.MaxAbsZ, 1)));
		}

		private static void PrintTopOffenders(IReadOnlyList<AnomalyOffenderRow> rows)
		{
			if (rows == null || rows.Count == 0)
			{
				Console.WriteLine("  (none)");
				return;
			}

			foreach (var row in rows)
			{
				var scope = row.AnomalyType == "grade"
					? row.GradeKey
					: string.Format("{0}h window", row.WindowHours ?? 0);
				Console.WriteLine(string.Format(
					"  {0} lane {1} {2}: repeats={3} high={4} maxScore={5}",
					row.AnomalyType,
					row.LaneNo,
					scope ?? string.Empty,
					row.RepeatCount,
					row.HighCount,
					FormatDouble(row.MaxAbsPct, 1)));
			}
		}

		private static void PrintImpactFamilyRows(IEnumerable<AnomalyImpactFamilyRow> rows)
		{
			var any = false;
			foreach (var row in rows)
			{
				any = true;
				var scope = row.AnomalyType == "grade"
					? row.GradeKey
					: string.Format("{0}h window", row.WindowHours ?? 0);
				Console.WriteLine(string.Format(
					"  {0} lane {1} {2}: events={3} high={4} avgPostOee={5} avgPostThr={6} negPost={7} highNeg={8} class={9}",
					row.AnomalyType,
					row.LaneNo,
					scope ?? string.Empty,
					row.EventCount,
					row.HighCount,
					FormatSignedDouble(row.AvgDeltaOeePostVsPre, 3, false),
					FormatSignedDouble(row.AvgDeltaThroughputPostVsPre, 3, false),
					row.NegativePostImpactCount,
					row.HighSeverityNegativeCount,
					row.MaterialityLabel ?? "mixed_unclear"));
			}

			if (!any)
				Console.WriteLine("  (none)");
		}

		private static bool RequireSerial(Dictionary<string, string> options, out string serial)
		{
			if (options.TryGetValue("serial", out serial) && !string.IsNullOrWhiteSpace(serial))
			{
				serial = serial.Trim();
				return true;
			}

			Console.WriteLine("Missing required option: --serial <serial_no>");
			serial = null;
			return false;
		}

		private static bool TryResolveWindow(
			Dictionary<string, string> options,
			out DateTimeOffset fromTs,
			out DateTimeOffset toTs,
			out string error)
		{
			if (TryResolveRange(options, "from", "to", out fromTs, out toTs, out error))
				return true;

			int hours = 24;
			if (options.TryGetValue("hours", out var hoursRaw) &&
				(!int.TryParse(hoursRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out hours) || hours < 1))
			{
				error = "Invalid option: --hours <integer>";
				return false;
			}

			toTs = DateTimeOffset.UtcNow;
			fromTs = toTs.AddHours(-hours);
			error = null;
			return true;
		}

		private static bool TryResolveRange(
			Dictionary<string, string> options,
			string fromKey,
			string toKey,
			out DateTimeOffset fromTs,
			out DateTimeOffset toTs,
			out string error)
		{
			fromTs = default(DateTimeOffset);
			toTs = default(DateTimeOffset);

			if (!options.TryGetValue(fromKey, out var fromRaw) ||
				!DateTimeOffset.TryParse(fromRaw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out fromTs))
			{
				error = string.Format("Missing or invalid option: --{0} <datetime>", fromKey);
				return false;
			}

			if (!options.TryGetValue(toKey, out var toRaw) ||
				!DateTimeOffset.TryParse(toRaw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out toTs))
			{
				error = string.Format("Missing or invalid option: --{0} <datetime>", toKey);
				return false;
			}

			if (toTs <= fromTs)
			{
				error = string.Format("--{0} must be later than --{1}", toKey, fromKey);
				return false;
			}

			error = null;
			return true;
		}

		private static int ParseLimit(Dictionary<string, string> options, int fallback)
		{
			if (options.TryGetValue("limit", out var raw) &&
				int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) &&
				value > 0)
				return value;

			return fallback;
		}

		private static string NormalizeType(Dictionary<string, string> options)
		{
			if (!options.TryGetValue("type", out var raw) || string.IsNullOrWhiteSpace(raw))
				return "both";

			var value = raw.Trim().ToLowerInvariant();
			return value == "grade" || value == "size" || value == "both" ? value : "both";
		}

		private static string DisplayType(string type)
		{
			return string.IsNullOrWhiteSpace(type) || type == "both" ? "grade+size" : type;
		}

		private static CollectorConfig LoadConfig()
		{
			try
			{
				var provider = new CollectorSettingsProvider();
				var settings = provider.Load();
				var config = new CollectorConfig(settings);

				if (string.IsNullOrWhiteSpace(config.TimescaleConnectionString))
				{
					Console.WriteLine("No database connection string configured. Run 'set-db' first.");
					return null;
				}

				return config;
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Failed to load configuration: {ex.Message}");
				return null;
			}
		}

		private static string FormatDouble(double? value, int decimals)
		{
			return value.HasValue
				? value.Value.ToString("F" + decimals, CultureInfo.InvariantCulture)
				: "-";
		}

		private static string FormatSignedDouble(double? value, int decimals, bool includePercent)
		{
			if (!value.HasValue) return "-";
			var suffix = includePercent ? "%" : string.Empty;
			return value.Value.ToString("+#0." + new string('0', decimals) + ";-#0." + new string('0', decimals) + ";0." + new string('0', decimals), CultureInfo.InvariantCulture) + suffix;
		}

		private static string Trim(string value, int length)
		{
			if (string.IsNullOrEmpty(value) || value.Length <= length) return value;
			return value.Substring(0, length - 3) + "...";
		}

		private static string FormatBatch(long? batchRecordId)
		{
			return batchRecordId.HasValue
				? batchRecordId.Value.ToString(CultureInfo.InvariantCulture)
				: "-";
		}

		private static void ShowUsage()
		{
			Console.WriteLine("Usage: SizerDataCollector.Service.exe anomaly <command> [options]");
			Console.WriteLine("Commands:");
			Console.WriteLine("  offenders       --serial <sn> [--type grade|size|both] [--hours <h> | --from <dt> --to <dt>] [--limit <n>]");
			Console.WriteLine("  impact          --serial <sn> [--type grade|size|both] [--hours <h> | --from <dt> --to <dt>] [--limit <n>]");
			Console.WriteLine("  impact-summary  --serial <sn> [--type grade|size|both] [--hours <h> | --from <dt> --to <dt>] [--limit <n>]");
			Console.WriteLine("  tuning-compare  --serial <sn> [--type grade|size|both]");
			Console.WriteLine("                  --baseline-from <dt> --baseline-to <dt> --candidate-from <dt> --candidate-to <dt> [--limit <n>]");
		}
	}
}
