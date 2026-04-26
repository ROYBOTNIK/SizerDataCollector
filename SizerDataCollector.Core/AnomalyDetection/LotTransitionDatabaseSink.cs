using System;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;
using NpgsqlTypes;
using SizerDataCollector.Core.Logging;

namespace SizerDataCollector.Core.AnomalyDetection
{
	public sealed class LotTransitionDatabaseSink
	{
		private const string InsertSql = @"
INSERT INTO oee.lot_transition_throughput_events
    (transition_ts, serial_no, outgoing_batch_record_id, incoming_batch_record_id,
     outgoing_grower_code, incoming_grower_code, outgoing_label, incoming_label,
     disruption_start_ts, trough_ts, stable_recovery_ts, disruption_duration_minutes,
     pre_stable_fpm, trough_fpm, post_stable_fpm, pre_peak_fpm, post_peak_fpm,
     opportunity_window_start_ts, opportunity_window_end_ts, opportunity_window_minutes,
     integrated_fpm_minutes, counterfactual_fpm_minutes, fruit_opportunity_shortfall,
     availability_avg_during_disruption, availability_avg_opportunity_window,
     explanation, model_version, delivered_to)
VALUES
    (@transition_ts, @serial_no, @outgoing_batch_record_id, @incoming_batch_record_id,
     @outgoing_grower_code, @incoming_grower_code, @outgoing_label, @incoming_label,
     @disruption_start_ts, @trough_ts, @stable_recovery_ts, @disruption_duration_minutes,
     @pre_stable_fpm, @trough_fpm, @post_stable_fpm, @pre_peak_fpm, @post_peak_fpm,
     @opportunity_window_start_ts, @opportunity_window_end_ts, @opportunity_window_minutes,
     @integrated_fpm_minutes, @counterfactual_fpm_minutes, @fruit_opportunity_shortfall,
     @availability_avg_during_disruption, @availability_avg_opportunity_window,
     @explanation::jsonb, @model_version, @delivered_to)
ON CONFLICT (serial_no, incoming_batch_record_id) DO NOTHING;";

		private readonly string _connectionString;

		public LotTransitionDatabaseSink(string connectionString)
		{
			_connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
		}

		public async Task<bool> DeliverAsync(LotTransitionEvent evt, CancellationToken cancellationToken)
		{
			try
			{
				using (var connection = new NpgsqlConnection(_connectionString))
				{
					await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
					using (var cmd = new NpgsqlCommand(InsertSql, connection))
					{
						cmd.Parameters.Add(new NpgsqlParameter("transition_ts", NpgsqlDbType.TimestampTz) { Value = evt.TransitionTs.UtcDateTime });
						cmd.Parameters.AddWithValue("serial_no", evt.SerialNo);
						cmd.Parameters.AddWithValue("outgoing_batch_record_id", evt.OutgoingBatchRecordId);
						cmd.Parameters.AddWithValue("incoming_batch_record_id", evt.IncomingBatchRecordId);
						cmd.Parameters.AddWithValue("outgoing_grower_code", (object)evt.OutgoingGrowerCode ?? DBNull.Value);
						cmd.Parameters.AddWithValue("incoming_grower_code", (object)evt.IncomingGrowerCode ?? DBNull.Value);
						cmd.Parameters.AddWithValue("outgoing_label", (object)evt.OutgoingLabel ?? DBNull.Value);
						cmd.Parameters.AddWithValue("incoming_label", (object)evt.IncomingLabel ?? DBNull.Value);
						cmd.Parameters.Add(new NpgsqlParameter("disruption_start_ts", NpgsqlDbType.TimestampTz) { Value = evt.DisruptionStartTs.UtcDateTime });
						cmd.Parameters.Add(new NpgsqlParameter("trough_ts", NpgsqlDbType.TimestampTz) { Value = evt.TroughTs.UtcDateTime });
						cmd.Parameters.Add(new NpgsqlParameter("stable_recovery_ts", NpgsqlDbType.TimestampTz) { Value = evt.StableRecoveryTs.UtcDateTime });
						cmd.Parameters.AddWithValue("disruption_duration_minutes", evt.DisruptionDurationMinutes);
						cmd.Parameters.AddWithValue("pre_stable_fpm", evt.PreStableFpm);
						cmd.Parameters.AddWithValue("trough_fpm", evt.TroughFpm);
						cmd.Parameters.AddWithValue("post_stable_fpm", evt.PostStableFpm);
						cmd.Parameters.AddWithValue("pre_peak_fpm", evt.PrePeakFpm);
						cmd.Parameters.AddWithValue("post_peak_fpm", evt.PostPeakFpm);
						cmd.Parameters.Add(new NpgsqlParameter("opportunity_window_start_ts", NpgsqlDbType.TimestampTz) { Value = evt.OpportunityWindowStartTs.UtcDateTime });
						cmd.Parameters.Add(new NpgsqlParameter("opportunity_window_end_ts", NpgsqlDbType.TimestampTz) { Value = evt.OpportunityWindowEndTs.UtcDateTime });
						cmd.Parameters.AddWithValue("opportunity_window_minutes", evt.OpportunityWindowMinutes);
						cmd.Parameters.AddWithValue("integrated_fpm_minutes", evt.IntegratedFpmMinutes);
						cmd.Parameters.AddWithValue("counterfactual_fpm_minutes", evt.CounterfactualFpmMinutes);
						cmd.Parameters.AddWithValue("fruit_opportunity_shortfall", evt.FruitOpportunityShortfall);
						cmd.Parameters.AddWithValue("availability_avg_during_disruption", (object)evt.AvailabilityAvgDuringDisruption ?? DBNull.Value);
						cmd.Parameters.AddWithValue("availability_avg_opportunity_window", (object)evt.AvailabilityAvgOpportunityWindow ?? DBNull.Value);
						cmd.Parameters.AddWithValue("explanation", (object)evt.ExplanationJson ?? DBNull.Value);
						cmd.Parameters.AddWithValue("model_version", evt.ModelVersion);
						cmd.Parameters.AddWithValue("delivered_to", evt.DeliveredTo ?? string.Empty);

						var inserted = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
						if (inserted > 0)
						{
							Logger.Log($"Lot transition event persisted to DB: serial={evt.SerialNo}, incoming_batch={evt.IncomingBatchRecordId}, shortfall={evt.FruitOpportunityShortfall:F0}",
								level: LogLevel.Debug);
						}

						return inserted > 0;
					}
				}
			}
			catch (Exception ex) when (!(ex is OperationCanceledException))
			{
				Logger.Log($"Failed to persist lot transition event: serial={evt.SerialNo}, incoming_batch={evt.IncomingBatchRecordId}", ex, LogLevel.Warn);
				return false;
			}
		}
	}
}
