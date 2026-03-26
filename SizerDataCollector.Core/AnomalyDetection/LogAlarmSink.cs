using System.Threading;
using System.Threading.Tasks;
using SizerDataCollector.Core.Logging;

namespace SizerDataCollector.Core.AnomalyDetection
{
	public sealed class LogAlarmSink : IAlarmSink
	{
		public Task DeliverAsync(AnomalyEvent evt, CancellationToken cancellationToken)
		{
			Logger.Log(
				$"ANOMALY [{evt.Severity}] {evt.AlarmTitle} | {evt.AlarmDetails} " +
				$"(lane={evt.LaneNo}, grade={evt.GradeKey}, z={evt.AnomalyScore:F1}, " +
				$"pct={evt.Pct:F1}%, batch={evt.BatchRecordId})");

			return Task.CompletedTask;
		}
	}
}
