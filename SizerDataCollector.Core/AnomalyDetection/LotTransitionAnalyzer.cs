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
using SizerDataCollector.Core.Logging;

namespace SizerDataCollector.Core.AnomalyDetection
{
	public sealed class LotTransitionAnalyzer
	{
		private const double MinBreakLikeStopMinutes = 10.0;
		private const double MaxBreakLikeStopMinutes = 35.0;

		private const string PointQuery = @"
SELECT m.ts,
       m.batch_record_id,
       m.value_json::text,
       b.grower_code,
       b.comments
FROM public.metrics m
LEFT JOIN public.batches b ON b.id = m.batch_record_id
WHERE m.metric = 'machine_total_fpm'
  AND m.serial_no = @serial_no
  AND m.ts >= @from_ts
  AND m.ts <= @to_ts
ORDER BY m.ts ASC;";

		private const string AvailabilityQuery = @"
SELECT avg(availability_ratio)::double precision
FROM oee.v_oee_minute_batch
WHERE serial_no = @serial_no
  AND minute_ts >= @from_ts
  AND minute_ts <= @to_ts;";

		private const string TargetThroughputQuery = @"
SELECT avg(target_throughput)::double precision
FROM oee.v_operational_minute_batch
WHERE serial_no = @serial_no
  AND minute_ts >= @from_ts
  AND minute_ts <= @to_ts
  AND target_throughput IS NOT NULL
  AND target_throughput > 0;";

		private readonly LotTransitionConfig _config;
		private readonly string _connectionString;

		public LotTransitionAnalyzer(LotTransitionConfig config, string connectionString)
		{
			_config = config ?? throw new ArgumentNullException(nameof(config));
			_connectionString = connectionString ?? string.Empty;
		}

		public async Task<LotTransitionReport> AnalyzeRangeAsync(
			string serialNo,
			DateTimeOffset fromTs,
			DateTimeOffset toTs,
			CancellationToken cancellationToken)
		{
			var report = new LotTransitionReport
			{
				SerialNo = serialNo,
				FromTs = fromTs,
				ToTs = toTs
			};

			if (string.IsNullOrWhiteSpace(serialNo) || toTs <= fromTs)
				return report;

			var batchInfos = new Dictionary<long, LotTransitionBatchInfo>();
			var points = await LoadPointsAsync(serialNo.Trim(), fromTs, toTs, batchInfos, cancellationToken).ConfigureAwait(false);
			report = AnalyzePoints(serialNo.Trim(), fromTs, toTs, points, batchInfos);

			if (!string.IsNullOrWhiteSpace(_connectionString))
			{
				foreach (var evt in report.Events)
				{
					evt.AvailabilityAvgDuringDisruption = await QueryAvailabilityAverageAsync(
						evt.SerialNo,
						evt.DisruptionStartTs,
						evt.StableRecoveryTs,
						cancellationToken).ConfigureAwait(false);

					evt.AvailabilityAvgOpportunityWindow = await QueryAvailabilityAverageAsync(
						evt.SerialNo,
						evt.OpportunityWindowStartTs,
						evt.OpportunityWindowEndTs,
						cancellationToken).ConfigureAwait(false);

					var targetThroughput = await QueryTargetThroughputAverageAsync(
						evt.SerialNo,
						evt.OpportunityWindowStartTs,
						evt.OpportunityWindowEndTs,
						cancellationToken).ConfigureAwait(false);
					ApplyTargetImpact(evt, targetThroughput);
				}
			}

			return report;
		}

		public LotTransitionReport AnalyzePoints(
			string serialNo,
			DateTimeOffset fromTs,
			DateTimeOffset toTs,
			IEnumerable<FpmBatchPoint> points,
			IDictionary<long, LotTransitionBatchInfo> batchInfos)
		{
			var ordered = (points ?? Enumerable.Empty<FpmBatchPoint>())
				.Where(p => p != null)
				.OrderBy(p => p.Ts)
				.ToList();

			var report = new LotTransitionReport
			{
				SerialNo = serialNo,
				FromTs = fromTs,
				ToTs = toTs
			};

			if (ordered.Count < 2)
				return report;

			for (var index = 1; index < ordered.Count; index++)
			{
				if (ordered[index].BatchRecordId == ordered[index - 1].BatchRecordId)
					continue;

				report.TransitionCandidates++;

				if (TryBuildEvent(serialNo, toTs, ordered, index, batchInfos, out var evt))
				{
					report.Events.Add(evt);
				}
			}

			return report;
		}

