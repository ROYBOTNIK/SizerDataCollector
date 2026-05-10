using System;
using System.Collections.Generic;
using System.Configuration.Install;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.ServiceProcess;
using System.Threading;
using SizerDataCollector.Core.AnomalyDetection;
using SizerDataCollector.Core.Config;
using SizerDataCollector.Core.Db;
using SizerDataCollector.Core.Logging;
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
				case "shift":
					return ShiftCommands.Run(args.Skip(1).ToArray(), options);
				case "anomaly":
					return AnomalyCommands.Run(args.Skip(1).ToArray(), options);
				case "lot-transition":
					return LotTransitionCommands.Run(args.Skip(1).ToArray(), options);
				case "machine-event":
					return MachineEventCommands.Run(args.Skip(1).ToArray(), options);
				case "downtime":
					return MachineEventCommands.Run(args.Skip(1).ToArray(), options, "downtime");
				case "slowdown":
					return MachineEventCommands.Run(args.Skip(1).ToArray(), options, "slowdown");
				case "set-anomaly":
					return SetAnomaly(options);
				case "set-sizer-alarm":
					return SetSizerAlarm(options);
				case "replay-anomaly":
					return ReplayAnomaly(options);
				case "set-size-anomaly":
					return SetSizeAnomaly(options);
				case "set-lot-transition":
					return SetLotTransition(options);
				case "set-machine-events":
					return SetMachineEvents(options);
				case "size-health":
					return SizeHealth(options);
				case "test-alarm":
					return TestAlarm(options);
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
			Console.WriteLine();
			Console.WriteLine("  Anomaly Detection:");
			Console.WriteLine($"    Enabled:             {settings.EnableAnomalyDetection}");
			Console.WriteLine($"    Window (min):        {settings.AnomalyWindowMinutes}");
			Console.WriteLine($"    Z-Gate:              {settings.AnomalyZGate}");
			Console.WriteLine($"    Bands (Low/Med):     {settings.BandLowMin}-{settings.BandLowMax}% / {settings.BandLowMax}-{settings.BandMediumMax}% / {settings.BandMediumMax}%+");
			Console.WriteLine($"    Cooldown (sec):      {settings.AlarmCooldownSeconds}");
			Console.WriteLine($"    Recycle Grade Key:   {settings.RecycleGradeKey}");
			Console.WriteLine($"    Min Lane FPM:        {settings.AnomalyMinLaneFpm}");
			Console.WriteLine($"    Min Peer Lane FPM:   {settings.AnomalyMinPeerLaneFpm}");
			Console.WriteLine($"    Min Peer Lanes:      {settings.AnomalyMinActivePeerLanes}");
			Console.WriteLine($"    Consecutive Windows: {settings.AnomalyMinConsecutiveWindows}");
			Console.WriteLine($"    Sizer Alarm:         {settings.EnableSizerAlarm}");
			Console.WriteLine($"    LLM Enrichment:      {settings.EnableLlmEnrichment}");
			if (settings.EnableLlmEnrichment)
				Console.WriteLine($"    LLM Endpoint:        {settings.LlmEndpoint}");
			Console.WriteLine();
			Console.WriteLine("  Size Anomaly Detection:");
			Console.WriteLine($"    Enabled:             {settings.EnableSizeAnomalyDetection}");
			Console.WriteLine($"    Eval Interval (min): {settings.SizeEvalIntervalMinutes}");
			Console.WriteLine($"    Window (hours):      {settings.SizeWindowHours}");
			Console.WriteLine($"    Z-Gate:              {settings.SizeZGate}");
			Console.WriteLine($"    PctDev Min:          {settings.SizePctDevMin}%");
			Console.WriteLine($"    Cooldown (min):      {settings.SizeCooldownMinutes}");
			Console.WriteLine($"    Sizer Alarm:         {settings.EnableSizerSizeAlarm}");
			Console.WriteLine();
			Console.WriteLine("  Lot Transition Detection:");
			Console.WriteLine($"    Enabled:             {settings.EnableLotTransitionDetection}");
			Console.WriteLine($"    Eval Interval (min): {settings.LotTransitionEvalIntervalMinutes}");
			Console.WriteLine($"    Scan Window (hours): {settings.LotTransitionScanWindowHours}");
			Console.WriteLine($"    Stable Window (min): {settings.LotTransitionStableWindowMinutes}");
			Console.WriteLine($"    Peak Search (min):   {settings.LotTransitionPeakSearchMinutes}");
			Console.WriteLine($"    Slowdown Fraction:   {settings.LotTransitionSlowdownFraction}");
			Console.WriteLine($"    Recovery Fraction:   {settings.LotTransitionRecoveryFraction}");
			Console.WriteLine($"    Slowdown Samples:    {settings.LotTransitionConsecutiveSamplesForSlowdown}");
			Console.WriteLine($"    Recovery Samples:    {settings.LotTransitionRecoveryConsecutiveSamples}");
			Console.WriteLine($"    Min Stable Samples:  {settings.LotTransitionMinPreStableSamples}/{settings.LotTransitionMinPostStableSamples}");
			Console.WriteLine($"    Min Baseline FPM:    {settings.LotTransitionMinFpmForBaseline}");
			Console.WriteLine();
			Console.WriteLine("  Machine Event Detection:");
			Console.WriteLine($"    Enabled:             {settings.EnableMachineEventDetection}");
			Console.WriteLine($"    Eval Interval (min): {settings.MachineEventEvalIntervalMinutes}");
			Console.WriteLine($"    Scan Window (hours): {settings.MachineEventScanWindowHours}");
			Console.WriteLine($"    Downtime Max Avail:  {settings.MachineEventDowntimeMaxAvailabilityRatio}");
			Console.WriteLine($"    Slowdown Max Thrpt:  {settings.MachineEventSlowdownMaxThroughputRatio}");
			Console.WriteLine($"    Slowdown Min Avail:  {settings.MachineEventSlowdownMinAvailabilityRatio}");
			Console.WriteLine($"    Slowdown Min FPM:    {settings.MachineEventSlowdownMinTotalFpm}");
			Console.WriteLine($"    Min Duration (min):  {settings.MachineEventMinDurationMinutes}");
			Console.WriteLine($"    Merge Gap (min):     {settings.MachineEventMergeGapMinutes}");
			Console.WriteLine($"    Exclude Transitions: {settings.MachineEventExcludeLotTransitions}");
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

		private static int SetAnomaly(Dictionary<string, string> options)
		{
			if (options.Count == 0)
			{
				Console.WriteLine("Usage: set-anomaly --enabled true|false [--window <min>] [--z-gate <val>]");
				Console.WriteLine("       [--band-low-min <share-pts>] [--band-low-max <share-pts>] [--band-medium-max <share-pts>]");
				Console.WriteLine("       [--cooldown <sec>] [--recycle-key <name>] [--min-lane-fpm <val>]");
				Console.WriteLine("       [--min-peer-lane-fpm <val>] [--min-peer-lanes <count>] [--consecutive-windows <count>]");
				Console.WriteLine("       [--llm true|false] [--llm-endpoint <url>]");
				return 1;
			}

			var provider = new CollectorSettingsProvider();
			var settings = provider.Load();
			bool changed = false;

			if (options.TryGetValue("enabled", out var enabledRaw) && bool.TryParse(enabledRaw, out var enabled))
			{
				settings.EnableAnomalyDetection = enabled;
				Console.WriteLine($"  EnableAnomalyDetection = {enabled}");
				changed = true;
			}

			if (options.TryGetValue("window", out var windowRaw) && int.TryParse(windowRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var window) && window >= 1)
			{
				settings.AnomalyWindowMinutes = window;
				Console.WriteLine($"  AnomalyWindowMinutes = {window}");
				changed = true;
			}

			if (options.TryGetValue("z-gate", out var zgRaw) && double.TryParse(zgRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out var zg) && zg > 0)
			{
				settings.AnomalyZGate = zg;
				Console.WriteLine($"  AnomalyZGate = {zg}");
				changed = true;
			}

			if (options.TryGetValue("band-low-min", out var blmRaw) && double.TryParse(blmRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out var blm) && blm > 0)
			{
				settings.BandLowMin = blm;
				Console.WriteLine($"  BandLowMin = {blm}");
				changed = true;
			}

			if (options.TryGetValue("band-low-max", out var blxRaw) && double.TryParse(blxRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out var blx) && blx > 0)
			{
				settings.BandLowMax = blx;
				Console.WriteLine($"  BandLowMax = {blx}");
				changed = true;
			}

			if (options.TryGetValue("band-medium-max", out var bmmRaw) && double.TryParse(bmmRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out var bmm) && bmm > 0)
			{
				settings.BandMediumMax = bmm;
				Console.WriteLine($"  BandMediumMax = {bmm}");
				changed = true;
			}

			if (options.TryGetValue("cooldown", out var cdRaw) && int.TryParse(cdRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var cd) && cd >= 0)
			{
				settings.AlarmCooldownSeconds = cd;
				Console.WriteLine($"  AlarmCooldownSeconds = {cd}");
				changed = true;
			}

			if (options.TryGetValue("recycle-key", out var rk) && !string.IsNullOrWhiteSpace(rk))
			{
				settings.RecycleGradeKey = rk.Trim();
				Console.WriteLine($"  RecycleGradeKey = {settings.RecycleGradeKey}");
				changed = true;
			}

			if (options.TryGetValue("min-lane-fpm", out var minLaneRaw) && double.TryParse(minLaneRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out var minLane) && minLane > 0)
			{
				settings.AnomalyMinLaneFpm = minLane;
				Console.WriteLine($"  AnomalyMinLaneFpm = {minLane}");
				changed = true;
			}

			if (options.TryGetValue("min-peer-lane-fpm", out var minPeerRaw) && double.TryParse(minPeerRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out var minPeer) && minPeer > 0)
			{
				settings.AnomalyMinPeerLaneFpm = minPeer;
				Console.WriteLine($"  AnomalyMinPeerLaneFpm = {minPeer}");
				changed = true;
			}

			if (options.TryGetValue("min-peer-lanes", out var minPeersRaw) && int.TryParse(minPeersRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var minPeers) && minPeers >= 1)
			{
				settings.AnomalyMinActivePeerLanes = minPeers;
				Console.WriteLine($"  AnomalyMinActivePeerLanes = {minPeers}");
				changed = true;
			}

			if (options.TryGetValue("consecutive-windows", out var consecutiveRaw) && int.TryParse(consecutiveRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var consecutive) && consecutive >= 1)
			{
				settings.AnomalyMinConsecutiveWindows = consecutive;
				Console.WriteLine($"  AnomalyMinConsecutiveWindows = {consecutive}");
				changed = true;
			}

			if (options.TryGetValue("llm", out var llmRaw) && bool.TryParse(llmRaw, out var llm))
			{
				settings.EnableLlmEnrichment = llm;
				Console.WriteLine($"  EnableLlmEnrichment = {llm}");
				changed = true;
			}

			if (options.TryGetValue("llm-endpoint", out var llmEp) && !string.IsNullOrWhiteSpace(llmEp))
			{
				settings.LlmEndpoint = llmEp.Trim();
				Console.WriteLine($"  LlmEndpoint = {settings.LlmEndpoint}");
				changed = true;
			}

			if (!changed)
			{
				Console.WriteLine("No valid options provided. Run 'set-anomaly' without arguments for usage.");
				return 1;
			}

			provider.Save(settings);
			Console.WriteLine("Anomaly detection settings updated. Restart the service for changes to take effect.");
			return 0;
		}

		private static int SetSizerAlarm(Dictionary<string, string> options)
		{
			if (!options.TryGetValue("enabled", out var enabledRaw) || !bool.TryParse(enabledRaw, out var enabled))
			{
				Console.WriteLine("Missing or invalid option. Use: set-sizer-alarm --enabled true|false");
				return 1;
			}

			var provider = new CollectorSettingsProvider();
			var settings = provider.Load();
			settings.EnableSizerAlarm = enabled;
			provider.Save(settings);

			Console.WriteLine($"EnableSizerAlarm set to {enabled}. Restart the service for changes to take effect.");
			return 0;
		}

		private static int ReplayAnomaly(Dictionary<string, string> options)
		{
			if (!options.TryGetValue("serial", out var serial) || string.IsNullOrWhiteSpace(serial))
			{
				Console.WriteLine("Missing required option: --serial <serial_no>");
				Console.WriteLine("Usage: replay-anomaly --serial <sn> --from <yyyy-MM-dd> --to <yyyy-MM-dd> [--persist] [--diag] [--diag-lane <N>]");
				return 1;
			}

			if (!options.TryGetValue("from", out var fromRaw) || !DateTimeOffset.TryParse(fromRaw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var fromTs))
			{
				Console.WriteLine("Missing or invalid option: --from <yyyy-MM-dd>");
				return 1;
			}

			if (!options.TryGetValue("to", out var toRaw) || !DateTimeOffset.TryParse(toRaw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var toTs))
			{
				Console.WriteLine("Missing or invalid option: --to <yyyy-MM-dd>");
				return 1;
			}

			var persist = options.ContainsKey("persist");
			var diag = options.ContainsKey("diag");
			int diagLaneFilter = -1;
			if (options.TryGetValue("diag-lane", out var laneFilterRaw) && int.TryParse(laneFilterRaw, out var parsedLane))
			{
				diagLaneFilter = parsedLane;
			}

			var provider = new CollectorSettingsProvider();
			var runtimeSettings = provider.Load();
			var config = new CollectorConfig(runtimeSettings);

			if (string.IsNullOrWhiteSpace(config.TimescaleConnectionString))
			{
				Console.WriteLine("No TimescaleDB connection string configured. Run 'set-db' first.");
				return 1;
			}

			Console.WriteLine($"Replaying anomaly detection for serial '{serial}' from {fromTs:O} to {toTs:O}...");
			Console.WriteLine();

			var detectorConfig = new AnomalyDetectorConfig(config);
			var detector = new AnomalyDetector(detectorConfig);
			var replaySource = new ReplayDataSource(config.TimescaleConnectionString);

			var rows = replaySource.LoadAsync(serial, fromTs, toTs, CancellationToken.None).GetAwaiter().GetResult();
			if (rows.Count == 0)
			{
				Console.WriteLine("No lanes_grade_fpm rows found for the specified range.");
				return 0;
			}

			Console.WriteLine($"Loaded {rows.Count} data points.");
			if (diag)
			{
				Console.WriteLine($"[DIAG] Detector config: Window={detectorConfig.WindowMinutes}min ZGate={detectorConfig.ZGate} BandLowMin={detectorConfig.BandLowMin}pts BandMedMax={detectorConfig.BandMediumMax}pts MinLaneFpm={detectorConfig.MinLaneFpm} MinPeerLaneFpm={detectorConfig.MinPeerLaneFpm} MinActivePeerLanes={detectorConfig.MinActivePeerLanes} MinConsecutiveWindows={detectorConfig.MinConsecutiveWindows}");
			}
			Console.WriteLine();

			var allEvents = new List<AnomalyEvent>();
			long previousBatch = 0;
			int batchChangeCount = 0;
			int snapshotCount = 0;
			bool dumpedRawKeys = false;

			foreach (var row in rows)
			{
				if (row.BatchRecordId != previousBatch && previousBatch != 0)
				{
					Console.WriteLine($"  [Batch change at {row.Ts:HH:mm:ss}: {previousBatch} -> {row.BatchRecordId}, detector reset]");
					detector.Reset();
					batchChangeCount++;
				}
				previousBatch = row.BatchRecordId;

				if (diag && !dumpedRawKeys)
				{
					var rawKeys = GradeMatrixParser.GetRawKeys(row.ValueJson, 30);
					if (rawKeys.Count > 0)
					{
						Console.WriteLine($"[DIAG] First snapshot raw payload keys (first {rawKeys.Count}):");
						foreach (var k in rawKeys)
							Console.WriteLine($"       {k}");
						dumpedRawKeys = true;
					}
				}

				var matrix = GradeMatrixParser.Parse(row.ValueJson);
				if (matrix == null) continue;

				var events = detector.Update(matrix, row.Ts, row.SerialNo, (int)row.BatchRecordId);
				snapshotCount++;
				foreach (var evt in events)
				{
					evt.ModelVersion = "replay-v1";
					evt.DeliveredTo = persist ? "replay" : "console";
					allEvents.Add(evt);
					Console.WriteLine($"  {evt.EventTs:yyyy-MM-dd HH:mm:ss} [{evt.Severity,6}] {evt.AlarmDetails}");
				}
			}

			if (diag)
			{
				DumpReplayDiagnostics(detector, detectorConfig, snapshotCount, batchChangeCount, diagLaneFilter);
			}

			Console.WriteLine();
			Console.WriteLine($"Replay complete. {allEvents.Count} anomaly events detected across {rows.Count} data points.");

			if (allEvents.Count > 0)
			{
				var byseverity = allEvents.GroupBy(e => e.Severity).OrderBy(g => g.Key);
				foreach (var group in byseverity)
				{
					Console.WriteLine($"  {group.Key}: {group.Count()}");
				}
			}

			if (persist && allEvents.Count > 0)
			{
				Console.WriteLine();
				Console.WriteLine("Persisting replay events to oee.grade_lane_anomalies...");
				var dbSink = new DatabaseAlarmSink(config.TimescaleConnectionString);
				foreach (var evt in allEvents)
				{
					dbSink.DeliverAsync(evt, CancellationToken.None).GetAwaiter().GetResult();
				}
				Console.WriteLine($"Persisted {allEvents.Count} events.");
			}

			return 0;
		}

		private static void DumpReplayDiagnostics(
			AnomalyDetector detector,
			AnomalyDetectorConfig cfg,
			int snapshotsProcessed,
			int batchChangeCount,
			int diagLaneFilter)
		{
			Console.WriteLine();
			Console.WriteLine("========== DIAGNOSTIC DUMP ==========");
			Console.WriteLine($"Snapshots processed       : {snapshotsProcessed}");
			Console.WriteLine($"Detector resets (batch)   : {batchChangeCount}");
			Console.WriteLine($"Final window sample count : {detector.WindowSampleCount}");
			Console.WriteLine($"Detector lane count       : {detector.LaneCount}");
			Console.WriteLine($"Detector grade count      : {detector.GradeCount}");

			if (detector.GradeKeys != null && detector.GradeKeys.Count > 0)
			{
				Console.WriteLine($"Grade keys (order)        : [{string.Join(", ", detector.GradeKeys)}]");
			}

			if (detector.LaneCount == 0 || detector.GradeCount == 0
				|| detector.LaneAverageFpm == null || detector.LaneSharePct == null)
			{
				Console.WriteLine("[DIAG] Detector never built stable state (no dims). Check parser / row consistency.");
				Console.WriteLine("======================================");
				return;
			}

			var laneStats = new List<(int lane, double avgFpm, int peers, double skewL1, int consecutive)>();
			for (int lane = 0; lane < detector.LaneCount; lane++)
			{
				double l1 = 0;
				if (detector.PctDeviation != null && detector.PctDeviation[lane] != null)
				{
					for (int g = 0; g < detector.GradeCount; g++)
						l1 += Math.Abs(detector.PctDeviation[lane][g]);
					l1 *= 0.5;
				}
				laneStats.Add((
					lane,
					detector.LaneAverageFpm[lane],
					detector.EligiblePeerCounts != null ? detector.EligiblePeerCounts[lane] : 0,
					l1,
					detector.ConsecutiveLaneSignals != null ? detector.ConsecutiveLaneSignals[lane] : 0));
			}

			Console.WriteLine();
			Console.WriteLine("-- Lane summary (sorted by composition skew L1 desc, top 10) --");
			Console.WriteLine($"  {"Lane#",5} {"AvgFpm",8} {"Peers",5} {"SkewL1",8} {"Cons",4}  {"GuardPass"}");

			IEnumerable<(int lane, double avgFpm, int peers, double skewL1, int consecutive)> rows = laneStats;
			if (diagLaneFilter > 0)
			{
				rows = laneStats; // still print top 10, lane filter just adds a focused dump below
			}

			var top = laneStats.OrderByDescending(s => s.skewL1).ThenByDescending(s => s.avgFpm).Take(10).ToList();
			foreach (var s in top)
			{
				bool passLaneFpm = s.avgFpm >= cfg.MinLaneFpm;
				bool passPeers = s.peers >= cfg.MinActivePeerLanes;
				string guard = (passLaneFpm && passPeers) ? "ok" :
					(!passLaneFpm && !passPeers ? "LOW_FPM+LOW_PEERS" :
					 !passLaneFpm ? "LOW_FPM" : "LOW_PEERS");
				Console.WriteLine($"  {s.lane + 1,5} {s.avgFpm,8:F1} {s.peers,5} {s.skewL1,8:F2} {s.consecutive,4}  {guard}");
			}

			int focusLane = diagLaneFilter > 0 ? diagLaneFilter - 1 : top.Count > 0 ? top[0].lane : -1;
			if (focusLane >= 0 && focusLane < detector.LaneCount)
			{
				Console.WriteLine();
				Console.WriteLine($"-- Focus lane {focusLane + 1} grade breakdown --");
				Console.WriteLine($"  AvgFpm={detector.LaneAverageFpm[focusLane]:F1}  Peers={detector.EligiblePeerCounts[focusLane]}  Consecutive={detector.ConsecutiveLaneSignals[focusLane]}");
				Console.WriteLine($"  Gates: MinLaneFpm={cfg.MinLaneFpm} MinPeerLaneFpm={cfg.MinPeerLaneFpm} MinActivePeerLanes={cfg.MinActivePeerLanes} MinConsecutiveWindows={cfg.MinConsecutiveWindows}");
				Console.WriteLine($"  Thresholds: BandLowMin(basePass)={cfg.BandLowMin}pts  ZGate={cfg.ZGate}  BandMediumMax(extremeFallback)={cfg.BandMediumMax}pts");
				Console.WriteLine();
				Console.WriteLine($"  {"Grade",-20} {"LanePct",8} {"PeerMed",8} {"DeltaPts",9} {"Score",7} {"Gates"}");

				double skewL1 = 0;
				var gradeRows = new List<(string key, double lanePct, double peerMed, double delta, double score)>();
				for (int g = 0; g < detector.GradeCount; g++)
				{
					var key = detector.GradeKeys[g];
					double lanePct = detector.LaneSharePct[focusLane][g];
					double peerMed = detector.PeerMedianPct[focusLane][g];
					double delta = detector.PctDeviation[focusLane][g];
					double score = detector.CurrentZScores[focusLane][g];
					skewL1 += Math.Abs(delta);
					gradeRows.Add((key, lanePct, peerMed, delta, score));
				}
				skewL1 *= 0.5;

				foreach (var gr in gradeRows.OrderByDescending(r => Math.Abs(r.delta)))
				{
					double absDelta = Math.Abs(gr.delta);
					double absScore = Math.Abs(gr.score);
					bool baseGate = absDelta >= cfg.BandLowMin;
					bool zGate = absScore >= cfg.ZGate;
					bool extremeGate = absDelta >= cfg.BandMediumMax;
					bool primary = baseGate && (zGate || extremeGate);
					string gates = $"base={(baseGate ? "Y" : "n")} z={(zGate ? "Y" : "n")} extreme={(extremeGate ? "Y" : "n")} => {(primary ? "TRIGGER" : ".")}";
					Console.WriteLine($"  {Truncate(gr.key, 20),-20} {gr.lanePct,8:F2} {gr.peerMed,8:F2} {gr.delta,9:F2} {gr.score,7:F2} {gates}");
				}

				Console.WriteLine();
				Console.WriteLine($"  Composition skew L1 (sum|delta|/2) = {skewL1:F2}pts  (laneSkewFallback trigger at >= {cfg.BandMediumMax})");
				if (detector.LaneAverageFpm[focusLane] < cfg.MinLaneFpm)
					Console.WriteLine($"  [EXCLUDED] AvgFpm {detector.LaneAverageFpm[focusLane]:F1} < MinLaneFpm {cfg.MinLaneFpm}. Lower via: set-anomaly --min-lane-fpm <value>");
				if (detector.EligiblePeerCounts[focusLane] < cfg.MinActivePeerLanes)
					Console.WriteLine($"  [EXCLUDED] Peers {detector.EligiblePeerCounts[focusLane]} < MinActivePeerLanes {cfg.MinActivePeerLanes}. Lower via: set-anomaly --min-peer-lanes <value>  or --min-peer-lane-fpm <value>");
			}

			Console.WriteLine("======================================");
		}

		private static string Truncate(string value, int length)
		{
			if (string.IsNullOrEmpty(value)) return value ?? string.Empty;
			return value.Length <= length ? value : value.Substring(0, length);
		}

		private static int SetSizeAnomaly(Dictionary<string, string> options)
		{
			if (options.Count == 0)
			{
				Console.WriteLine("Usage: set-size-anomaly --enabled true|false [--interval <min>] [--window <hours>]");
				Console.WriteLine("       [--z-gate <val>] [--pct-dev-min <val>] [--cooldown <min>]");
				Console.WriteLine("       [--sizer-alarm true|false]");
				return 1;
			}

			var provider = new CollectorSettingsProvider();
			var settings = provider.Load();
			bool changed = false;

			if (options.TryGetValue("enabled", out var enabledRaw) && bool.TryParse(enabledRaw, out var enabled))
			{
				settings.EnableSizeAnomalyDetection = enabled;
				Console.WriteLine($"  EnableSizeAnomalyDetection = {enabled}");
				changed = true;
			}

			if (options.TryGetValue("interval", out var intRaw) && int.TryParse(intRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var interval) && interval >= 1)
			{
				settings.SizeEvalIntervalMinutes = interval;
				Console.WriteLine($"  SizeEvalIntervalMinutes = {interval}");
				changed = true;
			}

			if (options.TryGetValue("window", out var winRaw) && int.TryParse(winRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var window) && window >= 1)
			{
				settings.SizeWindowHours = window;
				Console.WriteLine($"  SizeWindowHours = {window}");
				changed = true;
			}

			if (options.TryGetValue("z-gate", out var zgRaw) && double.TryParse(zgRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out var zg) && zg > 0)
			{
				settings.SizeZGate = zg;
				Console.WriteLine($"  SizeZGate = {zg}");
				changed = true;
			}

			if (options.TryGetValue("pct-dev-min", out var pdRaw) && double.TryParse(pdRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out var pd) && pd > 0)
			{
				settings.SizePctDevMin = pd;
				Console.WriteLine($"  SizePctDevMin = {pd}");
				changed = true;
			}

			if (options.TryGetValue("cooldown", out var cdRaw) && int.TryParse(cdRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var cd) && cd >= 0)
			{
				settings.SizeCooldownMinutes = cd;
				Console.WriteLine($"  SizeCooldownMinutes = {cd}");
				changed = true;
			}

			if (options.TryGetValue("sizer-alarm", out var saRaw) && bool.TryParse(saRaw, out var sa))
			{
				settings.EnableSizerSizeAlarm = sa;
				Console.WriteLine($"  EnableSizerSizeAlarm = {sa}");
				changed = true;
			}

			if (!changed)
			{
				Console.WriteLine("No valid options provided. Run 'set-size-anomaly' without arguments for usage.");
				return 1;
			}

			provider.Save(settings);
			Console.WriteLine("Size anomaly detection settings updated. Restart the service for changes to take effect.");
			return 0;
		}

		private static int SizeHealth(Dictionary<string, string> options)
		{
			var provider = new CollectorSettingsProvider();
			var runtimeSettings = provider.Load();
			var config = new CollectorConfig(runtimeSettings);

			if (string.IsNullOrWhiteSpace(config.TimescaleConnectionString))
			{
				Console.WriteLine("No TimescaleDB connection string configured. Run 'set-db' first.");
				return 1;
			}

			string serial;
			if (options.TryGetValue("serial", out var s) && !string.IsNullOrWhiteSpace(s))
			{
				serial = s.Trim();
			}
			else
			{
				Console.WriteLine("Missing required option: --serial <serial_no>");
				Console.WriteLine("Usage: size-health --serial <sn> [--hours <h>]");
				Console.WriteLine("       size-health --serial <sn> --from <yyyy-MM-dd> --to <yyyy-MM-dd>");
				return 1;
			}

			DateTimeOffset fromTs, toTs;
			if (options.TryGetValue("from", out var fromRaw) &&
				DateTimeOffset.TryParse(fromRaw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out fromTs) &&
				options.TryGetValue("to", out var toRaw) &&
				DateTimeOffset.TryParse(toRaw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out toTs))
			{
			}
			else
			{
				int hours = 24;
				if (options.TryGetValue("hours", out var hRaw) && int.TryParse(hRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var h) && h >= 1)
					hours = h;
				toTs = DateTimeOffset.UtcNow;
				fromTs = toTs.AddHours(-hours);
			}

			Console.WriteLine($"Querying lane size health for {serial} from {fromTs:yyyy-MM-dd HH:mm} to {toTs:yyyy-MM-dd HH:mm} ...");
			Console.WriteLine();

			var sizeConfig = new SizeAnomalyConfig(config);
			var evaluator = new SizeAnomalyEvaluator(sizeConfig, config.TimescaleConnectionString);
			var report = evaluator.EvaluateRangeAsync(serial, fromTs, toTs, CancellationToken.None).GetAwaiter().GetResult();

			if (report.Rows == null || report.Rows.Count == 0)
			{
				Console.WriteLine("No size data found for the specified range.");
				return 0;
			}

			var windowLabel = (toTs - fromTs).TotalHours >= 24
				? string.Format("{0:F0} days", (toTs - fromTs).TotalDays)
				: string.Format("{0:F0}h", (toTs - fromTs).TotalHours);
			Console.WriteLine(string.Format("Lane size health for {0} ({1}, machine avg = {2:F1}mm)", serial, windowLabel, report.MachineAvg));
			Console.WriteLine();

			Console.WriteLine("Lane | Avg Size | vs Machine |  pctDev  |  z-score | Status");
			Console.WriteLine("-----|----------|------------|----------|----------|--------");
			foreach (var row in report.Rows)
			{
				var diffStr = string.Format("{0:+0.0;-0.0}mm", row.Diff);
				var pctStr = string.Format("{0:+0.0;-0.0}%", row.PctDev);
				var zStr = string.Format("{0:+0.0;-0.0}", row.ZScore);
				Console.WriteLine(string.Format(" {0,3} | {1,6:F1}mm | {2,10} | {3,8} | {4,8} | {5}",
					row.LaneNo, row.AvgSize, diffStr, pctStr, zStr, row.Status));
			}

			Console.WriteLine();
			return 0;
		}

		private static int SetLotTransition(Dictionary<string, string> options)
		{
			if (options.Count == 0)
			{
				Console.WriteLine("Usage: set-lot-transition --enabled true|false [--interval <min>] [--scan-hours <hours>]");
				Console.WriteLine("       [--stable-window <min>] [--peak-search <min>] [--slowdown-fraction <0-1>]");
				Console.WriteLine("       [--recovery-fraction <0-1>] [--slowdown-samples <count>] [--recovery-samples <count>]");
				Console.WriteLine("       [--min-pre-samples <count>] [--min-post-samples <count>] [--min-fpm <value>]");
				return 1;
			}

			var provider = new CollectorSettingsProvider();
			var settings = provider.Load();
			bool changed = false;

			if (options.TryGetValue("enabled", out var enabledRaw) && bool.TryParse(enabledRaw, out var enabled))
			{
				settings.EnableLotTransitionDetection = enabled;
				Console.WriteLine($"  EnableLotTransitionDetection = {enabled}");
				changed = true;
			}

			if (TryGetIntOption(options, "interval", 1, out var interval))
			{
				settings.LotTransitionEvalIntervalMinutes = interval;
				Console.WriteLine($"  LotTransitionEvalIntervalMinutes = {interval}");
				changed = true;
			}

			if (TryGetIntOption(options, "scan-hours", 1, out var scanHours))
			{
				settings.LotTransitionScanWindowHours = scanHours;
				Console.WriteLine($"  LotTransitionScanWindowHours = {scanHours}");
				changed = true;
			}

			if (TryGetIntOption(options, "stable-window", 1, out var stableWindow))
			{
				settings.LotTransitionStableWindowMinutes = stableWindow;
				Console.WriteLine($"  LotTransitionStableWindowMinutes = {stableWindow}");
				changed = true;
			}

			if (TryGetIntOption(options, "peak-search", 1, out var peakSearch))
			{
				settings.LotTransitionPeakSearchMinutes = peakSearch;
				Console.WriteLine($"  LotTransitionPeakSearchMinutes = {peakSearch}");
				changed = true;
			}

			if (TryGetFractionOption(options, "slowdown-fraction", out var slowdownFraction))
			{
				settings.LotTransitionSlowdownFraction = slowdownFraction;
				Console.WriteLine($"  LotTransitionSlowdownFraction = {slowdownFraction}");
				changed = true;
			}

			if (TryGetFractionOption(options, "recovery-fraction", out var recoveryFraction))
			{
				settings.LotTransitionRecoveryFraction = recoveryFraction;
				Console.WriteLine($"  LotTransitionRecoveryFraction = {recoveryFraction}");
				changed = true;
			}

			if (TryGetIntOption(options, "slowdown-samples", 1, out var slowdownSamples))
			{
				settings.LotTransitionConsecutiveSamplesForSlowdown = slowdownSamples;
				Console.WriteLine($"  LotTransitionConsecutiveSamplesForSlowdown = {slowdownSamples}");
				changed = true;
			}

			if (TryGetIntOption(options, "recovery-samples", 1, out var recoverySamples))
			{
				settings.LotTransitionRecoveryConsecutiveSamples = recoverySamples;
				Console.WriteLine($"  LotTransitionRecoveryConsecutiveSamples = {recoverySamples}");
				changed = true;
			}

			if (TryGetIntOption(options, "min-pre-samples", 1, out var minPreSamples))
			{
				settings.LotTransitionMinPreStableSamples = minPreSamples;
				Console.WriteLine($"  LotTransitionMinPreStableSamples = {minPreSamples}");
				changed = true;
			}

			if (TryGetIntOption(options, "min-post-samples", 1, out var minPostSamples))
			{
				settings.LotTransitionMinPostStableSamples = minPostSamples;
				Console.WriteLine($"  LotTransitionMinPostStableSamples = {minPostSamples}");
				changed = true;
			}

			if (options.TryGetValue("min-fpm", out var minFpmRaw) &&
				double.TryParse(minFpmRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out var minFpm) &&
				minFpm > 0)
			{
				settings.LotTransitionMinFpmForBaseline = minFpm;
				Console.WriteLine($"  LotTransitionMinFpmForBaseline = {minFpm}");
				changed = true;
			}

			if (!changed)
			{
				Console.WriteLine("No valid options provided. Run 'set-lot-transition' without arguments for usage.");
				return 1;
			}

			provider.Save(settings);
			Console.WriteLine("Lot transition detection settings updated. Restart the service for changes to affect the background loop.");
			return 0;
		}

		private static int SetMachineEvents(Dictionary<string, string> options)
		{
			if (options.Count == 0)
			{
				Console.WriteLine("Usage: set-machine-events --enabled true|false [--interval <min>] [--scan-hours <hours>]");
				Console.WriteLine("       [--downtime-max-availability <0-1>] [--slowdown-max-throughput <0-1>]");
				Console.WriteLine("       [--slowdown-min-availability <0-1>] [--slowdown-min-fpm <value>]");
				Console.WriteLine("       [--min-duration <min>] [--merge-gap <min>] [--exclude-lot-transitions true|false]");
				return 1;
			}

			var provider = new CollectorSettingsProvider();
			var settings = provider.Load();
			bool changed = false;

			if (options.TryGetValue("enabled", out var enabledRaw) && bool.TryParse(enabledRaw, out var enabled))
			{
				settings.EnableMachineEventDetection = enabled;
				Console.WriteLine($"  EnableMachineEventDetection = {enabled}");
				changed = true;
			}

			if (TryGetIntOption(options, "interval", 1, out var interval))
			{
				settings.MachineEventEvalIntervalMinutes = interval;
				Console.WriteLine($"  MachineEventEvalIntervalMinutes = {interval}");
				changed = true;
			}

			if (TryGetIntOption(options, "scan-hours", 1, out var scanHours))
			{
				settings.MachineEventScanWindowHours = scanHours;
				Console.WriteLine($"  MachineEventScanWindowHours = {scanHours}");
				changed = true;
			}

			if (TryGetZeroToOneOption(options, "downtime-max-availability", out var downtimeMaxAvailability))
			{
				settings.MachineEventDowntimeMaxAvailabilityRatio = downtimeMaxAvailability;
				Console.WriteLine($"  MachineEventDowntimeMaxAvailabilityRatio = {downtimeMaxAvailability}");
				changed = true;
			}

			if (TryGetFractionOption(options, "slowdown-max-throughput", out var slowdownMaxThroughput))
			{
				settings.MachineEventSlowdownMaxThroughputRatio = slowdownMaxThroughput;
				Console.WriteLine($"  MachineEventSlowdownMaxThroughputRatio = {slowdownMaxThroughput}");
				changed = true;
			}

			if (TryGetZeroToOneOption(options, "slowdown-min-availability", out var slowdownMinAvailability))
			{
				settings.MachineEventSlowdownMinAvailabilityRatio = slowdownMinAvailability;
				Console.WriteLine($"  MachineEventSlowdownMinAvailabilityRatio = {slowdownMinAvailability}");
				changed = true;
			}

			if (options.TryGetValue("slowdown-min-fpm", out var minFpmRaw) &&
				double.TryParse(minFpmRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out var minFpm) && minFpm >= 0)
			{
				settings.MachineEventSlowdownMinTotalFpm = minFpm;
				Console.WriteLine($"  MachineEventSlowdownMinTotalFpm = {minFpm}");
				changed = true;
			}

			if (TryGetIntOption(options, "min-duration", 1, out var minDuration))
			{
				settings.MachineEventMinDurationMinutes = minDuration;
				Console.WriteLine($"  MachineEventMinDurationMinutes = {minDuration}");
				changed = true;
			}

			if (TryGetIntOption(options, "merge-gap", 0, out var mergeGap))
			{
				settings.MachineEventMergeGapMinutes = mergeGap;
				Console.WriteLine($"  MachineEventMergeGapMinutes = {mergeGap}");
				changed = true;
			}

			if (options.TryGetValue("exclude-lot-transitions", out var excludeRaw) && bool.TryParse(excludeRaw, out var exclude))
			{
				settings.MachineEventExcludeLotTransitions = exclude;
				Console.WriteLine($"  MachineEventExcludeLotTransitions = {exclude}");
				changed = true;
			}

			if (!changed)
			{
				Console.WriteLine("No valid options provided. Run 'set-machine-events' without arguments for usage.");
				return 1;
			}

			provider.Save(settings);
			Console.WriteLine("Machine event detection settings updated. Restart the service for changes to affect the background loop.");
			return 0;
		}

		private static bool TryGetIntOption(Dictionary<string, string> options, string key, int minimum, out int value)
		{
			value = 0;
			return options.TryGetValue(key, out var raw)
				&& int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)
				&& value >= minimum;
		}

		private static bool TryGetFractionOption(Dictionary<string, string> options, string key, out double value)
		{
			value = 0;
			return options.TryGetValue(key, out var raw)
				&& double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
				&& value > 0
				&& value < 1;
		}

		private static bool TryGetZeroToOneOption(Dictionary<string, string> options, string key, out double value)
		{
			value = 0;
			return options.TryGetValue(key, out var raw)
				&& double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
				&& value >= 0
				&& value <= 1;
		}

		private static int TestAlarm(Dictionary<string, string> options)
		{
			var provider = new CollectorSettingsProvider();
			var runtimeSettings = provider.Load();
			var config = new CollectorConfig(runtimeSettings);

			string type = "both";
			if (options.TryGetValue("type", out var t) && !string.IsNullOrWhiteSpace(t))
				type = t.Trim().ToLowerInvariant();

			string severity = "Low";
			if (options.TryGetValue("severity", out var sev) && !string.IsNullOrWhiteSpace(sev))
			{
				var s = sev.Trim();
				if (s.Equals("medium", StringComparison.OrdinalIgnoreCase)) severity = "Medium";
				else if (s.Equals("high", StringComparison.OrdinalIgnoreCase)) severity = "High";
			}

			Console.WriteLine($"Connecting to Sizer API at {config.SizerHost}:{config.SizerPort} ...");

			var sink = new SizerAlarmSink(config.SizerHost, config.SizerPort, config.SendTimeoutSec);
			var ts = DateTime.Now.ToString("HH:mm:ss");
			bool anyFailed = false;

			if (type == "grade" || type == "both")
			{
				var title = "TEST Grade Alarm";
				var details = string.Format("[{0}] Test grade anomaly alarm ({1} priority). If you see this on the Sizer screen, alarm delivery is working.", ts, severity);
				Console.WriteLine($"  Sending grade test alarm ({severity}) ...");
				try
				{
					sink.SendTestAlarmAsync(title, details, severity, CancellationToken.None).GetAwaiter().GetResult();
					Console.WriteLine("  Grade test alarm delivered successfully.");
				}
				catch (Exception ex)
				{
					Console.WriteLine($"  Grade test alarm FAILED: {ex.Message}");
					anyFailed = true;
				}
			}

			if (type == "size" || type == "both")
			{
				var title = "TEST Size Alarm";
				var details = string.Format("[{0}] Test size anomaly alarm ({1} priority). If you see this on the Sizer screen, alarm delivery is working.", ts, severity);
				Console.WriteLine($"  Sending size test alarm ({severity}) ...");
				try
				{
					sink.SendTestAlarmAsync(title, details, severity, CancellationToken.None).GetAwaiter().GetResult();
					Console.WriteLine("  Size test alarm delivered successfully.");
				}
				catch (Exception ex)
				{
					Console.WriteLine($"  Size test alarm FAILED: {ex.Message}");
					anyFailed = true;
				}
			}

			if (type != "grade" && type != "size" && type != "both")
			{
				Console.WriteLine("Invalid --type. Use: grade, size, or both (default).");
				Console.WriteLine("Usage: test-alarm [--type grade|size|both] [--severity low|medium|high]");
				return 1;
			}

			return anyFailed ? 1 : 0;
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
			Console.WriteLine("  SizerDataCollector.Service.exe set-anomaly --enabled true|false");
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
			Console.WriteLine("  SizerDataCollector.Service.exe db list-views   [--include-legacy]");
			Console.WriteLine("  SizerDataCollector.Service.exe db list-caggs   [--include-legacy]");
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
			Console.WriteLine("  SizerDataCollector.Service.exe shift list                  --serial <sn>");
			Console.WriteLine("  SizerDataCollector.Service.exe shift add                   --serial <sn> --name <shift> --start <HH:mm> --end <HH:mm>");
			Console.WriteLine("      [--tz <IANA zone>] [--dow Mon-Fri|Mon,Wed,Fri|all] [--effective-from <yyyy-MM-dd>] [--effective-to <yyyy-MM-dd>] [--active true|false]");
			Console.WriteLine("  SizerDataCollector.Service.exe shift update                --serial <sn> --name <shift> [--start <HH:mm>] [--end <HH:mm>] [--tz <IANA zone>]");
			Console.WriteLine("  SizerDataCollector.Service.exe shift remove                --serial <sn> --name <shift>");
			Console.WriteLine("  SizerDataCollector.Service.exe shift show                  --serial <sn> [--day <yyyy-MM-dd>]");
			Console.WriteLine();
			Console.WriteLine("Grade anomaly detection:");
			Console.WriteLine("  SizerDataCollector.Service.exe set-anomaly --enabled true|false [--window <min>] [--z-gate <val>]");
			Console.WriteLine("      [--band-low-min <share-pts>] [--band-low-max <share-pts>] [--band-medium-max <share-pts>]");
			Console.WriteLine("      [--cooldown <sec>] [--recycle-key <name>] [--min-lane-fpm <val>]");
			Console.WriteLine("      [--min-peer-lane-fpm <val>] [--min-peer-lanes <count>] [--consecutive-windows <count>]");
			Console.WriteLine("      [--llm true|false] [--llm-endpoint <url>]");
			Console.WriteLine("  SizerDataCollector.Service.exe set-sizer-alarm --enabled true|false");
			Console.WriteLine("  SizerDataCollector.Service.exe replay-anomaly --serial <sn> --from <date> --to <date> [--persist] [--diag] [--diag-lane <N>]");
			Console.WriteLine("  SizerDataCollector.Service.exe anomaly offenders --serial <sn> [--type grade|size|both] [--hours <h>]");
			Console.WriteLine("  SizerDataCollector.Service.exe anomaly impact --serial <sn> [--type grade|size|both] [--hours <h>]");
			Console.WriteLine("  SizerDataCollector.Service.exe anomaly impact-summary --serial <sn> [--type grade|size|both] [--hours <h>]");
			Console.WriteLine("  SizerDataCollector.Service.exe anomaly tuning-compare --serial <sn> [--type grade|size|both]");
			Console.WriteLine("      --baseline-from <date> --baseline-to <date> --candidate-from <date> --candidate-to <date>");
			Console.WriteLine();
			Console.WriteLine("Size anomaly detection:");
			Console.WriteLine("  SizerDataCollector.Service.exe set-size-anomaly --enabled true|false [--interval <min>]");
			Console.WriteLine("      [--window <hours>] [--z-gate <val>] [--pct-dev-min <val>] [--cooldown <min>]");
			Console.WriteLine("      [--sizer-alarm true|false]");
			Console.WriteLine("  SizerDataCollector.Service.exe size-health --serial <sn> [--hours <h>]");
			Console.WriteLine("  SizerDataCollector.Service.exe size-health --serial <sn> --from <date> --to <date>");
			Console.WriteLine();
			Console.WriteLine("Lot transition throughput detection:");
			Console.WriteLine("  SizerDataCollector.Service.exe set-lot-transition --enabled true|false [--interval <min>]");
			Console.WriteLine("      [--scan-hours <hours>] [--stable-window <min>] [--peak-search <min>]");
			Console.WriteLine("      [--slowdown-fraction <0-1>] [--recovery-fraction <0-1>] [--min-fpm <value>]");
			Console.WriteLine("  SizerDataCollector.Service.exe lot-transition scan --serial <sn> [--hours <h> | --day <yyyy-MM-dd> | --month <yyyy-MM> | --year <yyyy>]");
			Console.WriteLine("  SizerDataCollector.Service.exe lot-transition list --serial <sn> [--hours <h> | --day <yyyy-MM-dd> | --month <yyyy-MM> | --year <yyyy>] [--format csv]");
			Console.WriteLine("  SizerDataCollector.Service.exe lot-transition export --serial <sn> [same window options]");
			Console.WriteLine();
			Console.WriteLine("Machine downtime/slowdown event detection:");
			Console.WriteLine("  SizerDataCollector.Service.exe set-machine-events --enabled true|false [--interval <min>] [--scan-hours <hours>]");
			Console.WriteLine("      [--downtime-max-availability <0-1>] [--slowdown-max-throughput <0-1>] [--slowdown-min-availability <0-1>]");
			Console.WriteLine("      [--slowdown-min-fpm <value>] [--min-duration <min>] [--merge-gap <min>] [--exclude-lot-transitions true|false]");
			Console.WriteLine("  SizerDataCollector.Service.exe machine-event scan --serial <sn> [--type downtime|slowdown|both] [--hours <h> | --day <yyyy-MM-dd> | --month <yyyy-MM> | --year <yyyy>] [--no-persist]");
			Console.WriteLine("  SizerDataCollector.Service.exe downtime list --serial <sn> [--hours <h> | --day <yyyy-MM-dd> | --month <yyyy-MM> | --year <yyyy>] [--format csv]");
			Console.WriteLine("  SizerDataCollector.Service.exe slowdown list --serial <sn> [--hours <h> | --day <yyyy-MM-dd> | --month <yyyy-MM> | --year <yyyy>] [--format csv]");
			Console.WriteLine();
			Console.WriteLine("Alarm testing:");
			Console.WriteLine("  SizerDataCollector.Service.exe test-alarm [--type grade|size|both] [--severity low|medium|high]");
		}
	}
}
