using System;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;
using NpgsqlTypes;
using SizerDataCollector.Core.Logging;

namespace SizerDataCollector.Core.AnomalyDetection
{
	/// <summary>
	/// Persists size anomaly events to oee.lane_size_anomalies in TimescaleDB.
	/// </summary>
	public sealed class SizeDatabaseAlarmSink
	{
		private const string InsertSql = @"
INSERT INTO oee.lane_size_anomalies
    (event_ts, serial_no, lane_no, window_hours, lane_avg_size, machine_avg_size, pct_deviation, z_score, severity, model_version, delivered_to)
VALUES
    (@event_ts, @serial_no, @lane_no, @window_hours, @lane_avg_size, @machine_avg_size, @pct_deviation, @z_score, @severity, @model_version, @delivered_to);";

		private readonly string _connectionString;

		public SizeDatabaseAlarmSink(string connectionString)
		{
			_connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
		}

		public async Task DeliverAsync(SizeAnomalyEvent evt, CancellationToken cancellationToken)
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
						cmd.Parameters.AddWithValue("lane_no", (short)evt.LaneNo);
						cmd.Parameters.AddWithValue("window_hours", evt.WindowHours);
						cmd.Parameters.AddWithValue("lane_avg_size", evt.LaneAvgSize);
						cmd.Parameters.AddWithValue("machine_avg_size", evt.MachineAvgSize);
						cmd.Parameters.AddWithValue("pct_deviation", evt.PctDeviation);
						cmd.Parameters.AddWithValue("z_score", evt.ZScore);
						cmd.Parameters.AddWithValue("severity", evt.Severity);
						cmd.Parameters.AddWithValue("model_version", evt.ModelVersion);
						cmd.Parameters.AddWithValue("delivered_to", evt.DeliveredTo ?? string.Empty);

						await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
					}
				}

				Logger.Log($"Size anomaly persisted to DB: lane={evt.LaneNo}, pctDev={evt.PctDeviation:F1}%, severity={evt.Severity}",
					level: LogLevel.Debug);
			}
			catch (Exception ex) when (!(ex is OperationCanceledException))
			{
				Logger.Log($"Failed to persist size anomaly to DB: lane={evt.LaneNo}", ex, LogLevel.Warn);
			}
		}
	}
}
