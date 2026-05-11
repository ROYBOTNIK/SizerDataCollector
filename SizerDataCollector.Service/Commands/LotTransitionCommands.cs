using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using Npgsql;
using NpgsqlTypes;
using SizerDataCollector.Core.AnomalyDetection;
using SizerDataCollector.Core.Config;

namespace SizerDataCollector.Service.Commands
{
	internal static class LotTransitionCommands
	{
		private const string ListSql = @"
SELECT transition_ts, serial_no, outgoing_batch_record_id, incoming_batch_record_id,
       outgoing_label, incoming_label, disruption_duration_minutes,
       opportunity_window_minutes, primary_fruit_opportunity_shortfall,
       primary_throughput_loss_ratio, primary_equivalent_lost_minutes,
       fruit_opportunity_shortfall, peak_throughput_loss_ratio, peak_equivalent_lost_minutes,
       target_throughput, target_fruit_opportunity_shortfall, target_equivalent_lost_minutes,
       break_overlap_detected, break_overlap_minutes,
       break_adjusted_stable_fruit_opportunity_shortfall,
       break_adjusted_stable_equivalent_lost_minutes,
       availability_avg_during_disruption, availability_avg_opportunity_window
FROM oee.v_lot_transition_throughput_event_detail
WHERE serial_no = @serial_no
  AND transition_ts >= @from_ts
  AND transition_ts <= @to_ts
ORDER BY transition_ts ASC;";

		public static int Run(string[] args, Dictionary<string, string> options)
		{
			var subCommand = args.Length > 0
				? (args[0] ?? string.Empty).Trim().ToLowerInvariant()
				: string.Empty;

			switch (subCommand)
			{
				case "scan":
					return Scan(options);
				case "list":
					return List(options, csv: false);
				case "export":
					return List(options, csv: true);
				default:
					ShowUsage();
					return 1;
			}
		}

		private static int Scan(Dictionary<string, string> options)
		{
			if (!RequireSerial(options, out var serialNo)) return 1;
			if (!TryResolveWindow(options, out var fromTs, out var toTs, out var error))
			{
				Console.WriteLine(error);
				return 1;
			}

			var config = LoadConfig();
			if (config == null) return 1;

			var lotConfig = new LotTransitionConfig(config);
			var analyzer = new LotTransitionAnalyzer(lotConfig, config.TimescaleConnectionString);
			var report = analyzer.AnalyzeRangeAsync(serialNo, fromTs, toTs, CancellationToken.None)
				.GetAwaiter()
				.GetResult();

			Console.WriteLine($"Lot transition scan for '{serialNo}' from {fromTs:u} to {toTs:u}");
			Console.WriteLine($"Candidates found: {report.TransitionCandidates}; reportable events: {report.Events.Count}");
			Console.WriteLine();

			PrintEvents(report.Events, includeAvailability: true);

			if (options.ContainsKey("no-persist"))
				return 0;

			if (report.Events.Count == 0)
				return 0;

			var sink = new LotTransitionDatabaseSink(config.TimescaleConnectionString);
			var inserted = 0;
			foreach (var evt in report.Events)
			{
				evt.DeliveredTo = "cli";
				if (sink.DeliverAsync(evt, CancellationToken.None).GetAwaiter().GetResult())
					inserted++;
			}

			Console.WriteLine();
			Console.WriteLine($"Persisted {inserted} new lot transition event(s). Existing events were skipped by idempotency key.");
			return 0;
		}

		private static int List(Dictionary<string, string> options, bool csv)
		{
			if (!RequireSerial(options, out var serialNo)) return 1;
			if (!TryResolveWindow(options, out var fromTs, out var toTs, out var error))
			{
				Console.WriteLine(error);
				return 1;
			}

			var config = LoadConfig();
			if (config == null) return 1;

			var rows = QueryRows(config.TimescaleConnectionString, serialNo, fromTs, toTs);
			if (rows.Count == 0)
			{
				Console.WriteLine("No lot transition throughput events found for the requested window.");
				return 0;
			}

			if (csv || (options.TryGetValue("format", out var format) && string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase)))
			{
				PrintCsv(rows);
				return 0;
			}

