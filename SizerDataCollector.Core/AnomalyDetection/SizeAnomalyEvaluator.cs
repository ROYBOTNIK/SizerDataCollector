using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;
using SizerDataCollector.Core.Logging;

namespace SizerDataCollector.Core.AnomalyDetection
{
	/// <summary>
	/// Periodic evaluator that queries cagg_lane_size_minute for one serial_no and
	/// a configurable time window, computes each lane's weighted-average fruit size,
	/// and compares it against the cross-lane mean for that sizer only.
	/// </summary>
	public sealed class SizeAnomalyEvaluator
	{
		private const string LaneAvgQuery = @"
SELECT lane_idx + 1                                           AS lane_no,
       sum(fruit_cnt)                                         AS total_fruit,
       sum(fruit_cnt::double precision * avg_size)
         / NULLIF(sum(fruit_cnt), 0)::double precision        AS lane_avg_size
FROM   public.cagg_lane_size_minute
WHERE  serial_no = @serial_no
  AND  minute_ts >= @from_ts AND minute_ts < @to_ts
GROUP  BY lane_idx
HAVING sum(fruit_cnt) > 0
ORDER  BY lane_idx;";

		private readonly SizeAnomalyConfig _config;
		private readonly string _connectionString;
		private readonly CooldownTracker _cooldown;

		public SizeAnomalyEvaluator(SizeAnomalyConfig config, string connectionString)
		{
			_config = config ?? throw new ArgumentNullException(nameof(config));
			_connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
			_cooldown = new CooldownTracker(_config.CooldownMinutes * 60);
		}

		/// <summary>
		/// Evaluate lane size health over the configured window ending at <paramref name="now"/>.
		/// Returns anomaly events for lanes that deviate significantly from the cross-lane mean.
		/// </summary>
		public async Task<List<SizeAnomalyEvent>> EvaluateAsync(
			string serialNo, DateTimeOffset now, CancellationToken cancellationToken)
		{
			if (string.IsNullOrWhiteSpace(serialNo))
				return new List<SizeAnomalyEvent>();

			var toTs = now;
			var fromTs = now.AddHours(-_config.WindowHours);

			var lanes = await QueryLaneAveragesAsync(serialNo, fromTs, toTs, cancellationToken).ConfigureAwait(false);
			return Evaluate(lanes, serialNo, now);
		}

		/// <summary>
		/// Evaluate for a specific time range (used by the size-health CLI command).
		/// </summary>
		public async Task<SizeHealthReport> EvaluateRangeAsync(
			string serialNo, DateTimeOffset fromTs, DateTimeOffset toTs, CancellationToken cancellationToken)
		{
			if (string.IsNullOrWhiteSpace(serialNo))
				return new SizeHealthReport { SerialNo = serialNo, FromTs = fromTs, ToTs = toTs, Rows = new List<SizeHealthRow>() };

			var lanes = await QueryLaneAveragesAsync(serialNo, fromTs, toTs, cancellationToken).ConfigureAwait(false);
			return BuildReport(lanes, serialNo, fromTs, toTs);
		}

		private List<SizeAnomalyEvent> Evaluate(List<LaneAvg> lanes, string serialNo, DateTimeOffset now)
		{
			var events = new List<SizeAnomalyEvent>();
			if (lanes.Count < 2) return events;

			double machineAvg, stddev;
			ComputeCrossLaneStats(lanes, out machineAvg, out stddev);

			if (machineAvg <= 0) return events;

			foreach (var lane in lanes)
			{
				double z = stddev > 0 ? (lane.AvgSize - machineAvg) / stddev : 0;
				double pctDev = 100.0 * (lane.AvgSize - machineAvg) / machineAvg;

				if (Math.Abs(z) < _config.ZGate || Math.Abs(pctDev) < _config.PctDevMin)
					continue;

				if (_cooldown.IsOnCooldown(lane.LaneNo, "size", now))
					continue;

				_cooldown.Record(lane.LaneNo, "size", now);

				string severity = ClassifySeverity(Math.Abs(pctDev));
				string direction = pctDev > 0 ? "larger" : "smaller";
				double diff = lane.AvgSize - machineAvg;

				string title = string.Format("Lane {0}: sorting {1:F1}% {2} fruit than average",
					lane.LaneNo, Math.Abs(pctDev), direction);
				string details = string.Format("Lane {0}: {1:F1}mm vs {2:F1}mm avg ({3:+0.0;-0.0}mm, {4:+0.0;-0.0}%, z={5:+0.0;-0.0}, last {6}h)",
					lane.LaneNo, lane.AvgSize, machineAvg, diff, pctDev, z, _config.WindowHours);

				events.Add(new SizeAnomalyEvent
				{
					EventTs = now,
					SerialNo = serialNo,
					LaneNo = lane.LaneNo,
					WindowHours = _config.WindowHours,
					LaneAvgSize = lane.AvgSize,
					MachineAvgSize = machineAvg,
					PctDeviation = pctDev,
					ZScore = z,
					Severity = severity,
					ModelVersion = "size-v1",
					DeliveredTo = string.Empty,
					AlarmTitle = title,
					AlarmDetails = details
				});
			}

			return events;
		}

