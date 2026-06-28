using System;
using System.ServiceModel;
using System.Threading;
using System.Threading.Tasks;
using SizerDataCollector.Core.Logging;
using SizerDataCollector.SizerServiceReference;

namespace SizerDataCollector.Core.AnomalyDetection
{
	/// <summary>
	/// Delivers anomaly alerts to the Sizer alarm screen via the WCF RaiseAlarmWithPriority endpoint.
	/// Creates its own short-lived WCF connection for alarm delivery to avoid interfering
	/// with the collector's data-fetching client.
	/// </summary>
	public sealed class SizerAlarmSink : IAlarmSink
	{
		private readonly string _serviceUrl;
		private readonly TimeSpan _timeout;

		public SizerAlarmSink(string sizerHost, int sizerPort, int timeoutSec)
		{
			_serviceUrl = $"http://{sizerHost}:{sizerPort}/SizerService/";
			_timeout = TimeSpan.FromSeconds(timeoutSec > 0 ? timeoutSec : 5);
		}

		public async Task DeliverAsync(AnomalyEvent evt, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();

			var priority = PriorityClassifier.ToAlarmPriority(evt.Severity);

			try
			{
				await SendAlarmAsync(evt.AlarmTitle, evt.AlarmDetails, priority, cancellationToken).ConfigureAwait(false);
				evt.DeliveredTo = "sizer";
				Logger.Log($"Alarm delivered to Sizer: [{evt.Severity}] {evt.AlarmTitle}");
			}
			catch (Exception ex) when (!(ex is OperationCanceledException))
			{
				Logger.Log($"Failed to deliver alarm to Sizer: {evt.AlarmTitle}", ex, LogLevel.Warn);
			}
		}

		/// <summary>
		/// Deliver an alarm by title, details, and severity string (used by size anomaly evaluator).
		/// </summary>
		public async Task DeliverAsync(string title, string details, string severity, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();

			var priority = PriorityClassifier.ToAlarmPriority(severity);

			try
			{
				await SendAlarmAsync(title, details, priority, cancellationToken).ConfigureAwait(false);
				Logger.Log($"Size alarm delivered to Sizer: {title}");
			}
			catch (Exception ex) when (!(ex is OperationCanceledException))
			{
				Logger.Log($"Failed to deliver size alarm to Sizer: {title}", ex, LogLevel.Warn);
			}
		}

		/// <summary>
		/// Sends a test alarm and lets exceptions propagate to the caller for connection verification.
		/// </summary>
		public async Task SendTestAlarmAsync(string title, string details, string severity, CancellationToken cancellationToken)
		{
			var priority = PriorityClassifier.ToAlarmPriority(severity);
			await SendAlarmAsync(title, details, priority, cancellationToken).ConfigureAwait(false);
		}

		private async Task SendAlarmAsync(string title, string details, SizerServiceReference.AlarmPriority priority, CancellationToken cancellationToken)
		{
			var binding = new WSHttpBinding(SecurityMode.None)
			{
				OpenTimeout = _timeout,
				SendTimeout = _timeout,
				ReceiveTimeout = _timeout
			};

			var endpoint = new EndpointAddress(_serviceUrl);
			var client = new SizerServiceClient(binding, endpoint);

			try
			{
				client.Open();
				await Task.Run(() =>
				{
					cancellationToken.ThrowIfCancellationRequested();
					client.RaiseAlarmWithPriority(title, details, priority);
				}, cancellationToken).ConfigureAwait(false);

				try { client.Close(); } catch { client.Abort(); }
			}
			catch
			{
				try { client.Abort(); } catch { }
				throw;
			}
		}
	}
}
