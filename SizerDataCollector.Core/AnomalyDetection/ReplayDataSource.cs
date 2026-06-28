using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;
using NpgsqlTypes;
using SizerDataCollector.Core.Logging;

namespace SizerDataCollector.Core.AnomalyDetection
{
	/// <summary>
	/// Row returned from the replay query against public.metrics.
	/// </summary>
	public sealed class ReplayRow
	{
		public DateTimeOffset Ts { get; set; }
		public string SerialNo { get; set; }
		public long BatchRecordId { get; set; }
		public string ValueJson { get; set; }
	}

	/// <summary>
	/// Reads raw lanes_grade_fpm JSON payloads from public.metrics for
	/// offline replay through the anomaly detector.
	/// </summary>
	public sealed class ReplayDataSource
	{
		private const string QuerySql = @"
SELECT ts, serial_no, batch_record_id, value_json::text
FROM public.metrics
WHERE metric = 'lanes_grade_fpm'
  AND serial_no = @serial
  AND ts >= @from_ts
  AND ts <= @to_ts
ORDER BY ts ASC;";

		private readonly string _connectionString;

		public ReplayDataSource(string connectionString)
		{
			_connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
		}

		public async Task<List<ReplayRow>> LoadAsync(
			string serialNo,
			DateTimeOffset fromTs,
			DateTimeOffset toTs,
			CancellationToken cancellationToken)
		{
			var rows = new List<ReplayRow>();

			Logger.Log($"Replay: loading lanes_grade_fpm from {fromTs:O} to {toTs:O} for serial '{serialNo}'.");

			using (var connection = new NpgsqlConnection(_connectionString))
			{
				await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
				using (var cmd = new NpgsqlCommand(QuerySql, connection))
				{
					cmd.Parameters.Add(new NpgsqlParameter("serial", NpgsqlDbType.Text) { Value = serialNo });
					cmd.Parameters.Add(new NpgsqlParameter("from_ts", NpgsqlDbType.TimestampTz) { Value = fromTs.UtcDateTime });
					cmd.Parameters.Add(new NpgsqlParameter("to_ts", NpgsqlDbType.TimestampTz) { Value = toTs.UtcDateTime });

					using (var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
					{
						while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
						{
							rows.Add(new ReplayRow
							{
								Ts = reader.GetFieldValue<DateTimeOffset>(0),
								SerialNo = reader.GetString(1),
								BatchRecordId = reader.GetInt64(2),
								ValueJson = reader.IsDBNull(3) ? null : reader.GetString(3)
							});
						}
					}
				}
			}

			Logger.Log($"Replay: loaded {rows.Count} lanes_grade_fpm rows.");
			return rows;
		}
	}
}