		private async Task<List<FpmBatchPoint>> LoadPointsAsync(
			string serialNo,
			DateTimeOffset fromTs,
			DateTimeOffset toTs,
			Dictionary<long, LotTransitionBatchInfo> batchInfos,
			CancellationToken cancellationToken)
		{
			var rows = new List<FpmBatchPoint>();
			Logger.Log($"Lot transition analysis: loading machine_total_fpm from {fromTs:O} to {toTs:O} for serial '{serialNo}'.");

			using (var connection = new NpgsqlConnection(_connectionString))
			{
				await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
				using (var cmd = new NpgsqlCommand(PointQuery, connection))
				{
					cmd.Parameters.Add(new NpgsqlParameter("serial_no", NpgsqlDbType.Text) { Value = serialNo });
					cmd.Parameters.Add(new NpgsqlParameter("from_ts", NpgsqlDbType.TimestampTz) { Value = fromTs.UtcDateTime });
					cmd.Parameters.Add(new NpgsqlParameter("to_ts", NpgsqlDbType.TimestampTz) { Value = toTs.UtcDateTime });

					using (var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
					{
						while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
						{
							var rawValue = reader.IsDBNull(2) ? null : reader.GetString(2);
							if (!TryParseFpm(rawValue, out var fpm))
								continue;

							var batchRecordId = reader.GetInt64(1);
							rows.Add(new FpmBatchPoint
							{
								Ts = reader.GetFieldValue<DateTimeOffset>(0),
								BatchRecordId = batchRecordId,
								Fpm = fpm
							});

							if (!batchInfos.ContainsKey(batchRecordId))
							{
								var grower = reader.IsDBNull(3) ? null : reader.GetString(3);
								var comments = reader.IsDBNull(4) ? null : reader.GetString(4);
								batchInfos[batchRecordId] = new LotTransitionBatchInfo
								{
									BatchRecordId = batchRecordId,
									GrowerCode = grower,
									Label = BuildBatchLabel(grower, comments)
								};
							}
						}
					}
				}
			}

			Logger.Log($"Lot transition analysis: loaded {rows.Count} machine_total_fpm points.");
			return rows;
		}

		private bool TryBuildEvent(
			string serialNo,
			DateTimeOffset toTs,
			List<FpmBatchPoint> points,
			int transitionIndex,
			IDictionary<long, LotTransitionBatchInfo> batchInfos,
			out LotTransitionEvent evt)
		{
			evt = null;

			var transitionPoint = points[transitionIndex];
			var previousPoint = points[transitionIndex - 1];
			var transitionTs = transitionPoint.Ts;
			var outgoingBatchId = previousPoint.BatchRecordId;
			var incomingBatchId = transitionPoint.BatchRecordId;

			if (outgoingBatchId <= 0 || incomingBatchId <= 0)
				return false;

			var nextTransitionIndex = points.Count;
			for (var i = transitionIndex + 1; i < points.Count; i++)
			{
				if (points[i].BatchRecordId != incomingBatchId)
				{
					nextTransitionIndex = i;
					break;
				}
			}

			var incomingSegmentEndTs = nextTransitionIndex < points.Count ? points[nextTransitionIndex].Ts : toTs;
			var stableWindow = TimeSpan.FromMinutes(_config.StableWindowMinutes);
			var peakWindow = TimeSpan.FromMinutes(_config.PeakSearchMinutes);
			var preWindowStart = transitionTs - stableWindow;
			var postWindowEnd = MinDateTimeOffset(incomingSegmentEndTs, transitionTs + TimeSpan.FromMinutes(Math.Max(_config.StableWindowMinutes * 2, _config.PeakSearchMinutes)));

			var preStableSamples = points
				.Where(p => p.BatchRecordId == outgoingBatchId && p.Ts >= preWindowStart && p.Ts < transitionTs && p.Fpm > _config.MinFpmForBaseline)
				.Select(p => p.Fpm)
				.ToList();
			if (preStableSamples.Count < _config.MinPreStableSamples)
				return false;

			var incomingPositiveSamples = points
				.Where(p => p.BatchRecordId == incomingBatchId && p.Ts >= transitionTs && p.Ts <= postWindowEnd && p.Fpm > _config.MinFpmForBaseline)
				.Select(p => p.Fpm)
				.ToList();
			if (incomingPositiveSamples.Count < _config.MinPostStableSamples)
				return false;

			var preStableFpm = Median(preStableSamples);
			var postStableFpm = UpperStableBaseline(incomingPositiveSamples);
			if (preStableFpm <= 0 || postStableFpm <= 0)
				return false;

			var searchStartTs = transitionTs - peakWindow;
			var searchEndTs = MinDateTimeOffset(incomingSegmentEndTs, transitionTs + peakWindow);
			var searchStartIndex = FirstIndexAtOrAfter(points, searchStartTs);
			var searchEndIndex = LastIndexAtOrBefore(points, searchEndTs);
			if (searchStartIndex < 0 || searchEndIndex <= searchStartIndex)
				return false;

			var slowdownThreshold = preStableFpm * (1.0 - _config.SlowdownFraction);
			if (!TryFindConsecutive(
				points,
				searchStartIndex,
				searchEndIndex,
				p => p.Fpm <= slowdownThreshold,
				_config.ConsecutiveSamplesForSlowdown,
				out var disruptionStartIndex,
				out _))
			{
				return false;
			}

			var preliminaryTroughIndex = FindMinimumIndex(points, disruptionStartIndex, searchEndIndex);
			var recoveryThreshold = postStableFpm * (1.0 - _config.RecoveryFraction);
			if (!TryFindConsecutive(
				points,
				preliminaryTroughIndex,
				searchEndIndex,
				p => p.BatchRecordId == incomingBatchId && p.Fpm >= recoveryThreshold,
				_config.RecoveryConsecutiveSamples,
				out var stableRecoveryIndex,
				out _))
			{
				return false;
			}

			var troughIndex = FindMinimumIndex(points, disruptionStartIndex, stableRecoveryIndex);
			var outgoingPeakStartTs = transitionTs - peakWindow;
			var prePeak = points
				.Where(p => p.BatchRecordId == outgoingBatchId && p.Ts >= outgoingPeakStartTs && p.Ts < transitionTs)
				.OrderByDescending(p => p.Fpm)
				.ThenBy(p => p.Ts)
				.FirstOrDefault();

			var postPeakEndTs = MinDateTimeOffset(incomingSegmentEndTs, transitionTs + peakWindow);
			var postPeak = points
				.Where(p => p.BatchRecordId == incomingBatchId && p.Ts >= points[stableRecoveryIndex].Ts && p.Ts <= postPeakEndTs)
				.OrderByDescending(p => p.Fpm)
				.ThenBy(p => p.Ts)
				.FirstOrDefault();

			if (prePeak == null || postPeak == null || postPeak.Ts <= prePeak.Ts)
				return false;

			var integrationPoints = points
				.Where(p => p.Ts >= prePeak.Ts && p.Ts <= postPeak.Ts)
				.OrderBy(p => p.Ts)
				.ToList();
			if (integrationPoints.Count < 2)
				return false;

			var actualFpmMinutes = IntegrateFpmMinutes(integrationPoints);
			var opportunityMinutes = (postPeak.Ts - prePeak.Ts).TotalMinutes;
			if (opportunityMinutes <= 0)
				return false;

			var breakWindows = FindBreakLikeStopWindows(points, searchStartIndex, searchEndIndex, _config.MinFpmForBaseline);
			var breakOverlapMinutes = SumOverlapMinutes(breakWindows, prePeak.Ts, postPeak.Ts);
			var breakDisruptionOverlapMinutes = SumOverlapMinutes(breakWindows, points[disruptionStartIndex].Ts, points[stableRecoveryIndex].Ts);
			var breakAdjustedOpportunityMinutes = Math.Max(0.0, opportunityMinutes - breakOverlapMinutes);
			var breakAdjustedActualFpmMinutes = IntegrateFpmMinutesExcludingWindows(integrationPoints, breakWindows);
			var counterfactualFpmMinutes = prePeak.Fpm * opportunityMinutes;
			var shortfall = Math.Max(0.0, counterfactualFpmMinutes - actualFpmMinutes);
			var stableCounterfactualFpmMinutes = preStableFpm * opportunityMinutes;
			var stableShortfall = Math.Max(0.0, stableCounterfactualFpmMinutes - actualFpmMinutes);
			var breakAdjustedStableCounterfactual = preStableFpm * breakAdjustedOpportunityMinutes;
			var breakAdjustedStableShortfall = Math.Max(0.0, breakAdjustedStableCounterfactual - breakAdjustedActualFpmMinutes);
			var disruptionMinutes = (points[stableRecoveryIndex].Ts - points[disruptionStartIndex].Ts).TotalMinutes;
			if (disruptionMinutes <= 0)
				return false;
			var breakAdjustedDisruptionMinutes = Math.Max(0.0, disruptionMinutes - breakDisruptionOverlapMinutes);

			var outgoingInfo = GetBatchInfo(batchInfos, outgoingBatchId);
			var incomingInfo = GetBatchInfo(batchInfos, incomingBatchId);

			evt = new LotTransitionEvent
			{
				TransitionTs = transitionTs,
				SerialNo = serialNo,
				OutgoingBatchRecordId = outgoingBatchId,
				IncomingBatchRecordId = incomingBatchId,
				OutgoingGrowerCode = outgoingInfo?.GrowerCode,
				IncomingGrowerCode = incomingInfo?.GrowerCode,
				OutgoingLabel = outgoingInfo?.Label,
				IncomingLabel = incomingInfo?.Label,
				DisruptionStartTs = points[disruptionStartIndex].Ts,
				TroughTs = points[troughIndex].Ts,
				StableRecoveryTs = points[stableRecoveryIndex].Ts,
				DisruptionDurationMinutes = disruptionMinutes,
				PreStableFpm = preStableFpm,
				TroughFpm = points[troughIndex].Fpm,
				PostStableFpm = postStableFpm,
				PrePeakFpm = prePeak.Fpm,
				PostPeakFpm = postPeak.Fpm,
				OpportunityWindowStartTs = prePeak.Ts,
				OpportunityWindowEndTs = postPeak.Ts,
				OpportunityWindowMinutes = opportunityMinutes,
				IntegratedFpmMinutes = actualFpmMinutes,
				CounterfactualFpmMinutes = counterfactualFpmMinutes,
				FruitOpportunityShortfall = shortfall,
				StableCounterfactualFpmMinutes = stableCounterfactualFpmMinutes,
				StableFruitOpportunityShortfall = stableShortfall,
				StableThroughputLossRatio = SafeRatio(stableShortfall, stableCounterfactualFpmMinutes),
				StableEquivalentLostMinutes = SafeRatio(stableShortfall, preStableFpm),
				PeakThroughputLossRatio = SafeRatio(shortfall, counterfactualFpmMinutes),
				PeakEquivalentLostMinutes = SafeRatio(shortfall, prePeak.Fpm),
				BreakOverlapDetected = breakOverlapMinutes > 0,
				BreakOverlapMinutes = breakOverlapMinutes,
				BreakAdjustedDisruptionMinutes = breakAdjustedDisruptionMinutes,
				BreakAdjustedOpportunityWindowMinutes = breakAdjustedOpportunityMinutes,
				BreakAdjustedStableFruitOpportunityShortfall = breakAdjustedStableShortfall,
				BreakAdjustedStableEquivalentLostMinutes = SafeRatio(breakAdjustedStableShortfall, preStableFpm),
				BreakAdjustedStableThroughputLossRatio = SafeRatio(breakAdjustedStableShortfall, breakAdjustedStableCounterfactual),
				ExplanationJson = BuildExplanationJson(
					preStableSamples.Count,
					incomingPositiveSamples.Count,
					integrationPoints.Count,
					slowdownThreshold,
					recoveryThreshold,
					_config.StableWindowMinutes,
					_config.PeakSearchMinutes,
					points[troughIndex].Fpm <= _config.MinFpmForBaseline,
					breakWindows.Count,
					breakOverlapMinutes),
				ModelVersion = _config.ModelVersion,
				DeliveredTo = string.Empty
			};

			return true;
		}

		private async Task<double?> QueryAvailabilityAverageAsync(
			string serialNo,
			DateTimeOffset fromTs,
			DateTimeOffset toTs,
			CancellationToken cancellationToken)
		{
			if (toTs <= fromTs)
				return null;

			try
			{
				using (var connection = new NpgsqlConnection(_connectionString))
				{
					await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
					using (var cmd = new NpgsqlCommand(AvailabilityQuery, connection))
					{
						cmd.Parameters.Add(new NpgsqlParameter("serial_no", NpgsqlDbType.Text) { Value = serialNo });
						cmd.Parameters.Add(new NpgsqlParameter("from_ts", NpgsqlDbType.TimestampTz) { Value = fromTs.UtcDateTime });
						cmd.Parameters.Add(new NpgsqlParameter("to_ts", NpgsqlDbType.TimestampTz) { Value = toTs.UtcDateTime });

						var value = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
						if (value == null || value == DBNull.Value)
							return null;

						return Convert.ToDouble(value, CultureInfo.InvariantCulture);
					}
				}
			}
			catch (Exception ex) when (!(ex is OperationCanceledException))
			{
				Logger.Log("Lot transition availability enrichment failed; event will be persisted without availability context.", ex, LogLevel.Debug);
				return null;
			}
		}

		private async Task<double?> QueryTargetThroughputAverageAsync(
			string serialNo,
			DateTimeOffset fromTs,
			DateTimeOffset toTs,
			CancellationToken cancellationToken)
		{
			if (toTs <= fromTs)
				return null;

			try
			{
				using (var connection = new NpgsqlConnection(_connectionString))
				{
					await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
					using (var cmd = new NpgsqlCommand(TargetThroughputQuery, connection))
					{
						cmd.Parameters.Add(new NpgsqlParameter("serial_no", NpgsqlDbType.Text) { Value = serialNo });
						cmd.Parameters.Add(new NpgsqlParameter("from_ts", NpgsqlDbType.TimestampTz) { Value = fromTs.UtcDateTime });
						cmd.Parameters.Add(new NpgsqlParameter("to_ts", NpgsqlDbType.TimestampTz) { Value = toTs.UtcDateTime });

						var value = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
						if (value == null || value == DBNull.Value)
							return null;

						var target = Convert.ToDouble(value, CultureInfo.InvariantCulture);
						return target > 0 ? target : (double?)null;
					}
				}
			}
			catch (Exception ex) when (!(ex is OperationCanceledException))
			{
				Logger.Log("Lot transition target throughput enrichment failed; event will be persisted without target impact context.", ex, LogLevel.Debug);
				return null;
			}
		}

		private static void ApplyTargetImpact(LotTransitionEvent evt, double? targetThroughput)
		{
			if (evt == null || !targetThroughput.HasValue || targetThroughput.Value <= 0 || evt.OpportunityWindowMinutes <= 0)
				return;

			var counterfactual = targetThroughput.Value * evt.OpportunityWindowMinutes;
			var shortfall = Math.Max(0.0, counterfactual - evt.IntegratedFpmMinutes);

			evt.TargetThroughput = targetThroughput.Value;
			evt.TargetCounterfactualFpmMinutes = counterfactual;
			evt.TargetFruitOpportunityShortfall = shortfall;
			evt.TargetEquivalentLostMinutes = SafeRatio(shortfall, targetThroughput.Value);
		}

		private static bool TryParseFpm(string rawValue, out double fpm)
		{
			fpm = 0;
			if (string.IsNullOrWhiteSpace(rawValue))
				return false;

			var trimmed = rawValue.Trim();
			if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out fpm))
				return true;

