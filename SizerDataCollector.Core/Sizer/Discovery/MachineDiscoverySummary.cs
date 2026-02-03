using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace SizerDataCollector.Core.Sizer.Discovery
{
	/// <summary>
	/// Lightweight, non-semantic rollup for quick inspection only.
	/// Derives simple counts/keys; if parsing fails values remain null.
	/// </summary>
	public sealed class MachineDiscoverySummary
	{
		public int? OutletCount { get; set; }
		public IReadOnlyList<string> OutletNames { get; set; }
		public int? LaneViewCount { get; set; }
		public int? GradeLaneArrayLength { get; set; }
		public int? SizeLaneArrayLength { get; set; }
		public int? LastNonNullGradeLaneIndex { get; set; }
		public int? LastNonNullSizeLaneIndex { get; set; }
		public int? DerivedLaneCountCandidate { get; set; }
		public string LaneCountConfidence { get; set; }
		public IReadOnlyCollection<string> DistinctGradeKeys { get; set; }
		public IReadOnlyCollection<string> DistinctSizeKeys { get; set; }
		public IReadOnlyCollection<string> DistinctGradeKeysSample { get; set; }
		public IReadOnlyCollection<string> DistinctSizeKeysSample { get; set; }

		public static MachineDiscoverySummary FromPayloads(IDictionary<string, JToken> payloads)
		{
			if (payloads == null)
			{
				return null;
			}

			var summary = new MachineDiscoverySummary();

			TryPopulateOutlets(payloads, summary);
			TryPopulateLaneViews(payloads, summary);
			TryPopulateGradeKeys(payloads, summary);
			TryPopulateSizeKeys(payloads, summary);
			DeriveLaneConfidence(summary);

			if (summary.OutletCount == null &&
			    summary.OutletNames == null &&
			    summary.LaneViewCount == null &&
			    summary.GradeLaneArrayLength == null &&
			    summary.SizeLaneArrayLength == null &&
			    summary.LastNonNullGradeLaneIndex == null &&
			    summary.LastNonNullSizeLaneIndex == null &&
			    summary.DerivedLaneCountCandidate == null &&
			    summary.LaneCountConfidence == null &&
			    summary.DistinctGradeKeys == null &&
			    summary.DistinctSizeKeys == null &&
			    summary.DistinctGradeKeysSample == null &&
			    summary.DistinctSizeKeysSample == null)
			{
				return null;
			}

			return summary;
		}

		private static void TryPopulateOutlets(IDictionary<string, JToken> payloads, MachineDiscoverySummary summary)
		{
			try
			{
				if (!payloads.TryGetValue("outlets_details", out var token))
				{
					return;
				}

				var array = Normalize(token) as JArray;
				if (array == null)
				{
					return;
				}

				summary.OutletCount = array.Count;

				var names = array
					.Select(t => t?["Name"]?.Value<string>())
					.Where(n => !string.IsNullOrWhiteSpace(n))
					.Take(20)
					.ToList();

				if (names.Count > 0)
				{
					summary.OutletNames = names;
				}
			}
			catch
			{
				// Leave summary values null if parsing fails.
			}
		}

		private static void TryPopulateLaneViews(IDictionary<string, JToken> payloads, MachineDiscoverySummary summary)
		{
			try
			{
				JArray array = null;

				if (payloads.TryGetValue("lanes_grade_fpm", out var grade))
				{
					array = Normalize(grade) as JArray;
				}

				if (array == null && payloads.TryGetValue("lanes_size_fpm", out var size))
				{
					array = Normalize(size) as JArray;
				}

				if (array == null)
				{
					return;
				}

				var count = array.Count(t => t != null && t.Type != JTokenType.Null);
				summary.LaneViewCount = count;
			}
			catch
			{
				// Leave summary values null if parsing fails.
			}
		}

		private static void TryPopulateGradeKeys(IDictionary<string, JToken> payloads, MachineDiscoverySummary summary)
		{
			try
			{
				if (!payloads.TryGetValue("lanes_grade_fpm", out var token))
				{
					return;
				}

				var array = Normalize(token) as JArray;
				if (array == null)
				{
					return;
				}

				summary.GradeLaneArrayLength = array.Count;

				int lastIndex = -1;
				var keys = array
					.Select((t, idx) =>
					{
						if (t != null && t.Type != JTokenType.Null)
						{
							lastIndex = idx;
							return t as JObject;
						}
						return null;
					})
					.Where(o => o != null)
					.SelectMany(o => o.Properties())
					.Select(p => p.Name)
					.Where(n => !string.IsNullOrWhiteSpace(n))
					.Distinct(StringComparer.OrdinalIgnoreCase)
					.ToList();

				if (keys.Count > 0)
				{
					summary.DistinctGradeKeys = keys;
					summary.DistinctGradeKeysSample = keys.Take(20).ToList();
				}

				if (lastIndex >= 0)
				{
					summary.LastNonNullGradeLaneIndex = lastIndex;
				}
			}
			catch
			{
				// Leave summary values null if parsing fails.
			}
		}

		private static void TryPopulateSizeKeys(IDictionary<string, JToken> payloads, MachineDiscoverySummary summary)
		{
			try
			{
				if (!payloads.TryGetValue("lanes_size_fpm", out var token))
				{
					return;
				}

				var array = Normalize(token) as JArray;
				if (array == null)
				{
					return;
				}

				summary.SizeLaneArrayLength = array.Count;

				int lastIndex = -1;
				var keys = array
					.Select((t, idx) =>
					{
						if (t != null && t.Type != JTokenType.Null)
						{
							lastIndex = idx;
							return t as JObject;
						}
						return null;
					})
					.Where(o => o != null)
					.SelectMany(o => o.Properties())
					.Select(p => p.Name)
					.Where(n => !string.IsNullOrWhiteSpace(n))
					.Distinct(StringComparer.OrdinalIgnoreCase)
					.ToList();

				if (keys.Count > 0)
				{
					summary.DistinctSizeKeys = keys;
					summary.DistinctSizeKeysSample = keys.Take(20).ToList();
				}

				if (lastIndex >= 0)
				{
					summary.LastNonNullSizeLaneIndex = lastIndex;
				}
			}
			catch
			{
				// Leave summary values null if parsing fails.
			}
		}

		private static void DeriveLaneConfidence(MachineDiscoverySummary summary)
		{
			int? candidate = null;

			if (summary.LastNonNullGradeLaneIndex.HasValue)
			{
				candidate = Math.Max(candidate ?? -1, summary.LastNonNullGradeLaneIndex.Value) + 1;
			}

			if (summary.LastNonNullSizeLaneIndex.HasValue)
			{
				candidate = Math.Max(candidate ?? -1, summary.LastNonNullSizeLaneIndex.Value) + 1;
			}

			if (candidate.HasValue && candidate.Value > 0)
			{
				summary.DerivedLaneCountCandidate = candidate.Value;
				summary.LaneCountConfidence = candidate.Value >= 1 ? "medium" : "low";
			}
			else
			{
				summary.LaneCountConfidence = "low";
			}
		}

		private static JToken Normalize(JToken token)
		{
			if (token is JValue value && value.Type == JTokenType.String)
			{
				var s = value.Value<string>();
				if (!string.IsNullOrWhiteSpace(s))
				{
					try
					{
						return JToken.Parse(s);
					}
					catch
					{
						return token;
					}
				}
			}

			return token;
		}
	}
}