			Console.WriteLine($"Lot transition throughput events for '{serialNo}' from {fromTs:u} to {toTs:u}");
			Console.WriteLine();
			PrintRows(rows);
			return 0;
		}

		private static CollectorConfig LoadConfig()
		{
			var provider = new CollectorSettingsProvider();
			var runtimeSettings = provider.Load();
			var config = new CollectorConfig(runtimeSettings);
			if (string.IsNullOrWhiteSpace(config.TimescaleConnectionString))
			{
				Console.WriteLine("No TimescaleDB connection string configured. Run 'set-db' first.");
				return null;
			}

			return config;
		}

		private static List<LotTransitionListRow> QueryRows(
			string connectionString,
			string serialNo,
			DateTimeOffset fromTs,
			DateTimeOffset toTs)
		{
			var rows = new List<LotTransitionListRow>();
			using (var connection = new NpgsqlConnection(connectionString))
			{
				connection.Open();
				using (var cmd = new NpgsqlCommand(ListSql, connection))
				{
					cmd.Parameters.Add(new NpgsqlParameter("serial_no", NpgsqlDbType.Text) { Value = serialNo });
					cmd.Parameters.Add(new NpgsqlParameter("from_ts", NpgsqlDbType.TimestampTz) { Value = fromTs.UtcDateTime });
					cmd.Parameters.Add(new NpgsqlParameter("to_ts", NpgsqlDbType.TimestampTz) { Value = toTs.UtcDateTime });

					using (var reader = cmd.ExecuteReader())
					{
						while (reader.Read())
						{
							rows.Add(new LotTransitionListRow
							{
								TransitionTs = reader.GetFieldValue<DateTimeOffset>(0),
								SerialNo = reader.GetString(1),
								OutgoingBatchRecordId = reader.GetInt64(2),
								IncomingBatchRecordId = reader.GetInt64(3),
								OutgoingLabel = reader.IsDBNull(4) ? null : reader.GetString(4),
								IncomingLabel = reader.IsDBNull(5) ? null : reader.GetString(5),
								DisruptionMinutes = reader.GetDouble(6),
								OpportunityMinutes = reader.GetDouble(7),
								PrimaryFruitShortfall = reader.GetDouble(8),
								PrimaryThroughputLossRatio = ReadNullableDouble(reader, 9),
								PrimaryEquivalentLostMinutes = ReadNullableDouble(reader, 10),
								PeakFruitShortfall = reader.GetDouble(11),
								PeakThroughputLossRatio = ReadNullableDouble(reader, 12),
								PeakEquivalentLostMinutes = ReadNullableDouble(reader, 13),
								TargetThroughput = ReadNullableDouble(reader, 14),
								TargetFruitShortfall = ReadNullableDouble(reader, 15),
								TargetEquivalentLostMinutes = ReadNullableDouble(reader, 16),
								BreakOverlapDetected = !reader.IsDBNull(17) && reader.GetBoolean(17),
								BreakOverlapMinutes = ReadNullableDouble(reader, 18),
								BreakAdjustedStableFruitShortfall = ReadNullableDouble(reader, 19),
								BreakAdjustedStableEquivalentLostMinutes = ReadNullableDouble(reader, 20),
								AvailabilityDuringDisruption = ReadNullableDouble(reader, 21),
								AvailabilityDuringOpportunity = ReadNullableDouble(reader, 22)
							});
						}
					}
				}
			}

			return rows;
		}