			try
			{
				var token = JToken.Parse(trimmed);
				if (token.Type == JTokenType.Integer || token.Type == JTokenType.Float)
				{
					fpm = token.Value<double>();
					return true;
				}

				if (token.Type == JTokenType.String &&
					double.TryParse(token.Value<string>(), NumberStyles.Float, CultureInfo.InvariantCulture, out fpm))
				{
					return true;
				}

				var obj = token as JObject;
				if (obj != null)
				{
					foreach (var key in new[] { "value", "Value", "fpm", "Fpm", "machine_total_fpm" })
					{
						var child = obj[key];
						if (child != null && double.TryParse(child.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out fpm))
							return true;
					}
				}
			}
			catch (JsonException)
			{
				return false;
			}

			return false;
		}

		private static LotTransitionBatchInfo GetBatchInfo(IDictionary<long, LotTransitionBatchInfo> batchInfos, long batchRecordId)
		{
			if (batchInfos != null && batchInfos.TryGetValue(batchRecordId, out var info))
				return info;

			return new LotTransitionBatchInfo
			{
				BatchRecordId = batchRecordId,
				Label = batchRecordId.ToString(CultureInfo.InvariantCulture)
			};
		}

		private static string BuildBatchLabel(string growerCode, string comments)
		{
			if (!string.IsNullOrWhiteSpace(growerCode) && !string.IsNullOrWhiteSpace(comments))
				return growerCode.Trim() + " " + comments.Trim();

			if (!string.IsNullOrWhiteSpace(growerCode))
				return growerCode.Trim();

			return string.IsNullOrWhiteSpace(comments) ? null : comments.Trim();
		}

