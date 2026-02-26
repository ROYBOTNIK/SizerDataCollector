using System;
using System.Collections.Generic;
using System.Configuration.Install;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.ServiceProcess;
using System.Threading;
using SizerDataCollector.Core.Config;
using SizerDataCollector.Core.Db;
using SizerDataCollector.Core.Sizer;
using SizerDataCollector.Service.Commands;

namespace SizerDataCollector.Service
{
	internal static class Program
	{
		private const string ServiceName = "SizerDataCollectorService";
		private static readonly TimeSpan DefaultServiceTimeout = TimeSpan.FromSeconds(30);

		private static int Main(string[] args)
		{
			if (args != null && args.Length > 0)
			{
				return RunCli(args);
			}

			ServiceBase.Run(new ServiceBase[]
			{
				new SizerCollectorService()
			});

			return 0;
		}

		private static int RunCli(string[] args)
		{
			var command = (args[0] ?? string.Empty).Trim().ToLowerInvariant();
			var options = ParseOptions(args.Skip(1).ToArray());

			switch (command)
			{
				case "help":
				case "--help":
				case "-h":
					ShowUsage();
					return 0;
				case "show-config":
					return ShowConfig();
				case "set-sizer":
					return SetSizer(options);
				case "set-db":
					return SetDb(options);
				case "set-ingestion":
					return SetIngestion(options);
				case "set-shared-dir":
					return SetSharedDir(options);
				case "configure":
					return ConfigureInteractive();
				case "test-connections":
					return TestConnections();
				case "console":
					return RunConsoleMode();
				case "service":
					return RunServiceCommand(args.Skip(1).ToArray());
				case "db":
					return DbCommands.Run(args.Skip(1).ToArray());
				case "machine":
					return MachineCommands.Run(args.Skip(1).ToArray(), options);
				default:
					Console.WriteLine($"Unknown command '{command}'.");
					ShowUsage();
					return 1;
			}
		}

		private static Dictionary<string, string> ParseOptions(string[] args)
		{
			var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

			for (var index = 0; index < args.Length; index++)
			{
				var current = args[index];
				if (string.IsNullOrWhiteSpace(current) || !current.StartsWith("--", StringComparison.Ordinal))
				{
					continue;
				}

				var key = current.Substring(2);
				string value = "true";

				if (index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal))
				{
					value = args[++index];
				}

				options[key] = value;
			}

			return options;
		}

		private static int ShowConfig()
		{
			var provider = new CollectorSettingsProvider();
			var settings = provider.Load();

			Console.WriteLine("Current SizerDataCollector runtime settings:");
			Console.WriteLine($"  Sizer API Host:        {settings.SizerHost}");
			Console.WriteLine($"  Sizer API Port:        {settings.SizerPort}");
			Console.WriteLine($"  Open Timeout (sec):    {settings.OpenTimeoutSec}");
			Console.WriteLine($"  Send Timeout (sec):    {settings.SendTimeoutSec}");
			Console.WriteLine($"  Receive Timeout (sec): {settings.ReceiveTimeoutSec}");
			Console.WriteLine($"  Timescale Connection:  {settings.TimescaleConnectionString}");
			Console.WriteLine($"  Enable Ingestion:      {settings.EnableIngestion}");
			Console.WriteLine($"  Shared Data Directory: {settings.SharedDataDirectory}");
			Console.WriteLine($"  Enabled Metrics:       {string.Join(", ", settings.EnabledMetrics ?? new List<string>())}");
			return 0;
		}

		private static int SetSizer(Dictionary<string, string> options)
		{
			var provider = new CollectorSettingsProvider();
			var settings = provider.Load();

			if (options.TryGetValue("host", out var host) && !string.IsNullOrWhiteSpace(host))
			{
				settings.SizerHost = host.Trim();
			}

			if (options.TryGetValue("port", out var portRaw) && int.TryParse(portRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var port))
			{
				settings.SizerPort = port;
			}

			if (options.TryGetValue("open-timeout", out var openRaw) && int.TryParse(openRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var openTimeout))
			{
				settings.OpenTimeoutSec = openTimeout;
			}

			if (options.TryGetValue("send-timeout", out var sendRaw) && int.TryParse(sendRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sendTimeout))
			{
				settings.SendTimeoutSec = sendTimeout;
			}

			if (options.TryGetValue("receive-timeout", out var receiveRaw) && int.TryParse(receiveRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var receiveTimeout))
			{
				settings.ReceiveTimeoutSec = receiveTimeout;
			}

			provider.Save(settings);
			Console.WriteLine("Sizer API settings updated.");
			return 0;
		}

