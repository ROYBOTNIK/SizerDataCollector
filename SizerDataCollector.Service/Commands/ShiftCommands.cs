using System;
using System.Collections.Generic;
using System.Globalization;
using Npgsql;
using NpgsqlTypes;
using SizerDataCollector.Core.Config;

namespace SizerDataCollector.Service.Commands
{
	internal static class ShiftCommands
	{
		public static int Run(string[] args, Dictionary<string, string> options)
		{
			var subCommand = args.Length > 0
				? (args[0] ?? string.Empty).Trim().ToLowerInvariant()
				: string.Empty;

			switch (subCommand)
			{
				case "list":
					return List(options);
				case "add":
					return Upsert(options, isAdd: true);
				case "update":
					return Upsert(options, isAdd: false);
				case "remove":
					return Remove(options);
				case "show":
					return Show(options);
				default:
					ShowUsage();
					return 1;
			}
		}

		private static int List(Dictionary<string, string> options)
		{
			if (!RequireSerial(options, out var serialNo))
			{
				return 1;
			}

			var config = LoadConfig();
			if (config == null)
			{
				return 1;
			}

			using (var connection = new NpgsqlConnection(config.TimescaleConnectionString))
			{
				connection.Open();
				const string sql = @"
SELECT shift_name, start_local, end_local, crosses_midnight, timezone, dow_mask, is_active, effective_from, effective_to, created_at
FROM oee.shifts
WHERE serial_no = @serial_no
ORDER BY shift_name;";
				using (var cmd = new NpgsqlCommand(sql, connection))
				{
					cmd.Parameters.Add(new NpgsqlParameter("serial_no", NpgsqlDbType.Text) { Value = serialNo });
					using (var reader = cmd.ExecuteReader())
					{
						var hasRows = false;
						Console.WriteLine($"Shift definitions for '{serialNo}':");
						Console.WriteLine("Name           Start   End     CrossMidnight  Timezone             DOW        Active  Effective");
						Console.WriteLine("-------------- ------- ------- -------------- -------------------- ---------- ------- -------------------------");
						while (reader.Read())
						{
							hasRows = true;
							var name = reader.GetString(0);
							var startLocal = reader.GetTimeSpan(1);
							var endLocal = reader.GetTimeSpan(2);
							var crosses = reader.GetBoolean(3);
							var timezone = reader.GetString(4);
							var dowMask = reader.GetInt16(5);
							var active = reader.GetBoolean(6);
							var effectiveFrom = reader.IsDBNull(7) ? (DateTime?)null : reader.GetDateTime(7);
							var effectiveTo = reader.IsDBNull(8) ? (DateTime?)null : reader.GetDateTime(8);

							Console.WriteLine(string.Format(
								CultureInfo.InvariantCulture,
								"{0,-14} {1:hh\\:mm}   {2:hh\\:mm}   {3,-14} {4,-20} {5,-10} {6,-7} {7}",
								Trim(name, 14),
								startLocal,
								endLocal,
								crosses ? "yes" : "no",
								Trim(timezone, 20),
								FormatDowMask(dowMask),
								active ? "yes" : "no",
								FormatEffectiveRange(effectiveFrom, effectiveTo)));
						}

						if (!hasRows)
						{
							Console.WriteLine("No shift definitions found.");
						}
					}
				}
			}

			return 0;
		}