		private static void PrintEvents(List<LotTransitionEvent> events, bool includeAvailability)
		{
			if (events == null || events.Count == 0)
			{
				Console.WriteLine("No reportable lot transition throughput events found.");
				return;
			}

			Console.WriteLine("Transition           Out Batch In Batch  Lot Transition                 Disrupt  OppWin  AdjLoss Loss%  AdjMin Break PeakLoss TargetLoss");
			Console.WriteLine("------------------- --------- --------- ------------------------------ -------- ------- -------- ------ ------ ----- -------- ----------");
			foreach (var evt in events)
			{
				Console.WriteLine(string.Format(
					CultureInfo.InvariantCulture,
					"{0:u} {1,9} {2,9} {3,-30} {4,7:F1}m {5,6:F1}m {6,8:F0} {7,5} {8,5:F1}m {9,5} {10,8:F0} {11,10}",
					evt.TransitionTs.UtcDateTime,
					evt.OutgoingBatchRecordId,
					evt.IncomingBatchRecordId,
					Trim(BuildTransitionLabel(evt.OutgoingLabel, evt.IncomingLabel), 30),
					evt.DisruptionDurationMinutes,
					evt.OpportunityWindowMinutes,
					evt.BreakAdjustedStableFruitOpportunityShortfall,
					FormatPct(evt.BreakAdjustedStableThroughputLossRatio),
					evt.BreakAdjustedStableEquivalentLostMinutes,
					FormatBreak(evt.BreakOverlapDetected, evt.BreakOverlapMinutes),
					evt.FruitOpportunityShortfall,
					FormatNullableNum(evt.TargetFruitOpportunityShortfall, "F0")));

				if (includeAvailability && (evt.AvailabilityAvgDuringDisruption.HasValue || evt.AvailabilityAvgOpportunityWindow.HasValue))
				{
					Console.WriteLine(string.Format(
						CultureInfo.InvariantCulture,
						"                                      availability disruption={0} opportunity={1}",
						FormatNullablePct(evt.AvailabilityAvgDuringDisruption),
						FormatNullablePct(evt.AvailabilityAvgOpportunityWindow)));
				}
			}
		}

		private static void PrintRows(List<LotTransitionListRow> rows)
		{
			Console.WriteLine("Transition           Out Batch In Batch  Lot Transition                 Disrupt  OppWin  AdjLoss Loss%  AdjMin Break PeakLoss TargetLoss");
			Console.WriteLine("------------------- --------- --------- ------------------------------ -------- ------- -------- ------ ------ ----- -------- ----------");
			foreach (var row in rows)
			{
				Console.WriteLine(string.Format(
					CultureInfo.InvariantCulture,
					"{0:u} {1,9} {2,9} {3,-30} {4,7:F1}m {5,6:F1}m {6,8:F0} {7,5} {8,5}m {9,5} {10,8:F0} {11,10}",
					row.TransitionTs.UtcDateTime,
					row.OutgoingBatchRecordId,
					row.IncomingBatchRecordId,
					Trim(BuildTransitionLabel(row.OutgoingLabel, row.IncomingLabel), 30),
					row.DisruptionMinutes,
					row.OpportunityMinutes,
					row.PrimaryFruitShortfall,
					FormatNullablePct(row.PrimaryThroughputLossRatio),
					FormatNullableNum(row.PrimaryEquivalentLostMinutes, "F1"),
					FormatBreak(row.BreakOverlapDetected, row.BreakOverlapMinutes),
					row.PeakFruitShortfall,
					FormatNullableNum(row.TargetFruitShortfall, "F0")));
			}
		}

