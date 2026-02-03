using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace SizerDataCollector.Core.Sizer.Discovery
{
	public sealed class MachineDiscoverySnapshot
	{
		public string SerialNo { get; set; }
		public string MachineName { get; set; }

		public string SourceHost { get; set; }
		public int? SourcePort { get; set; }
		public string ClientKind { get; set; }

		public DateTimeOffset StartedAtUtc { get; set; }
		public DateTimeOffset FinishedAtUtc { get; set; }
		public int? DurationMs { get; set; }

		public bool Success { get; set; }
		public string ErrorText { get; set; }

		/// <summary>
		/// Raw payloads keyed by logical metric name (e.g., "serial_no", "outlets_details").
		/// Stored exactly as returned from the wrapper; no transformation.
		/// </summary>
		public IDictionary<string, JToken> Payloads { get; set; } =
			new Dictionary<string, JToken>(StringComparer.OrdinalIgnoreCase);

		/// <summary>
		/// Optional, non-semantic summary. See <see cref="MachineDiscoverySummary"/> for details.
		/// </summary>
		public MachineDiscoverySummary Summary { get; set; }
	}
}