		private static string BuildExplanationJson(
			int preStableSampleCount,
			int postStableSampleCount,
			int integrationSampleCount,
			double slowdownThreshold,
			double recoveryThreshold,
			int stableWindowMinutes,
			int peakSearchMinutes,
			bool flatlineZero,
			int breakLikeStopCount,
			double breakOverlapMinutes)
		{
			var obj = new JObject
			{
				["pre_stable_sample_count"] = preStableSampleCount,
				["post_stable_sample_count"] = postStableSampleCount,
				["integration_sample_count"] = integrationSampleCount,
				["slowdown_threshold_fpm"] = slowdownThreshold,
				["recovery_threshold_fpm"] = recoveryThreshold,
				["flatline_zero"] = flatlineZero,
				["stable_window_minutes"] = stableWindowMinutes,
				["peak_search_minutes"] = peakSearchMinutes,
				["break_like_stop_count"] = breakLikeStopCount,
				["break_overlap_minutes"] = breakOverlapMinutes,
				["break_like_stop_min_minutes"] = MinBreakLikeStopMinutes,
				["break_like_stop_max_minutes"] = MaxBreakLikeStopMinutes
			};
			return obj.ToString(Formatting.None);
		}

		private static bool TryFindConsecutive(
			List<FpmBatchPoint> points,
			int startIndex,
			int endIndex,
			Func<FpmBatchPoint, bool> predicate,
			int requiredCount,
			out int runStartIndex,
			out int runEndIndex)
		{
			runStartIndex = -1;
			runEndIndex = -1;
			var runCount = 0;
			var currentRunStart = -1;
			var needed = Math.Max(1, requiredCount);

			for (var i = Math.Max(0, startIndex); i <= Math.Min(endIndex, points.Count - 1); i++)
			{
				if (predicate(points[i]))
				{
					if (runCount == 0)
						currentRunStart = i;

					runCount++;
					if (runCount >= needed)
					{
						runStartIndex = currentRunStart;
						runEndIndex = i;
						return true;
					}
				}
				else
				{
					runCount = 0;
					currentRunStart = -1;
				}
			}

			return false;
		}

