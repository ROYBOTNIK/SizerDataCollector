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
     stable_counterfactual_fpm_minutes, stable_fruit_opportunity_shortfall,
     stable_throughput_loss_ratio, stable_equivalent_lost_minutes,
     peak_throughput_loss_ratio, peak_equivalent_lost_minutes,
     target_throughput, target_counterfactual_fpm_minutes,
     target_fruit_opportunity_shortfall, target_equivalent_lost_minutes,
     break_overlap_detected, break_overlap_minutes,
     break_adjusted_disruption_minutes, break_adjusted_opportunity_window_minutes,
     break_adjusted_stable_fruit_opportunity_shortfall,
     break_adjusted_stable_equivalent_lost_minutes,
     break_adjusted_stable_throughput_loss_ratio,
     availability_avg_during_disruption, availability_avg_opportunity_window,
     explanation, model_version, delivered_to)
VALUES
    (@transition_ts, @serial_no, @outgoing_batch_record_id, @incoming_batch_record_id,
     @outgoing_grower_code, @incoming_grower_code, @outgoing_label, @incoming_label,
     @disruption_start_ts, @trough_ts, @stable_recovery_ts, @disruption_duration_minutes,
     @pre_stable_fpm, @trough_fpm, @post_stable_fpm, @pre_peak_fpm, @post_peak_fpm,
     @opportunity_window_start_ts, @opportunity_window_end_ts, @opportunity_window_minutes,
     @integrated_fpm_minutes, @counterfactual_fpm_minutes, @fruit_opportunity_shortfall,
     @stable_counterfactual_fpm_minutes, @stable_fruit_opportunity_shortfall,
     @stable_throughput_loss_ratio, @stable_equivalent_lost_minutes,
     @peak_throughput_loss_ratio, @peak_equivalent_lost_minutes,
     @target_throughput, @target_counterfactual_fpm_minutes,
     @target_fruit_opportunity_shortfall, @target_equivalent_lost_minutes,
     @break_overlap_detected, @break_overlap_minutes,
     @break_adjusted_disruption_minutes, @break_adjusted_opportunity_window_minutes,
     @break_adjusted_stable_fruit_opportunity_shortfall,
     @break_adjusted_stable_equivalent_lost_minutes,
     @break_adjusted_stable_throughput_loss_ratio,
     @availability_avg_during_disruption, @availability_avg_opportunity_window,
     @explanation::jsonb, @model_version, @delivered_to)
