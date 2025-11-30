using System.ServiceProcess;

namespace SizerDataCollector.Service
{
	internal static class Program
	{
		private static void Main()
		{
			ServiceBase.Run(new ServiceBase[]
			{
				new SizerCollectorService()
			});
		}
	}
}

