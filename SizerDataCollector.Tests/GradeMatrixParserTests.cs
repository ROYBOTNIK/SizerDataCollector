using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SizerDataCollector.Core.AnomalyDetection;

namespace SizerDataCollector.Tests
{
	/// <summary>
	/// Regression tests for <see cref="GradeMatrixParser"/> that lock in the
	/// real live-payload format observed on machine 140578:
	///
	///   - The top-level value is an array of outlets.
	///   - The outlet array index IS the lane number (0-based).
	///   - Each outlet's property keys have the shape "&lt;descriptor&gt;_&lt;grade&gt;"
	///     where the descriptor is a product label like "2026 Delta Map" and
	///     contains no lane information.
	///
	/// Historical bug: the previous parser tried to extract the lane number from
	/// the key prefix and was picking up the year "2026" as a lane, producing a
	/// 2027-lane matrix and scattering real lane data. These tests pin the fix.
	/// </summary>
	[TestClass]
	public class GradeMatrixParserTests
	{
		[TestMethod]
		public void Parse_ArrayIndexIsLane_Lane32Style()
		{
			// Build a 32-lane payload where lane 32 (index 31) is the heavy lane.
			var outlets = new Dictionary<string, double>[32];
			for (int i = 0; i < 31; i++)
			{
				outlets[i] = new Dictionary<string, double>
				{
					{ "2026 Delta Map_Peddler", 180.0 },
					{ "2026 Delta Map_Cull D/S", 120.0 },
					{ "2026 Delta Map_E4", 165.0 }
				};
			}

			outlets[31] = new Dictionary<string, double>
			{
				{ "2026 Delta Map_Peddler", 696.0 },
				{ "2026 Delta Map_Cull D/S", 330.0 },
				{ "2026 Delta Map_Cull", 40.0 }
			};

			var matrix = GradeMatrixParser.Parse(BuildOutletArrayJson(outlets));

			Assert.IsNotNull(matrix);
			Assert.AreEqual(32, matrix.LaneCount, "Lane count must equal array length.");

			int peddler = IndexOf(matrix.GradeKeys, "Peddler");
			int cullDs = IndexOf(matrix.GradeKeys, "Cull D/S");

			Assert.IsTrue(peddler >= 0, "Parser must produce a 'Peddler' grade key.");
			Assert.IsTrue(cullDs >= 0, "Parser must produce a 'Cull D/S' grade key.");

			Assert.AreEqual(696.0, matrix[31, peddler], 0.001, "Lane 32 (index 31) peddler count must match payload.");
			Assert.AreEqual(330.0, matrix[31, cullDs], 0.001, "Lane 32 (index 31) Cull D/S count must match payload.");
			Assert.AreEqual(180.0, matrix[0, peddler], 0.001, "Lane 1 (index 0) peddler count must match payload.");
		}

		[TestMethod]
		public void Parse_EmptyOutletTreatedAsZeroRow()
		{
			var outlets = new Dictionary<string, double>[3];
			outlets[0] = new Dictionary<string, double> { { "2026 Delta Map_Peddler", 10.0 } };
			outlets[1] = new Dictionary<string, double>(); // {}
			outlets[2] = new Dictionary<string, double> { { "2026 Delta Map_Peddler", 20.0 } };

			var matrix = GradeMatrixParser.Parse(BuildOutletArrayJson(outlets));

			Assert.IsNotNull(matrix);
			Assert.AreEqual(3, matrix.LaneCount);
			int peddler = IndexOf(matrix.GradeKeys, "Peddler");
			Assert.AreEqual(10.0, matrix[0, peddler], 0.001);
			Assert.AreEqual(0.0, matrix[1, peddler], 0.001);
			Assert.AreEqual(20.0, matrix[2, peddler], 0.001);
		}

		[TestMethod]
		public void Parse_DescriptorWithYearPrefix_DoesNotConfuseParser()
		{
			// The key descriptor contains "2026" - historical bug interpreted this as
			// lane 2026. The parser must ignore descriptor content and rely purely on
			// array position.
			var outlets = new Dictionary<string, double>[2];
			outlets[0] = new Dictionary<string, double> { { "2026 Delta Map_Peddler", 50.0 } };
			outlets[1] = new Dictionary<string, double> { { "2026 Delta Map_Peddler", 75.0 } };

			var matrix = GradeMatrixParser.Parse(BuildOutletArrayJson(outlets));

			Assert.IsNotNull(matrix);
			Assert.AreEqual(2, matrix.LaneCount, "Lane count must equal outlet array length, not a year embedded in the key.");
			int peddler = IndexOf(matrix.GradeKeys, "Peddler");
			Assert.AreEqual(50.0, matrix[0, peddler], 0.001);
			Assert.AreEqual(75.0, matrix[1, peddler], 0.001);
		}