		private static int FindMinimumIndex(List<FpmBatchPoint> points, int startIndex, int endIndex)
		{
			var minIndex = Math.Max(0, startIndex);
			var lastIndex = Math.Min(endIndex, points.Count - 1);
			for (var i = minIndex + 1; i <= lastIndex; i++)
			{
				if (points[i].Fpm < points[minIndex].Fpm)
					minIndex = i;
			}

			return minIndex;
		}

		private static int FirstIndexAtOrAfter(List<FpmBatchPoint> points, DateTimeOffset ts)
		{
			for (var i = 0; i < points.Count; i++)
			{
				if (points[i].Ts >= ts)
					return i;
			}

			return -1;
		}

		private static int LastIndexAtOrBefore(List<FpmBatchPoint> points, DateTimeOffset ts)
		{
			for (var i = points.Count - 1; i >= 0; i--)
			{
				if (points[i].Ts <= ts)
					return i;
			}

			return -1;
		}

		private static double Median(IEnumerable<double> values)
		{
			var ordered = values.OrderBy(v => v).ToList();
			if (ordered.Count == 0)
				return 0;

			var middle = ordered.Count / 2;
			if (ordered.Count % 2 == 1)
				return ordered[middle];

			return (ordered[middle - 1] + ordered[middle]) / 2.0;
		}

