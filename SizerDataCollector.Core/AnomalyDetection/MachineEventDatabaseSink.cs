using System;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;
using NpgsqlTypes;
using SizerDataCollector.Core.Logging;

namespace SizerDataCollector.Core.AnomalyDetection
{
	public sealed class MachineEventDatabaseSink
	{
		private const string DowntimeInsertSql = @"
INSERT INTO oee.downtime_events
    (start_ts, end_ts, duration_minutes, serial_no, batch_record_id, lot, variety,
     avg_availability_ratio, min_availability_ratio, avg_throughput_ratio, min_throughput_ratio,
     avg_total_fpm, min_total_fpm, avg_oee_score, reason, overlaps_lot_transition,
     explanation, model_version, delivered_to)
VALUES
    (@start_ts, @end_ts, @duration_minutes, @serial_no, @batch_record_id, @lot, @variety,
     @avg_availability_ratio, @min_availability_ratio, @avg_throughput_ratio, @min_throughput_ratio,
     @avg_total_fpm, @min_total_fpm, @avg_oee_score, @reason, @overlaps_lot_transition,
     @explanation::jsonb, @model_version, @delivered_to)
ON CONFLICT (serial_no, start_ts, end_ts) DO NOTHING;";

		private const string SlowdownInsertSql = @"
INSERT INTO oee.slowdown_events
    (start_ts, end_ts, duration_minutes, serial_no, batch_record_id, lot, variety,
     avg_availability_ratio, min_availability_ratio, avg_throughput_ratio, min_throughput_ratio,
     avg_total_fpm, min_total_fpm, avg_oee_score, reason, overlaps_lot_transition,
     explanation, model_version, delivered_to)
VALUES
    (@start_ts, @end_ts, @duration_minutes, @serial_no, @batch_record_id, @lot, @variety,
     @avg_availability_ratio, @min_availability_ratio, @avg_throughput_ratio, @min_throughput_ratio,
     @avg_total_fpm, @min_total_fpm, @avg_oee_score, @reason, @overlaps_lot_transition,
     @explanation::jsonb, @model_version, @delivered_to)
ON CONFLICT (serial_no, start_ts, end_ts) DO NOTHING;";

		private readonly string _connectionString;

		public MachineEventDatabaseSink(string connectionString)
		{
			_connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
		}

		public async Task<bool> DeliverAsync(MachineEvent evt, CancellationToken cancellationToken)
		{
			try
			{
				using (var connection = new NpgsqlConnection(_connectionString))
				{
					await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
					var sql = string.Equals(evt.EventType, "downtime", StringComparison.OrdinalIgnoreCase)
						? DowntimeInsertSql
						: SlowdownInsertSql;
					using (var cmd = new NpgsqlCommand(sql, connection))
					{
						AddParameters(cmd, evt);
						var inserted = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
						return inserted > 0;
					}
				}
			}
			catch (Exception ex) when (!(ex is OperationCanceledException))
			{
				Logger.Log("Machine event persistence failed.", ex, LogLevel.Warn);
				return false;
			}
		}

		private static void AddParameters(NpgsqlCommand cmd, MachineEvent evt)
		{
			cmd.Parameters.Add(new NpgsqlParameter("start_ts", NpgsqlDbType.TimestampTz) { Value = evt.StartTs.UtcDateTime });
			cmd.Parameters.Add(new NpgsqlParameter("end_ts", NpgsqlDbType.TimestampTz) { Value = evt.EndTs.UtcDateTime });
			cmd.Parameters.AddWithValue("duration_minutes", evt.DurationMinutes);
			cmd.Parameters.AddWithValue("serial_no", evt.SerialNo);
			cmd.Parameters.AddWithValue("batch_record_id", (object)evt.BatchRecordId ?? DBNull.Value);
			cmd.Parameters.AddWithValue("lot", (object)evt.Lot ?? DBNull.Value);
			cmd.Parameters.AddWithValue("variety", (object)evt.Variety ?? DBNull.Value);
			cmd.Parameters.AddWithValue("avg_availability_ratio", (object)evt.AvgAvailabilityRatio ?? DBNull.Value);
			cmd.Parameters.AddWithValue("min_availability_ratio", (object)evt.MinAvailabilityRatio ?? DBNull.Value);
			cmd.Parameters.AddWithValue("avg_throughput_ratio", (object)evt.AvgThroughputRatio ?? DBNull.Value);
			cmd.Parameters.AddWithValue("min_throughput_ratio", (object)evt.MinThroughputRatio ?? DBNull.Value);
			cmd.Parameters.AddWithValue("avg_total_fpm", (object)evt.AvgTotalFpm ?? DBNull.Value);
			cmd.Parameters.AddWithValue("min_total_fpm", (object)evt.MinTotalFpm ?? DBNull.Value);
			cmd.Parameters.AddWithValue("avg_oee_score", (object)evt.AvgOeeScore ?? DBNull.Value);
			cmd.Parameters.AddWithValue("reason", (object)evt.Reason ?? DBNull.Value);
			cmd.Parameters.AddWithValue("overlaps_lot_transition", evt.OverlapsLotTransition);
			cmd.Parameters.AddWithValue("explanation", (object)evt.ExplanationJson ?? DBNull.Value);
			cmd.Parameters.AddWithValue("model_version", evt.ModelVersion ?? string.Empty);
			cmd.Parameters.AddWithValue("delivered_to", evt.DeliveredTo ?? string.Empty);
		}
	}
}
