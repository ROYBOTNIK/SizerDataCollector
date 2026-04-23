using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace SizerDataCollector.Core.AnomalyDetection
{
	/// <summary>
	/// Peer-relative lane composition detector for lane x grade FPM data.
	/// Maintains a rolling window of snapshots, converts each lane to a grade-share mix,
	/// and compares each lane/grade against the median peer share using a MAD-derived
	/// robust score. Alerts are emitted at lane level once skew persists for enough
	/// consecutive windows and throughput guardrails are satisfied.
	///
	/// Dimension handling: the detector tracks a canonical, monotonically-growing
	/// set of lane indices and grade keys across snapshots. Snapshots that introduce
	/// a new grade or a higher lane number simply extend the internal matrices with
	/// zero-padded columns/rows - the rolling window is NOT cleared. Snapshots that
	/// omit a previously-seen grade (e.g. no fruit graded as "D4" that minute)
	/// contribute zeros for that grade rather than collapsing the canonical shape.
	/// This is critical on live machines where the emitted grade set fluctuates
	/// minute-to-minute around a stable underlying mix.
	/// </summary>
	public sealed class AnomalyDetector
	{
		private const double MinimumRobustSpreadPoints = 1.0;
		private const string ModelVersion = "composition-mad-v3";

		private readonly AnomalyDetectorConfig _config;
		private readonly CooldownTracker _cooldown;
		private readonly NarrativeBuilder _narrative;
		private readonly Queue<GradeMatrix> _window;

		private double[][] _aggregate;
		private double[][] _zScores;
		private double[][] _pctDeviation;
		private double[][] _peerMedianPct;
		private double[][] _laneSharePct;
		private double[] _laneAverageFpm;
		private int[] _eligiblePeerCounts;
		private int[] _consecutiveLaneSignals;

		private int _lanes;
		private int _grades;
		private readonly List<string> _canonicalGradeKeys = new List<string>();
		private readonly Dictionary<string, int> _canonicalGradeIndex
			= new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

		public AnomalyDetector(AnomalyDetectorConfig config)
		{
			_config = config ?? throw new ArgumentNullException(nameof(config));
			_cooldown = new CooldownTracker(config.CooldownSeconds);
			_narrative = new NarrativeBuilder(config.RecycleGradeKey);
			_window = new Queue<GradeMatrix>();
		}

		/// <summary>
		/// Current robust score matrix for external consumers (e.g. heatmaps).
		/// May be null before the first update.
		/// </summary>
		public double[][] CurrentZScores => _zScores;

		// --- Diagnostic accessors (read-only views into the last computed window) ---
		public int LaneCount => _lanes;
		public int GradeCount => _grades;
		public IReadOnlyList<string> GradeKeys => _canonicalGradeKeys;
		public int WindowSampleCount => _window?.Count ?? 0;
		public double[] LaneAverageFpm => _laneAverageFpm;
		public int[] EligiblePeerCounts => _eligiblePeerCounts;
		public double[][] LaneSharePct => _laneSharePct;
		public double[][] PeerMedianPct => _peerMedianPct;
		public double[][] PctDeviation => _pctDeviation;
		public int[] ConsecutiveLaneSignals => _consecutiveLaneSignals;
		public AnomalyDetectorConfig Config => _config;

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

			EnsureCanonicalDimensions(matrix);
			_window.Enqueue(matrix);

			while (_window.Count > _config.WindowMinutes)
				_window.Dequeue();

			ComputeAggregate();

			if (!ComputeCompositionStatistics())
			{
				ResetScores();
				Array.Clear(_consecutiveLaneSignals, 0, _consecutiveLaneSignals.Length);
				return new List<AnomalyEvent>();
			}

			return EvaluateAlarms(now, serialNo, batchRecordId);
		}

		public void Reset()
		{
			_window.Clear();
			_cooldown.Reset();
			if (_aggregate != null) Zero(_aggregate);
			ResetScores();
			if (_consecutiveLaneSignals != null)
				Array.Clear(_consecutiveLaneSignals, 0, _consecutiveLaneSignals.Length);
			_canonicalGradeKeys.Clear();
			_canonicalGradeIndex.Clear();
			_lanes = 0;
			_grades = 0;
			_aggregate = null;
			_zScores = null;
			_pctDeviation = null;
			_peerMedianPct = null;
			_laneSharePct = null;
			_laneAverageFpm = null;
			_eligiblePeerCounts = null;
			_consecutiveLaneSignals = null;
		}

		/// <summary>
		/// Grows the detector's internal lane/grade dimensions to accommodate any new
		/// lanes or grade keys in the incoming snapshot. Existing aggregated values and
		/// rolling-window samples are preserved - new cells are zero-initialised.
		/// This intentionally never shrinks the canonical shape: a grade that appears
		/// once remains indexable even if later snapshots omit it.
		/// </summary>
		private void EnsureCanonicalDimensions(GradeMatrix matrix)
		{
			bool gradesGrew = false;
			for (int g = 0; g < matrix.GradeKeys.Count; g++)
			{
				var key = matrix.GradeKeys[g];
				if (!_canonicalGradeIndex.ContainsKey(key))
				{
					_canonicalGradeIndex[key] = _canonicalGradeKeys.Count;
					_canonicalGradeKeys.Add(key);
					gradesGrew = true;
				}
			}

			int newLanes = Math.Max(_lanes, matrix.LaneCount);
			int newGrades = _canonicalGradeKeys.Count;

			if (_aggregate == null)
			{
				AllocateState(newLanes, newGrades);
				return;
			}

			if (newLanes != _lanes || gradesGrew)
				GrowState(newLanes, newGrades);
		}

		private void AllocateState(int lanes, int grades)
		{
			_lanes = lanes;
			_grades = grades;
			_aggregate = CreateArray(lanes, grades);
			_zScores = CreateArray(lanes, grades);
			_pctDeviation = CreateArray(lanes, grades);
			_peerMedianPct = CreateArray(lanes, grades);
			_laneSharePct = CreateArray(lanes, grades);
			_laneAverageFpm = new double[lanes];
			_eligiblePeerCounts = new int[lanes];
			_consecutiveLaneSignals = new int[lanes];
		}

		private void GrowState(int newLanes, int newGrades)
		{
			_aggregate = GrowMatrix(_aggregate, newLanes, newGrades);
			_zScores = GrowMatrix(_zScores, newLanes, newGrades);
			_pctDeviation = GrowMatrix(_pctDeviation, newLanes, newGrades);
			_peerMedianPct = GrowMatrix(_peerMedianPct, newLanes, newGrades);
			_laneSharePct = GrowMatrix(_laneSharePct, newLanes, newGrades);
			_laneAverageFpm = GrowVector(_laneAverageFpm, newLanes);
			_eligiblePeerCounts = GrowIntVector(_eligiblePeerCounts, newLanes);
			_consecutiveLaneSignals = GrowIntVector(_consecutiveLaneSignals, newLanes);
			_lanes = newLanes;
			_grades = newGrades;
		}

		private void ComputeAggregate()
		{
			Zero(_aggregate);
			foreach (var m in _window)
			{
				// Project this snapshot's (lane, snapGradeIdx) values into the detector's
				// canonical (lane, canonicalGradeIdx) coordinates. A snapshot that omits
				// a previously-seen grade simply contributes zero for that canonical slot.
				var snapKeys = m.GradeKeys;
				var mapping = new int[snapKeys.Count];
				for (int j = 0; j < snapKeys.Count; j++)
				{
					int canonicalIdx;
					mapping[j] = _canonicalGradeIndex.TryGetValue(snapKeys[j], out canonicalIdx)
						? canonicalIdx : -1;
				}

				int lanesInSnap = Math.Min(m.LaneCount, _lanes);
				for (int lane = 0; lane < lanesInSnap; lane++)
				{
					var targetRow = _aggregate[lane];
					for (int j = 0; j < snapKeys.Count; j++)
					{
						int canonicalIdx = mapping[j];
						if (canonicalIdx < 0) continue;
						targetRow[canonicalIdx] += m[lane, j];
					}
				}
			}
		}

		private bool ComputeCompositionStatistics()
		{
			ResetScores();

			if (_window.Count == 0)
				return false;

			var laneTotals = RowSums(_aggregate);
			int sampleCount = _window.Count;

			for (int lane = 0; lane < _lanes; lane++)
			{
				_laneAverageFpm[lane] = laneTotals[lane] / sampleCount;
				if (laneTotals[lane] <= 0)
					continue;

				for (int grade = 0; grade < _grades; grade++)
					_laneSharePct[lane][grade] = 100.0 * _aggregate[lane][grade] / laneTotals[lane];
			}

			bool anyComparableLane = false;
			for (int lane = 0; lane < _lanes; lane++)
			{
				if (_laneAverageFpm[lane] < _config.MinLaneFpm)
					continue;

				var peerLanes = GetEligiblePeerLanes(lane);
				_eligiblePeerCounts[lane] = peerLanes.Count;
				if (peerLanes.Count < _config.MinActivePeerLanes)
					continue;

				anyComparableLane = true;
				for (int grade = 0; grade < _grades; grade++)
				{
					var peerShares = new List<double>(peerLanes.Count);
					for (int peerIndex = 0; peerIndex < peerLanes.Count; peerIndex++)
						peerShares.Add(_laneSharePct[peerLanes[peerIndex]][grade]);

					double median = ComputeMedian(peerShares);
					double mad = ComputeMad(peerShares, median);
					double spread = Math.Max(mad * 1.4826, MinimumRobustSpreadPoints);
					double deltaPts = _laneSharePct[lane][grade] - median;

					_peerMedianPct[lane][grade] = median;
					_pctDeviation[lane][grade] = deltaPts;
					_zScores[lane][grade] = spread > 0 ? deltaPts / spread : 0;
				}
			}

			return anyComparableLane;
		}

		private List<AnomalyEvent> EvaluateAlarms(DateTimeOffset now, string serialNo, int batchRecordId)
		{
			var events = new List<AnomalyEvent>();
			for (int lane = 0; lane < _lanes; lane++)
			{
				var laneEvent = EvaluateCompositionSkew(lane, now, serialNo, batchRecordId);
				if (laneEvent != null)
					events.Add(laneEvent);
			}

			return events;
		}

		private AnomalyEvent EvaluateCompositionSkew(int lane, DateTimeOffset now, string serialNo, int batchRecordId)
		{
			if (_laneAverageFpm[lane] < _config.MinLaneFpm || _eligiblePeerCounts[lane] < _config.MinActivePeerLanes)
			{
				_consecutiveLaneSignals[lane] = 0;
				return null;
			}

			int dominantIdx = -1;
			double dominantScore = 0;
			double dominantDeltaPts = 0;

			int bestPositiveIdx = -1;
			int bestNegativeIdx = -1;
			double bestPositiveDelta = double.MinValue;
			double bestNegativeDelta = double.MaxValue;
			double maxAbsDelta = 0;
			int maxAbsDeltaIdx = -1;
			double laneCompositionSkew = 0;

			for (int grade = 0; grade < _grades; grade++)
			{
				double deltaPts = _pctDeviation[lane][grade];
				double score = _zScores[lane][grade];
				double absDelta = Math.Abs(deltaPts);
				double absScore = Math.Abs(score);
				laneCompositionSkew += absDelta;

				if (deltaPts > bestPositiveDelta)
				{
					bestPositiveDelta = deltaPts;
					bestPositiveIdx = grade;
				}

				if (deltaPts < bestNegativeDelta)
				{
					bestNegativeDelta = deltaPts;
					bestNegativeIdx = grade;
				}

				if (absDelta > maxAbsDelta)
				{
					maxAbsDelta = absDelta;
					maxAbsDeltaIdx = grade;
				}

				var passesBaseDelta = absDelta >= _config.BandLowMin;
				var passesRobustScore = absScore >= _config.ZGate;
				var passesExtremeDeltaFallback = absDelta >= _config.BandMediumMax;

				// Primary gate: material share delta + robust-score significance.
				// Fallback gate: allow very large share skews even when peer variance is broad.
				if (!passesBaseDelta || (!passesRobustScore && !passesExtremeDeltaFallback))
					continue;

				if (dominantIdx < 0
					|| Math.Abs(deltaPts) > Math.Abs(dominantDeltaPts)
					|| (Math.Abs(deltaPts).Equals(Math.Abs(dominantDeltaPts)) && Math.Abs(score) > Math.Abs(dominantScore)))
				{
					dominantIdx = grade;
					dominantDeltaPts = deltaPts;
					dominantScore = score;
				}
			}

			// L1 composition distance in percentage points; divide by 2 to normalize.
			laneCompositionSkew *= 0.5;

			var passesLaneSkewFallback = laneCompositionSkew >= _config.BandMediumMax;
			if (dominantIdx < 0 && passesLaneSkewFallback && maxAbsDeltaIdx >= 0)
			{
				dominantIdx = maxAbsDeltaIdx;
				dominantDeltaPts = _pctDeviation[lane][dominantIdx];
				dominantScore = _zScores[lane][dominantIdx];
			}

			if (dominantIdx < 0)
			{
				_consecutiveLaneSignals[lane] = 0;
				return null;
			}

			_consecutiveLaneSignals[lane]++;
			if (_consecutiveLaneSignals[lane] < _config.MinConsecutiveWindows)
				return null;

			var severityBasis = Math.Max(Math.Abs(dominantDeltaPts), laneCompositionSkew);
			string severity = PriorityClassifier.Classify(severityBasis, _config);
			if (severity == null)
				return null;

			string gradeKey = _canonicalGradeKeys[dominantIdx];
			if (_cooldown.IsOnCooldown(lane, gradeKey, now))
				return null;

			_cooldown.Record(lane, gradeKey, now);

			var narrative = _narrative.BuildCompositionSkewNarrative(
				lane,
				gradeKey,
				_laneSharePct[lane][dominantIdx],
				_peerMedianPct[lane][dominantIdx],
				dominantDeltaPts,
				dominantScore,
				bestPositiveIdx >= 0 ? _canonicalGradeKeys[bestPositiveIdx] : string.Empty,
				bestPositiveIdx >= 0 ? _laneSharePct[lane][bestPositiveIdx] : 0,
				bestPositiveIdx >= 0 ? _peerMedianPct[lane][bestPositiveIdx] : 0,
				bestPositiveIdx >= 0 ? _pctDeviation[lane][bestPositiveIdx] : 0,
				bestNegativeIdx >= 0 ? _canonicalGradeKeys[bestNegativeIdx] : string.Empty,
				bestNegativeIdx >= 0 ? _laneSharePct[lane][bestNegativeIdx] : 0,
				bestNegativeIdx >= 0 ? _peerMedianPct[lane][bestNegativeIdx] : 0,
				bestNegativeIdx >= 0 ? _pctDeviation[lane][bestNegativeIdx] : 0);

			return new AnomalyEvent
			{
				EventTs = now,
				SerialNo = serialNo,
				BatchRecordId = batchRecordId,
				LaneNo = lane + 1,
				GradeKey = gradeKey,
				Qty = _aggregate[lane][dominantIdx],
				Pct = severityBasis,
				AnomalyScore = dominantScore,
				Severity = severity,
				ExplanationJson = BuildExplanationJson(lane, dominantIdx, bestPositiveIdx, bestNegativeIdx, laneCompositionSkew),
				ModelVersion = ModelVersion,
				DeliveredTo = string.Empty,
				AlarmTitle = narrative.Title,
				AlarmDetails = narrative.Details
			};
		}

		private string BuildExplanationJson(int lane, int dominantIdx, int bestPositiveIdx, int bestNegativeIdx, double laneCompositionSkew)
		{
			return JsonConvert.SerializeObject(new
			{
				type = "lane_composition_skew",
				lane = lane + 1,
				laneAverageFpm = Math.Round(_laneAverageFpm[lane], 2),
				activePeerLanes = _eligiblePeerCounts[lane],
				windowSamples = _window.Count,
				compositionSkewPts = Math.Round(laneCompositionSkew, 2),
				dominant = BuildGradeExplanation(lane, dominantIdx),
				surplus = BuildGradeExplanation(lane, bestPositiveIdx),
				deficit = BuildGradeExplanation(lane, bestNegativeIdx)
			});
		}

		private object BuildGradeExplanation(int lane, int gradeIdx)
		{
			if (gradeIdx < 0)
				return null;

			return new
			{
				gradeKey = _canonicalGradeKeys[gradeIdx],
				laneSharePct = Math.Round(_laneSharePct[lane][gradeIdx], 2),
				peerMedianPct = Math.Round(_peerMedianPct[lane][gradeIdx], 2),
				deltaPts = Math.Round(_pctDeviation[lane][gradeIdx], 2),
				score = Math.Round(_zScores[lane][gradeIdx], 2)
			};
		}

		private List<int> GetEligiblePeerLanes(int lane)
		{
			var peers = new List<int>();
			for (int peer = 0; peer < _lanes; peer++)
			{
				if (peer == lane)
					continue;

				if (_laneAverageFpm[peer] >= _config.MinPeerLaneFpm)
					peers.Add(peer);
			}

			return peers;
		}

		private void ResetScores()
		{
			if (_zScores != null) Zero(_zScores);
			if (_pctDeviation != null) Zero(_pctDeviation);
			if (_peerMedianPct != null) Zero(_peerMedianPct);
			if (_laneSharePct != null) Zero(_laneSharePct);
			if (_laneAverageFpm != null) Array.Clear(_laneAverageFpm, 0, _laneAverageFpm.Length);
			if (_eligiblePeerCounts != null) Array.Clear(_eligiblePeerCounts, 0, _eligiblePeerCounts.Length);
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

		/// <summary>
		/// Expands a 2D array to at least <paramref name="rows"/> x <paramref name="cols"/>,
		/// preserving existing values. New cells are zero-initialised. Used to grow the
		/// detector state when a new lane or grade key is seen.
		/// </summary>
		private static double[][] GrowMatrix(double[][] src, int rows, int cols)
		{
			if (src != null && src.Length >= rows && (src.Length == 0 || src[0].Length >= cols))
				return src;

			var dst = new double[rows][];
			for (int i = 0; i < rows; i++)
			{
				dst[i] = new double[cols];
				if (src != null && i < src.Length && src[i] != null)
				{
					int copyLen = Math.Min(src[i].Length, cols);
					Array.Copy(src[i], dst[i], copyLen);
				}
			}
			return dst;
		}

		private static double[] GrowVector(double[] src, int length)
		{
			if (src != null && src.Length >= length)
				return src;
			var dst = new double[length];
			if (src != null)
				Array.Copy(src, dst, Math.Min(src.Length, length));
			return dst;
		}

		private static int[] GrowIntVector(int[] src, int length)
		{
			if (src != null && src.Length >= length)
				return src;
			var dst = new int[length];
			if (src != null)
				Array.Copy(src, dst, Math.Min(src.Length, length));
			return dst;
		}

		private static double[] RowSums(double[][] arr)
		{
			var sums = new double[arr.Length];
			for (int i = 0; i < arr.Length; i++)
				for (int j = 0; j < arr[i].Length; j++)
					sums[i] += arr[i][j];
			return sums;
		}

		private static double ComputeMedian(List<double> values)
		{
			if (values == null || values.Count == 0)
				return 0;

			values.Sort();
			int mid = values.Count / 2;
			if ((values.Count % 2) == 0)
				return (values[mid - 1] + values[mid]) / 2.0;

			return values[mid];
		}

		private static double ComputeMad(List<double> values, double median)
		{
			if (values == null || values.Count == 0)
				return 0;

			var deviations = new List<double>(values.Count);
			for (int i = 0; i < values.Count; i++)
				deviations.Add(Math.Abs(values[i] - median));

			return ComputeMedian(deviations);
		}
	}
}