		private static int SetDb(Dictionary<string, string> options)
		{
			if (!options.TryGetValue("connection", out var connectionString) || string.IsNullOrWhiteSpace(connectionString))
			{
				Console.WriteLine("Missing required option: --connection \"Host=...;Port=5432;...\"");
				return 1;
			}

			var provider = new CollectorSettingsProvider();
			var settings = provider.Load();
			settings.TimescaleConnectionString = connectionString.Trim();
			provider.Save(settings);

			Console.WriteLine("Timescale connection string updated.");
			return 0;
		}

		private static int SetIngestion(Dictionary<string, string> options)
		{
			if (!options.TryGetValue("enabled", out var enabledRaw) || !bool.TryParse(enabledRaw, out var enabled))
			{
				Console.WriteLine("Missing or invalid option. Use: set-ingestion --enabled true|false");
				return 1;
			}

			var provider = new CollectorSettingsProvider();
			var settings = provider.Load();
			settings.EnableIngestion = enabled;
			provider.Save(settings);

			Console.WriteLine($"EnableIngestion set to {enabled}.");
			return 0;
		}

		private static int SetSharedDir(Dictionary<string, string> options)
		{
			if (!options.TryGetValue("path", out var path) || string.IsNullOrWhiteSpace(path))
			{
				Console.WriteLine("Missing required option. Use: set-shared-dir --path \"C:\\ProgramData\\Opti-Fresh\\SizerCollector\"");
				return 1;
			}

			var provider = new CollectorSettingsProvider();
			var settings = provider.Load();
			settings.SharedDataDirectory = path.Trim();
			provider.Save(settings);

			Console.WriteLine("Shared data directory updated.");
			return 0;
		}

		private static int ConfigureInteractive()
		{
			var provider = new CollectorSettingsProvider();
			var settings = provider.Load();

			Console.WriteLine("SizerDataCollector interactive configuration");
			settings.SizerHost = Prompt("Sizer API host", settings.SizerHost);
			settings.SizerPort = PromptInt("Sizer API port", settings.SizerPort);
			settings.OpenTimeoutSec = PromptInt("Open timeout seconds", settings.OpenTimeoutSec);
			settings.SendTimeoutSec = PromptInt("Send timeout seconds", settings.SendTimeoutSec);
			settings.ReceiveTimeoutSec = PromptInt("Receive timeout seconds", settings.ReceiveTimeoutSec);
			settings.TimescaleConnectionString = Prompt("Timescale connection string", settings.TimescaleConnectionString);
			settings.EnableIngestion = PromptBool("Enable ingestion", settings.EnableIngestion);
			settings.SharedDataDirectory = Prompt("Shared data directory", settings.SharedDataDirectory);

			provider.Save(settings);
			Console.WriteLine("Configuration saved.");
			return 0;
		}

