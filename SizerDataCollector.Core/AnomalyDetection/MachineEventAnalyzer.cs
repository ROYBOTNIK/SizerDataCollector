using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Npgsql;
using NpgsqlTypes;

namespace SizerDataCollector.Core.AnomalyDetection
{
	public sealed class MachineEventAnalyzer
	{
		private const string OperationalMinuteQuery = @"
SELECT minute_ts, serial_no, batch_record_id, lot, variety,
       availability_ratio, throughput_ratio, oee_score, total_fpm
FROM oee.v_operational_minute_batch
WHERE serial_no = @serial_no
  AND minute_ts >= @from_ts
  AND minute_ts <= @to_ts
ORDER BY minute_ts ASC;";

		private const string LotTransitionWindowQuery = @"
SELECT opportunity_window_start_ts, opportunity_window_end_ts
FROM oee.lot_transition_throughput_events
WHERE serial_no = @serial_no
  AND opportunity_window_start_ts <= @to_ts
  AND opportunity_window_end_ts >= @from_ts;";

		private readonly MachineEventConfig _config;
		private readonly string _connectionString;

		public MachineEventAnalyzer(MachineEventConfig config, string connectionString)
		{
			_config = config ?? throw new ArgumentNullException(nameof(config));
			_connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
		}

		public async Task<MachineEventReport> AnalyzeRangeAsync(
			string serialNo,
			DateTimeOffset fromTs,
			DateTimeOffset toTs,
			CancellationToken cancellationToken)
		{
			var report = new MachineEventReport
			{
				SerialNo = serialNo,
				FromTs = fromTs,
				ToTs = toTs
			};

			if (string.IsNullOrWhiteSpace(serialNo) || toTs <= fromTs)
				return report;

			var minutes = await LoadOperationalMinutesAsync(serialNo.Trim(), fromTs, toTs, cancellationToken).ConfigureAwait(false);
			var transitionWindows = _config.ExcludeLotTransitions
				? await LoadLotTransitionWindowsAsync(serialNo.Trim(), fromTs, toTs, cancellationToken).ConfigureAwait(false)
				: new List<TimeWindow>();

			return AnalyzeMinutes(serialNo.Trim(), fromTs, toTs, minutes, transitionWindows);
		}

		public MachineEventReport AnalyzeMinutes(
			string serialNo,
			DateTimeOffset fromTs,
			DateTimeOffset toTs,
			List<OperationalMinute> minutes,
			List<TimeWindow> transitionWindows)
		{
			var report = new MachineEventReport
			{
				SerialNo = serialNo,
				FromTs = fromTs,
				ToTs = toTs,
				MinuteCount = minutes?.Count ?? 0
			};

			if (minutes == null || minutes.Count == 0)
				return report;

			transitionWindows = transitionWindows ?? new List<TimeWindow>();
			var annotated = minutes
				.OrderBy(m => m.MinuteTs)
				.Select(m =>
				{
					m.OverlapsLotTransition = transitionWindows.Any(w => m.MinuteTs >= w.StartTs && m.MinuteTs <= w.EndTs);
					return m;
				})
				.ToList();

			report.DowntimeCandidates = annotated.Count(IsDowntimeMinute);
			report.SlowdownCandidates = annotated.Count(IsSlowdownMinute);
			report.DowntimeEvents = BuildEvents(serialNo, "downtime", annotated, IsDowntimeMinute);
			report.SlowdownEvents = BuildEvents(serialNo, "slowdown", annotated, IsSlowdownMinute);
			return report;
		}

