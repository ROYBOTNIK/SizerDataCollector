using System;
using System.Collections.Generic;

namespace SizerDataCollector.Core.AnomalyDetection
{
	public struct AlarmNarrative
	{
		public string Title;
		public string Details;
	}

	/// <summary>
	/// Generates human-readable alarm text using grade suffixes
	/// and quality-aware direction language ("good fruit downgrading" vs "bad fruit upgrading").
	/// </summary>
	public sealed class NarrativeBuilder
	{
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

			// posGrade has surplus (more than expected), negGrade has deficit (less than expected).
			// Fruit is effectively flowing FROM the deficit grade INTO the surplus grade.
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