		private static int TestConnections()
		{
			var provider = new CollectorSettingsProvider();
			var settings = provider.Load();
			var config = new CollectorConfig(settings);

			Console.WriteLine("Testing API and Timescale connections...");

			try
			{
				using (var client = new SizerClient(config))
				{
					var serial = client.GetSerialNoAsync(CancellationToken.None).GetAwaiter().GetResult();
					Console.WriteLine($"Sizer API OK. Serial: {serial}");
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Sizer API test failed: {ex.Message}");
				return 1;
			}

			DatabaseTester.TestAndInitialize(config);
			Console.WriteLine("Timescale test attempted. Check logs for schema/bootstrap details.");
			return 0;
		}

		private static int RunConsoleMode()
		{
			var service = new SizerCollectorService();
			service.StartAsConsole();
			Console.WriteLine("Collector running in console mode. Press ENTER to stop.");
			Console.ReadLine();
			service.StopAsConsole();
			return 0;
		}

		private static int RunServiceCommand(string[] args)
		{
			var subCommand = args.Length > 0
				? (args[0] ?? string.Empty).Trim().ToLowerInvariant()
				: string.Empty;

			var options = ParseOptions(args.Skip(1).ToArray());
			var timeout = DefaultServiceTimeout;

			if (options.TryGetValue("timeout", out var timeoutRaw) &&
				int.TryParse(timeoutRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var timeoutSec) &&
				timeoutSec > 0)
			{
				timeout = TimeSpan.FromSeconds(timeoutSec);
			}

			switch (subCommand)
			{
				case "status":
					return ServiceStatus();
				case "install":
					return ServiceInstall();
				case "uninstall":
					return ServiceUninstall();
				case "start":
					return ServiceStart(timeout);
				case "stop":
					return ServiceStop(timeout);
				case "restart":
					return ServiceRestart(timeout);
				default:
					Console.WriteLine("Usage: SizerDataCollector.Service.exe service <status|install|uninstall|start|stop|restart> [--timeout <seconds>]");
					return 1;
			}
		}

		private static int ServiceStatus()
		{
			try
			{
				using (var controller = new ServiceController(ServiceName))
				{
					var status = controller.Status;
					Console.WriteLine($"Service '{ServiceName}' is installed.");
					Console.WriteLine($"  Status: {status}");
					return 0;
				}
			}
			catch (InvalidOperationException)
			{
				Console.WriteLine($"Service '{ServiceName}' is not installed.");
				return 1;
			}
		}

		private static int ServiceInstall()
		{
			try
			{
				using (var controller = new ServiceController(ServiceName))
				{
					var _ = controller.Status;
					Console.WriteLine($"Service '{ServiceName}' is already installed.");
					return 0;
				}
			}
			catch (InvalidOperationException)
			{
			}

			try
			{
				var exePath = Assembly.GetExecutingAssembly().Location;
				ManagedInstallerClass.InstallHelper(new[] { exePath });
				Console.WriteLine($"Service '{ServiceName}' installed successfully.");
				return 0;
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Failed to install service: {ex.Message}");
				Console.WriteLine("Ensure you are running as Administrator.");
				return 1;
			}
		}

		private static int ServiceUninstall()
		{
			try
			{
				using (var controller = new ServiceController(ServiceName))
				{
					var status = controller.Status;
					if (status != ServiceControllerStatus.Stopped)
					{
						Console.WriteLine($"Service is currently {status}. Stopping first...");
						if (status != ServiceControllerStatus.StopPending)
						{
							controller.Stop();
						}
						controller.WaitForStatus(ServiceControllerStatus.Stopped, DefaultServiceTimeout);
					}
				}
			}
			catch (InvalidOperationException)
			{
				Console.WriteLine($"Service '{ServiceName}' is not installed.");
				return 1;
			}

			try
			{
				var exePath = Assembly.GetExecutingAssembly().Location;
				ManagedInstallerClass.InstallHelper(new[] { "/u", exePath });
				Console.WriteLine($"Service '{ServiceName}' uninstalled successfully.");
				return 0;
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Failed to uninstall service: {ex.Message}");
				Console.WriteLine("Ensure you are running as Administrator.");
				return 1;
			}
		}

		private static int ServiceStart(TimeSpan timeout)
		{
			try
			{
				using (var controller = new ServiceController(ServiceName))
				{
					if (controller.Status == ServiceControllerStatus.Running)
					{
						Console.WriteLine($"Service '{ServiceName}' is already running.");
						return 0;
					}

					if (controller.Status != ServiceControllerStatus.StartPending)
					{
						Console.WriteLine($"Starting service '{ServiceName}'...");
						controller.Start();
					}

					controller.WaitForStatus(ServiceControllerStatus.Running, timeout);
					Console.WriteLine($"Service '{ServiceName}' started.");
					return 0;
				}
			}
			catch (InvalidOperationException)
			{
				Console.WriteLine($"Service '{ServiceName}' is not installed.");
				return 1;
			}
			catch (System.ServiceProcess.TimeoutException)
			{
				Console.WriteLine($"Timed out waiting for service '{ServiceName}' to start.");
				return 1;
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Failed to start service: {ex.Message}");
				return 1;
			}
		}

		private static int ServiceStop(TimeSpan timeout)
		{
			try
			{
				using (var controller = new ServiceController(ServiceName))
				{
					if (controller.Status == ServiceControllerStatus.Stopped)
					{
						Console.WriteLine($"Service '{ServiceName}' is already stopped.");
						return 0;
					}

					if (controller.Status != ServiceControllerStatus.StopPending)
					{
						Console.WriteLine($"Stopping service '{ServiceName}'...");
						controller.Stop();
					}

					controller.WaitForStatus(ServiceControllerStatus.Stopped, timeout);
					Console.WriteLine($"Service '{ServiceName}' stopped.");
					return 0;
				}
			}
			catch (InvalidOperationException)
			{
				Console.WriteLine($"Service '{ServiceName}' is not installed.");
				return 1;
			}
			catch (System.ServiceProcess.TimeoutException)
			{
				Console.WriteLine($"Timed out waiting for service '{ServiceName}' to stop.");
				return 1;
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Failed to stop service: {ex.Message}");
				return 1;
			}
		}

		private static int ServiceRestart(TimeSpan timeout)
		{
			var stopResult = ServiceStop(timeout);
			if (stopResult != 0)
			{
				return stopResult;
			}

			return ServiceStart(timeout);
		}

		private static string Prompt(string label, string current)
		{
			Console.Write($"{label} [{current}]: ");
			var value = Console.ReadLine();
			return string.IsNullOrWhiteSpace(value) ? current : value.Trim();
		}

		private static int PromptInt(string label, int current)
		{
			while (true)
			{
				var raw = Prompt(label, current.ToString(CultureInfo.InvariantCulture));
				if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
				{
					return value;
				}

				Console.WriteLine("Please enter a valid integer.");
			}
		}

		private static bool PromptBool(string label, bool current)
		{
			while (true)
			{
				var raw = Prompt(label, current ? "true" : "false");
				if (bool.TryParse(raw, out var value))
				{
					return value;
				}

				Console.WriteLine("Please enter true or false.");
			}
		}

		private static void ShowUsage()
		{
			Console.WriteLine("SizerDataCollector.Service CLI");
			Console.WriteLine("Usage:");
			Console.WriteLine("  SizerDataCollector.Service.exe                       (run as Windows Service)");
			Console.WriteLine("  SizerDataCollector.Service.exe console               (run collector in foreground)");
			Console.WriteLine("  SizerDataCollector.Service.exe show-config");
			Console.WriteLine("  SizerDataCollector.Service.exe configure");
			Console.WriteLine("  SizerDataCollector.Service.exe set-sizer --host <host> --port <port>");
			Console.WriteLine("      [--open-timeout <sec>] [--send-timeout <sec>] [--receive-timeout <sec>]");
			Console.WriteLine("  SizerDataCollector.Service.exe set-db --connection \"Host=...;Database=...;Username=...;Password=...\"");
			Console.WriteLine("  SizerDataCollector.Service.exe set-ingestion --enabled true|false");
			Console.WriteLine("  SizerDataCollector.Service.exe set-shared-dir --path \"C:\\ProgramData\\Opti-Fresh\\SizerCollector\"");
			Console.WriteLine("  SizerDataCollector.Service.exe test-connections");
			Console.WriteLine();
			Console.WriteLine("Service control (requires Administrator):");
			Console.WriteLine("  SizerDataCollector.Service.exe service status");
			Console.WriteLine("  SizerDataCollector.Service.exe service install");
			Console.WriteLine("  SizerDataCollector.Service.exe service uninstall");
			Console.WriteLine("  SizerDataCollector.Service.exe service start     [--timeout <seconds>]");
			Console.WriteLine("  SizerDataCollector.Service.exe service stop      [--timeout <seconds>]");
			Console.WriteLine("  SizerDataCollector.Service.exe service restart   [--timeout <seconds>]");
			Console.WriteLine();
			Console.WriteLine("Database management:");
			Console.WriteLine("  SizerDataCollector.Service.exe db status");
			Console.WriteLine("  SizerDataCollector.Service.exe db init                (create/update full schema)");
			Console.WriteLine("  SizerDataCollector.Service.exe db apply-functions");
			Console.WriteLine("  SizerDataCollector.Service.exe db apply-caggs");
			Console.WriteLine("  SizerDataCollector.Service.exe db apply-views");
			Console.WriteLine("  SizerDataCollector.Service.exe db apply-all");
			Console.WriteLine("  SizerDataCollector.Service.exe db list-functions");
			Console.WriteLine("  SizerDataCollector.Service.exe db list-views");
			Console.WriteLine("  SizerDataCollector.Service.exe db list-caggs");
			Console.WriteLine();
			Console.WriteLine("Machine setup:");
			Console.WriteLine("  SizerDataCollector.Service.exe machine list");
			Console.WriteLine("  SizerDataCollector.Service.exe machine register       --serial <sn> --name <name>");
			Console.WriteLine("  SizerDataCollector.Service.exe machine status         --serial <sn>");
			Console.WriteLine("  SizerDataCollector.Service.exe machine set-thresholds --serial <sn> --min-rpm <val> --min-total-fpm <val>");
			Console.WriteLine("  SizerDataCollector.Service.exe machine set-settings   --serial <sn> --target-speed <val>");
			Console.WriteLine("      --lane-count <val> --target-pct <val> --recycle-outlet <val>");
			Console.WriteLine("  SizerDataCollector.Service.exe machine grade-map      --serial <sn>");
			Console.WriteLine("  SizerDataCollector.Service.exe machine grade-map      --serial <sn> --set --grade <key> --category <0-3>");
			Console.WriteLine("  SizerDataCollector.Service.exe machine commission     --serial <sn>");
			Console.WriteLine("  SizerDataCollector.Service.exe machine show-quality-params --serial <sn>");
			Console.WriteLine("  SizerDataCollector.Service.exe machine set-quality-params  --serial <sn> [--tgt-good <v>] ...");
			Console.WriteLine("  SizerDataCollector.Service.exe machine show-perf-params    --serial <sn>");
			Console.WriteLine("  SizerDataCollector.Service.exe machine set-perf-params     --serial <sn> [--min-effective <v>] ...");
			Console.WriteLine("  SizerDataCollector.Service.exe machine show-bands          --serial <sn>");
			Console.WriteLine("  SizerDataCollector.Service.exe machine set-band            --serial <sn> --band <name> --lower <v> --upper <v>");
			Console.WriteLine("  SizerDataCollector.Service.exe machine remove-band         --serial <sn> --band <name>");
		}
	}
}
