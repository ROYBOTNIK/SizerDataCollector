using System;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;
using NpgsqlTypes;
using SizerDataCollector.Core.Logging;

namespace SizerDataCollector.Core.AnomalyDetection
{
	/// <summary>
	/// Persists anomaly events to oee.grade_lane_anomalies in TimescaleDB.
	/// </summary>
	public sealed class DatabaseAlarmSink : IAlarmSink
	{
		private const string InsertSql = @"
INSERT INTO oee.grade_lane_anomalies
    (event_ts, serial_no, batch_record_id, lane_no, grade_key, qty, pct, anomaly_score, severity, explanation, model_version, delivered_to)
VALUES
    (@event_ts, @serial_no, @batch_record_id, @lane_no, @grade_key, @qty, @pct, @anomaly_score, @severity, @explanation::jsonb, @model_version, @delivered_to);";

		private readonly string _connectionString;

		public DatabaseAlarmSink(string connectionString)
		{
			_connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
		}

		public async Task DeliverAsync(AnomalyEvent evt, CancellationToken cancellationToken)
		{
			try
			{
				using (var connection = new NpgsqlConnection(_connectionString))
				{
					await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
					using (var cmd = new NpgsqlCommand(InsertSql, connection))
					{
						cmd.Parameters.Add(new NpgsqlParameter("event_ts", NpgsqlDbType.TimestampTz) { Value = evt.EventTs.UtcDateTime });
						cmd.Parameters.AddWithValue("serial_no", evt.SerialNo);
						cmd.Parameters.AddWithValue("batch_record_id", evt.BatchRecordId);
						cmd.Parameters.AddWithValue("lane_no", (short)evt.LaneNo);
						cmd.Parameters.AddWithValue("grade_key", evt.GradeKey);
						cmd.Parameters.AddWithValue("qty", evt.Qty);
						cmd.Parameters.AddWithValue("pct", evt.Pct);
						cmd.Parameters.AddWithValue("anomaly_score", evt.AnomalyScore);
						cmd.Parameters.AddWithValue("severity", evt.Severity);
						cmd.Parameters.AddWithValue("explanation", (object)evt.ExplanationJson ?? DBNull.Value);
						cmd.Parameters.AddWithValue("model_version", evt.ModelVersion);
						cmd.Parameters.AddWithValue("delivered_to", evt.DeliveredTo ?? string.Empty);

						await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
					}
				}

				Logger.Log($"Anomaly event persisted to DB: lane={evt.LaneNo}, grade={evt.GradeKey}, severity={evt.Severity}",
					level: LogLevel.Debug);
			}
			catch (Exception ex) when (!(ex is OperationCanceledException))
			{
				Logger.Log($"Failed to persist anomaly event to DB: lane={evt.LaneNo}, grade={evt.GradeKey}", ex, LogLevel.Warn);
			}
		}
	}
}