		private static void PrintCsv(List<LotTransitionListRow> rows)
		{
			Console.WriteLine("transition_ts,serial_no,outgoing_batch_record_id,incoming_batch_record_id,outgoing_label,incoming_label,disruption_minutes,opportunity_minutes,primary_fruit_opportunity_shortfall,primary_throughput_loss_ratio,primary_equivalent_lost_minutes,peak_fruit_opportunity_shortfall,peak_throughput_loss_ratio,peak_equivalent_lost_minutes,target_throughput,target_fruit_opportunity_shortfall,target_equivalent_lost_minutes,break_overlap_detected,break_overlap_minutes,break_adjusted_stable_fruit_opportunity_shortfall,break_adjusted_stable_equivalent_lost_minutes,availability_disruption,availability_opportunity");
			foreach (var row in rows)
			{
				Console.WriteLine(string.Join(",",
					Csv(row.TransitionTs.UtcDateTime.ToString("u", CultureInfo.InvariantCulture)),
					Csv(row.SerialNo),
					row.OutgoingBatchRecordId.ToString(CultureInfo.InvariantCulture),
					row.IncomingBatchRecordId.ToString(CultureInfo.InvariantCulture),
					Csv(row.OutgoingLabel),
					Csv(row.IncomingLabel),
					row.DisruptionMinutes.ToString("F3", CultureInfo.InvariantCulture),
					row.OpportunityMinutes.ToString("F3", CultureInfo.InvariantCulture),
					row.PrimaryFruitShortfall.ToString("F0", CultureInfo.InvariantCulture),
					FormatNullable(row.PrimaryThroughputLossRatio),
					FormatNullable(row.PrimaryEquivalentLostMinutes),
					row.PeakFruitShortfall.ToString("F0", CultureInfo.InvariantCulture),
					FormatNullable(row.PeakThroughputLossRatio),
					FormatNullable(row.PeakEquivalentLostMinutes),
					FormatNullable(row.TargetThroughput),
					FormatNullable(row.TargetFruitShortfall),
					FormatNullable(row.TargetEquivalentLostMinutes),
					row.BreakOverlapDetected ? "true" : "false",
					FormatNullable(row.BreakOverlapMinutes),
					FormatNullable(row.BreakAdjustedStableFruitShortfall),
					FormatNullable(row.BreakAdjustedStableEquivalentLostMinutes),
					FormatNullable(row.AvailabilityDuringDisruption),
					FormatNullable(row.AvailabilityDuringOpportunity)));
			}
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
			fromTs = default(DateTimeOffset);
			toTs = default(DateTimeOffset);
			error = null;

			if (options.TryGetValue("from", out var fromRaw) &&
				DateTimeOffset.TryParse(fromRaw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out fromTs) &&
				options.TryGetValue("to", out var toRaw) &&
				DateTimeOffset.TryParse(toRaw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out toTs))
			{
				return ValidateWindow(fromTs, toTs, out error);
			}

			if (options.TryGetValue("day", out var dayRaw))
			{
				if (!DateTime.TryParseExact(dayRaw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var day))
				{
					error = "Invalid option: --day <yyyy-MM-dd>";
					return false;
				}

				fromTs = new DateTimeOffset(DateTime.SpecifyKind(day.Date, DateTimeKind.Utc));
				toTs = fromTs.AddDays(1);
				return true;
			}

			if (options.TryGetValue("month", out var monthRaw))
			{
				if (!DateTime.TryParseExact(monthRaw, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var month))
				{
					error = "Invalid option: --month <yyyy-MM>";
					return false;
				}

				fromTs = new DateTimeOffset(DateTime.SpecifyKind(new DateTime(month.Year, month.Month, 1), DateTimeKind.Utc));
				toTs = fromTs.AddMonths(1);
				return true;
			}

			if (options.TryGetValue("year", out var yearRaw))
			{
				if (!int.TryParse(yearRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var year) || year < 2000 || year > 2100)
				{
					error = "Invalid option: --year <yyyy>";
					return false;
				}

				fromTs = new DateTimeOffset(new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc));
				toTs = fromTs.AddYears(1);
				return true;
			}

			var hours = 24;
			if (options.TryGetValue("hours", out var hoursRaw) &&
				(!int.TryParse(hoursRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out hours) || hours < 1))
			{
				error = "Invalid option: --hours <integer>";
				return false;
			}

