using System;
using System.Collections.Generic;

namespace SizerDataCollector.Core.AnomalyDetection
{
	/// <summary>
	/// Proportion-based cross-lane anomaly detector for lane x grade FPM data.
	/// Maintains a rolling window of count matrices. For each grade, normalises
	/// each lane's count to a percentage of that lane's total, then compares the
	/// proportions across lanes via z-score (standard deviations from the cross-lane
	/// mean). This makes the detector invariant to lane throughput differences.
	/// </summary>
	public sealed class AnomalyDetector
	{
		private readonly AnomalyDetectorConfig _config;
		private readonly CooldownTracker _cooldown;
		private readonly NarrativeBuilder _narrative;
		private readonly Queue<GradeMatrix> _window;

		private double[][] _aggregate;
		private double[][] _zScores;
		private double[][] _pctDeviation;

		private int _lanes;
		private int _grades;
		private IReadOnlyList<string> _gradeKeys;

		public AnomalyDetector(AnomalyDetectorConfig config)
		{
			_config = config ?? throw new ArgumentNullException(nameof(config));
			_cooldown = new CooldownTracker(config.CooldownSeconds);
			_narrative = new NarrativeBuilder(config.RecycleGradeKey);
			_window = new Queue<GradeMatrix>();
		}

		/// <summary>
		/// Current z-score matrix for external consumers (e.g. heatmaps).
		/// May be null before the first update.
		/// </summary>
		public double[][] CurrentZScores => _zScores;

		/// <summary>
		/// Process a new FPM snapshot. Returns any anomaly events detected.
		/// </summary>
		public List<AnomalyEvent> Update(
			GradeMatrix matrix,
			DateTimeOffset now,
			string serialNo,
			int batchRecordId)
		{
			if (matrix == null) throw new ArgumentNullException(nameof(matrix));

			EnsureDimensions(matrix);
			_window.Enqueue(matrix);

			while (_window.Count > _config.WindowMinutes)
				_window.Dequeue();

			ComputeAggregate();

			if (!ComputeProportionStatistics())
			{
				Zero(_zScores);
				Zero(_pctDeviation);
				return new List<AnomalyEvent>();
			}

			return EvaluateAlarms(now, serialNo, batchRecordId);
		}

		public void Reset()
		{
			_window.Clear();
			_cooldown.Reset();
			if (_aggregate != null) Zero(_aggregate);
			if (_zScores != null) Zero(_zScores);
			if (_pctDeviation != null) Zero(_pctDeviation);
		}

		private void EnsureDimensions(GradeMatrix matrix)
		{
			if (_aggregate == null || _lanes != matrix.LaneCount || _grades != matrix.GradeCount)
			{
				_window.Clear();
				_lanes = matrix.LaneCount;
				_grades = matrix.GradeCount;
				_gradeKeys = matrix.GradeKeys;
				_aggregate = CreateArray(_lanes, _grades);
				_zScores = CreateArray(_lanes, _grades);
				_pctDeviation = CreateArray(_lanes, _grades);
			}
		}

		private void ComputeAggregate()
		{
			Zero(_aggregate);
			foreach (var m in _window)
			{
				for (int i = 0; i < _lanes; i++)
					for (int j = 0; j < _grades; j++)
						_aggregate[i][j] += m[i, j];
			}
		}

