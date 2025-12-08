using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SizerDataCollector.Core.Config;
using SizerDataCollector.Core.Db;
using SizerDataCollector.Core.Logging;
using SizerDataCollector.Core.Sizer;

namespace SizerDataCollector.Core.Collector
{
	public sealed class CollectorEngine
	{
		private readonly CollectorConfig _config;
		private readonly ITimescaleRepository _repository;
		private readonly ISizerClient _sizerClient;

		public CollectorEngine(
			CollectorConfig config,
			ITimescaleRepository repository,
			ISizerClient sizerClient)
		{
			_config = config ?? throw new ArgumentNullException(nameof(config));
			_repository = repository ?? throw new ArgumentNullException(nameof(repository));
			_sizerClient = sizerClient ?? throw new ArgumentNullException(nameof(sizerClient));
		}

		public async Task RunSinglePollAsync(CollectorStatus status, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();

			var runId = Guid.NewGuid().ToString("N");

			string serialNo;
			string machineName;

			lock (status.SyncRoot)
			{
				status.LastRunId = runId;
				status.LastPollStartedUtc = DateTime.UtcNow;
			}

			try
			{
				serialNo = await _sizerClient.GetSerialNoAsync(cancellationToken).ConfigureAwait(false);
				machineName = await _sizerClient.GetMachineNameAsync(cancellationToken).ConfigureAwait(false);
				Logger.Log($"RunId={runId} - Sizer identification: serial='{serialNo}', name='{machineName}'.");

				lock (status.SyncRoot)
				{
					status.MachineSerial = serialNo;
					status.MachineName = machineName;
				}

				await _repository.UpsertMachineAsync(serialNo, machineName, cancellationToken).ConfigureAwait(false);
			}
			catch (Exception ex)
			{
				Logger.Log($"RunId={runId} - ERROR: Failed to resolve or upsert machine metadata.", ex);
				lock (status.SyncRoot)
				{
					status.LastErrorUtc = DateTime.UtcNow;
					status.LastErrorMessage = ex.Message;
					status.LastPollCompletedUtc = DateTime.UtcNow;
				}
				throw;
			}

			CurrentBatchInfo batchInfo;
			try
			{
				batchInfo = await _sizerClient.GetCurrentBatchAsync(cancellationToken).ConfigureAwait(false);
			}
			catch (Exception ex)
			{
				Logger.Log($"RunId={runId} - ERROR: Failed to fetch current batch from Sizer.", ex);
				lock (status.SyncRoot)
				{
					status.LastErrorUtc = DateTime.UtcNow;
					status.LastErrorMessage = ex.Message;
					status.LastPollCompletedUtc = DateTime.UtcNow;
				}
				throw;
			}

			if (batchInfo == null)
			{
				Logger.Log($"RunId={runId} - WARN: No current batch for serial '{serialNo}' – skipping metrics insert.");
				lock (status.SyncRoot)
				{
					status.LastPollCompletedUtc = DateTime.UtcNow;
				}
				return;
			}

			long batchRecordId;
			try
			{
				batchRecordId = await _repository.GetOrCreateBatchAsync(
					serialNo,
					batchInfo.BatchId,
					batchInfo.GrowerCode,
					batchInfo.StartTime,
					batchInfo.Comments,
					cancellationToken).ConfigureAwait(false);
			}
			catch (Exception ex)
			{
				Logger.Log($"RunId={runId} - ERROR: Failed to persist batch metadata for serial '{serialNo}', batch_id '{batchInfo?.BatchId}'.", ex);
				lock (status.SyncRoot)
				{
					status.LastErrorUtc = DateTime.UtcNow;
					status.LastErrorMessage = ex.Message;
					status.LastPollCompletedUtc = DateTime.UtcNow;
				}
				throw;
			}

			var enabledMetrics = _config.EnabledMetrics;
			if (enabledMetrics == null || enabledMetrics.Count == 0)
			{
				Logger.Log($"RunId={runId} - WARN: EnabledMetrics list is empty. No metrics will be collected this cycle.");
				lock (status.SyncRoot)
				{
					status.LastPollCompletedUtc = DateTime.UtcNow;
				}
				return;
			}

			var metricRows = new List<MetricRow>();
			foreach (var metricName in enabledMetrics)
			{
				cancellationToken.ThrowIfCancellationRequested();

				var logicalName = metricName?.Trim();
				if (string.IsNullOrEmpty(logicalName))
				{
					continue;
				}

				try
				{
					var payload = await _sizerClient.GetMetricJsonAsync(logicalName, cancellationToken).ConfigureAwait(false);
					metricRows.Add(new MetricRow
					{
						Timestamp = DateTimeOffset.UtcNow,
						SerialNo = serialNo,
						BatchRecordId = batchRecordId,
						MetricName = logicalName,
						JsonPayload = payload
					});
				}
				catch (Exception ex)
				{
					Logger.Log($"RunId={runId} - ERROR: Failed to capture metric '{logicalName}' for serial '{serialNo}'.", ex);
					lock (status.SyncRoot)
					{
						status.LastErrorUtc = DateTime.UtcNow;
						status.LastErrorMessage = ex.Message;
						status.LastPollCompletedUtc = DateTime.UtcNow;
					}
					throw;
				}
			}

			if (metricRows.Count == 0)
			{
				Logger.Log($"RunId={runId} - WARN: No metric rows generated for serial '{serialNo}'; skipping insert.");
				return;
			}

			try
			{
				await _repository.InsertMetricsAsync(metricRows, cancellationToken).ConfigureAwait(false);
				Logger.Log($"RunId={runId} - Inserted {metricRows.Count} metric rows for serial '{serialNo}', batch_record_id={batchRecordId}.");
			}
			catch (Exception ex)
			{
				Logger.Log($"RunId={runId} - ERROR: Failed to insert metrics into TimescaleDB for serial '{serialNo}', batch_record_id={batchRecordId}.", ex);
				lock (status.SyncRoot)
				{
					status.LastErrorUtc = DateTime.UtcNow;
					status.LastErrorMessage = ex.Message;
					status.LastPollCompletedUtc = DateTime.UtcNow;
				}
				throw;
			}

			lock (status.SyncRoot)
			{
				status.LastSuccessUtc = DateTime.UtcNow;
				status.LastPollCompletedUtc = DateTime.UtcNow;
				status.LastErrorMessage = null;
			}
		}
	}
}