		private static double UpperStableBaseline(IEnumerable<double> values)
		{
			var ordered = values.OrderBy(v => v).ToList();
			if (ordered.Count == 0)
				return 0;

			var start = ordered.Count / 2;
			return Median(ordered.Skip(start));
		}

		private static double IntegrateFpmMinutes(List<FpmBatchPoint> points)
		{
			double total = 0;
			for (var i = 1; i < points.Count; i++)
			{
				var minutes = (points[i].Ts - points[i - 1].Ts).TotalMinutes;
				if (minutes <= 0)
					continue;

				total += ((points[i - 1].Fpm + points[i].Fpm) / 2.0) * minutes;
			}

			return total;
		}

		private static List<TimeWindow> FindBreakLikeStopWindows(
			List<FpmBatchPoint> points,
			int startIndex,
			int endIndex,
			double lowFpmThreshold)
		{
			var windows = new List<TimeWindow>();
			var first = Math.Max(1, startIndex + 1);
			var last = Math.Min(endIndex, points.Count - 1);
			TimeWindow current = null;

			for (var i = first; i <= last; i++)
			{
				var left = points[i - 1];
				var right = points[i];
				if (right.Ts <= left.Ts)
					continue;

				var lowInterval = left.Fpm <= lowFpmThreshold && right.Fpm <= lowFpmThreshold;
				if (lowInterval)
				{
					if (current == null)
					{
						current = new TimeWindow
						{
							StartTs = left.Ts,
							EndTs = right.Ts
						};
					}
					else
					{
						current.EndTs = right.Ts;
					}
				}
				else if (current != null)
				{
					AddBreakWindowIfInRange(windows, current);
					current = null;
				}
			}

			if (current != null)
				AddBreakWindowIfInRange(windows, current);

			return windows;
		}

