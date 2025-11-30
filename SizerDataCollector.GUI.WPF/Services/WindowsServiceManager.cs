using System;
using System.ServiceProcess;
using System.Threading.Tasks;

namespace SizerDataCollector.GUI.WPF.Services
{
	public sealed class WindowsServiceManager
	{
		private readonly string _serviceName;

		public WindowsServiceManager(string serviceName = "SizerDataCollectorService")
		{
			if (string.IsNullOrWhiteSpace(serviceName))
			{
				throw new ArgumentException("Service name must be provided.", nameof(serviceName));
			}

			_serviceName = serviceName;
		}

		public bool IsInstalled()
		{
			try
			{
				using (var controller = new ServiceController(_serviceName))
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
				using (var controller = new ServiceController(_serviceName))
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
				return new ServiceController(_serviceName);
			}
			catch (InvalidOperationException ex)
			{
				throw new InvalidOperationException($"Service '{_serviceName}' is not installed.", ex);
			}
		}
	}
}

