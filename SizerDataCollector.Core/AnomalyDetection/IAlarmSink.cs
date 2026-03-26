using System.Threading;
using System.Threading.Tasks;

namespace SizerDataCollector.Core.AnomalyDetection
{
	public interface IAlarmSink
	{
		Task DeliverAsync(AnomalyEvent evt, CancellationToken cancellationToken);
	}
}