		private static void AddBreakWindowIfInRange(List<TimeWindow> windows, TimeWindow window)
		{
			var minutes = (window.EndTs - window.StartTs).TotalMinutes;
			if (minutes >= MinBreakLikeStopMinutes && minutes <= MaxBreakLikeStopMinutes)
				windows.Add(window);
		}

		private static double IntegrateFpmMinutesExcludingWindows(List<FpmBatchPoint> points, List<TimeWindow> excludedWindows)
		{
			if (excludedWindows == null || excludedWindows.Count == 0)
				return IntegrateFpmMinutes(points);

			double total = 0;
			for (var i = 1; i < points.Count; i++)
			{
				var left = points[i - 1];
				var right = points[i];
				var minutes = (right.Ts - left.Ts).TotalMinutes;
				if (minutes <= 0)
					continue;

				var excludedMinutes = SumOverlapMinutes(excludedWindows, left.Ts, right.Ts);
				var includedMinutes = Math.Max(0.0, minutes - excludedMinutes);
				if (includedMinutes <= 0)
					continue;

				total += ((left.Fpm + right.Fpm) / 2.0) * includedMinutes;
			}

			return total;
		}

		private static double SumOverlapMinutes(IEnumerable<TimeWindow> windows, DateTimeOffset startTs, DateTimeOffset endTs)
		{
			if (endTs <= startTs)
				return 0.0;

			double total = 0;
			foreach (var window in windows)
			{
				var overlapStart = MaxDateTimeOffset(startTs, window.StartTs);
				var overlapEnd = MinDateTimeOffset(endTs, window.EndTs);
				if (overlapEnd > overlapStart)
					total += (overlapEnd - overlapStart).TotalMinutes;
			}

			return total;
		}

		private static double SafeRatio(double numerator, double denominator)
		{
			return denominator > 0 ? numerator / denominator : 0.0;
		}

		private static DateTimeOffset MinDateTimeOffset(DateTimeOffset left, DateTimeOffset right)
		{
			return left <= right ? left : right;
		}

		private static DateTimeOffset MaxDateTimeOffset(DateTimeOffset left, DateTimeOffset right)
		{
			return left >= right ? left : right;
		}

		private sealed class TimeWindow
		{
			public DateTimeOffset StartTs { get; set; }
			public DateTimeOffset EndTs { get; set; }
		}
	}
}
