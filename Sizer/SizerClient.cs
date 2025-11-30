using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.ServiceModel;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using SizerDataCollector.Core.Config;
using SizerDataCollector.Core.Logging;
using SizerDataCollector.SizerServiceReference;

namespace SizerDataCollector.Core.Sizer
{
	public sealed class CurrentBatchInfo
	{
		public int BatchId { get; set; }

		public string GrowerCode { get; set; }

		public DateTimeOffset StartTime { get; set; }

		public string Comments { get; set; }
	}

	public interface ISizerClient : IDisposable
	{
		Task<string> GetSerialNoAsync(CancellationToken cancellationToken);

		Task<string> GetMachineNameAsync(CancellationToken cancellationToken);

		Task<CurrentBatchInfo> GetCurrentBatchAsync(CancellationToken cancellationToken);

		Task<string> GetMetricJsonAsync(string logicalMetricName, CancellationToken cancellationToken);
	}

	public sealed class SizerClient : ISizerClient
	{
		private static readonly IReadOnlyDictionary<string, Func<SizerServiceClient, object>> MetricResolvers =
			new Dictionary<string, Func<SizerServiceClient, object>>(StringComparer.OrdinalIgnoreCase)
			{
				["lanes_grade_fpm"] = client => client.GetLanesGradeFPM(),
				["lanes_size_fpm"] = client => client.GetLanesSizeFPM(),
				["machine_total_fpm"] = client => client.GetMachineTotalFPM(),
				["machine_cupfill"] = client => client.GetMachineCupfill(),
				["outlets_details"] = client => client.GetOutlets()
			};

		private readonly string _serviceUrl;
		private readonly TimeSpan _openTimeout;
		private readonly TimeSpan _sendTimeout;
		private readonly TimeSpan _receiveTimeout;
		private readonly object _clientSync = new object();
		private SizerServiceClient _client;
		private bool _disposed;

		public SizerClient(CollectorConfig config)
		{
			if (config == null) throw new ArgumentNullException(nameof(config));

			_serviceUrl = $"http://{config.SizerHost}:{config.SizerPort}/SizerService/";
			_openTimeout = TimeSpan.FromSeconds(config.OpenTimeoutSec);
			_sendTimeout = TimeSpan.FromSeconds(config.SendTimeoutSec);
			_receiveTimeout = TimeSpan.FromSeconds(config.ReceiveTimeoutSec);
			Logger.Log($"SizerClient configured for endpoint {_serviceUrl}");
		}

		public Task<string> GetSerialNoAsync(CancellationToken cancellationToken)
		{
			Logger.Log("Requesting machine serial number...");
			return InvokeAsync(
				client => client.GetSerialNo(),
				"GetSerialNo",
				cancellationToken,
				result =>
				{
					Logger.Log($"Received serial number '{result}'.");
					return result;
				});
		}

		public Task<string> GetMachineNameAsync(CancellationToken cancellationToken)
		{
			Logger.Log("Requesting machine name...");
			return InvokeAsync(
				client => client.GetMachineName(),
				"GetMachineName",
				cancellationToken,
				name =>
				{
					Logger.Log($"Received machine name '{name}'.");
					return name;
				});
		}

		public async Task<CurrentBatchInfo> GetCurrentBatchAsync(CancellationToken cancellationToken)
		{
			var batches = await InvokeAsync(
				client => client.GetCurrentBatches(),
				"GetCurrentBatches",
				cancellationToken).ConfigureAwait(false);

			var batch = batches?.FirstOrDefault();
			if (batch == null)
			{
				Logger.Log("Sizer returned no active batches.");
				return null;
			}

			var info = MapBatch(batch);
			Logger.Log($"Active batch resolved. GrowerCode='{info.GrowerCode}', BatchId={info.BatchId}.");
			return info;
		}

		public async Task<string> GetMetricJsonAsync(string logicalMetricName, CancellationToken cancellationToken)
		{
			if (string.IsNullOrWhiteSpace(logicalMetricName))
			{
				throw new ArgumentException("Metric name must be provided.", nameof(logicalMetricName));
			}

			if (!MetricResolvers.TryGetValue(logicalMetricName, out var resolver))
			{
				throw new NotSupportedException($"Metric '{logicalMetricName}' is not supported.");
			}

			var payload = await InvokeAsync(
				client => resolver(client),
				$"GetMetric:{logicalMetricName}",
				cancellationToken).ConfigureAwait(false);

			var json = JsonConvert.SerializeObject(payload, Formatting.None);
			Logger.Log($"Fetched metric '{logicalMetricName}'. Payload size: {json.Length} characters.");
			return json;
		}