		internal SizeHealthReport BuildReport(List<LaneAvg> lanes, string serialNo, DateTimeOffset fromTs, DateTimeOffset toTs)
		{
			double machineAvg = 0, stddev = 0;
			if (lanes.Count >= 2)
				ComputeCrossLaneStats(lanes, out machineAvg, out stddev);
			else if (lanes.Count == 1)
				machineAvg = lanes[0].AvgSize;

			var rows = new List<SizeHealthRow>();
			foreach (var lane in lanes)
			{
				double z = stddev > 0 ? (lane.AvgSize - machineAvg) / stddev : 0;
				double pctDev = machineAvg > 0 ? 100.0 * (lane.AvgSize - machineAvg) / machineAvg : 0;

				bool isAlarm = Math.Abs(z) >= _config.ZGate && Math.Abs(pctDev) >= _config.PctDevMin;
				string status = "";
				if (isAlarm)
					status = pctDev > 0 ? "ALARM (oversizing)" : "ALARM (undersizing)";

				rows.Add(new SizeHealthRow
				{
					LaneNo = lane.LaneNo,
					TotalFruit = lane.TotalFruit,
					AvgSize = lane.AvgSize,
					Diff = lane.AvgSize - machineAvg,
					PctDev = pctDev,
					ZScore = z,
					Status = status
				});
			}

			return new SizeHealthReport
			{
				SerialNo = serialNo,
				FromTs = fromTs,
				ToTs = toTs,
				MachineAvg = machineAvg,
				Rows = rows
			};
		}

		private async Task<List<LaneAvg>> QueryLaneAveragesAsync(
			string serialNo, DateTimeOffset fromTs, DateTimeOffset toTs, CancellationToken cancellationToken)
		{
			var lanes = new List<LaneAvg>();

			using (var conn = new NpgsqlConnection(_connectionString))
			{
				await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
				using (var cmd = new NpgsqlCommand(LaneAvgQuery, conn))
				{
					cmd.Parameters.AddWithValue("serial_no", serialNo.Trim());
					cmd.Parameters.AddWithValue("from_ts", fromTs.UtcDateTime);
					cmd.Parameters.AddWithValue("to_ts", toTs.UtcDateTime);

					using (var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
					{
						while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
						{
							if (reader.IsDBNull(2)) continue;
							lanes.Add(new LaneAvg
							{
								LaneNo = reader.GetInt32(0),
								TotalFruit = reader.GetInt64(1),
								AvgSize = reader.GetDouble(2)
							});
						}
					}
				}
			}

			return lanes;
		}

		private static void ComputeCrossLaneStats(List<LaneAvg> lanes, out double mean, out double stddev)
		{
			double sum = 0;
			foreach (var l in lanes) sum += l.AvgSize;
			mean = sum / lanes.Count;

			double sumSq = 0;
			foreach (var l in lanes)
			{
				double d = l.AvgSize - mean;
				sumSq += d * d;
			}
			stddev = Math.Sqrt(sumSq / lanes.Count);
		}

		private static string ClassifySeverity(double absPctDev)
		{
			if (absPctDev < 5.0) return "Low";
			if (absPctDev < 10.0) return "Medium";
			return "High";
		}

		internal sealed class LaneAvg
		{
			public int LaneNo;
			public long TotalFruit;
			public double AvgSize;
		}
	}

	public sealed class SizeHealthRow
	{
		public int LaneNo { get; set; }
		public long TotalFruit { get; set; }
		public double AvgSize { get; set; }
		public double Diff { get; set; }
		public double PctDev { get; set; }
		public double ZScore { get; set; }
		public string Status { get; set; }
	}

	public sealed class SizeHealthReport
	{
		public string SerialNo { get; set; }
		public DateTimeOffset FromTs { get; set; }
		public DateTimeOffset ToTs { get; set; }
		public double MachineAvg { get; set; }
		public List<SizeHealthRow> Rows { get; set; }
	}
}