		private static int Upsert(Dictionary<string, string> options, bool isAdd)
		{
			if (!RequireSerial(options, out var serialNo) || !RequireName(options, out var shiftName))
			{
				return 1;
			}

			var config = LoadConfig();
			if (config == null)
			{
				return 1;
			}

			TimeSpan? startLocal = null;
			TimeSpan? endLocal = null;
			string timezone = null;
			short? dowMask = null;
			DateTime? effectiveFrom = null;
			DateTime? effectiveTo = null;
			bool? isActive = null;

			if (options.TryGetValue("start", out var startRaw))
			{
				if (!TryParseClockTime(startRaw, out var parsedStart))
				{
					Console.WriteLine("Invalid --start. Use HH:mm (24h).");
					return 1;
				}
				startLocal = parsedStart;
			}

			if (options.TryGetValue("end", out var endRaw))
			{
				if (!TryParseClockTime(endRaw, out var parsedEnd))
				{
					Console.WriteLine("Invalid --end. Use HH:mm (24h).");
					return 1;
				}
				endLocal = parsedEnd;
			}

			if (isAdd && (!startLocal.HasValue || !endLocal.HasValue))
			{
				Console.WriteLine("Missing required options for shift add: --start HH:mm --end HH:mm");
				return 1;
			}

			if (options.TryGetValue("tz", out var tzRaw) && !string.IsNullOrWhiteSpace(tzRaw))
			{
				timezone = tzRaw.Trim();
			}

			if (options.TryGetValue("dow", out var dowRaw))
			{
				if (!TryParseDowMask(dowRaw, out var parsedMask, out var parseError))
				{
					Console.WriteLine(parseError);
					return 1;
				}

				dowMask = parsedMask;
			}

			if (options.TryGetValue("effective-from", out var fromRaw))
			{
				if (!DateTime.TryParseExact(fromRaw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedFrom))
				{
					Console.WriteLine("Invalid --effective-from. Use yyyy-MM-dd.");
					return 1;
				}
				effectiveFrom = parsedFrom.Date;
			}

			if (options.TryGetValue("effective-to", out var toRaw))
			{
				if (!DateTime.TryParseExact(toRaw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedTo))
				{
					Console.WriteLine("Invalid --effective-to. Use yyyy-MM-dd.");
					return 1;
				}
				effectiveTo = parsedTo.Date;
			}

			if (effectiveFrom.HasValue && effectiveTo.HasValue && effectiveTo.Value < effectiveFrom.Value)
			{
				Console.WriteLine("--effective-to must be on or after --effective-from.");
				return 1;
			}

			if (options.TryGetValue("active", out var activeRaw))
			{
				if (!bool.TryParse(activeRaw, out var parsedActive))
				{
					Console.WriteLine("Invalid --active. Use true|false.");
					return 1;
				}

				isActive = parsedActive;
			}

			using (var connection = new NpgsqlConnection(config.TimescaleConnectionString))
			{
				connection.Open();

				if (!isAdd && !ShiftExists(connection, serialNo, shiftName))
				{
					Console.WriteLine($"Shift '{shiftName}' does not exist for '{serialNo}'. Use shift add first.");
					return 1;
				}

				if (isAdd && ShiftExists(connection, serialNo, shiftName))
				{
					Console.WriteLine($"Shift '{shiftName}' already exists for '{serialNo}'. Use shift update to modify it.");
					return 1;
				}

				if (isAdd)
				{
					const string insertSql = @"
INSERT INTO oee.shifts (
    serial_no, shift_name, start_local, end_local, timezone, dow_mask, is_active, effective_from, effective_to
) VALUES (
    @serial_no, @shift_name, @start_local, @end_local, COALESCE(@timezone, 'UTC'), COALESCE(@dow_mask, 127), COALESCE(@is_active, true), @effective_from, @effective_to
);";
					using (var cmd = new NpgsqlCommand(insertSql, connection))
					{
						cmd.Parameters.Add(new NpgsqlParameter("serial_no", NpgsqlDbType.Text) { Value = serialNo });
						cmd.Parameters.Add(new NpgsqlParameter("shift_name", NpgsqlDbType.Text) { Value = shiftName });
						cmd.Parameters.Add(new NpgsqlParameter("start_local", NpgsqlDbType.Time) { Value = startLocal.Value });
						cmd.Parameters.Add(new NpgsqlParameter("end_local", NpgsqlDbType.Time) { Value = endLocal.Value });
						cmd.Parameters.Add(new NpgsqlParameter("timezone", NpgsqlDbType.Text) { Value = (object)timezone ?? DBNull.Value });
						cmd.Parameters.Add(new NpgsqlParameter("dow_mask", NpgsqlDbType.Smallint) { Value = (object)dowMask ?? DBNull.Value });
						cmd.Parameters.Add(new NpgsqlParameter("is_active", NpgsqlDbType.Boolean) { Value = (object)isActive ?? DBNull.Value });
						cmd.Parameters.Add(new NpgsqlParameter("effective_from", NpgsqlDbType.Date) { Value = (object)effectiveFrom ?? DBNull.Value });
						cmd.Parameters.Add(new NpgsqlParameter("effective_to", NpgsqlDbType.Date) { Value = (object)effectiveTo ?? DBNull.Value });
						cmd.ExecuteNonQuery();
					}

					Console.WriteLine($"Added shift '{shiftName}' for '{serialNo}'.");
				}
				else
				{
					const string updateSql = @"
UPDATE oee.shifts
SET start_local = COALESCE(@start_local, start_local),
    end_local = COALESCE(@end_local, end_local),
    timezone = COALESCE(@timezone, timezone),
    dow_mask = COALESCE(@dow_mask, dow_mask),
    is_active = COALESCE(@is_active, is_active),
    effective_from = CASE WHEN @effective_from_set THEN @effective_from ELSE effective_from END,
    effective_to = CASE WHEN @effective_to_set THEN @effective_to ELSE effective_to END
WHERE serial_no = @serial_no
  AND shift_name = @shift_name;";
					using (var cmd = new NpgsqlCommand(updateSql, connection))
					{
						cmd.Parameters.Add(new NpgsqlParameter("serial_no", NpgsqlDbType.Text) { Value = serialNo });
						cmd.Parameters.Add(new NpgsqlParameter("shift_name", NpgsqlDbType.Text) { Value = shiftName });
						cmd.Parameters.Add(new NpgsqlParameter("start_local", NpgsqlDbType.Time) { Value = (object)startLocal ?? DBNull.Value });
						cmd.Parameters.Add(new NpgsqlParameter("end_local", NpgsqlDbType.Time) { Value = (object)endLocal ?? DBNull.Value });
						cmd.Parameters.Add(new NpgsqlParameter("timezone", NpgsqlDbType.Text) { Value = (object)timezone ?? DBNull.Value });
						cmd.Parameters.Add(new NpgsqlParameter("dow_mask", NpgsqlDbType.Smallint) { Value = (object)dowMask ?? DBNull.Value });
						cmd.Parameters.Add(new NpgsqlParameter("is_active", NpgsqlDbType.Boolean) { Value = (object)isActive ?? DBNull.Value });
						cmd.Parameters.Add(new NpgsqlParameter("effective_from_set", NpgsqlDbType.Boolean) { Value = options.ContainsKey("effective-from") });
						cmd.Parameters.Add(new NpgsqlParameter("effective_to_set", NpgsqlDbType.Boolean) { Value = options.ContainsKey("effective-to") });
						cmd.Parameters.Add(new NpgsqlParameter("effective_from", NpgsqlDbType.Date) { Value = (object)effectiveFrom ?? DBNull.Value });
						cmd.Parameters.Add(new NpgsqlParameter("effective_to", NpgsqlDbType.Date) { Value = (object)effectiveTo ?? DBNull.Value });
						var affected = cmd.ExecuteNonQuery();
						if (affected == 0)
						{
							Console.WriteLine("No rows updated.");
							return 1;
						}
					}

					Console.WriteLine($"Updated shift '{shiftName}' for '{serialNo}'.");
				}
			}

			return 0;
		}