		private async Task<List<OperationalMinute>> LoadOperationalMinutesAsync(
			string serialNo,
			DateTimeOffset fromTs,
			DateTimeOffset toTs,
			CancellationToken cancellationToken)
		{
			var rows = new List<OperationalMinute>();
			using (var connection = new NpgsqlConnection(_connectionString))
			{
				await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
				using (var cmd = new NpgsqlCommand(OperationalMinuteQuery, connection))
				{
					cmd.Parameters.Add(new NpgsqlParameter("serial_no", NpgsqlDbType.Text) { Value = serialNo });
					cmd.Parameters.Add(new NpgsqlParameter("from_ts", NpgsqlDbType.TimestampTz) { Value = fromTs.UtcDateTime });
					cmd.Parameters.Add(new NpgsqlParameter("to_ts", NpgsqlDbType.TimestampTz) { Value = toTs.UtcDateTime });

					using (var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
					{
						while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
						{
							rows.Add(new OperationalMinute
							{
								MinuteTs = reader.GetFieldValue<DateTimeOffset>(0),
								SerialNo = reader.GetString(1),
								BatchRecordId = reader.IsDBNull(2) ? (long?)null : Convert.ToInt64(reader.GetValue(2), CultureInfo.InvariantCulture),
								Lot = reader.IsDBNull(3) ? null : reader.GetString(3),
								Variety = reader.IsDBNull(4) ? null : reader.GetString(4),
								AvailabilityRatio = reader.IsDBNull(5) ? (double?)null : Convert.ToDouble(reader.GetValue(5), CultureInfo.InvariantCulture),
								ThroughputRatio = reader.IsDBNull(6) ? (double?)null : Convert.ToDouble(reader.GetValue(6), CultureInfo.InvariantCulture),
								OeeScore = reader.IsDBNull(7) ? (double?)null : Convert.ToDouble(reader.GetValue(7), CultureInfo.InvariantCulture),
								TotalFpm = reader.IsDBNull(8) ? (double?)null : Convert.ToDouble(reader.GetValue(8), CultureInfo.InvariantCulture)
							});
						}
					}
				}
			}

			return rows;
		}

		private async Task<List<TimeWindow>> LoadLotTransitionWindowsAsync(
			string serialNo,
			DateTimeOffset fromTs,
			DateTimeOffset toTs,
			CancellationToken cancellationToken)
		{
			var rows = new List<TimeWindow>();
			using (var connection = new NpgsqlConnection(_connectionString))
			{
				await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
				using (var cmd = new NpgsqlCommand(LotTransitionWindowQuery, connection))
				{
					cmd.Parameters.Add(new NpgsqlParameter("serial_no", NpgsqlDbType.Text) { Value = serialNo });
					cmd.Parameters.Add(new NpgsqlParameter("from_ts", NpgsqlDbType.TimestampTz) { Value = fromTs.UtcDateTime });
					cmd.Parameters.Add(new NpgsqlParameter("to_ts", NpgsqlDbType.TimestampTz) { Value = toTs.UtcDateTime });

					using (var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
					{
						while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
						{
							rows.Add(new TimeWindow
							{
								StartTs = reader.GetFieldValue<DateTimeOffset>(0),
								EndTs = reader.GetFieldValue<DateTimeOffset>(1)
							});
						}
					}
				}
			}

			return rows;
		}

		private bool IsDowntimeMinute(OperationalMinute minute)
		{
			if (_config.ExcludeLotTransitions && minute.OverlapsLotTransition)
				return false;

			return minute.AvailabilityRatio.HasValue
				&& minute.AvailabilityRatio.Value <= _config.DowntimeMaxAvailabilityRatio;
		}

		private bool IsSlowdownMinute(OperationalMinute minute)
		{
			if (_config.ExcludeLotTransitions && minute.OverlapsLotTransition)
				return false;

			return minute.ThroughputRatio.HasValue
				&& minute.ThroughputRatio.Value <= _config.SlowdownMaxThroughputRatio
				&& (!minute.AvailabilityRatio.HasValue || minute.AvailabilityRatio.Value >= _config.SlowdownMinAvailabilityRatio)
				&& (!minute.TotalFpm.HasValue || minute.TotalFpm.Value >= _config.SlowdownMinTotalFpm);
		}

		private List<MachineEvent> BuildEvents(
			string serialNo,
			string eventType,
			List<OperationalMinute> minutes,
			Func<OperationalMinute, bool> predicate)
		{
			var events = new List<MachineEvent>();
			var current = new List<OperationalMinute>();
			var mergeGap = TimeSpan.FromMinutes(_config.MergeGapMinutes + 1);

			foreach (var minute in minutes)
			{
				if (!predicate(minute))
					continue;

				if (current.Count == 0 || minute.MinuteTs - current[current.Count - 1].MinuteTs <= mergeGap)
				{
					current.Add(minute);
					continue;
				}

				TryAddEvent(serialNo, eventType, current, events);
				current = new List<OperationalMinute> { minute };
			}

			TryAddEvent(serialNo, eventType, current, events);
			return events;
		}

		private void TryAddEvent(string serialNo, string eventType, List<OperationalMinute> rows, List<MachineEvent> events)
		{
			if (rows == null || rows.Count == 0)
				return;

			var start = rows[0].MinuteTs;
			var end = rows[rows.Count - 1].MinuteTs.AddMinutes(1);
			var duration = (end - start).TotalMinutes;
			if (duration < _config.MinDurationMinutes)
				return;

			var batchId = CommonBatchId(rows);
			var lot = batchId.HasValue ? MostCommon(rows.Select(r => r.Lot)) : "mixed";
			var variety = batchId.HasValue ? MostCommon(rows.Select(r => r.Variety)) : "mixed";

			events.Add(new MachineEvent
			{
				EventType = eventType,
				StartTs = start,
				EndTs = end,
				DurationMinutes = duration,
				SerialNo = serialNo,
				BatchRecordId = batchId,
				Lot = lot,
				Variety = variety,
				AvgAvailabilityRatio = Average(rows.Select(r => r.AvailabilityRatio)),
				MinAvailabilityRatio = Minimum(rows.Select(r => r.AvailabilityRatio)),
				AvgThroughputRatio = Average(rows.Select(r => r.ThroughputRatio)),
				MinThroughputRatio = Minimum(rows.Select(r => r.ThroughputRatio)),
				AvgTotalFpm = Average(rows.Select(r => r.TotalFpm)),
				MinTotalFpm = Minimum(rows.Select(r => r.TotalFpm)),
				AvgOeeScore = Average(rows.Select(r => r.OeeScore)),
				Reason = BuildReason(eventType),
				OverlapsLotTransition = rows.Any(r => r.OverlapsLotTransition),
				ExplanationJson = BuildExplanationJson(eventType, rows.Count),
				ModelVersion = _config.ModelVersion,
				DeliveredTo = string.Empty
			});
		}

		private string BuildReason(string eventType)
		{
			if (eventType == "downtime")
				return string.Format(CultureInfo.InvariantCulture, "availability_ratio <= {0:F3}", _config.DowntimeMaxAvailabilityRatio);

			return string.Format(CultureInfo.InvariantCulture,
				"throughput_ratio <= {0:F3}, availability_ratio >= {1:F3}, total_fpm >= {2:F1}",
				_config.SlowdownMaxThroughputRatio,
				_config.SlowdownMinAvailabilityRatio,
				_config.SlowdownMinTotalFpm);
		}

		private string BuildExplanationJson(string eventType, int minuteCount)
		{
			var json = new JObject
			{
				["event_type"] = eventType,
				["minute_count"] = minuteCount,
				["min_duration_minutes"] = _config.MinDurationMinutes,
				["merge_gap_minutes"] = _config.MergeGapMinutes,
				["exclude_lot_transitions"] = _config.ExcludeLotTransitions,
				["downtime_max_availability_ratio"] = _config.DowntimeMaxAvailabilityRatio,
				["slowdown_max_throughput_ratio"] = _config.SlowdownMaxThroughputRatio,
				["slowdown_min_availability_ratio"] = _config.SlowdownMinAvailabilityRatio,
				["slowdown_min_total_fpm"] = _config.SlowdownMinTotalFpm
			};

			return json.ToString(Formatting.None);
		}

		private static long? CommonBatchId(List<OperationalMinute> rows)
		{
			var values = rows.Select(r => r.BatchRecordId).Distinct().ToList();
			return values.Count == 1 ? values[0] : null;
		}

		private static string MostCommon(IEnumerable<string> values)
		{
			return values
				.Where(v => !string.IsNullOrWhiteSpace(v))
				.GroupBy(v => v)
				.OrderByDescending(g => g.Count())
				.Select(g => g.Key)
				.FirstOrDefault();
		}

		private static double? Average(IEnumerable<double?> values)
		{
			var present = values.Where(v => v.HasValue).Select(v => v.Value).ToList();
			return present.Count == 0 ? (double?)null : present.Average();
		}

		private static double? Minimum(IEnumerable<double?> values)
		{
			var present = values.Where(v => v.HasValue).Select(v => v.Value).ToList();
			return present.Count == 0 ? (double?)null : present.Min();
		}

		public sealed class OperationalMinute
		{
			public DateTimeOffset MinuteTs { get; set; }
			public string SerialNo { get; set; }
			public long? BatchRecordId { get; set; }
			public string Lot { get; set; }
			public string Variety { get; set; }
			public double? AvailabilityRatio { get; set; }
			public double? ThroughputRatio { get; set; }
			public double? OeeScore { get; set; }
			public double? TotalFpm { get; set; }
			public bool OverlapsLotTransition { get; set; }
		}

		public sealed class TimeWindow
		{
			public DateTimeOffset StartTs { get; set; }
			public DateTimeOffset EndTs { get; set; }
		}
	}
}
