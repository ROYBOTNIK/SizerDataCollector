using System;
using System.ServiceProcess;
using System.Threading.Tasks;

namespace SizerDataCollector.GUI.WPF.Services
{
	public sealed class WindowsServiceManager
	{
		private const string ServiceName = "SizerDataCollector.Service";

		public WindowsServiceManager()
		{
		}

		public bool IsInstalled()
		{
			try
			{
				using (var controller = new ServiceController(ServiceName))
				{
					var _ = controller.Status;
					return true;
				}
			}
			catch (InvalidOperationException)
			{
				return false;
			}
		}

		public ServiceControllerStatus? GetStatus()
		{
			try
			{
				using (var controller = new ServiceController(ServiceName))
				{
					return controller.Status;
				}
			}
			catch (InvalidOperationException)
			{
				return null;
			}
		}

		public Task StartAsync(TimeSpan timeout)
		{
			return Task.Run(() =>
			{
				using (var controller = GetControllerOrThrow())
				{
					if (controller.Status == ServiceControllerStatus.Running ||
						controller.Status == ServiceControllerStatus.StartPending)
					{
						controller.WaitForStatus(ServiceControllerStatus.Running, timeout);
						return;
					}

					controller.Start();
					controller.WaitForStatus(ServiceControllerStatus.Running, timeout);
				}
			});
		}

		public Task StopAsync(TimeSpan timeout)
		{
			return Task.Run(() =>
			{
				using (var controller = GetControllerOrThrow())
				{
					if (controller.Status == ServiceControllerStatus.Stopped ||
						controller.Status == ServiceControllerStatus.StopPending)
					{
						controller.WaitForStatus(ServiceControllerStatus.Stopped, timeout);
						return;
					}

					controller.Stop();
					controller.WaitForStatus(ServiceControllerStatus.Stopped, timeout);
				}
			});
		}

		private ServiceController GetControllerOrThrow()
		{
			try
			{
				return new ServiceController(ServiceName);
			}
			catch (InvalidOperationException ex)
			{
				throw new InvalidOperationException($"Service '{ServiceName}' is not installed.", ex);
			}
		}
	}
}

