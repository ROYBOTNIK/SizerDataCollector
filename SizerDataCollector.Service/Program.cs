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
				case "set-anomaly":
					return SetAnomaly(options);
				case "set-sizer-alarm":
					return SetSizerAlarm(options);
				case "replay-anomaly":
					return ReplayAnomaly(options);
				case "set-size-anomaly":
					return SetSizeAnomaly(options);
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
				Console.WriteLine("       [--band-low-min <pct>] [--band-low-max <pct>] [--band-medium-max <pct>]");
				Console.WriteLine("       [--cooldown <sec>] [--recycle-key <name>]");
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
				Console.WriteLine("Usage: replay-anomaly --serial <sn> --from <yyyy-MM-dd> --to <yyyy-MM-dd> [--persist]");
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
			Console.WriteLine();

			var allEvents = new List<AnomalyEvent>();
			long previousBatch = 0;

			foreach (var row in rows)
			{
				if (row.BatchRecordId != previousBatch && previousBatch != 0)
				{
					Console.WriteLine($"  [Batch change at {row.Ts:HH:mm:ss}: {previousBatch} -> {row.BatchRecordId}, detector reset]");
					detector.Reset();
				}
				previousBatch = row.BatchRecordId;

				var matrix = GradeMatrixParser.Parse(row.ValueJson);
				if (matrix == null) continue;

				var events = detector.Update(matrix, row.Ts, row.SerialNo, (int)row.BatchRecordId);
				foreach (var evt in events)
				{
					evt.ModelVersion = "replay-v1";
					evt.DeliveredTo = persist ? "replay" : "console";
					allEvents.Add(evt);
					Console.WriteLine($"  {evt.EventTs:yyyy-MM-dd HH:mm:ss} [{evt.Severity,6}] {evt.AlarmDetails}");
				}
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
			Console.WriteLine();
			Console.WriteLine("Grade anomaly detection:");
			Console.WriteLine("  SizerDataCollector.Service.exe set-anomaly --enabled true|false [--window <min>] [--z-gate <val>]");
			Console.WriteLine("      [--band-low-min <pct>] [--band-low-max <pct>] [--band-medium-max <pct>]");
			Console.WriteLine("      [--cooldown <sec>] [--recycle-key <name>] [--llm true|false] [--llm-endpoint <url>]");
			Console.WriteLine("  SizerDataCollector.Service.exe set-sizer-alarm --enabled true|false");
			Console.WriteLine("  SizerDataCollector.Service.exe replay-anomaly --serial <sn> --from <date> --to <date> [--persist]");
			Console.WriteLine();
			Console.WriteLine("Size anomaly detection:");
			Console.WriteLine("  SizerDataCollector.Service.exe set-size-anomaly --enabled true|false [--interval <min>]");
			Console.WriteLine("      [--window <hours>] [--z-gate <val>] [--pct-dev-min <val>] [--cooldown <min>]");
			Console.WriteLine("      [--sizer-alarm true|false]");
			Console.WriteLine("  SizerDataCollector.Service.exe size-health --serial <sn> [--hours <h>]");
			Console.WriteLine("  SizerDataCollector.Service.exe size-health --serial <sn> --from <date> --to <date>");
			Console.WriteLine();
			Console.WriteLine("Alarm testing:");
			Console.WriteLine("  SizerDataCollector.Service.exe test-alarm [--type grade|size|both] [--severity low|medium|high]");
		}
	}
}
