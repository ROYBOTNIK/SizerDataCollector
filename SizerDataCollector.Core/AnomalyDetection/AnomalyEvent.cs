using System;

namespace SizerDataCollector.Core.AnomalyDetection
{
	/// <summary>
	/// Matches the oee.grade_lane_anomalies table schema exactly.
	/// </summary>
	public sealed class AnomalyEvent
	{
		public DateTimeOffset EventTs { get; set; }
		public string SerialNo { get; set; }
		public int BatchRecordId { get; set; }
		public int LaneNo { get; set; }
		public string GradeKey { get; set; }
		public double Qty { get; set; }
		public double Pct { get; set; }
		public double AnomalyScore { get; set; }
		public string Severity { get; set; }
		public string ExplanationJson { get; set; }
		public string ModelVersion { get; set; }
		public string DeliveredTo { get; set; }

		public string AlarmTitle { get; set; }
		public string AlarmDetails { get; set; }
	}
}