		[TestMethod]
		public void Parse_GradeSuffixWithSlashAndSpace_PreservedExactly()
		{
			var outlets = new Dictionary<string, double>[1];
			outlets[0] = new Dictionary<string, double>
			{
				{ "2026 Delta Map_D/S", 11.0 },
				{ "2026 Delta Map_Cull D/S", 22.0 }
			};

			var matrix = GradeMatrixParser.Parse(BuildOutletArrayJson(outlets));

			Assert.IsNotNull(matrix);
			CollectionAssert.Contains((List<string>)new List<string>(matrix.GradeKeys), "D/S");
			CollectionAssert.Contains((List<string>)new List<string>(matrix.GradeKeys), "Cull D/S");
		}

		[TestMethod]
		public void GetRawKeys_ReturnsDistinctKeysFromPayload()
		{
			var outlets = new Dictionary<string, double>[2];
			outlets[0] = new Dictionary<string, double>
			{
				{ "2026 Delta Map_Peddler", 10.0 },
				{ "2026 Delta Map_Cull D/S", 5.0 }
			};
			outlets[1] = new Dictionary<string, double>
			{
				{ "2026 Delta Map_Peddler", 3.0 } // duplicate key name, different outlet
			};

			var keys = GradeMatrixParser.GetRawKeys(BuildOutletArrayJson(outlets), maxKeys: 50);

			Assert.AreEqual(2, keys.Count, "Distinct raw keys across outlets must de-duplicate.");
			CollectionAssert.Contains(keys, "2026 Delta Map_Peddler");
			CollectionAssert.Contains(keys, "2026 Delta Map_Cull D/S");
		}

		[TestMethod]
		public void Parse_KeyWithoutUnderscore_IsIgnored()
		{
			var outlets = new Dictionary<string, double>[1];
			outlets[0] = new Dictionary<string, double>
			{
				{ "NoUnderscoreKey", 99.0 },
				{ "2026 Delta Map_Peddler", 42.0 }
			};

			var matrix = GradeMatrixParser.Parse(BuildOutletArrayJson(outlets));

			Assert.IsNotNull(matrix);
			Assert.AreEqual(1, matrix.GradeCount, "Keys without an underscore must be skipped.");
			Assert.AreEqual("Peddler", matrix.GradeKeys[0]);
			Assert.AreEqual(42.0, matrix[0, 0], 0.001);
		}

		private static int IndexOf(IReadOnlyList<string> keys, string value)
		{
			for (int i = 0; i < keys.Count; i++)
			{
				if (string.Equals(keys[i], value, System.StringComparison.OrdinalIgnoreCase))
					return i;
			}
			return -1;
		}

		private static string BuildOutletArrayJson(Dictionary<string, double>[] outlets)
		{
			var sb = new StringBuilder();
			sb.Append('[');
			for (int i = 0; i < outlets.Length; i++)
			{
				if (i > 0) sb.Append(',');
				sb.Append('{');
				var outlet = outlets[i];
				if (outlet != null)
				{
					bool first = true;
					foreach (var kv in outlet)
					{
						if (!first) sb.Append(',');
						first = false;
						sb.Append('"');
						sb.Append(EscapeJsonString(kv.Key));
						sb.Append("\":");
						sb.Append(kv.Value.ToString("G", CultureInfo.InvariantCulture));
					}
				}
				sb.Append('}');
			}
			sb.Append(']');
			return sb.ToString();
		}

		private static string EscapeJsonString(string s)
		{
			var sb = new StringBuilder(s.Length);
			foreach (var c in s)
			{
				switch (c)
				{
					case '\\': sb.Append("\\\\"); break;
					case '"': sb.Append("\\\""); break;
					case '\b': sb.Append("\\b"); break;
					case '\f': sb.Append("\\f"); break;
					case '\n': sb.Append("\\n"); break;
					case '\r': sb.Append("\\r"); break;
					case '\t': sb.Append("\\t"); break;
					default: sb.Append(c); break;
				}
			}
			return sb.ToString();
		}
	}
}
