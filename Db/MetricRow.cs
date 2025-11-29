using System;

namespace SizerDataCollector.Db
{
	public sealed class MetricRow
	{
		public DateTimeOffset Timestamp { get; set; }

		public string SerialNo { get; set; }

		public long BatchRecordId { get; set; }

		public string MetricName { get; set; }

		public string JsonPayload { get; set; }
	}
}

