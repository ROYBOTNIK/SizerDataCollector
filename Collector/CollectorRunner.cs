using System;
using System.Threading;
using System.Threading.Tasks;
using SizerDataCollector.Core.Config;
using SizerDataCollector.Core.Logging;

namespace SizerDataCollector.Core.Collector
{
	public sealed class CollectorRunner
	{
		private readonly CollectorConfig _config;
		private readonly CollectorEngine _engine;
		private readonly CollectorStatus _status;
		private readonly HeartbeatWriter _heartbeatWriter;

		public CollectorRunner(
			CollectorConfig config,
			CollectorEngine engine,
			CollectorStatus status,
			HeartbeatWriter heartbeatWriter)
		{
			_config = config ?? throw new ArgumentNullException(nameof(config));
			_engine = engine ?? throw new ArgumentNullException(nameof(engine));
			_status = status ?? throw new ArgumentNullException(nameof(status));
			_heartbeatWriter = heartbeatWriter ?? throw new ArgumentNullException(nameof(heartbeatWriter));
		}

		public async Task RunAsync(CancellationToken cancellationToken)
		{
			var pollInterval = TimeSpan.FromSeconds(_config.PollIntervalSeconds);
			var initialBackoff = TimeSpan.FromSeconds(_config.InitialBackoffSeconds);
			var maxBackoff = TimeSpan.FromSeconds(_config.MaxBackoffSeconds);
			var currentBackoff = initialBackoff;

			while (!cancellationToken.IsCancellationRequested)
			{
				var cycleStart = DateTimeOffset.UtcNow;
				Logger.Log($"Starting ingestion cycle at {cycleStart:O}...");
				RecordPollStart(cycleStart.UtcDateTime);

				try
				{
					await _engine.RunSinglePollAsync(_status, cancellationToken).ConfigureAwait(false);

					var elapsed = DateTimeOffset.UtcNow - cycleStart;
					Logger.Log($"Ingestion cycle succeeded in {elapsed.TotalMilliseconds:F0} ms.");
					RecordPollSuccess(DateTime.UtcNow);
					WriteHeartbeat();

					currentBackoff = initialBackoff;
					await DelayAsync(pollInterval, cancellationToken).ConfigureAwait(false);
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					break;
				}
				catch (OperationCanceledException ex)
				{
					Logger.Log($"Ingestion cycle failed; will retry after {currentBackoff.TotalSeconds:F0}s.", ex);
					RecordPollFailure(DateTime.UtcNow, ex);
					WriteHeartbeat();
					currentBackoff = await ApplyBackoffAsync(currentBackoff, initialBackoff, maxBackoff, cancellationToken).ConfigureAwait(false);
				}
				catch (Exception ex)
				{
					Logger.Log($"Ingestion cycle failed; will retry after {currentBackoff.TotalSeconds:F0}s.", ex);
					RecordPollFailure(DateTime.UtcNow, ex);
					WriteHeartbeat();
					currentBackoff = await ApplyBackoffAsync(currentBackoff, initialBackoff, maxBackoff, cancellationToken).ConfigureAwait(false);
				}
			}
		}

		private void RecordPollStart(DateTime startUtc)
		{
			lock (_status.SyncRoot)
			{
				_status.LastPollStartUtc = startUtc;
			}
		}

		private void RecordPollSuccess(DateTime endUtc)
		{
			lock (_status.SyncRoot)
			{
				_status.LastPollEndUtc = endUtc;
				_status.TotalPollsStarted++;
				_status.TotalPollsSucceeded++;
				_status.LastPollError = null;
			}
		}

		private void RecordPollFailure(DateTime endUtc, Exception ex)
		{
			lock (_status.SyncRoot)
			{
				_status.LastPollEndUtc = endUtc;
				_status.TotalPollsStarted++;
				_status.TotalPollsFailed++;
				_status.LastPollError = ex?.Message ?? "Unknown error";
			}
		}

		private static Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
		{
			if (delay <= TimeSpan.Zero)
			{
				return Task.CompletedTask;
			}

			return Task.Delay(delay, cancellationToken);
		}

		private static async Task<TimeSpan> ApplyBackoffAsync(
			TimeSpan currentBackoff,
			TimeSpan initialBackoff,
			TimeSpan maxBackoff,
			CancellationToken cancellationToken)
		{
			await DelayAsync(currentBackoff, cancellationToken).ConfigureAwait(false);

			var doubledSeconds = Math.Min(currentBackoff.TotalSeconds * 2, maxBackoff.TotalSeconds);
			return TimeSpan.FromSeconds(Math.Max(doubledSeconds, initialBackoff.TotalSeconds));
		}

		private void WriteHeartbeat()
		{
			var snapshot = _status.CreateSnapshot();
			var payload = new HeartbeatPayload
			{
				MachineSerial = snapshot.MachineSerial,
				MachineName = snapshot.MachineName,
				LastPollUtc = snapshot.LastPollCompletedUtc ?? snapshot.LastPollStartedUtc,
				LastSuccessUtc = snapshot.LastSuccessUtc,
				LastErrorUtc = snapshot.LastErrorUtc,
				LastErrorMessage = snapshot.LastErrorMessage,
				LastRunId = snapshot.LastRunId,
				CommissioningIngestionEnabled = snapshot.CommissioningIngestionEnabled,
				CommissioningSerial = snapshot.CommissioningSerial,
				CommissioningBlockingReasons = snapshot.CommissioningBlockingReasons
			};

			_heartbeatWriter.Write(payload);
		}
	}
}


