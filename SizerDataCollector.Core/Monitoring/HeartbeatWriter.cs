using System;
using System.IO;
using Newtonsoft.Json;

namespace SizerDataCollector.Core.Monitoring
{
	internal static class HeartbeatWriter
	{
		private static readonly object SyncRoot = new object();
		private static readonly string HeartbeatPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "heartbeat.json");

		public static void Write(DateTime timestampUtc, string error)
		{
			var payload = new HeartbeatPayload
			{
				LastPollUtc = timestampUtc,
				LastError = error
			};

			var json = JsonConvert.SerializeObject(payload, Formatting.Indented);

			try
			{
				lock (SyncRoot)
				{
					File.WriteAllText(HeartbeatPath, json);
				}
			}
			catch
			{
				// Avoid throwing from heartbeat logging.
			}
		}

		private sealed class HeartbeatPayload
		{
			[JsonProperty("last_poll_utc")]
			public DateTime LastPollUtc { get; set; }

			[JsonProperty("last_error")]
			public string LastError { get; set; }
		}
	}
}