		public void Dispose()
		{
			if (_disposed)
			{
				return;
			}

			lock (_clientSync)
			{
				if (_disposed)
				{
					return;
				}

				DisposeClient();
				_disposed = true;
			}
		}

		private async Task<TResult> InvokeAsync<TResult>(
			Func<SizerServiceClient, TResult> operation,
			string operationName,
			CancellationToken cancellationToken,
			Func<TResult, TResult> onSuccess = null)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var client = EnsureClient();

			try
			{
				var result = await Task.Run(() =>
				{
					cancellationToken.ThrowIfCancellationRequested();
					return operation(client);
				}, cancellationToken).ConfigureAwait(false);

				return onSuccess != null ? onSuccess(result) : result;
			}
			catch (CommunicationException ex)
			{
				HandleCommunicationFailure(operationName, ex);
				throw;
			}
			catch (TimeoutException ex)
			{
				HandleCommunicationFailure(operationName, ex);
				throw;
			}
		}

		private SizerServiceClient EnsureClient()
		{
			if (_disposed)
			{
				throw new ObjectDisposedException(nameof(SizerClient));
			}

			lock (_clientSync)
			{
				if (_client != null && _client.State == CommunicationState.Opened)
				{
					return _client;
				}

				if (_client != null)
				{
					SafeAbort(_client);
					_client = null;
				}

				var binding = CreateBinding();
				var endpointAddress = new EndpointAddress(_serviceUrl);

				var client = new SizerServiceClient(binding, endpointAddress);
				try
				{
					client.Open();
				}
				catch
				{
					SafeAbort(client);
					throw;
				}

				Logger.Log("Opened Sizer WCF client.");

				_client = client;
				return _client;
			}
		}

		private WSHttpBinding CreateBinding()
		{
			return new WSHttpBinding(SecurityMode.None)
			{
				OpenTimeout = _openTimeout,
				SendTimeout = _sendTimeout,
				ReceiveTimeout = _receiveTimeout,
				MaxReceivedMessageSize = 10 * 1024 * 1024L
			};
		}

		private void HandleCommunicationFailure(string operationName, Exception ex)
		{
			Logger.Log($"Sizer WCF call '{operationName}' failed. Resetting client.", ex);
			lock (_clientSync)
			{
				if (_client != null)
				{
					SafeAbort(_client);
					_client = null;
				}
			}
		}

		private void DisposeClient()
		{
			if (_client == null)
			{
				return;
			}

			try
			{
				if (_client.State == CommunicationState.Faulted)
				{
					_client.Abort();
				}
				else
				{
					_client.Close();
				}
			}
			catch
			{
				_client.Abort();
			}
			finally
			{
				_client = null;
			}
		}

		private static CurrentBatchInfo MapBatch(Batch batch)
		{
			var batchId = TryParseBatchId(batch.GrowerCode);
			if (batchId == 0)
			{
				batchId = batch.Id;
			}

			return new CurrentBatchInfo
			{
				BatchId = batchId,
				GrowerCode = batch.GrowerCode,
				StartTime = ToDateTimeOffset(batch.StartTime),
				Comments = CollapseComments(batch.Comments)
			};
		}

		private static int TryParseBatchId(string value)
		{
			if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
			{
				return parsed;
			}

			return 0;
		}

		private static DateTimeOffset ToDateTimeOffset(DateTime value)
		{
			if (value.Kind == DateTimeKind.Unspecified)
			{
				value = DateTime.SpecifyKind(value, DateTimeKind.Utc);
			}

			return value.Kind == DateTimeKind.Utc
				? new DateTimeOffset(value)
				: new DateTimeOffset(value.ToUniversalTime(), TimeSpan.Zero);
		}

		private static string CollapseComments(string[] comments)
		{
			if (comments == null || comments.Length == 0)
			{
				return string.Empty;
			}

			return string.Join(" | ", comments.Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim()));
		}

		private static void SafeAbort(ICommunicationObject client)
		{
			try
			{
				client.Abort();
			}
			catch
			{
				// Ignore abort exceptions.
			}
		}
	}
}

