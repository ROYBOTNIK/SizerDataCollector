using System.ComponentModel;
using System.Configuration.Install;
using System.ServiceProcess;

namespace SizerDataCollector.Service
{
	[RunInstaller(true)]
	public class ProjectInstaller : Installer
	{
		private readonly ServiceProcessInstaller _processInstaller;
		private readonly ServiceInstaller _serviceInstaller;

		public ProjectInstaller()
		{
			_processInstaller = new ServiceProcessInstaller
			{
				Account = ServiceAccount.LocalSystem
			};

			_serviceInstaller = new ServiceInstaller
			{
				ServiceName = "SizerDataCollectorService",
				DisplayName = "Opti-Fresh Sizer Data Collector",
				StartType = ServiceStartMode.Automatic
			};

			Installers.Add(_processInstaller);
			Installers.Add(_serviceInstaller);
		}
	}
}

