using System;

namespace SizerDataCollector.Collector
{
	public sealed class CollectorStatus
	{
		private readonly object _syncRoot = new object();

		public DateTime? LastPollStartUtc { get; set; }
		public DateTime? LastPollEndUtc { get; set; }
		public string LastPollError { get; set; }
		public long TotalPollsStarted { get; set; }
		public long TotalPollsSucceeded { get; set; }
		public long TotalPollsFailed { get; set; }

		internal object SyncRoot => _syncRoot;

		public CollectorStatusSnapshot CreateSnapshot()
		{
			lock (_syncRoot)
			{
				return new CollectorStatusSnapshot
				{
					LastPollStartUtc = LastPollStartUtc,
					LastPollEndUtc = LastPollEndUtc,
					LastPollError = LastPollError,
					TotalPollsStarted = TotalPollsStarted,
					TotalPollsSucceeded = TotalPollsSucceeded,
					TotalPollsFailed = TotalPollsFailed
				};
			}
		}
	}

	public sealed class CollectorStatusSnapshot
	{
		public DateTime? LastPollStartUtc { get; set; }
		public DateTime? LastPollEndUtc { get; set; }
		public string LastPollError { get; set; }
		public long TotalPollsStarted { get; set; }
		public long TotalPollsSucceeded { get; set; }
		public long TotalPollsFailed { get; set; }
	}
}