		private static int Remove(Dictionary<string, string> options)
		{
			if (!RequireSerial(options, out var serialNo) || !RequireName(options, out var shiftName))
			{
				return 1;
			}

			var config = LoadConfig();
			if (config == null)
			{
				return 1;
			}

			using (var connection = new NpgsqlConnection(config.TimescaleConnectionString))
			{
				connection.Open();
				const string sql = @"
DELETE FROM oee.shifts
WHERE serial_no = @serial_no
  AND shift_name = @shift_name;";
				using (var cmd = new NpgsqlCommand(sql, connection))
				{
					cmd.Parameters.Add(new NpgsqlParameter("serial_no", NpgsqlDbType.Text) { Value = serialNo });
					cmd.Parameters.Add(new NpgsqlParameter("shift_name", NpgsqlDbType.Text) { Value = shiftName });
					var affected = cmd.ExecuteNonQuery();
					if (affected == 0)
					{
						Console.WriteLine($"Shift '{shiftName}' was not found for '{serialNo}'.");
						return 1;
					}
				}
			}

			Console.WriteLine($"Removed shift '{shiftName}' for '{serialNo}'.");
			return 0;
		}

		private static int Show(Dictionary<string, string> options)
		{
			if (!RequireSerial(options, out var serialNo))
			{
				return 1;
			}

			DateTime dayLocal;
			if (options.TryGetValue("day", out var dayRaw))
			{
				if (!DateTime.TryParseExact(dayRaw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out dayLocal))
				{
					Console.WriteLine("Invalid --day. Use yyyy-MM-dd.");
					return 1;
				}
				dayLocal = dayLocal.Date;
			}
			else
			{
				dayLocal = DateTime.UtcNow.Date;
			}

			var config = LoadConfig();
			if (config == null)
			{
				return 1;
			}

			using (var connection = new NpgsqlConnection(config.TimescaleConnectionString))
			{
				connection.Open();
				const string sql = @"
SELECT day_local, shift_name, timezone, start_ts, end_ts
FROM oee.v_shift_window
WHERE serial_no = @serial_no
  AND day_local = @day_local
ORDER BY start_ts;";
				using (var cmd = new NpgsqlCommand(sql, connection))
				{
					cmd.Parameters.Add(new NpgsqlParameter("serial_no", NpgsqlDbType.Text) { Value = serialNo });
					cmd.Parameters.Add(new NpgsqlParameter("day_local", NpgsqlDbType.Date) { Value = dayLocal });
					using (var reader = cmd.ExecuteReader())
					{
						var hasRows = false;
						Console.WriteLine($"Shift windows for '{serialNo}' on {dayLocal:yyyy-MM-dd}:");
						Console.WriteLine("Shift          Timezone             Start (UTC)               End (UTC)");
						Console.WriteLine("-------------- -------------------- ------------------------- -------------------------");
						while (reader.Read())
						{
							hasRows = true;
							var shiftName = reader.GetString(1);
							var timezone = reader.GetString(2);
							var startTs = reader.GetFieldValue<DateTimeOffset>(3);
							var endTs = reader.GetFieldValue<DateTimeOffset>(4);
							Console.WriteLine(string.Format(
								CultureInfo.InvariantCulture,
								"{0,-14} {1,-20} {2} {3}",
								Trim(shiftName, 14),
								Trim(timezone, 20),
								startTs.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
								endTs.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)));
						}

						if (!hasRows)
						{
							Console.WriteLine("No active shift windows found for that date.");
						}
					}
				}
			}

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

