using System;
using System.Collections.Generic;
using System.Text;

namespace SizerDataCollector.Core.AnomalyDetection
{
	public struct AlarmNarrative
	{
		public string Title;
		public string Details;
	}

	/// <summary>
	/// Generates operator-friendly alarm text for grade-composition anomalies.
	/// The messages avoid statistical jargon (z-scores, MAD, "peer median") and
	/// talk about lanes producing more or less of a grade than what's typical on
	/// the rest of the machine. Raw numeric details are still written to the
	/// event's ExplanationJson for programmatic consumers.
	/// </summary>
	public sealed class NarrativeBuilder
	{
		/// <summary>
		/// Minimum magnitude (percentage points) for a secondary grade to be
		/// included in the narrative. Avoids cluttering the message with tiny
		/// secondary deltas that are not operationally interesting.
		/// </summary>
		private const double SecondaryDeltaFloor = 2.0;

		private readonly string _recycleSuffix;

		private static readonly Dictionary<string, int> QualityRanks =
			new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
			{
				{ "EXP LIGHT", 8 },
				{ "EXP DARK",  7 },
				{ "DOM LIGHT", 6 },
				{ "DOM DARK",  5 },
				{ "GATE",      4 },
				{ "D/S",       3 },
				{ "SOFT",      2 },
				{ "CULLS",     1 },
				{ "RCY",       0 },
			};

		public NarrativeBuilder(string recycleGradeKey)
		{
			_recycleSuffix = ShortName(recycleGradeKey ?? "RCY");
		}

		public AlarmNarrative BuildRecycleNarrative(int lane, double pct, double z)
		{
			string direction = pct > 0 ? "more Recycle than expected" : "less Recycle than expected";
			return new AlarmNarrative
			{
				Title = $"Lane {lane + 1}: Recycle {(pct > 0 ? "excess" : "shortage")}",
				Details = $"Lane {lane + 1}: {direction} ({pct:+0.0;-0.0}%, z={z:+0.0;-0.0})"
			};
		}

		/// <summary>
		/// Builds an operator-friendly narrative for a lane whose grade composition
		/// has diverged from the rest of the machine.
		/// </summary>
		/// <param name="lane">Zero-based lane index (emitted as Lane {lane+1} in text).</param>
		/// <param name="dominantGradeKey">Grade driving the alarm (largest |share delta|).</param>
		/// <param name="dominantLanePct">Lane's share of the dominant grade (0-100%).</param>
		/// <param name="dominantPeerPct">Typical peer-lane share of the dominant grade (median, 0-100%).</param>
		/// <param name="dominantDeltaPts">Signed deviation of the dominant grade vs peers (percentage points).</param>
		/// <param name="dominantScore">Robust score (for ExplanationJson only; not surfaced in text).</param>
		/// <param name="posGrade">Grade with the most-positive delta on this lane.</param>
		/// <param name="posLanePct">Lane share of that surplus grade.</param>
		/// <param name="posPeerPct">Typical peer share of that surplus grade.</param>
		/// <param name="posDeltaPts">Positive delta in percentage points.</param>
		/// <param name="negGrade">Grade with the most-negative delta on this lane.</param>
		/// <param name="negLanePct">Lane share of that deficit grade.</param>
		/// <param name="negPeerPct">Typical peer share of that deficit grade.</param>
		/// <param name="negDeltaPts">Negative delta in percentage points.</param>
		public AlarmNarrative BuildCompositionSkewNarrative(
			int lane,
			string dominantGradeKey,
			double dominantLanePct,
			double dominantPeerPct,
			double dominantDeltaPts,
			double dominantScore,
			string posGrade,
			double posLanePct,
			double posPeerPct,
			double posDeltaPts,
			string negGrade,
			double negLanePct,
			double negPeerPct,
			double negDeltaPts)
		{
			int laneNo = lane + 1;
			string dominant = ShortName(dominantGradeKey);
			string surplus = ShortName(posGrade);
			string deficit = ShortName(negGrade);

			bool dominantIsSurplus = dominantDeltaPts >= 0;
			bool hasMeaningfulSurplus = !string.IsNullOrWhiteSpace(surplus) && posDeltaPts >= SecondaryDeltaFloor;
			bool hasMeaningfulDeficit = !string.IsNullOrWhiteSpace(deficit) && negDeltaPts <= -SecondaryDeltaFloor;
			bool mentionBothSides = hasMeaningfulSurplus && hasMeaningfulDeficit
				&& !string.Equals(surplus, deficit, StringComparison.OrdinalIgnoreCase);

			string title;
			if (mentionBothSides)
			{
				title = $"Lane {laneNo}: heavy on {surplus}, light on {deficit}";
			}
			else if (dominantIsSurplus)
			{
				title = $"Lane {laneNo}: producing mostly {dominant} "
					+ $"({dominantLanePct:0}% vs {dominantPeerPct:0}% typical)";
			}
			else
			{
				title = $"Lane {laneNo}: very little {dominant} "
					+ $"({dominantLanePct:0}% vs {dominantPeerPct:0}% typical)";
			}

			var details = new StringBuilder();
			details.Append($"Lane {laneNo} is grading differently from the rest of the machine. ");
			details.Append(FormatGradeClause(dominant, dominantLanePct, dominantPeerPct, dominantDeltaPts));

			// Pick the most useful counterpart grade to add context:
			// if dominant is a surplus, show the biggest deficit (and vice versa).
			string counterpartGrade;
			double counterpartLane, counterpartPeer, counterpartDelta;
			if (dominantIsSurplus)
			{
				counterpartGrade = deficit;
				counterpartLane = negLanePct;
				counterpartPeer = negPeerPct;
				counterpartDelta = negDeltaPts;
			}
			else
			{
				counterpartGrade = surplus;
				counterpartLane = posLanePct;
				counterpartPeer = posPeerPct;
				counterpartDelta = posDeltaPts;
			}

			if (!string.IsNullOrWhiteSpace(counterpartGrade)
				&& !string.Equals(counterpartGrade, dominant, StringComparison.OrdinalIgnoreCase)
				&& Math.Abs(counterpartDelta) >= SecondaryDeltaFloor)
			{
				details.Append("; at the same time ");
				details.Append(FormatGradeClause(counterpartGrade, counterpartLane, counterpartPeer, counterpartDelta));
			}

			details.Append('.');

			return new AlarmNarrative
			{
				Title = title,
				Details = details.ToString()
			};
		}

