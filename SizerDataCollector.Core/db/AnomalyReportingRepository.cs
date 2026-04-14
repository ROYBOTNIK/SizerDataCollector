using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;

namespace SizerDataCollector.Core.Db
{
	public sealed class AnomalyReportingRepository
	{
		private const string OffendersSql = @"
WITH operational_window AS (
    SELECT count(DISTINCT o.minute_ts) AS operational_minutes
    FROM oee.v_operational_minute_batch o
    WHERE o.serial_no = @serial_no
      AND o.minute_ts >= @from_ts
      AND o.minute_ts < @to_ts
),
offender_base AS (
SELECT e.anomaly_type,
       e.serial_no,
       e.batch_record_id,
       e.lane_no,
       e.grade_key,
       e.window_hours,
       count(*) AS repeat_count,
       min(event_ts) AS first_event_ts,
       max(event_ts) AS last_event_ts,
       count(*) FILTER (WHERE severity = 'Low') AS low_count,
       count(*) FILTER (WHERE severity = 'Medium') AS medium_count,
       count(*) FILTER (WHERE severity = 'High') AS high_count,
       avg(score_pct) AS avg_score_pct,
       avg(score_z) AS avg_score_z,
       avg(abs(score_pct)) AS avg_abs_pct,
       max(abs(score_pct)) AS max_abs_pct,
       avg(abs(score_z)) AS avg_abs_z,
       max(abs(score_z)) AS max_abs_z,
       count(DISTINCT minute_ts) AS active_minutes,
       extract(epoch FROM (max(event_ts) - min(event_ts))) / 60.0 AS span_minutes,
       count(DISTINCT batch_record_id) AS affected_batches,
       count(DISTINCT COALESCE(b.grower_code, b.comments, '(unknown)')) AS affected_lots
FROM oee.v_anomaly_event_detail e
LEFT JOIN public.batches b
  ON b.id = e.batch_record_id
WHERE e.serial_no = @serial_no
  AND e.event_ts >= @from_ts
  AND e.event_ts < @to_ts
  AND (@anomaly_type = '' OR e.anomaly_type = @anomaly_type)
GROUP BY e.anomaly_type, e.serial_no, e.batch_record_id, e.lane_no, e.grade_key, e.window_hours
)
SELECT o.anomaly_type,
       o.serial_no,
       o.batch_record_id,
       o.lane_no,
       o.grade_key,
       o.window_hours,
       o.repeat_count,
       o.first_event_ts,
       o.last_event_ts,
       o.low_count,
       o.medium_count,
       o.high_count,
       o.avg_score_pct,
       o.avg_score_z,
       o.avg_abs_pct,
       o.max_abs_pct,
       o.avg_abs_z,
       o.max_abs_z,
       o.active_minutes,
       o.span_minutes,
       o.affected_batches,
       o.affected_lots,
       CASE
           WHEN o.avg_score_pct > 0.25 THEN 'positive_skew'
           WHEN o.avg_score_pct < -0.25 THEN 'negative_skew'
           ELSE 'balanced'
       END AS direction_label,
       CASE
           WHEN COALESCE(w.operational_minutes, 0) = 0 THEN NULL
           ELSE (100.0 * o.active_minutes::double precision) / w.operational_minutes::double precision
       END AS runtime_share_pct
FROM offender_base o
CROSS JOIN operational_window w
ORDER BY repeat_count DESC, high_count DESC, max_abs_pct DESC, last_event_ts DESC
LIMIT @limit;";