		private static bool ShiftExists(NpgsqlConnection connection, string serialNo, string shiftName)
		{
			const string sql = @"SELECT 1 FROM oee.shifts WHERE serial_no = @serial_no AND shift_name = @shift_name LIMIT 1;";
			using (var cmd = new NpgsqlCommand(sql, connection))
			{
				cmd.Parameters.Add(new NpgsqlParameter("serial_no", NpgsqlDbType.Text) { Value = serialNo });
				cmd.Parameters.Add(new NpgsqlParameter("shift_name", NpgsqlDbType.Text) { Value = shiftName });
				return cmd.ExecuteScalar() != null;
			}
		}

		private static bool RequireSerial(Dictionary<string, string> options, out string serialNo)
		{
			if (options.TryGetValue("serial", out serialNo) && !string.IsNullOrWhiteSpace(serialNo))
			{
				serialNo = serialNo.Trim();
				return true;
			}

			Console.WriteLine("Missing required option: --serial <serial_no>");
			serialNo = null;
			return false;
		}

		private static bool RequireName(Dictionary<string, string> options, out string shiftName)
		{
			if (options.TryGetValue("name", out shiftName) && !string.IsNullOrWhiteSpace(shiftName))
			{
				shiftName = shiftName.Trim();
				return true;
			}

			Console.WriteLine("Missing required option: --name <shift_name>");
			shiftName = null;
			return false;
		}

		private static bool TryParseClockTime(string raw, out TimeSpan value)
		{
			return TimeSpan.TryParseExact(raw.Trim(), @"hh\:mm", CultureInfo.InvariantCulture, out value);
		}