		/// <summary>
		/// Normalises each lane to grade proportions, then computes cross-lane
		/// z-scores and percent deviations per (lane, grade) cell.
		/// Returns false when there are fewer than 2 active lanes (no comparison possible).
		/// </summary>
		private bool ComputeProportionStatistics()
		{
			var laneTotals = RowSums(_aggregate);

			// Identify active lanes (non-zero throughput).
			int activeLanes = 0;
			for (int i = 0; i < _lanes; i++)
				if (laneTotals[i] > 0) activeLanes++;

			if (activeLanes < 2)
				return false;

			// Compute per-lane grade proportions (% of that lane's total).
			var proportions = CreateArray(_lanes, _grades);
			for (int i = 0; i < _lanes; i++)
			{
				if (laneTotals[i] <= 0) continue;
				for (int j = 0; j < _grades; j++)
					proportions[i][j] = 100.0 * _aggregate[i][j] / laneTotals[i];
			}

			// Per-grade: mean and standard deviation of proportions across active lanes.
			var meanPct = new double[_grades];
			var stdPct = new double[_grades];

			for (int j = 0; j < _grades; j++)
			{
				double sum = 0;
				for (int i = 0; i < _lanes; i++)
					if (laneTotals[i] > 0) sum += proportions[i][j];
				meanPct[j] = sum / activeLanes;

				double sumSq = 0;
				for (int i = 0; i < _lanes; i++)
				{
					if (laneTotals[i] <= 0) continue;
					double diff = proportions[i][j] - meanPct[j];
					sumSq += diff * diff;
				}
				stdPct[j] = Math.Sqrt(sumSq / activeLanes);
			}

			// Populate z-scores and percent deviation for every cell.
			for (int i = 0; i < _lanes; i++)
			{
				for (int j = 0; j < _grades; j++)
				{
					if (laneTotals[i] <= 0 || stdPct[j] <= 0 || meanPct[j] <= 0)
					{
						_zScores[i][j] = 0;
						_pctDeviation[i][j] = 0;
					}
					else
					{
						_zScores[i][j] = (proportions[i][j] - meanPct[j]) / stdPct[j];
						_pctDeviation[i][j] = 100.0 * (proportions[i][j] - meanPct[j]) / meanPct[j];
					}
				}
			}

			return true;
		}

		private List<AnomalyEvent> EvaluateAlarms(DateTimeOffset now, string serialNo, int batchRecordId)
		{
			var events = new List<AnomalyEvent>();
			int recycleIdx = FindGradeIndex(_config.RecycleGradeKey);

			for (int lane = 0; lane < _lanes; lane++)
			{
				if (recycleIdx >= 0)
				{
					var recycleEvent = EvaluateRecycle(lane, recycleIdx, now, serialNo, batchRecordId);
					if (recycleEvent != null)
						events.Add(recycleEvent);
				}

				var gradeEvent = EvaluateGradeShift(lane, recycleIdx, now, serialNo, batchRecordId);
				if (gradeEvent != null)
					events.Add(gradeEvent);
			}

			return events;
		}

		private AnomalyEvent EvaluateRecycle(int lane, int recycleIdx, DateTimeOffset now, string serialNo, int batchRecordId)
		{
			double z = _zScores[lane][recycleIdx];
			double pct = _pctDeviation[lane][recycleIdx];

			if (Math.Abs(z) < _config.ZGate || Math.Abs(pct) < _config.BandLowMin)
				return null;

			string severity = PriorityClassifier.Classify(Math.Abs(pct), _config);
			if (severity == null)
				return null;

			string gradeKey = _gradeKeys[recycleIdx];
			if (_cooldown.IsOnCooldown(lane, gradeKey, now))
				return null;

			_cooldown.Record(lane, gradeKey, now);

			var narrative = _narrative.BuildRecycleNarrative(lane, pct, z);

			return new AnomalyEvent
			{
				EventTs = now,
				SerialNo = serialNo,
				BatchRecordId = batchRecordId,
				LaneNo = lane + 1,
				GradeKey = gradeKey,
				Qty = _aggregate[lane][recycleIdx],
				Pct = pct,
				AnomalyScore = z,
				Severity = severity,
				ModelVersion = "proportion-v1",
				DeliveredTo = string.Empty,
				AlarmTitle = narrative.Title,
				AlarmDetails = narrative.Details
			};
		}