		private const string ImpactSql = @"
WITH target_events AS (
    SELECT anomaly_type,
           event_ts,
           minute_ts,
           serial_no,
           batch_record_id,
           lane_no,
           grade_key,
           window_hours,
           severity,
           score_pct,
           score_z,
           model_version,
           delivered_to
    FROM oee.v_anomaly_event_detail
    WHERE serial_no = @serial_no
      AND event_ts >= @from_ts
      AND event_ts < @to_ts
      AND (@anomaly_type = '' OR anomaly_type = @anomaly_type)
    ORDER BY abs(score_pct) DESC, event_ts DESC
    LIMIT @limit
)
SELECT e.anomaly_type,
       e.event_ts,
       e.minute_ts,
       e.serial_no,
       e.batch_record_id,
       e.lane_no,
       e.grade_key,
       e.window_hours,
       e.severity,
       e.score_pct,
       e.score_z,
       cur.lot,
       cur.variety,
       pre.availability_ratio AS pre_availability_ratio,
       cur.availability_ratio AS event_availability_ratio,
       post.availability_ratio AS post_availability_ratio,
       pre.throughput_ratio AS pre_throughput_ratio,
       cur.throughput_ratio AS event_throughput_ratio,
       post.throughput_ratio AS post_throughput_ratio,
       pre.quality_ratio AS pre_quality_ratio,
       cur.quality_ratio AS event_quality_ratio,
       post.quality_ratio AS post_quality_ratio,
       pre.oee_score AS pre_oee_score,
       cur.oee_score AS event_oee_score,
       post.oee_score AS post_oee_score,
       cur.total_fpm AS event_total_fpm,
       cur.missed_fpm AS event_missed_fpm,
       cur.combined_recycle_fpm AS event_combined_recycle_fpm,
       cur.cupfill_pct AS event_cupfill_pct,
       cur.tph AS event_tph,
       cur.target_throughput AS event_target_throughput,
       (cur.oee_score - pre.oee_score) AS delta_oee_from_pre,
       (post.oee_score - pre.oee_score) AS delta_oee_post_vs_pre,
       (cur.throughput_ratio - pre.throughput_ratio) AS delta_throughput_from_pre,
       (post.throughput_ratio - pre.throughput_ratio) AS delta_throughput_post_vs_pre,
       (cur.quality_ratio - pre.quality_ratio) AS delta_quality_from_pre,
       (post.quality_ratio - pre.quality_ratio) AS delta_quality_post_vs_pre
FROM target_events e
LEFT JOIN LATERAL (
    SELECT o.lot,
           o.variety,
           o.availability_ratio,
           o.throughput_ratio,
           o.quality_ratio,
           o.oee_score,
           o.total_fpm,
           o.missed_fpm,
           o.combined_recycle_fpm,
           o.cupfill_pct,
           o.tph,
           o.target_throughput
    FROM oee.v_operational_minute_batch o
    WHERE o.serial_no = e.serial_no
      AND (e.batch_record_id IS NULL OR o.batch_record_id = e.batch_record_id)
      AND o.minute_ts = e.minute_ts
    ORDER BY o.minute_ts DESC
    LIMIT 1
) cur ON true
LEFT JOIN LATERAL (
    SELECT avg(o.availability_ratio) AS availability_ratio,
           avg(o.throughput_ratio) AS throughput_ratio,
           avg(o.quality_ratio) AS quality_ratio,
           avg(o.oee_score) AS oee_score
    FROM oee.v_operational_minute_batch o
    WHERE o.serial_no = e.serial_no
      AND (e.batch_record_id IS NULL OR o.batch_record_id = e.batch_record_id)
      AND o.minute_ts >= (e.minute_ts - '00:15:00'::interval)
      AND o.minute_ts < e.minute_ts
) pre ON true
LEFT JOIN LATERAL (
    SELECT avg(o.availability_ratio) AS availability_ratio,
           avg(o.throughput_ratio) AS throughput_ratio,
           avg(o.quality_ratio) AS quality_ratio,
           avg(o.oee_score) AS oee_score
    FROM oee.v_operational_minute_batch o
    WHERE o.serial_no = e.serial_no
      AND (e.batch_record_id IS NULL OR o.batch_record_id = e.batch_record_id)
      AND o.minute_ts > e.minute_ts
      AND o.minute_ts <= (e.minute_ts + '00:15:00'::interval)
) post ON true
ORDER BY abs(e.score_pct) DESC, e.event_ts DESC;";

