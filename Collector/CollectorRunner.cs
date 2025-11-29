using System;
using System.Threading;
using System.Threading.Tasks;
using SizerDataCollector.Config;
using Logger = SizerDataCollector.Logger;

namespace SizerDataCollector.Collector
{
	public sealed class CollectorRunner
	{
		private readonly CollectorConfig _config;
		private readonly CollectorEngine _engine;

		public CollectorRunner(
			CollectorConfig config,
			CollectorEngine engine)
		{
			_config = config ?? throw new ArgumentNullException(nameof(config));
			_engine = engine ?? throw new ArgumentNullException(nameof(engine));
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

				try
				{
					await _engine.RunSinglePollAsync(cancellationToken).ConfigureAwait(false);

					var elapsed = DateTimeOffset.UtcNow - cycleStart;
					Logger.Log($"Ingestion cycle succeeded in {elapsed.TotalMilliseconds:F0} ms.");

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
					await ApplyBackoffAsync(ref currentBackoff, initialBackoff, maxBackoff, cancellationToken).ConfigureAwait(false);
				}
				catch (Exception ex)
				{
					Logger.Log($"Ingestion cycle failed; will retry after {currentBackoff.TotalSeconds:F0}s.", ex);
					await ApplyBackoffAsync(ref currentBackoff, initialBackoff, maxBackoff, cancellationToken).ConfigureAwait(false);
				}
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

		private static async Task ApplyBackoffAsync(
			ref TimeSpan currentBackoff,
			TimeSpan initialBackoff,
			TimeSpan maxBackoff,
			CancellationToken cancellationToken)
		{
			await DelayAsync(currentBackoff, cancellationToken).ConfigureAwait(false);

			var doubledSeconds = Math.Min(currentBackoff.TotalSeconds * 2, maxBackoff.TotalSeconds);
			currentBackoff = TimeSpan.FromSeconds(Math.Max(doubledSeconds, initialBackoff.TotalSeconds));
		}
	}
}