ON CONFLICT (serial_no, incoming_batch_record_id) DO UPDATE SET
    transition_ts = EXCLUDED.transition_ts,
    outgoing_batch_record_id = EXCLUDED.outgoing_batch_record_id,
    outgoing_grower_code = EXCLUDED.outgoing_grower_code,
    incoming_grower_code = EXCLUDED.incoming_grower_code,
    outgoing_label = EXCLUDED.outgoing_label,
    incoming_label = EXCLUDED.incoming_label,
    disruption_start_ts = EXCLUDED.disruption_start_ts,
    trough_ts = EXCLUDED.trough_ts,
    stable_recovery_ts = EXCLUDED.stable_recovery_ts,
    disruption_duration_minutes = EXCLUDED.disruption_duration_minutes,
    pre_stable_fpm = EXCLUDED.pre_stable_fpm,
    trough_fpm = EXCLUDED.trough_fpm,
    post_stable_fpm = EXCLUDED.post_stable_fpm,
    pre_peak_fpm = EXCLUDED.pre_peak_fpm,
    post_peak_fpm = EXCLUDED.post_peak_fpm,
    opportunity_window_start_ts = EXCLUDED.opportunity_window_start_ts,
    opportunity_window_end_ts = EXCLUDED.opportunity_window_end_ts,
    opportunity_window_minutes = EXCLUDED.opportunity_window_minutes,
    integrated_fpm_minutes = EXCLUDED.integrated_fpm_minutes,
    counterfactual_fpm_minutes = EXCLUDED.counterfactual_fpm_minutes,
    fruit_opportunity_shortfall = EXCLUDED.fruit_opportunity_shortfall,
    stable_counterfactual_fpm_minutes = EXCLUDED.stable_counterfactual_fpm_minutes,
    stable_fruit_opportunity_shortfall = EXCLUDED.stable_fruit_opportunity_shortfall,
    stable_throughput_loss_ratio = EXCLUDED.stable_throughput_loss_ratio,
    stable_equivalent_lost_minutes = EXCLUDED.stable_equivalent_lost_minutes,
    peak_throughput_loss_ratio = EXCLUDED.peak_throughput_loss_ratio,
    peak_equivalent_lost_minutes = EXCLUDED.peak_equivalent_lost_minutes,
    target_throughput = EXCLUDED.target_throughput,
    target_counterfactual_fpm_minutes = EXCLUDED.target_counterfactual_fpm_minutes,
    target_fruit_opportunity_shortfall = EXCLUDED.target_fruit_opportunity_shortfall,
    target_equivalent_lost_minutes = EXCLUDED.target_equivalent_lost_minutes,
    break_overlap_detected = EXCLUDED.break_overlap_detected,
    break_overlap_minutes = EXCLUDED.break_overlap_minutes,
    break_adjusted_disruption_minutes = EXCLUDED.break_adjusted_disruption_minutes,
    break_adjusted_opportunity_window_minutes = EXCLUDED.break_adjusted_opportunity_window_minutes,
    break_adjusted_stable_fruit_opportunity_shortfall = EXCLUDED.break_adjusted_stable_fruit_opportunity_shortfall,
    break_adjusted_stable_equivalent_lost_minutes = EXCLUDED.break_adjusted_stable_equivalent_lost_minutes,
    break_adjusted_stable_throughput_loss_ratio = EXCLUDED.break_adjusted_stable_throughput_loss_ratio,
    availability_avg_during_disruption = EXCLUDED.availability_avg_during_disruption,
    availability_avg_opportunity_window = EXCLUDED.availability_avg_opportunity_window,
    explanation = EXCLUDED.explanation,
    model_version = EXCLUDED.model_version,
    delivered_to = EXCLUDED.delivered_to,
    inserted_at = now();";

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
						cmd.Parameters.AddWithValue("stable_counterfactual_fpm_minutes", evt.StableCounterfactualFpmMinutes);
						cmd.Parameters.AddWithValue("stable_fruit_opportunity_shortfall", evt.StableFruitOpportunityShortfall);
						cmd.Parameters.AddWithValue("stable_throughput_loss_ratio", evt.StableThroughputLossRatio);
						cmd.Parameters.AddWithValue("stable_equivalent_lost_minutes", evt.StableEquivalentLostMinutes);
						cmd.Parameters.AddWithValue("peak_throughput_loss_ratio", evt.PeakThroughputLossRatio);
						cmd.Parameters.AddWithValue("peak_equivalent_lost_minutes", evt.PeakEquivalentLostMinutes);
						cmd.Parameters.AddWithValue("target_throughput", (object)evt.TargetThroughput ?? DBNull.Value);
						cmd.Parameters.AddWithValue("target_counterfactual_fpm_minutes", (object)evt.TargetCounterfactualFpmMinutes ?? DBNull.Value);
						cmd.Parameters.AddWithValue("target_fruit_opportunity_shortfall", (object)evt.TargetFruitOpportunityShortfall ?? DBNull.Value);
						cmd.Parameters.AddWithValue("target_equivalent_lost_minutes", (object)evt.TargetEquivalentLostMinutes ?? DBNull.Value);
						cmd.Parameters.AddWithValue("break_overlap_detected", evt.BreakOverlapDetected);
						cmd.Parameters.AddWithValue("break_overlap_minutes", evt.BreakOverlapMinutes);
						cmd.Parameters.AddWithValue("break_adjusted_disruption_minutes", evt.BreakAdjustedDisruptionMinutes);
						cmd.Parameters.AddWithValue("break_adjusted_opportunity_window_minutes", evt.BreakAdjustedOpportunityWindowMinutes);
						cmd.Parameters.AddWithValue("break_adjusted_stable_fruit_opportunity_shortfall", evt.BreakAdjustedStableFruitOpportunityShortfall);
						cmd.Parameters.AddWithValue("break_adjusted_stable_equivalent_lost_minutes", evt.BreakAdjustedStableEquivalentLostMinutes);
						cmd.Parameters.AddWithValue("break_adjusted_stable_throughput_loss_ratio", evt.BreakAdjustedStableThroughputLossRatio);
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
