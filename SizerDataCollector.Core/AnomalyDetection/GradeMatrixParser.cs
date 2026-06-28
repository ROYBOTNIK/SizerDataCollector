using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace SizerDataCollector.Core.AnomalyDetection
{
	/// <summary>
	/// Parses the raw lanes_grade_fpm JSON payload (as returned by the Sizer WCF API
	/// and stored in public.metrics.value_json) into a GradeMatrix.
	///
	/// Real payload shape:
	///   - The top-level JSON value is an ARRAY of outlets.
	///   - The outlet ARRAY INDEX IS THE LANE (0-based): outlets[0] == lane 1,
	///     outlets[31] == lane 32, etc. The LaneNo in emitted events is therefore
	///     `arrayIndex + 1`.
	///   - Each outlet is an object whose property keys have the form
	///     "&lt;descriptor&gt;_&lt;GRADE_SUFFIX&gt;" (for example
	///     "2026 Delta Map_Peddler" or "2026 Delta Map_Cull D/S"). The descriptor
	///     is a product/map identifier that does NOT encode the lane number -
	///     only the suffix after the FINAL underscore is the grade.
	///   - Property values are the grade FPM count for that (lane, grade) cell.
	///   - An outlet may be empty (<c>{}</c>); that simply means zero FPM across
	///     all grades for that lane in this snapshot.
	///
	/// Historical note: older code attempted to extract the lane number from the
	/// leading digits of the property name, which on live machines was the YEAR
	/// (e.g. "2026 Delta Map_..."), producing a bogus 2027-lane matrix and hiding
	/// every real anomaly. This parser intentionally ignores any numbers inside
	/// the key and relies solely on array position for lane identity.
	/// </summary>
	public static class GradeMatrixParser
	{
		public static GradeMatrix Parse(string json)
		{
			if (string.IsNullOrWhiteSpace(json))
				return null;

			try
			{
				var token = JToken.Parse(json);
				return ParseToken(token);
			}
			catch
			{
				return null;
			}
		}

		/// <summary>
		/// Extract the distinct raw property keys (across all outlets) present in
		/// the payload. Purely for diagnostic / troubleshooting use.
		/// </summary>
		public static List<string> GetRawKeys(string json, int maxKeys = 50)
		{
			var keys = new List<string>();
			if (string.IsNullOrWhiteSpace(json))
				return keys;

			try
			{
				var token = JToken.Parse(json);
				if (token == null || token.Type != JTokenType.Array)
					return keys;

				var seen = new HashSet<string>(StringComparer.Ordinal);
				foreach (var outlet in (JArray)token)
				{
					if (outlet == null || outlet.Type != JTokenType.Object)
						continue;
					foreach (var prop in ((JObject)outlet).Properties())
					{
						if (seen.Add(prop.Name))
							keys.Add(prop.Name);
						if (keys.Count >= maxKeys)
							return keys;
					}
				}
			}
			catch
			{
			}
			return keys;
		}

		private static GradeMatrix ParseToken(JToken token)
		{
			if (token == null || token.Type != JTokenType.Array)
				return null;

			var outlets = (JArray)token;
			if (outlets.Count == 0)
				return null;

			int laneCount = outlets.Count;
			var suffixOrder = new LinkedHashSet();
			var cells = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

			for (int laneIdx = 0; laneIdx < outlets.Count; laneIdx++)
			{
				var outlet = outlets[laneIdx];
				if (outlet == null || outlet.Type != JTokenType.Object)
					continue;

				foreach (var prop in ((JObject)outlet).Properties())
				{
					if (prop.Value == null
						|| (prop.Value.Type != JTokenType.Integer && prop.Value.Type != JTokenType.Float))
						continue;

					var suffix = ExtractGradeSuffix(prop.Name);
					if (suffix == null)
						continue;

					suffixOrder.Add(suffix);

					string cellKey = laneIdx + "\t" + suffix;
					double existing;
					cells.TryGetValue(cellKey, out existing);
					cells[cellKey] = existing + prop.Value.Value<double>();
				}
			}

			if (suffixOrder.Count == 0)
				return null;

			var gradeKeys = suffixOrder.ToList();
			var values = new double[laneCount][];
			for (int lane = 0; lane < laneCount; lane++)
			{
				values[lane] = new double[gradeKeys.Count];
				for (int g = 0; g < gradeKeys.Count; g++)
				{
					string cellKey = lane + "\t" + gradeKeys[g];
					double val;
					if (cells.TryGetValue(cellKey, out val))
						values[lane][g] = val;
				}
			}

			return new GradeMatrix(values, gradeKeys.AsReadOnly());
		}

		/// <summary>
		/// Extracts the grade suffix from a Sizer outlet property key. The grade is
		/// everything after the FINAL underscore in the key. Returns null if the
		/// key has no underscore or the suffix is empty.
		/// </summary>
		internal static string ExtractGradeSuffix(string key)
		{
			if (string.IsNullOrEmpty(key))
				return null;

			int underscoreIdx = key.LastIndexOf('_');
			if (underscoreIdx < 0 || underscoreIdx >= key.Length - 1)
				return null;

			var suffix = key.Substring(underscoreIdx + 1);
			return string.IsNullOrWhiteSpace(suffix) ? null : suffix;
		}

		private sealed class LinkedHashSet
		{
			private readonly List<string> _list = new List<string>();
			private readonly HashSet<string> _set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

			public int Count => _list.Count;

			public void Add(string item)
			{
				if (_set.Add(item))
					_list.Add(item);
			}

			public List<string> ToList() => new List<string>(_list);
		}
	}
}
