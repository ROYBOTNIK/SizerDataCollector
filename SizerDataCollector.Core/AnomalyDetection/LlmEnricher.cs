using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using SizerDataCollector.Core.Logging;

namespace SizerDataCollector.Core.AnomalyDetection
{
	/// <summary>
	/// Decorator that enriches the AnomalyEvent.ExplanationJson by calling an LLM
	/// endpoint before forwarding to the inner sink. Falls back to the plain narrative
	/// if the LLM is unavailable or slow.
	/// </summary>
	public sealed class LlmEnricher : IAlarmSink
	{
		private static readonly HttpClient SharedClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

		private readonly IAlarmSink _inner;
		private readonly string _endpoint;

		public LlmEnricher(IAlarmSink inner, string endpoint)
		{
			_inner = inner ?? throw new ArgumentNullException(nameof(inner));
			_endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
		}

		public async Task DeliverAsync(AnomalyEvent evt, CancellationToken cancellationToken)
		{
			if (!string.IsNullOrWhiteSpace(_endpoint))
			{
				try
				{
					var enriched = await EnrichAsync(evt, cancellationToken).ConfigureAwait(false);
					if (!string.IsNullOrWhiteSpace(enriched))
					{
						evt.ExplanationJson = JsonConvert.SerializeObject(new
						{
							llm_summary = enriched,
							original_title = evt.AlarmTitle,
							original_details = evt.AlarmDetails
						});
					}
				}
				catch (Exception ex) when (!(ex is OperationCanceledException))
				{
					Logger.Log("LLM enrichment failed; proceeding with plain narrative.", ex, LogLevel.Debug);
				}
			}

			await _inner.DeliverAsync(evt, cancellationToken).ConfigureAwait(false);
		}

		private async Task<string> EnrichAsync(AnomalyEvent evt, CancellationToken cancellationToken)
		{
			var prompt = $"Summarize this sizer anomaly alert in one sentence for a fruit packing line operator: " +
			             $"{evt.AlarmTitle}. {evt.AlarmDetails}. Severity: {evt.Severity}. " +
			             $"Lane {evt.LaneNo}, grade {evt.GradeKey}, deviation {evt.Pct:F1}%.";

			var payload = JsonConvert.SerializeObject(new { prompt });
			var content = new StringContent(payload, Encoding.UTF8, "application/json");

			var response = await SharedClient.PostAsync(_endpoint, content, cancellationToken).ConfigureAwait(false);
			response.EnsureSuccessStatusCode();

			var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
			var result = JsonConvert.DeserializeAnonymousType(body, new { text = "" });
			return result?.text;
		}
	}
}