		private AnomalyEvent EvaluateGradeShift(int lane, int recycleIdx, DateTimeOffset now, string serialNo, int batchRecordId)
		{
			int bestPosIdx = -1, bestNegIdx = -1;
			double bestPosZ = double.MinValue, bestNegZ = double.MaxValue;

			for (int j = 0; j < _grades; j++)
			{
				if (j == recycleIdx) continue;

				double z = _zScores[lane][j];
				if (z > bestPosZ) { bestPosZ = z; bestPosIdx = j; }
				if (z < bestNegZ) { bestNegZ = z; bestNegIdx = j; }
			}

			if (bestPosIdx < 0) return null;

			string posSuffix = NarrativeBuilder.ShortName(_gradeKeys[bestPosIdx]);
			string negSuffix = NarrativeBuilder.ShortName(_gradeKeys[bestNegIdx]);

			// When both sides resolve to the same grade category (e.g. two sub-grade IDs
			// both mapped to EXP DARK), the shift is between Sizer configuration entries,
			// not a meaningful quality anomaly for the operator.
			if (string.Equals(posSuffix, negSuffix, StringComparison.OrdinalIgnoreCase))
				return null;

			double pctPos = _pctDeviation[lane][bestPosIdx];
			double pctNeg = _pctDeviation[lane][bestNegIdx];
			double dominantPct = Math.Abs(pctPos) >= Math.Abs(pctNeg) ? pctPos : pctNeg;

			string severity = PriorityClassifier.Classify(Math.Abs(dominantPct), _config);
			if (severity == null || Math.Max(Math.Abs(bestPosZ), Math.Abs(bestNegZ)) < _config.ZGate)
				return null;

			int dominantIdx = Math.Abs(pctPos) >= Math.Abs(pctNeg) ? bestPosIdx : bestNegIdx;
			string gradeKey = _gradeKeys[dominantIdx];

			if (_cooldown.IsOnCooldown(lane, gradeKey, now))
				return null;

			_cooldown.Record(lane, gradeKey, now);

			var narrative = _narrative.BuildGradeShiftNarrative(
				lane,
				_gradeKeys[bestPosIdx],
				_gradeKeys[bestNegIdx],
				pctPos, pctNeg,
				bestPosZ, bestNegZ,
				_config.BandLowMin);

			return new AnomalyEvent
			{
				EventTs = now,
				SerialNo = serialNo,
				BatchRecordId = batchRecordId,
				LaneNo = lane + 1,
				GradeKey = gradeKey,
				Qty = _aggregate[lane][dominantIdx],
				Pct = dominantPct,
				AnomalyScore = Math.Abs(pctPos) >= Math.Abs(pctNeg) ? bestPosZ : bestNegZ,
				Severity = severity,
				ModelVersion = "proportion-v1",
				DeliveredTo = string.Empty,
				AlarmTitle = narrative.Title,
				AlarmDetails = narrative.Details
			};
		}

		private int FindGradeIndex(string gradeKey)
		{
			if (_gradeKeys == null) return -1;

			string targetSuffix = NarrativeBuilder.ShortName(gradeKey);

			for (int i = 0; i < _gradeKeys.Count; i++)
			{
				if (string.Equals(_gradeKeys[i], gradeKey, StringComparison.OrdinalIgnoreCase))
					return i;
			}

			for (int i = 0; i < _gradeKeys.Count; i++)
			{
				string suffix = NarrativeBuilder.ShortName(_gradeKeys[i]);
				if (string.Equals(suffix, targetSuffix, StringComparison.OrdinalIgnoreCase))
					return i;
			}

			return -1;
		}

		private static double[][] CreateArray(int rows, int cols)
		{
			var arr = new double[rows][];
			for (int i = 0; i < rows; i++)
				arr[i] = new double[cols];
			return arr;
		}

		private static void Zero(double[][] arr)
		{
			for (int i = 0; i < arr.Length; i++)
				Array.Clear(arr[i], 0, arr[i].Length);
		}

		private static double[] RowSums(double[][] arr)
		{
			var sums = new double[arr.Length];
			for (int i = 0; i < arr.Length; i++)
				for (int j = 0; j < arr[i].Length; j++)
					sums[i] += arr[i][j];
			return sums;
		}
	}
}
