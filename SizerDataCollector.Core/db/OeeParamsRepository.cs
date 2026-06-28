using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;

namespace SizerDataCollector.Core.Db
{
	public sealed class OeeParamsRepository
	{
		private readonly string _connectionString;

		public OeeParamsRepository(string connectionString)
		{
			_connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
		}

		// ── Quality Params ──

		public async Task<QualityParamsRow> GetQualityParamsAsync(string serialNo, CancellationToken cancellationToken)
		{
			const string sql = @"
SELECT serial_no, tgt_good, tgt_peddler, tgt_bad, tgt_recycle,
       w_good, w_peddler, w_bad, w_recycle, sig_k, updated_at
FROM oee.quality_params
WHERE serial_no = @serial_no;";

			using (var conn = new NpgsqlConnection(_connectionString))
			{
				await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
				using (var cmd = new NpgsqlCommand(sql, conn))
				{
					cmd.Parameters.AddWithValue("serial_no", serialNo);
					using (var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
					{
						if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
							return null;

						return new QualityParamsRow
						{
							SerialNo = reader.GetString(0),
							TgtGood = reader.GetDecimal(1),
							TgtPeddler = reader.GetDecimal(2),
							TgtBad = reader.GetDecimal(3),
							TgtRecycle = reader.GetDecimal(4),
							WGood = reader.GetDecimal(5),
							WPeddler = reader.GetDecimal(6),
							WBad = reader.GetDecimal(7),
							WRecycle = reader.GetDecimal(8),
							SigK = reader.GetDecimal(9),
							UpdatedAt = reader.GetFieldValue<DateTimeOffset>(10)
						};
					}
				}
			}
		}

		public async Task UpsertQualityParamsAsync(
			string serialNo,
			decimal tgtGood, decimal tgtPeddler, decimal tgtBad, decimal tgtRecycle,
			decimal wGood, decimal wPeddler, decimal wBad, decimal wRecycle,
			decimal sigK,
			CancellationToken cancellationToken)
		{
			const string sql = @"
INSERT INTO oee.quality_params (serial_no, tgt_good, tgt_peddler, tgt_bad, tgt_recycle,
                                w_good, w_peddler, w_bad, w_recycle, sig_k, updated_at)
VALUES (@serial_no, @tgt_good, @tgt_peddler, @tgt_bad, @tgt_recycle,
        @w_good, @w_peddler, @w_bad, @w_recycle, @sig_k, now())
ON CONFLICT (serial_no) DO UPDATE SET
    tgt_good    = EXCLUDED.tgt_good,
    tgt_peddler = EXCLUDED.tgt_peddler,
    tgt_bad     = EXCLUDED.tgt_bad,
    tgt_recycle = EXCLUDED.tgt_recycle,
    w_good      = EXCLUDED.w_good,
    w_peddler   = EXCLUDED.w_peddler,
    w_bad       = EXCLUDED.w_bad,
    w_recycle   = EXCLUDED.w_recycle,
    sig_k       = EXCLUDED.sig_k,
    updated_at  = now();";

			using (var conn = new NpgsqlConnection(_connectionString))
			{
				await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
				using (var cmd = new NpgsqlCommand(sql, conn))
				{
					cmd.Parameters.AddWithValue("serial_no", serialNo);
					cmd.Parameters.AddWithValue("tgt_good", tgtGood);
					cmd.Parameters.AddWithValue("tgt_peddler", tgtPeddler);
					cmd.Parameters.AddWithValue("tgt_bad", tgtBad);
					cmd.Parameters.AddWithValue("tgt_recycle", tgtRecycle);
					cmd.Parameters.AddWithValue("w_good", wGood);
					cmd.Parameters.AddWithValue("w_peddler", wPeddler);
					cmd.Parameters.AddWithValue("w_bad", wBad);
					cmd.Parameters.AddWithValue("w_recycle", wRecycle);
					cmd.Parameters.AddWithValue("sig_k", sigK);
					await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
				}
			}
		}

		// ── Performance Params ──

		public async Task<PerfParamsRow> GetPerfParamsAsync(string serialNo, CancellationToken cancellationToken)
		{
			const string sql = @"
SELECT serial_no, min_effective_fpm, low_ratio_threshold, cap_asymptote, updated_at
FROM oee.perf_params
WHERE serial_no = @serial_no;";

			using (var conn = new NpgsqlConnection(_connectionString))
			{
				await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
				using (var cmd = new NpgsqlCommand(sql, conn))
				{
					cmd.Parameters.AddWithValue("serial_no", serialNo);
					using (var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
					{
						if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
							return null;

						return new PerfParamsRow
						{
							SerialNo = reader.GetString(0),
							MinEffectiveFpm = reader.GetDecimal(1),
							LowRatioThreshold = reader.GetDecimal(2),
							CapAsymptote = reader.GetDecimal(3),
							UpdatedAt = reader.GetFieldValue<DateTimeOffset>(4)
						};
					}
				}
			}
		}

		public async Task UpsertPerfParamsAsync(
			string serialNo,
			decimal minEffectiveFpm, decimal lowRatioThreshold, decimal capAsymptote,
			CancellationToken cancellationToken)
		{
			const string sql = @"
INSERT INTO oee.perf_params (serial_no, min_effective_fpm, low_ratio_threshold, cap_asymptote, updated_at)
VALUES (@serial_no, @min_effective_fpm, @low_ratio_threshold, @cap_asymptote, now())
ON CONFLICT (serial_no) DO UPDATE SET
    min_effective_fpm   = EXCLUDED.min_effective_fpm,
    low_ratio_threshold = EXCLUDED.low_ratio_threshold,
    cap_asymptote       = EXCLUDED.cap_asymptote,
    updated_at          = now();";

			using (var conn = new NpgsqlConnection(_connectionString))
			{
				await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
				using (var cmd = new NpgsqlCommand(sql, conn))
				{
					cmd.Parameters.AddWithValue("serial_no", serialNo);
					cmd.Parameters.AddWithValue("min_effective_fpm", minEffectiveFpm);
					cmd.Parameters.AddWithValue("low_ratio_threshold", lowRatioThreshold);
					cmd.Parameters.AddWithValue("cap_asymptote", capAsymptote);
					await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
				}
			}
		}

		// ── Band Definitions ──

		public async Task<IReadOnlyList<BandRow>> GetBandsAsync(string serialNo, string metricType, CancellationToken cancellationToken)
		{
			const string sql = @"
SELECT metric_type, band_name, lower_bound, upper_bound, effective_date, is_active, created_by,
       source, confidence, observed_minutes, tuned_from_ts, tuned_to_ts, notes
FROM oee.band_definitions
WHERE machine_serial_no = @serial_no
  AND metric_type = @metric_type
ORDER BY lower_bound;";

			var rows = new List<BandRow>();
			using (var conn = new NpgsqlConnection(_connectionString))
			{
				await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
				using (var cmd = new NpgsqlCommand(sql, conn))
				{
					cmd.Parameters.AddWithValue("serial_no", serialNo);
					cmd.Parameters.AddWithValue("metric_type", NormalizeMetricType(metricType));
					using (var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
					{
						while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
						{
							rows.Add(new BandRow
							{
								MetricType = reader.GetString(0),
								BandName = reader.GetString(1),
								LowerBound = reader.GetDecimal(2),
								UpperBound = reader.GetDecimal(3),
								EffectiveDate = reader.GetDateTime(4),
								IsActive = reader.GetBoolean(5),
								CreatedBy = reader.IsDBNull(6) ? null : reader.GetString(6),
								Source = reader.IsDBNull(7) ? null : reader.GetString(7),
								Confidence = reader.IsDBNull(8) ? (decimal?)null : reader.GetDecimal(8),
								ObservedMinutes = reader.IsDBNull(9) ? (int?)null : reader.GetInt32(9),
								TunedFromTs = reader.IsDBNull(10) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(10),
								TunedToTs = reader.IsDBNull(11) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(11),
								Notes = reader.IsDBNull(12) ? null : reader.GetString(12)
							});
						}
					}
				}
			}
			return rows;
		}

		public async Task UpsertBandAsync(
			string serialNo, string metricType, string bandName, decimal lowerBound, decimal upperBound,
			CancellationToken cancellationToken)
		{
			await UpsertBandAsync(serialNo, metricType, bandName, lowerBound, upperBound,
				"cli", null, null, null, null, null, cancellationToken).ConfigureAwait(false);
		}

		public async Task UpsertBandAsync(
			string serialNo, string metricType, string bandName, decimal lowerBound, decimal upperBound,
			string source, decimal? confidence, int? observedMinutes, DateTimeOffset? tunedFromTs, DateTimeOffset? tunedToTs, string notes,
			CancellationToken cancellationToken)
		{
			const string sql = @"
INSERT INTO oee.band_definitions (
    machine_serial_no, metric_type, effective_date, band_name, lower_bound, upper_bound,
    is_active, created_by, source, confidence, observed_minutes, tuned_from_ts, tuned_to_ts, notes
)
VALUES (
    @serial_no, @metric_type, CURRENT_DATE, @band_name, @lower_bound, @upper_bound,
    true, 'cli', @source, @confidence, @observed_minutes, @tuned_from_ts, @tuned_to_ts, @notes
)
ON CONFLICT (machine_serial_no, metric_type, effective_date, band_name) DO UPDATE SET
    lower_bound      = EXCLUDED.lower_bound,
    upper_bound      = EXCLUDED.upper_bound,
    is_active        = true,
    created_by       = 'cli',
    source           = EXCLUDED.source,
    confidence       = EXCLUDED.confidence,
    observed_minutes = EXCLUDED.observed_minutes,
    tuned_from_ts    = EXCLUDED.tuned_from_ts,
    tuned_to_ts      = EXCLUDED.tuned_to_ts,
    notes            = EXCLUDED.notes;";

			var normalizedMetric = NormalizeMetricType(metricType);
			using (var conn = new NpgsqlConnection(_connectionString))
			{
				await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
				using (var cmd = new NpgsqlCommand(sql, conn))
				{
					cmd.Parameters.AddWithValue("serial_no", serialNo);
					cmd.Parameters.AddWithValue("metric_type", normalizedMetric);
					cmd.Parameters.AddWithValue("band_name", bandName);
					cmd.Parameters.AddWithValue("lower_bound", lowerBound);
					cmd.Parameters.AddWithValue("upper_bound", upperBound);
					cmd.Parameters.AddWithValue("source", (object)source ?? DBNull.Value);
					cmd.Parameters.AddWithValue("confidence", (object)confidence ?? DBNull.Value);
					cmd.Parameters.AddWithValue("observed_minutes", (object)observedMinutes ?? DBNull.Value);
					cmd.Parameters.AddWithValue("tuned_from_ts", tunedFromTs.HasValue ? (object)tunedFromTs.Value : DBNull.Value);
					cmd.Parameters.AddWithValue("tuned_to_ts", tunedToTs.HasValue ? (object)tunedToTs.Value : DBNull.Value);
					cmd.Parameters.AddWithValue("notes", (object)notes ?? DBNull.Value);
					await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
				}
			}
		}

		public async Task UpsertBandsAsync(
			string serialNo, string metricType, IEnumerable<TunedBand> bands,
			string source, decimal? confidence, int? observedMinutes, DateTimeOffset? tunedFromTs, DateTimeOffset? tunedToTs, string notes,
			CancellationToken cancellationToken)
		{
			if (bands == null) throw new ArgumentNullException(nameof(bands));

			foreach (var band in bands)
			{
				await UpsertBandAsync(serialNo, metricType, band.BandName, band.LowerBound, band.UpperBound,
					source, confidence, observedMinutes, tunedFromTs, tunedToTs, notes, cancellationToken).ConfigureAwait(false);
			}
		}

		public async Task DeactivateBandAsync(string serialNo, string metricType, string bandName, CancellationToken cancellationToken)
		{
			const string sql = @"
UPDATE oee.band_definitions
SET is_active = false
WHERE machine_serial_no = @serial_no
  AND metric_type = @metric_type
  AND band_name = @band_name;";

			using (var conn = new NpgsqlConnection(_connectionString))
			{
				await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
				using (var cmd = new NpgsqlCommand(sql, conn))
				{
					cmd.Parameters.AddWithValue("serial_no", serialNo);
					cmd.Parameters.AddWithValue("metric_type", NormalizeMetricType(metricType));
					cmd.Parameters.AddWithValue("band_name", bandName);
					await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
				}
			}
		}

		public async Task<IReadOnlyList<double>> GetThroughputRatiosAsync(
			string serialNo, DateTimeOffset fromTs, DateTimeOffset toTs, CancellationToken cancellationToken)
		{
			const string sql = @"
SELECT throughput_ratio
FROM oee.v_operational_minute_batch
WHERE serial_no = @serial_no
  AND minute_ts >= @from_ts
  AND minute_ts < @to_ts
  AND target_throughput > 0
  AND total_fpm > 0
  AND throughput_ratio IS NOT NULL
ORDER BY minute_ts ASC;";

			var values = new List<double>();
			using (var conn = new NpgsqlConnection(_connectionString))
			{
				await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
				using (var cmd = new NpgsqlCommand(sql, conn))
				{
					cmd.Parameters.AddWithValue("serial_no", serialNo);
					cmd.Parameters.AddWithValue("from_ts", fromTs.UtcDateTime);
					cmd.Parameters.AddWithValue("to_ts", toTs.UtcDateTime);
					using (var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
					{
						while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
						{
							if (!reader.IsDBNull(0))
							{
								values.Add(Convert.ToDouble(reader.GetValue(0)));
							}
						}
					}
				}
			}

			return values;
		}

		private static string NormalizeMetricType(string metricType)
		{
			return string.IsNullOrWhiteSpace(metricType) ? "oee" : metricType.Trim().ToLowerInvariant();
		}
	}

	public sealed class QualityParamsRow
	{
		public string SerialNo { get; set; }
		public decimal TgtGood { get; set; }
		public decimal TgtPeddler { get; set; }
		public decimal TgtBad { get; set; }
		public decimal TgtRecycle { get; set; }
		public decimal WGood { get; set; }
		public decimal WPeddler { get; set; }
		public decimal WBad { get; set; }
		public decimal WRecycle { get; set; }
		public decimal SigK { get; set; }
		public DateTimeOffset UpdatedAt { get; set; }
	}

	public sealed class PerfParamsRow
	{
		public string SerialNo { get; set; }
		public decimal MinEffectiveFpm { get; set; }
		public decimal LowRatioThreshold { get; set; }
		public decimal CapAsymptote { get; set; }
		public DateTimeOffset UpdatedAt { get; set; }
	}

	public sealed class BandRow
	{
		public string MetricType { get; set; }
		public string BandName { get; set; }
		public decimal LowerBound { get; set; }
		public decimal UpperBound { get; set; }
		public DateTime EffectiveDate { get; set; }
		public bool IsActive { get; set; }
		public string CreatedBy { get; set; }
		public string Source { get; set; }
		public decimal? Confidence { get; set; }
		public int? ObservedMinutes { get; set; }
		public DateTimeOffset? TunedFromTs { get; set; }
		public DateTimeOffset? TunedToTs { get; set; }
		public string Notes { get; set; }
	}
}