			toTs = DateTimeOffset.UtcNow;
			fromTs = toTs.AddHours(-hours);
			return true;
		}

		private static bool ValidateWindow(DateTimeOffset fromTs, DateTimeOffset toTs, out string error)
		{
			if (toTs <= fromTs)
			{
				error = "--to must be after --from";
				return false;
			}

			error = null;
			return true;
		}

		private static string BuildTransitionLabel(string outgoing, string incoming)
		{
			return string.Format("{0} -> {1}",
				string.IsNullOrWhiteSpace(outgoing) ? "unknown" : outgoing,
				string.IsNullOrWhiteSpace(incoming) ? "unknown" : incoming);
		}

		private static string FormatNullablePct(double? value)
		{
			return value.HasValue
				? string.Format(CultureInfo.InvariantCulture, "{0:F1}%", value.Value * 100.0)
				: "-";
		}

		private static string FormatNullable(double? value)
		{
			return value.HasValue ? value.Value.ToString("F6", CultureInfo.InvariantCulture) : string.Empty;
		}

		private static double? ReadNullableDouble(NpgsqlDataReader reader, int index)
		{
			return reader.IsDBNull(index) ? (double?)null : reader.GetDouble(index);
		}

		private static string FormatPct(double value)
		{
			return string.Format(CultureInfo.InvariantCulture, "{0:F1}%", value * 100.0);
		}

		private static string FormatNullableNum(double? value, string format)
		{
			return value.HasValue ? value.Value.ToString(format, CultureInfo.InvariantCulture) : "-";
		}

		private static string FormatBreak(bool detected, double? minutes)
		{
			if (!detected)
				return "-";

			return minutes.HasValue
				? string.Format(CultureInfo.InvariantCulture, "{0:F0}m", minutes.Value)
				: "yes";
		}

		private static string Trim(string value, int length)
		{
			if (string.IsNullOrEmpty(value)) return string.Empty;
			return value.Length <= length ? value : value.Substring(0, length);
		}

		private static string Csv(string value)
		{
			value = value ?? string.Empty;
			return "\"" + value.Replace("\"", "\"\"") + "\"";
		}

		private static void ShowUsage()
		{
			Console.WriteLine("Lot transition commands:");
			Console.WriteLine("  SizerDataCollector.Service.exe lot-transition scan --serial <sn> [--from <dt> --to <dt> | --hours <h> | --day <yyyy-MM-dd> | --month <yyyy-MM> | --year <yyyy>] [--no-persist]");
			Console.WriteLine("  SizerDataCollector.Service.exe lot-transition list --serial <sn> [--from <dt> --to <dt> | --hours <h> | --day <yyyy-MM-dd> | --month <yyyy-MM> | --year <yyyy>] [--format csv]");
			Console.WriteLine("  SizerDataCollector.Service.exe lot-transition export --serial <sn> [same window options]");
		}

		private sealed class LotTransitionListRow
		{
			public DateTimeOffset TransitionTs { get; set; }
			public string SerialNo { get; set; }
			public long OutgoingBatchRecordId { get; set; }
			public long IncomingBatchRecordId { get; set; }
			public string OutgoingLabel { get; set; }
			public string IncomingLabel { get; set; }
			public double DisruptionMinutes { get; set; }
			public double OpportunityMinutes { get; set; }
			public double PrimaryFruitShortfall { get; set; }
			public double? PrimaryThroughputLossRatio { get; set; }
			public double? PrimaryEquivalentLostMinutes { get; set; }
			public double PeakFruitShortfall { get; set; }
			public double? PeakThroughputLossRatio { get; set; }
			public double? PeakEquivalentLostMinutes { get; set; }
			public double? TargetThroughput { get; set; }
			public double? TargetFruitShortfall { get; set; }
			public double? TargetEquivalentLostMinutes { get; set; }
			public bool BreakOverlapDetected { get; set; }
			public double? BreakOverlapMinutes { get; set; }
			public double? BreakAdjustedStableFruitShortfall { get; set; }
			public double? BreakAdjustedStableEquivalentLostMinutes { get; set; }
			public double? AvailabilityDuringDisruption { get; set; }
			public double? AvailabilityDuringOpportunity { get; set; }
		}
	}
}
