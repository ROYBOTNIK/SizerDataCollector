using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;
using SizerDataCollector.Core.Config;
using SizerDataCollector.Core.Sizer;
using SizerDataCollector.Core.Sizer.Discovery;

namespace SizerDataCollector.Tests
{
	[TestClass]
	public class DiscoverySummaryTests
	{
		private const string OutletsJson = @"[
  { ""Id"": 1, ""Name"": ""Outlet A"", ""Status"": ""Enabled"", ""CurrentProductId"": ""p1"" },
  { ""Id"": 2, ""Name"": ""Outlet B"", ""Status"": ""Disabled"", ""CurrentProductId"": ""p2"" }
]";

		private const string LanesGradeJson = @"[
  { ""A"": 10, ""B"": 5 },
  null,
  { ""B"": 3, ""C"": 7 }
]";

		private const string LanesSizeJson = @"[
  { ""Small"": 4, ""Large"": 2 },
  { ""Medium"": 6 },
  null
]";

		[TestMethod]
		public void Summary_Parses_Outlets()
		{
			var payloads = new Dictionary<string, JToken>
			{
				["outlets_details"] = JToken.Parse(OutletsJson)
			};

			var summary = MachineDiscoverySummary.FromPayloads(payloads);

			Assert.IsNotNull(summary);
			Assert.AreEqual(2, summary.OutletCount);
			CollectionAssert.AreEqual(new[] { "Outlet A", "Outlet B" }, (System.Collections.ICollection)summary.OutletNames);
		}

		[TestMethod]
		public void Summary_Parses_LaneGrades()
		{
			var payloads = new Dictionary<string, JToken>
			{
				["lanes_grade_fpm"] = JToken.Parse(LanesGradeJson)
			};

			var summary = MachineDiscoverySummary.FromPayloads(payloads);

			Assert.IsNotNull(summary);
			Assert.AreEqual(2, summary.LaneViewCount); // non-null entries
			Assert.AreEqual(3, summary.GradeLaneArrayLength);
			Assert.AreEqual(2, summary.LastNonNullGradeLaneIndex);
			Assert.AreEqual(3, summary.DerivedLaneCountCandidate);
			Assert.AreEqual("medium", summary.LaneCountConfidence);
			CollectionAssert.AreEquivalent(new[] { "A", "B", "C" }, new List<string>(summary.DistinctGradeKeys));
		}

		[TestMethod]
		public void Summary_Parses_LaneSizes()
		{
			var payloads = new Dictionary<string, JToken>
			{
				["lanes_size_fpm"] = JToken.Parse(LanesSizeJson)
			};

			var summary = MachineDiscoverySummary.FromPayloads(payloads);

			Assert.IsNotNull(summary);
			Assert.AreEqual(2, summary.LaneViewCount); // non-null entries
			Assert.AreEqual(3, summary.SizeLaneArrayLength);
			Assert.AreEqual(1, summary.LastNonNullSizeLaneIndex);
			Assert.AreEqual(3, summary.DerivedLaneCountCandidate);
			CollectionAssert.AreEquivalent(new[] { "Small", "Large", "Medium" }, new List<string>(summary.DistinctSizeKeys));
		}

		[TestMethod]
		public async Task DiscoveryRunner_Uses_Fake_Client()
		{
			var fake = new FakeSizerClient();
			var runner = new DiscoveryRunner(_ => fake);
			var snapshot = await runner.RunAsync(new CollectorConfig(new CollectorRuntimeSettings()), CancellationToken.None);

			Assert.IsNotNull(snapshot);
			Assert.AreEqual("SN-123", snapshot.SerialNo);
			Assert.AreEqual("Machine-X", snapshot.MachineName);
			Assert.IsNotNull(snapshot.Summary);
			Assert.AreEqual(2, snapshot.Summary.OutletCount);
			Assert.AreEqual(2, snapshot.Summary.LaneViewCount);
			Assert.AreEqual(3, snapshot.Summary.DistinctGradeKeys.Count);
			Assert.AreEqual(3, snapshot.Summary.DistinctSizeKeys.Count);
		}

		private sealed class FakeSizerClient : ISizerClient
		{
			public void Dispose() { }

			public Task<string> GetSerialNoAsync(CancellationToken cancellationToken)
				=> Task.FromResult("SN-123");

			public Task<string> GetMachineNameAsync(CancellationToken cancellationToken)
				=> Task.FromResult("Machine-X");

			public Task<CurrentBatchInfo> GetCurrentBatchAsync(CancellationToken cancellationToken)
				=> Task.FromResult<CurrentBatchInfo>(null);

			public Task<string> GetMetricJsonAsync(string logicalMetricName, CancellationToken cancellationToken)
			{
				switch (logicalMetricName)
				{
					case "lanes_grade_fpm":
						return Task.FromResult(LanesGradeJson);
					case "lanes_size_fpm":
						return Task.FromResult(LanesSizeJson);
					case "outlets_details":
						return Task.FromResult(OutletsJson);
					case "machine_total_fpm":
					case "machine_cupfill":
						return Task.FromResult("{}");
					default:
						return Task.FromResult("{}");
				}
			}
		}
	}
}

