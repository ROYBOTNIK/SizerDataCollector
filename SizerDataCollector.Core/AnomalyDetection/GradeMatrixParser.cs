using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace SizerDataCollector.Core.AnomalyDetection
{
	/// <summary>
	/// Parses the raw lanes_grade_fpm JSON payload (as returned by the Sizer WCF API
	/// and stored in public.metrics.value_json) into a GradeMatrix.
	///
	/// The payload is an array of outlets. Each outlet object has keys of the form
	/// "&lt;lane&gt; &lt;variety&gt; &lt;year&gt; &lt;version&gt;_&lt;GRADE_SUFFIX&gt;" with numeric FPM values.
	/// The parser extracts the lane number (leading digits) and grade suffix (after the
	/// last underscore), then aggregates counts across all outlets into a lanes x grades matrix.
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

		private static GradeMatrix ParseToken(JToken token)
		{
			if (token == null || token.Type != JTokenType.Array)
				return null;

			var outlets = (JArray)token;
			if (outlets.Count == 0)
				return null;

			int maxLane = -1;
			var suffixOrder = new LinkedHashSet();
			var cells = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

			foreach (var outlet in outlets)
			{
				if (outlet == null || outlet.Type != JTokenType.Object)
					continue;

				foreach (var prop in ((JObject)outlet).Properties())
				{
					if (prop.Value.Type != JTokenType.Integer && prop.Value.Type != JTokenType.Float)
						continue;

					int laneNo;
					string suffix;
					if (!TryParseKey(prop.Name, out laneNo, out suffix))
						continue;

					if (laneNo > maxLane)
						maxLane = laneNo;

					suffixOrder.Add(suffix);

					string cellKey = laneNo + "\t" + suffix;
					double existing;
					cells.TryGetValue(cellKey, out existing);
					cells[cellKey] = existing + prop.Value.Value<double>();
				}
			}

			if (maxLane < 0 || suffixOrder.Count == 0)
				return null;

			var gradeKeys = suffixOrder.ToList();
			int laneCount = maxLane + 1;

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
		/// Extracts lane number and grade suffix from a Sizer grade key.
		/// "7 Dark Cherry 2025 V3_EXP LIGHT" -> lane=7, suffix="EXP LIGHT"
		/// </summary>
		private static bool TryParseKey(string key, out int laneNo, out string suffix)
		{
			laneNo = -1;
			suffix = null;

			if (string.IsNullOrEmpty(key))
				return false;

			int numEnd = 0;
			while (numEnd < key.Length && char.IsDigit(key[numEnd]))
				numEnd++;

			if (numEnd == 0 || !int.TryParse(key.Substring(0, numEnd), out laneNo))
				return false;

			int underscoreIdx = key.LastIndexOf('_');
			if (underscoreIdx < 0 || underscoreIdx >= key.Length - 1)
				return false;

			suffix = key.Substring(underscoreIdx + 1);
			return true;
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