		private const string ImpactFamilySummarySql = @"
WITH filtered AS (
    SELECT anomaly_type,
           serial_no,
           lane_no,
           grade_key,
           window_hours,
           severity,
           delta_oee_post_vs_pre,
           delta_throughput_post_vs_pre,
           delta_quality_post_vs_pre
    FROM oee.v_anomaly_impact_summary
    WHERE serial_no = @serial_no
      AND event_ts >= @from_ts
      AND event_ts < @to_ts
      AND (@anomaly_type = '' OR anomaly_type = @anomaly_type)
),
family AS (
    SELECT anomaly_type,
           serial_no,
           lane_no,
           grade_key,
           window_hours,
           count(*) AS event_count,
           count(*) FILTER (WHERE severity = 'High') AS high_count,
           avg(delta_oee_post_vs_pre) AS avg_delta_oee_post_vs_pre,
           avg(delta_throughput_post_vs_pre) AS avg_delta_throughput_post_vs_pre,
           avg(delta_quality_post_vs_pre) AS avg_delta_quality_post_vs_pre,
           count(*) FILTER (
               WHERE COALESCE(delta_oee_post_vs_pre, 0) < -0.02
                  OR COALESCE(delta_throughput_post_vs_pre, 0) < -0.02
           ) AS negative_post_impact_count,
           count(*) FILTER (
               WHERE severity = 'High'
                 AND (
                     COALESCE(delta_oee_post_vs_pre, 0) < -0.02
                     OR COALESCE(delta_throughput_post_vs_pre, 0) < -0.02
                 )
           ) AS high_severity_negative_count
    FROM filtered
    GROUP BY anomaly_type, serial_no, lane_no, grade_key, window_hours
)
SELECT anomaly_type,
       serial_no,
       lane_no,
       grade_key,
       window_hours,
       event_count,
       high_count,
       avg_delta_oee_post_vs_pre,
       avg_delta_throughput_post_vs_pre,
       avg_delta_quality_post_vs_pre,
       negative_post_impact_count,
       high_severity_negative_count,
       CASE
           WHEN high_severity_negative_count >= 2 OR avg_delta_oee_post_vs_pre <= -0.03 THEN 'likely_material'
           WHEN negative_post_impact_count = 0 AND COALESCE(avg_delta_oee_post_vs_pre, 0) >= 0 THEN 'likely_non_material'
           ELSE 'mixed_unclear'
       END AS materiality_label
FROM family
ORDER BY avg_delta_oee_post_vs_pre ASC NULLS LAST, avg_delta_throughput_post_vs_pre ASC NULLS LAST, high_severity_negative_count DESC, event_count DESC
LIMIT @limit;";

		private const string WindowSummarySql = @"
SELECT count(*) AS total_events,
       count(*) FILTER (WHERE severity = 'Low') AS low_count,
       count(*) FILTER (WHERE severity = 'Medium') AS medium_count,
       count(*) FILTER (WHERE severity = 'High') AS high_count,
       avg(abs(score_pct)) AS avg_abs_pct,
       max(abs(score_pct)) AS max_abs_pct,
       avg(abs(score_z)) AS avg_abs_z,
       max(abs(score_z)) AS max_abs_z
FROM oee.v_anomaly_event_detail
WHERE serial_no = @serial_no
  AND event_ts >= @from_ts
  AND event_ts < @to_ts
  AND (@anomaly_type = '' OR anomaly_type = @anomaly_type);";

		private readonly string _connectionString;

		public AnomalyReportingRepository(string connectionString)
		{
			_connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
		}