		/// <summary>
		/// Formats a single grade observation in plain language:
		/// "D/S is 59.0% on this lane vs 13.6% typical (+45.8 pts above normal)".
		/// </summary>
		private static string FormatGradeClause(string grade, double lanePct, double peerPct, double deltaPts)
		{
			string direction = deltaPts >= 0 ? "above normal" : "below normal";
			return $"{grade} is {lanePct:0.0}% on this lane vs {peerPct:0.0}% typical "
				+ $"({deltaPts:+0.0;-0.0} pts {direction})";
		}

		/// <param name="lane">Zero-based lane index.</param>
		/// <param name="posGrade">Grade with the highest positive z-score (surplus).</param>
		/// <param name="negGrade">Grade with the most negative z-score (deficit).</param>
		/// <param name="pctPos">Percent deviation for the surplus grade.</param>
		/// <param name="pctNeg">Percent deviation for the deficit grade.</param>
		/// <param name="zPos">Z-score for the surplus grade.</param>
		/// <param name="zNeg">Z-score for the deficit grade.</param>
		/// <param name="bandLowMin">Minimum percent deviation threshold.</param>
		public AlarmNarrative BuildGradeShiftNarrative(
			int lane,
			string posGrade, string negGrade,
			double pctPos, double pctNeg,
			double zPos, double zNeg,
			double bandLowMin)
		{
			string posSuffix = ShortName(posGrade);
			string negSuffix = ShortName(negGrade);

			if (Math.Abs(pctNeg) < bandLowMin)
			{
				string direction = pctPos > 0 ? "surplus" : "shortage";
				return new AlarmNarrative
				{
					Title = $"Lane {lane + 1}: {posSuffix} {direction}",
					Details = $"Lane {lane + 1}: {direction} of {posSuffix} ({pctPos:+0.0;-0.0}%, z={zPos:+0.0;-0.0})"
				};
			}

			int posRank = GetQualityRank(posSuffix);
			int negRank = GetQualityRank(negSuffix);

			string qualityNote;
			if (posRank >= 0 && negRank >= 0 && posRank != negRank)
			{
				qualityNote = posRank < negRank
					? "good fruit downgrading"
					: "bad fruit upgrading";
			}
			else
			{
				qualityNote = "grade shift";
			}

			return new AlarmNarrative
			{
				Title = $"Lane {lane + 1}: {qualityNote} ({negSuffix} -> {posSuffix})",
				Details = $"Lane {lane + 1}: {qualityNote}"
					+ $" - less {negSuffix} than expected ({pctNeg:+0.0;-0.0}%),"
					+ $" more {posSuffix} than expected ({pctPos:+0.0;-0.0}%)"
			};
		}

		/// <summary>
		/// Extracts the grade suffix from a grade key. If the key is already
		/// a suffix (no underscore), returns it as-is.
		/// </summary>
		internal static string ShortName(string fullGradeKey)
		{
			if (string.IsNullOrEmpty(fullGradeKey))
				return fullGradeKey ?? string.Empty;

			int idx = fullGradeKey.LastIndexOf('_');
			if (idx >= 0 && idx < fullGradeKey.Length - 1)
				return fullGradeKey.Substring(idx + 1);

			return fullGradeKey;
		}

		private static int GetQualityRank(string suffix)
		{
			int rank;
			if (QualityRanks.TryGetValue(suffix, out rank))
				return rank;
			return -1;
		}
	}
}
