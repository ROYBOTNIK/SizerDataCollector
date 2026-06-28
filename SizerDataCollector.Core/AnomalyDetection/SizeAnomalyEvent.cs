using System;

namespace SizerDataCollector.Core.AnomalyDetection
{
	/// <summary>
	/// Matches the oee.lane_size_anomalies table schema.
	/// </summary>
	public sealed class SizeAnomalyEvent
	{
		public DateTimeOffset EventTs { get; set; }
		public string SerialNo { get; set; }
		public int LaneNo { get; set; }
		public int WindowHours { get; set; }
		public double LaneAvgSize { get; set; }
		public double MachineAvgSize { get; set; }
		public double PctDeviation { get; set; }
		public double ZScore { get; set; }
		public string Severity { get; set; }
		public string ModelVersion { get; set; }
		public string DeliveredTo { get; set; }

		public string AlarmTitle { get; set; }
		public string AlarmDetails { get; set; }
	}
}