		public async Task<IReadOnlyList<AnomalyOffenderRow>> GetOffendersAsync(
			string anomalyType,
			string serialNo,
			DateTimeOffset fromTs,
			DateTimeOffset toTs,
			int limit,
			CancellationToken cancellationToken)
		{
			var rows = new List<AnomalyOffenderRow>();

			using (var conn = new NpgsqlConnection(_connectionString))
			{
				await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
				using (var cmd = new NpgsqlCommand(OffendersSql, conn))
				{
					AddCommonParameters(cmd, anomalyType, serialNo, fromTs, toTs, limit);

					using (var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
					{
						while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
						{
							rows.Add(new AnomalyOffenderRow
							{
								AnomalyType = reader.GetString(0),
								SerialNo = reader.GetString(1),
								BatchRecordId = ReadNullableInt64(reader, 2),
								LaneNo = reader.GetInt16(3),
								GradeKey = reader.IsDBNull(4) ? null : reader.GetString(4),
								WindowHours = ReadNullableInt32(reader, 5),
								RepeatCount = reader.GetInt64(6),
								FirstEventTs = reader.GetFieldValue<DateTimeOffset>(7),
								LastEventTs = reader.GetFieldValue<DateTimeOffset>(8),
								LowCount = reader.GetInt64(9),
								MediumCount = reader.GetInt64(10),
								HighCount = reader.GetInt64(11),
								AvgScorePct = ReadNullableDouble(reader, 12),
								AvgScoreZ = ReadNullableDouble(reader, 13),
								AvgAbsPct = ReadNullableDouble(reader, 14),
								MaxAbsPct = ReadNullableDouble(reader, 15),
								AvgAbsZ = ReadNullableDouble(reader, 16),
								MaxAbsZ = ReadNullableDouble(reader, 17),
								ActiveMinutes = reader.GetInt64(18),
								SpanMinutes = ReadNullableDouble(reader, 19),
								AffectedBatches = reader.GetInt64(20),
								AffectedLots = reader.GetInt64(21),
								DirectionLabel = reader.IsDBNull(22) ? null : reader.GetString(22),
								RuntimeSharePct = ReadNullableDouble(reader, 23)
							});
						}
					}
				}
			}

			return rows;
		}

		public async Task<IReadOnlyList<AnomalyImpactRow>> GetImpactAsync(
			string anomalyType,
			string serialNo,
			DateTimeOffset fromTs,
			DateTimeOffset toTs,
			int limit,
			CancellationToken cancellationToken)
		{
			var rows = new List<AnomalyImpactRow>();

			using (var conn = new NpgsqlConnection(_connectionString))
			{
				await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
				using (var cmd = new NpgsqlCommand(ImpactSql, conn))
				{
					AddCommonParameters(cmd, anomalyType, serialNo, fromTs, toTs, limit);

					using (var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
					{
						while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
						{
							rows.Add(new AnomalyImpactRow
							{
								AnomalyType = reader.GetString(0),
								EventTs = reader.GetFieldValue<DateTimeOffset>(1),
								MinuteTs = reader.GetFieldValue<DateTimeOffset>(2),
								SerialNo = reader.GetString(3),
								BatchRecordId = ReadNullableInt64(reader, 4),
								LaneNo = reader.GetInt16(5),
								GradeKey = reader.IsDBNull(6) ? null : reader.GetString(6),
								WindowHours = ReadNullableInt32(reader, 7),
								Severity = reader.GetString(8),
								ScorePct = ReadNullableDouble(reader, 9),
								ScoreZ = ReadNullableDouble(reader, 10),
								Lot = reader.IsDBNull(11) ? null : reader.GetString(11),
								Variety = reader.IsDBNull(12) ? null : reader.GetString(12),
								PreAvailabilityRatio = ReadNullableDouble(reader, 13),
								EventAvailabilityRatio = ReadNullableDouble(reader, 14),
								PostAvailabilityRatio = ReadNullableDouble(reader, 15),
								PreThroughputRatio = ReadNullableDouble(reader, 16),
								EventThroughputRatio = ReadNullableDouble(reader, 17),
								PostThroughputRatio = ReadNullableDouble(reader, 18),
								PreQualityRatio = ReadNullableDouble(reader, 19),
								EventQualityRatio = ReadNullableDouble(reader, 20),
								PostQualityRatio = ReadNullableDouble(reader, 21),
								PreOeeScore = ReadNullableDouble(reader, 22),
								EventOeeScore = ReadNullableDouble(reader, 23),
								PostOeeScore = ReadNullableDouble(reader, 24),
								EventTotalFpm = ReadNullableDouble(reader, 25),
								EventMissedFpm = ReadNullableDouble(reader, 26),
								EventCombinedRecycleFpm = ReadNullableDouble(reader, 27),
								EventCupfillPct = ReadNullableDouble(reader, 28),
								EventTph = ReadNullableDouble(reader, 29),
								EventTargetThroughput = ReadNullableDouble(reader, 30),
								DeltaOeeFromPre = ReadNullableDouble(reader, 31),
								DeltaOeePostVsPre = ReadNullableDouble(reader, 32),
								DeltaThroughputFromPre = ReadNullableDouble(reader, 33),
								DeltaThroughputPostVsPre = ReadNullableDouble(reader, 34),
								DeltaQualityFromPre = ReadNullableDouble(reader, 35),
								DeltaQualityPostVsPre = ReadNullableDouble(reader, 36)
							});
						}
					}
				}
			}

			return rows;
		}

		public async Task<IReadOnlyList<AnomalyImpactFamilyRow>> GetImpactFamilySummaryAsync(
			string anomalyType,
			string serialNo,
			DateTimeOffset fromTs,
			DateTimeOffset toTs,
			int limit,
			CancellationToken cancellationToken)
		{
			var rows = new List<AnomalyImpactFamilyRow>();

			using (var conn = new NpgsqlConnection(_connectionString))
			{
				await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
				using (var cmd = new NpgsqlCommand(ImpactFamilySummarySql, conn))
				{
					AddCommonParameters(cmd, anomalyType, serialNo, fromTs, toTs, limit);

					using (var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
					{
						while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
						{
							rows.Add(new AnomalyImpactFamilyRow
							{
								AnomalyType = reader.GetString(0),
								SerialNo = reader.GetString(1),
								LaneNo = reader.GetInt16(2),
								GradeKey = reader.IsDBNull(3) ? null : reader.GetString(3),
								WindowHours = ReadNullableInt32(reader, 4),
								EventCount = reader.GetInt64(5),
								HighCount = reader.GetInt64(6),
								AvgDeltaOeePostVsPre = ReadNullableDouble(reader, 7),
								AvgDeltaThroughputPostVsPre = ReadNullableDouble(reader, 8),
								AvgDeltaQualityPostVsPre = ReadNullableDouble(reader, 9),
								NegativePostImpactCount = reader.GetInt64(10),
								HighSeverityNegativeCount = reader.GetInt64(11),
								MaterialityLabel = reader.IsDBNull(12) ? null : reader.GetString(12)
							});
						}
					}
				}
			}

			return rows;
		}

		public async Task<TuningComparisonReport> GetTuningComparisonAsync(
			string anomalyType,
			string serialNo,
			DateTimeOffset baselineFromTs,
			DateTimeOffset baselineToTs,
			DateTimeOffset candidateFromTs,
			DateTimeOffset candidateToTs,
			int limit,
			CancellationToken cancellationToken)
		{
			var baseline = await GetWindowSummaryAsync(
				anomalyType, serialNo, baselineFromTs, baselineToTs, "baseline", cancellationToken).ConfigureAwait(false);
			var candidate = await GetWindowSummaryAsync(
				anomalyType, serialNo, candidateFromTs, candidateToTs, "candidate", cancellationToken).ConfigureAwait(false);

			var baselineOffenders = await GetOffendersAsync(
				anomalyType, serialNo, baselineFromTs, baselineToTs, limit, cancellationToken).ConfigureAwait(false);
			var candidateOffenders = await GetOffendersAsync(
				anomalyType, serialNo, candidateFromTs, candidateToTs, limit, cancellationToken).ConfigureAwait(false);

			return new TuningComparisonReport
			{
				AnomalyType = NormalizeAnomalyType(anomalyType),
				SerialNo = serialNo,
				Baseline = baseline,
				Candidate = candidate,
				BaselineTopOffenders = baselineOffenders,
				CandidateTopOffenders = candidateOffenders
			};
		}

		private async Task<AnomalyWindowSummary> GetWindowSummaryAsync(
			string anomalyType,
			string serialNo,
			DateTimeOffset fromTs,
			DateTimeOffset toTs,
			string label,
			CancellationToken cancellationToken)
		{
			using (var conn = new NpgsqlConnection(_connectionString))
			{
				await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
				using (var cmd = new NpgsqlCommand(WindowSummarySql, conn))
				{
					AddCommonParameters(cmd, anomalyType, serialNo, fromTs, toTs, 0);

					using (var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
					{
						if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
						{
							return new AnomalyWindowSummary
							{
								Label = label,
								FromTs = fromTs,
								ToTs = toTs
							};
						}

						return new AnomalyWindowSummary
						{
							Label = label,
							FromTs = fromTs,
							ToTs = toTs,
							TotalEvents = reader.GetInt64(0),
							LowCount = reader.GetInt64(1),
							MediumCount = reader.GetInt64(2),
							HighCount = reader.GetInt64(3),
							AvgAbsPct = ReadNullableDouble(reader, 4),
							MaxAbsPct = ReadNullableDouble(reader, 5),
							AvgAbsZ = ReadNullableDouble(reader, 6),
							MaxAbsZ = ReadNullableDouble(reader, 7)
						};
					}
				}
			}
		}

		private static void AddCommonParameters(
			NpgsqlCommand cmd,
			string anomalyType,
			string serialNo,
			DateTimeOffset fromTs,
			DateTimeOffset toTs,
			int limit)
		{
			cmd.Parameters.AddWithValue("anomaly_type", NormalizeAnomalyType(anomalyType));
			cmd.Parameters.AddWithValue("serial_no", serialNo);
			cmd.Parameters.AddWithValue("from_ts", fromTs.UtcDateTime);
			cmd.Parameters.AddWithValue("to_ts", toTs.UtcDateTime);
			if (cmd.CommandText.IndexOf("@limit", StringComparison.Ordinal) >= 0)
				cmd.Parameters.AddWithValue("limit", limit);
		}

		private static string NormalizeAnomalyType(string anomalyType)
		{
			if (string.IsNullOrWhiteSpace(anomalyType) ||
				string.Equals(anomalyType, "both", StringComparison.OrdinalIgnoreCase))
				return string.Empty;

			return anomalyType.Trim().ToLowerInvariant();
		}

		private static int? ReadNullableInt32(NpgsqlDataReader reader, int ordinal)
		{
			return reader.IsDBNull(ordinal) ? (int?)null : Convert.ToInt32(reader.GetValue(ordinal));
		}

		private static long? ReadNullableInt64(NpgsqlDataReader reader, int ordinal)
		{
			return reader.IsDBNull(ordinal) ? (long?)null : Convert.ToInt64(reader.GetValue(ordinal));
		}

		private static double? ReadNullableDouble(NpgsqlDataReader reader, int ordinal)
		{
			return reader.IsDBNull(ordinal) ? (double?)null : Convert.ToDouble(reader.GetValue(ordinal));
		}
	}

	public sealed class AnomalyOffenderRow
	{
		public string AnomalyType { get; set; }
		public string SerialNo { get; set; }
		public long? BatchRecordId { get; set; }
		public int LaneNo { get; set; }
		public string GradeKey { get; set; }
		public int? WindowHours { get; set; }
		public long RepeatCount { get; set; }
		public DateTimeOffset FirstEventTs { get; set; }
		public DateTimeOffset LastEventTs { get; set; }
		public long LowCount { get; set; }
		public long MediumCount { get; set; }
		public long HighCount { get; set; }
		public double? AvgScorePct { get; set; }
		public double? AvgScoreZ { get; set; }
		public double? AvgAbsPct { get; set; }
		public double? MaxAbsPct { get; set; }
		public double? AvgAbsZ { get; set; }
		public double? MaxAbsZ { get; set; }
		public long ActiveMinutes { get; set; }
		public double? SpanMinutes { get; set; }
		public long AffectedBatches { get; set; }
		public long AffectedLots { get; set; }
		public string DirectionLabel { get; set; }
		public double? RuntimeSharePct { get; set; }
	}

	public sealed class AnomalyImpactRow
	{
		public string AnomalyType { get; set; }
		public DateTimeOffset EventTs { get; set; }
		public DateTimeOffset MinuteTs { get; set; }
		public string SerialNo { get; set; }
		public long? BatchRecordId { get; set; }
		public int LaneNo { get; set; }
		public string GradeKey { get; set; }
		public int? WindowHours { get; set; }
		public string Severity { get; set; }
		public double? ScorePct { get; set; }
		public double? ScoreZ { get; set; }
		public string Lot { get; set; }
		public string Variety { get; set; }
		public double? PreAvailabilityRatio { get; set; }
		public double? EventAvailabilityRatio { get; set; }
		public double? PostAvailabilityRatio { get; set; }
		public double? PreThroughputRatio { get; set; }
		public double? EventThroughputRatio { get; set; }
		public double? PostThroughputRatio { get; set; }
		public double? PreQualityRatio { get; set; }
		public double? EventQualityRatio { get; set; }
		public double? PostQualityRatio { get; set; }
		public double? PreOeeScore { get; set; }
		public double? EventOeeScore { get; set; }
		public double? PostOeeScore { get; set; }
		public double? EventTotalFpm { get; set; }
		public double? EventMissedFpm { get; set; }
		public double? EventCombinedRecycleFpm { get; set; }
		public double? EventCupfillPct { get; set; }
		public double? EventTph { get; set; }
		public double? EventTargetThroughput { get; set; }
		public double? DeltaOeeFromPre { get; set; }
		public double? DeltaOeePostVsPre { get; set; }
		public double? DeltaThroughputFromPre { get; set; }
		public double? DeltaThroughputPostVsPre { get; set; }
		public double? DeltaQualityFromPre { get; set; }
		public double? DeltaQualityPostVsPre { get; set; }
	}

	public sealed class AnomalyWindowSummary
	{
		public string Label { get; set; }
		public DateTimeOffset FromTs { get; set; }
		public DateTimeOffset ToTs { get; set; }
		public long TotalEvents { get; set; }
		public long LowCount { get; set; }
		public long MediumCount { get; set; }
		public long HighCount { get; set; }
		public double? AvgAbsPct { get; set; }
		public double? MaxAbsPct { get; set; }
		public double? AvgAbsZ { get; set; }
		public double? MaxAbsZ { get; set; }
	}

	public sealed class TuningComparisonReport
	{
		public string AnomalyType { get; set; }
		public string SerialNo { get; set; }
		public AnomalyWindowSummary Baseline { get; set; }
		public AnomalyWindowSummary Candidate { get; set; }
		public IReadOnlyList<AnomalyOffenderRow> BaselineTopOffenders { get; set; }
		public IReadOnlyList<AnomalyOffenderRow> CandidateTopOffenders { get; set; }
	}

	public sealed class AnomalyImpactFamilyRow
	{
		public string AnomalyType { get; set; }
		public string SerialNo { get; set; }
		public int LaneNo { get; set; }
		public string GradeKey { get; set; }
		public int? WindowHours { get; set; }
		public long EventCount { get; set; }
		public long HighCount { get; set; }
		public double? AvgDeltaOeePostVsPre { get; set; }
		public double? AvgDeltaThroughputPostVsPre { get; set; }
		public double? AvgDeltaQualityPostVsPre { get; set; }
		public long NegativePostImpactCount { get; set; }
		public long HighSeverityNegativeCount { get; set; }
		public string MaterialityLabel { get; set; }
	}
}
