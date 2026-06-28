using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SizerDataCollector.Core.Logging;

namespace SizerDataCollector.Core.AnomalyDetection
{
	/// <summary>
	/// Fans out alarm delivery to multiple sinks. Each sink is called sequentially
	/// so that the log sink records before network sinks attempt delivery.
	/// Individual sink failures are caught and logged without aborting the chain.
	/// </summary>
	public sealed class CompositeAlarmSink : IAlarmSink
	{
		private readonly IReadOnlyList<IAlarmSink> _sinks;

		public CompositeAlarmSink(IEnumerable<IAlarmSink> sinks)
		{
			_sinks = new List<IAlarmSink>(sinks ?? throw new ArgumentNullException(nameof(sinks)));
		}

		public async Task DeliverAsync(AnomalyEvent evt, CancellationToken cancellationToken)
		{
			foreach (var sink in _sinks)
			{
				try
				{
					await sink.DeliverAsync(evt, cancellationToken).ConfigureAwait(false);
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					throw;
				}
				catch (Exception ex)
				{
					Logger.Log($"Alarm sink {sink.GetType().Name} failed for event lane={evt.LaneNo}, grade={evt.GradeKey}.",
						ex, LogLevel.Warn);
				}
			}
		}
	}
}