		private static bool TryParseDowMask(string raw, out short mask, out string error)
		{
			mask = 0;
			error = null;
			if (string.IsNullOrWhiteSpace(raw))
			{
				error = "Invalid --dow value.";
				return false;
			}

			var value = raw.Trim();
			if (string.Equals(value, "all", StringComparison.OrdinalIgnoreCase))
			{
				mask = 127;
				return true;
			}

			var parts = value.Split(',');
			foreach (var part in parts)
			{
				var token = part.Trim();
				if (token.Length == 0)
				{
					continue;
				}

				if (token.Contains("-"))
				{
					var range = token.Split('-');
					if (range.Length != 2 || !TryResolveWeekday(range[0], out var start) || !TryResolveWeekday(range[1], out var end))
					{
						error = "Invalid --dow. Use 'Mon-Fri', 'Mon,Wed,Fri', or 'all'.";
						return false;
					}

					if (end < start)
					{
						error = "Invalid --dow range. Use ascending ranges like Mon-Fri.";
						return false;
					}

					for (var day = start; day <= end; day++)
					{
						mask = unchecked((short)(((int)mask) | (1 << day)));
					}
				}
				else
				{
					if (!TryResolveWeekday(token, out var day))
					{
						error = "Invalid --dow. Use weekday names Mon..Sun.";
						return false;
					}

					mask = unchecked((short)(((int)mask) | (1 << day)));
				}
			}

			if (mask == 0)
			{
				error = "Invalid --dow. No days selected.";
				return false;
			}

			return true;
		}

		// Bit mapping aligns with SQL: Mon=bit0 ... Sun=bit6.
		private static bool TryResolveWeekday(string token, out int dayIndex)
		{
			dayIndex = -1;
			if (string.IsNullOrWhiteSpace(token))
			{
				return false;
			}

			switch (token.Trim().ToLowerInvariant())
			{
				case "mon":
				case "monday":
					dayIndex = 0;
					return true;
				case "tue":
				case "tues":
				case "tuesday":
					dayIndex = 1;
					return true;
				case "wed":
				case "wednesday":
					dayIndex = 2;
					return true;
				case "thu":
				case "thur":
				case "thurs":
				case "thursday":
					dayIndex = 3;
					return true;
				case "fri":
				case "friday":
					dayIndex = 4;
					return true;
				case "sat":
				case "saturday":
					dayIndex = 5;
					return true;
				case "sun":
				case "sunday":
					dayIndex = 6;
					return true;
				default:
					return false;
			}
		}

		private static string FormatDowMask(short mask)
		{
			var names = new List<string>();
			var shortDays = new[] { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };
			for (var i = 0; i < shortDays.Length; i++)
			{
				if ((mask & (1 << i)) != 0)
				{
					names.Add(shortDays[i]);
				}
			}

			if (names.Count == 7)
			{
				return "all";
			}

			return names.Count == 0 ? "-" : string.Join(",", names);
		}

		private static string FormatEffectiveRange(DateTime? effectiveFrom, DateTime? effectiveTo)
		{
			var from = effectiveFrom.HasValue
				? effectiveFrom.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
				: "open";
			var to = effectiveTo.HasValue
				? effectiveTo.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
				: "open";
			return from + " -> " + to;
		}

		private static string Trim(string value, int length)
		{
			if (string.IsNullOrEmpty(value))
			{
				return string.Empty;
			}
			return value.Length <= length ? value : value.Substring(0, length);
		}

		private static void ShowUsage()
		{
			Console.WriteLine("Shift commands:");
			Console.WriteLine("  SizerDataCollector.Service.exe shift list --serial <sn>");
			Console.WriteLine("  SizerDataCollector.Service.exe shift add --serial <sn> --name <shift> --start <HH:mm> --end <HH:mm> [--tz <IANA zone>] [--dow Mon-Fri|Mon,Wed,Fri|all] [--effective-from <yyyy-MM-dd>] [--effective-to <yyyy-MM-dd>] [--active true|false]");
			Console.WriteLine("  SizerDataCollector.Service.exe shift update --serial <sn> --name <shift> [--start <HH:mm>] [--end <HH:mm>] [--tz <IANA zone>] [--dow ...] [--effective-from <yyyy-MM-dd>] [--effective-to <yyyy-MM-dd>] [--active true|false]");
			Console.WriteLine("  SizerDataCollector.Service.exe shift remove --serial <sn> --name <shift>");
			Console.WriteLine("  SizerDataCollector.Service.exe shift show --serial <sn> [--day <yyyy-MM-dd>]");
		}
	}
}
