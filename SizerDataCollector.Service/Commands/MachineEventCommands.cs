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
	internal static class MachineEventCommands
	{
		private const string DowntimeListSql = @"
WITH latest AS (
    SELECT DISTINCT ON (serial_no, start_ts)
           start_ts, end_ts, duration_minutes, serial_no, batch_record_id, lot, variety,
           avg_availability_ratio, min_availability_ratio, avg_throughput_ratio, min_throughput_ratio,
           avg_total_fpm, min_total_fpm, avg_oee_score, reason, overlaps_lot_transition
    FROM oee.downtime_events
    WHERE serial_no = @serial_no AND start_ts >= @from_ts AND start_ts <= @to_ts
    ORDER BY serial_no, start_ts, end_ts DESC, detected_at DESC
)
SELECT start_ts, end_ts, duration_minutes, serial_no, batch_record_id, lot, variety,
       avg_availability_ratio, min_availability_ratio, avg_throughput_ratio, min_throughput_ratio,
       avg_total_fpm, min_total_fpm, avg_oee_score, reason, overlaps_lot_transition
FROM latest
ORDER BY start_ts ASC;";

		private const string SlowdownListSql = @"
WITH latest AS (
    SELECT DISTINCT ON (serial_no, start_ts)
           start_ts, end_ts, duration_minutes, serial_no, batch_record_id, lot, variety,
           avg_availability_ratio, min_availability_ratio, avg_throughput_ratio, min_throughput_ratio,
           avg_total_fpm, min_total_fpm, avg_oee_score, reason, overlaps_lot_transition
    FROM oee.slowdown_events
    WHERE serial_no = @serial_no AND start_ts >= @from_ts AND start_ts <= @to_ts
    ORDER BY serial_no, start_ts, end_ts DESC, detected_at DESC
)
SELECT start_ts, end_ts, duration_minutes, serial_no, batch_record_id, lot, variety,
       avg_availability_ratio, min_availability_ratio, avg_throughput_ratio, min_throughput_ratio,
       avg_total_fpm, min_total_fpm, avg_oee_score, reason, overlaps_lot_transition
FROM latest
ORDER BY start_ts ASC;";

		public static int Run(string[] args, Dictionary<string, string> options, string forcedType = null)
		{
			var subCommand = args.Length > 0 ? (args[0] ?? string.Empty).Trim().ToLowerInvariant() : string.Empty;
			if (!string.IsNullOrWhiteSpace(forcedType))
				options["type"] = forcedType;

			switch (subCommand)
			{
				case "scan":
					return Scan(options);
				case "list":
					return List(options, csv: false);
				case "export":
					return List(options, csv: true);
				default:
					ShowUsage(forcedType);
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

			var type = ResolveType(options);
			var eventConfig = new MachineEventConfig(config);
			var analyzer = new MachineEventAnalyzer(eventConfig, config.TimescaleConnectionString);
			var report = analyzer.AnalyzeRangeAsync(serialNo, fromTs, toTs, CancellationToken.None).GetAwaiter().GetResult();

			Console.WriteLine($"Machine event scan for '{serialNo}' from {fromTs:u} to {toTs:u}");
			Console.WriteLine($"Operational minutes: {report.MinuteCount}; downtime candidate minutes: {report.DowntimeCandidates}; slowdown candidate minutes: {report.SlowdownCandidates}");
			Console.WriteLine();

			var events = SelectEvents(report, type);
			PrintEvents(events);

			if (options.ContainsKey("no-persist") || events.Count == 0)
				return 0;

			var sink = new MachineEventDatabaseSink(config.TimescaleConnectionString);
			var inserted = 0;
			foreach (var evt in events)
			{
				evt.DeliveredTo = "cli";
				if (sink.DeliverAsync(evt, CancellationToken.None).GetAwaiter().GetResult())
					inserted++;
			}

			Console.WriteLine();
			Console.WriteLine($"Persisted {inserted} new machine event(s). Existing events were skipped by idempotency key.");
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

			var rows = new List<MachineEvent>();
			var type = ResolveType(options);
			if (type == "downtime" || type == "both") rows.AddRange(QueryRows(config.TimescaleConnectionString, serialNo, fromTs, toTs, "downtime"));
			if (type == "slowdown" || type == "both") rows.AddRange(QueryRows(config.TimescaleConnectionString, serialNo, fromTs, toTs, "slowdown"));
			rows = rows.OrderBy(r => r.StartTs).ThenBy(r => r.EventType).ToList();

			if (rows.Count == 0)
			{
				Console.WriteLine("No machine downtime/slowdown events found for the requested window.");
				return 0;
			}

			if (csv || (options.TryGetValue("format", out var format) && string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase)))
			{
				PrintCsv(rows);
				return 0;
			}

			Console.WriteLine($"Machine events for '{serialNo}' from {fromTs:u} to {toTs:u}");
			Console.WriteLine();
			PrintEvents(rows);
			return 0;
		}

		private static List<MachineEvent> QueryRows(string connectionString, string serialNo, DateTimeOffset fromTs, DateTimeOffset toTs, string type)
		{
			var rows = new List<MachineEvent>();
			using (var connection = new NpgsqlConnection(connectionString))
			{
				connection.Open();
				using (var cmd = new NpgsqlCommand(type == "downtime" ? DowntimeListSql : SlowdownListSql, connection))
				{
					cmd.Parameters.Add(new NpgsqlParameter("serial_no", NpgsqlDbType.Text) { Value = serialNo });
					cmd.Parameters.Add(new NpgsqlParameter("from_ts", NpgsqlDbType.TimestampTz) { Value = fromTs.UtcDateTime });
					cmd.Parameters.Add(new NpgsqlParameter("to_ts", NpgsqlDbType.TimestampTz) { Value = toTs.UtcDateTime });

					using (var reader = cmd.ExecuteReader())
					{
						while (reader.Read())
						{
							rows.Add(new MachineEvent
							{
								EventType = type,
								StartTs = reader.GetFieldValue<DateTimeOffset>(0),
								EndTs = reader.GetFieldValue<DateTimeOffset>(1),
								DurationMinutes = reader.GetDouble(2),
								SerialNo = reader.GetString(3),
								BatchRecordId = reader.IsDBNull(4) ? (long?)null : Convert.ToInt64(reader.GetValue(4), CultureInfo.InvariantCulture),
								Lot = reader.IsDBNull(5) ? null : reader.GetString(5),
								Variety = reader.IsDBNull(6) ? null : reader.GetString(6),
								AvgAvailabilityRatio = ReadNullableDouble(reader, 7),
								MinAvailabilityRatio = ReadNullableDouble(reader, 8),
								AvgThroughputRatio = ReadNullableDouble(reader, 9),
								MinThroughputRatio = ReadNullableDouble(reader, 10),
								AvgTotalFpm = ReadNullableDouble(reader, 11),
								MinTotalFpm = ReadNullableDouble(reader, 12),
								AvgOeeScore = ReadNullableDouble(reader, 13),
								Reason = reader.IsDBNull(14) ? null : reader.GetString(14),
								OverlapsLotTransition = !reader.IsDBNull(15) && reader.GetBoolean(15)
							});
						}
					}
				}
			}

			return rows;
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

		private static List<MachineEvent> SelectEvents(MachineEventReport report, string type)
		{
			var events = new List<MachineEvent>();
			if (type == "downtime" || type == "both") events.AddRange(report.DowntimeEvents);
			if (type == "slowdown" || type == "both") events.AddRange(report.SlowdownEvents);
			return events.OrderBy(e => e.StartTs).ThenBy(e => e.EventType).ToList();
		}

		private static string ResolveType(Dictionary<string, string> options)
		{
			if (options.TryGetValue("type", out var raw))
			{
				var type = (raw ?? string.Empty).Trim().ToLowerInvariant();
				if (type == "downtime" || type == "slowdown" || type == "both") return type;
			}

			return "both";
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

		private static bool TryResolveWindow(Dictionary<string, string> options, out DateTimeOffset fromTs, out DateTimeOffset toTs, out string error)
		{
			fromTs = default(DateTimeOffset);
			toTs = default(DateTimeOffset);
			error = null;

			if (options.TryGetValue("from", out var fromRaw) && DateTimeOffset.TryParse(fromRaw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out fromTs) &&
				options.TryGetValue("to", out var toRaw) && DateTimeOffset.TryParse(toRaw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out toTs))
				return ValidateWindow(fromTs, toTs, out error);

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
			if (options.TryGetValue("hours", out var hoursRaw) && (!int.TryParse(hoursRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out hours) || hours < 1))
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

		private static double? ReadNullableDouble(NpgsqlDataReader reader, int index)
		{
			return reader.IsDBNull(index) ? (double?)null : Convert.ToDouble(reader.GetValue(index), CultureInfo.InvariantCulture);
		}

		private static void PrintEvents(List<MachineEvent> events)
		{
			if (events.Count == 0)
			{
				Console.WriteLine("No reportable machine events found.");
				return;
			}

			Console.WriteLine("Type      Start                End                  Duration  Batch     AvgAvail  AvgThrpt  AvgFPM  Reason");
			foreach (var evt in events)
			{
				Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
					"{0,-9} {1:u} {2:u} {3,8:F1} {4,8} {5,8} {6,8} {7,7}  {8}",
					evt.EventType,
					evt.StartTs,
					evt.EndTs,
					evt.DurationMinutes,
					evt.BatchRecordId.HasValue ? evt.BatchRecordId.Value.ToString(CultureInfo.InvariantCulture) : "mixed",
					FormatPct(evt.AvgAvailabilityRatio),
					FormatPct(evt.AvgThroughputRatio),
					FormatNum(evt.AvgTotalFpm),
					evt.Reason));
			}
		}

		private static void PrintCsv(List<MachineEvent> rows)
		{
			Console.WriteLine("event_type,start_ts,end_ts,duration_minutes,serial_no,batch_record_id,lot,variety,avg_availability_ratio,min_availability_ratio,avg_throughput_ratio,min_throughput_ratio,avg_total_fpm,min_total_fpm,avg_oee_score,reason,overlaps_lot_transition");
			foreach (var row in rows)
			{
				Console.WriteLine(string.Join(",", new[]
				{
					Csv(row.EventType), row.StartTs.ToString("O", CultureInfo.InvariantCulture), row.EndTs.ToString("O", CultureInfo.InvariantCulture),
					row.DurationMinutes.ToString("F3", CultureInfo.InvariantCulture), Csv(row.SerialNo), row.BatchRecordId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
					Csv(row.Lot), Csv(row.Variety), Nullable(row.AvgAvailabilityRatio), Nullable(row.MinAvailabilityRatio), Nullable(row.AvgThroughputRatio),
					Nullable(row.MinThroughputRatio), Nullable(row.AvgTotalFpm), Nullable(row.MinTotalFpm), Nullable(row.AvgOeeScore), Csv(row.Reason), row.OverlapsLotTransition.ToString()
				}));
			}
		}

		private static string FormatPct(double? value) => value.HasValue ? string.Format(CultureInfo.InvariantCulture, "{0:F1}%", value.Value * 100.0) : "-";
		private static string FormatNum(double? value) => value.HasValue ? value.Value.ToString("F1", CultureInfo.InvariantCulture) : "-";
		private static string Nullable(double? value) => value.HasValue ? value.Value.ToString("F6", CultureInfo.InvariantCulture) : string.Empty;
		private static string Csv(string value) => "\"" + (value ?? string.Empty).Replace("\"", "\"\"") + "\"";

		private static void ShowUsage(string forcedType)
		{
			var prefix = string.IsNullOrWhiteSpace(forcedType) ? "machine-event" : forcedType;
			Console.WriteLine("Machine downtime/slowdown commands:");
			Console.WriteLine($"  SizerDataCollector.Service.exe {prefix} scan --serial <sn> [--type downtime|slowdown|both] [--from <dt> --to <dt> | --hours <h> | --day <yyyy-MM-dd> | --month <yyyy-MM> | --year <yyyy>] [--no-persist]");
			Console.WriteLine($"  SizerDataCollector.Service.exe {prefix} list --serial <sn> [same window options] [--format csv]");
			Console.WriteLine($"  SizerDataCollector.Service.exe {prefix} export --serial <sn> [same window options]");
		}
	}
}
