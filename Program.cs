using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Npgsql;
using SizerDataCollector.Core.Collector;
using SizerDataCollector.Core.Config;
using SizerDataCollector.Core.Db;
using SizerDataCollector.Core.Logging;
using SizerDataCollector.Core.Sizer;
using SizerDataCollector.Core.Sizer.Discovery;

namespace SizerDataCollector
{
    internal class Program
    {
        static int Main(string[] args)
        {
            try
            {
                // Legacy harness entry-points for backwards compatibility:
                var legacyMode = ParseMode(args);
                if (legacyMode == HarnessMode.Probe || legacyMode == HarnessMode.SinglePoll)
                {
                    return RunLegacyHarness(legacyMode, args);
                }

                if (args == null || args.Length == 0)
                {
                    ShowUsage();
                    return 1;
                }

                var command = args[0]?.Trim().ToLowerInvariant();
                var tail = args.Skip(1).ToArray();

                switch (command)
                {
                    case "config":
                        return RunConfigCommand(tail);
                    case "metrics":
                        return RunMetricsCommand(tail);
                    case "db":
                        return RunDbCommand(tail);
                    case "collector":
                        return RunCollectorCommand(tail);
                    case "discovery":
                        return RunDiscoveryCommand(tail);
                    case "commissioning":
                        return RunCommissioningCommand(tail);
                    case "grades":
                        return RunGradesCommand(tail);
                    case "targets":
                        return RunTargetsCommand(tail);
                    case "help":
                    case "--help":
                    case "-h":
                        ShowUsage();
                        return 0;
                    default:
                        Console.Error.WriteLine("ERROR: Unknown command '" + command + "'.");
                        ShowUsage();
                        return 1;
                }
            }
            catch (Exception ex)
            {
                Logger.Log("SizerDataCollector CLI failed.", ex);
                Console.Error.WriteLine("ERROR: " + ex.Message);
                return 1;
            }
        }

        #region Legacy harness (probe / single-poll)

        private static int RunLegacyHarness(HarnessMode mode, string[] args)
        {
            var settingsProvider = new CollectorSettingsProvider();
            var runtimeSettings = settingsProvider.Load();
            var config = new CollectorConfig(runtimeSettings);
            LogEffectiveSettings(runtimeSettings);

            var force = HasForceFlag(args);

            Logger.Log(string.Format("SizerDataCollector legacy harness starting in '{0}' mode...", mode));

            switch (mode)
            {
                case HarnessMode.Probe:
                    RunProbe(config);
                    break;
                case HarnessMode.SinglePoll:
                    RunCollectorLoopAsync(config, runtimeSettings, force).GetAwaiter().GetResult();
                    break;
                default:
                    ShowUsage();
                    return 1;
            }

            Logger.Log("Operation completed successfully.");
            return 0;
        }

        private static HarnessMode ParseMode(string[] args)
        {
            if (args == null || args.Length == 0)
            {
                return HarnessMode.Unknown;
            }

            var mode = args[0]?.Trim().ToLowerInvariant();

            if (mode == "probe")
            {
                return HarnessMode.Probe;
            }

            if (mode == "single-poll")
            {
                return HarnessMode.SinglePoll;
            }

            return HarnessMode.Unknown;
        }

        private static bool HasForceFlag(string[] args)
        {
            if (args == null) return false;
            foreach (var arg in args)
            {
                if (string.Equals(arg?.Trim(), "--force", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static void RunProbe(CollectorConfig config)
        {
            Logger.Log("Running schema and Sizer API probe...");
            DatabaseTester.TestAndInitialize(config);
            SizerClientTester.TestSizerConnection(config);
        }

        #endregion

        #region CLI: config

        private static int RunConfigCommand(string[] args)
        {
            if (args == null || args.Length == 0)
            {
                Console.WriteLine("Usage: SizerDataCollector config [show|set] [options]");
                return 1;
            }

            var sub = args[0]?.Trim().ToLowerInvariant();
            var tail = args.Skip(1).ToArray();

            switch (sub)
            {
                case "show":
                    return RunConfigShow();
                case "set":
                    return RunConfigSet(tail);
                default:
                    Console.Error.WriteLine("ERROR: Unknown config subcommand '" + sub + "'.");
                    return 1;
            }
        }

        private static int RunConfigShow()
        {
            var provider = new CollectorSettingsProvider();
            var settings = provider.Load();

            Console.WriteLine("STATUS=OK");
            Console.WriteLine("SIZER_HOST=" + settings.SizerHost);
            Console.WriteLine("SIZER_PORT=" + settings.SizerPort);
            Console.WriteLine("OPEN_TIMEOUT_SEC=" + settings.OpenTimeoutSec);
            Console.WriteLine("SEND_TIMEOUT_SEC=" + settings.SendTimeoutSec);
            Console.WriteLine("RECEIVE_TIMEOUT_SEC=" + settings.ReceiveTimeoutSec);
            Console.WriteLine("ENABLE_INGESTION=" + settings.EnableIngestion);
            Console.WriteLine("POLL_INTERVAL_SECONDS=" + settings.PollIntervalSeconds);
            Console.WriteLine("INITIAL_BACKOFF_SECONDS=" + settings.InitialBackoffSeconds);
            Console.WriteLine("MAX_BACKOFF_SECONDS=" + settings.MaxBackoffSeconds);
            Console.WriteLine("SHARED_DATA_DIRECTORY=" + settings.SharedDataDirectory);
            Console.WriteLine("TIMESCALE_CONNECTION_STRING_CONFIGURED=" + !string.IsNullOrWhiteSpace(settings.TimescaleConnectionString));

            var enabledMetrics = settings.EnabledMetrics ?? new List<string>();
            Console.WriteLine("ENABLED_METRICS=" + string.Join(",", enabledMetrics));

            return 0;
        }

        private static int RunConfigSet(string[] args)
        {
            var options = ParseKeyValueArgs(args);
            if (options.Count == 0)
            {
                Console.Error.WriteLine("ERROR: No configuration keys supplied.");
                return 1;
            }

            var provider = new CollectorSettingsProvider();
            var settings = provider.Load();

            var changed = false;
            var hasError = false;

            string value;

            if (options.TryGetValue("sizer-host", out value))
            {
                settings.SizerHost = value;
                changed = true;
            }

            if (options.TryGetValue("sizer-port", out value))
            {
                int port;
                if (!int.TryParse(value, out port))
                {
                    Console.Error.WriteLine("ERROR: sizer-port must be an integer.");
                    hasError = true;
                }
                else
                {
                    settings.SizerPort = port;
                    changed = true;
                }
            }

            if (options.TryGetValue("open-timeout-sec", out value))
            {
                int parsed;
                if (!int.TryParse(value, out parsed))
                {
                    Console.Error.WriteLine("ERROR: open-timeout-sec must be an integer.");
                    hasError = true;
                }
                else
                {
                    settings.OpenTimeoutSec = parsed;
                    changed = true;
                }
            }

            if (options.TryGetValue("send-timeout-sec", out value))
            {
                int parsed;
                if (!int.TryParse(value, out parsed))
                {
                    Console.Error.WriteLine("ERROR: send-timeout-sec must be an integer.");
                    hasError = true;
                }
                else
                {
                    settings.SendTimeoutSec = parsed;
                    changed = true;
                }
            }

            if (options.TryGetValue("receive-timeout-sec", out value))
            {
                int parsed;
                if (!int.TryParse(value, out parsed))
                {
                    Console.Error.WriteLine("ERROR: receive-timeout-sec must be an integer.");
                    hasError = true;
                }
                else
                {
                    settings.ReceiveTimeoutSec = parsed;
                    changed = true;
                }
            }

            if (options.TryGetValue("enable-ingestion", out value))
            {
                bool flag;
                if (!TryParseBool(value, out flag))
                {
                    Console.Error.WriteLine("ERROR: enable-ingestion must be true/false/1/0.");
                    hasError = true;
                }
                else
                {
                    settings.EnableIngestion = flag;
                    changed = true;
                }
            }

            if (options.TryGetValue("poll-interval-seconds", out value))
            {
                int parsed;
                if (!int.TryParse(value, out parsed))
                {
                    Console.Error.WriteLine("ERROR: poll-interval-seconds must be an integer.");
                    hasError = true;
                }
                else
                {
                    settings.PollIntervalSeconds = parsed;
                    changed = true;
                }
            }

            if (options.TryGetValue("initial-backoff-seconds", out value))
            {
                int parsed;
                if (!int.TryParse(value, out parsed))
                {
                    Console.Error.WriteLine("ERROR: initial-backoff-seconds must be an integer.");
                    hasError = true;
                }
                else
                {
                    settings.InitialBackoffSeconds = parsed;
                    changed = true;
                }
            }

            if (options.TryGetValue("max-backoff-seconds", out value))
            {
                int parsed;
                if (!int.TryParse(value, out parsed))
                {
                    Console.Error.WriteLine("ERROR: max-backoff-seconds must be an integer.");
                    hasError = true;
                }
                else
                {
                    settings.MaxBackoffSeconds = parsed;
                    changed = true;
                }
            }

            if (options.TryGetValue("shared-data-directory", out value))
            {
                settings.SharedDataDirectory = value;
                changed = true;
            }

            if (options.TryGetValue("timescale-connection-string", out value))
            {
                settings.TimescaleConnectionString = value;
                changed = true;
            }

            if (options.TryGetValue("enabled-metrics", out value) || options.TryGetValue("metrics", out value))
            {
                var metrics = (value ?? string.Empty)
                    .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(m => m.Trim())
                    .Where(m => !string.IsNullOrWhiteSpace(m))
                    .ToList();

                settings.EnabledMetrics = metrics;
                changed = true;
            }

            if (hasError)
            {
                Console.Error.WriteLine("ERROR: One or more configuration values were invalid; no changes were saved.");
                return 1;
            }

            if (!changed)
            {
                Console.Error.WriteLine("ERROR: No recognized configuration keys were supplied.");
                return 1;
            }

            provider.Save(settings);
            Console.WriteLine("STATUS=OK");
            Console.WriteLine("MESSAGE=Runtime settings updated. Restart any running services/agents to apply.");
            return 0;
        }

        #endregion

        #region CLI: metrics

        private static int RunMetricsCommand(string[] args)
        {
            if (args == null || args.Length == 0)
            {
                Console.WriteLine("Usage: SizerDataCollector metrics [list|list-supported|set] [options]");
                return 1;
            }

            var sub = args[0]?.Trim().ToLowerInvariant();
            var tail = args.Skip(1).ToArray();

            switch (sub)
            {
                case "list":
                case "list-enabled":
                    return RunMetricsListEnabled();
                case "list-supported":
                    return RunMetricsListSupported();
                case "set":
                    return RunMetricsSet(tail);
                default:
                    Console.Error.WriteLine("ERROR: Unknown metrics subcommand '" + sub + "'.");
                    return 1;
            }
        }

        private static int RunMetricsListEnabled()
        {
            var provider = new CollectorSettingsProvider();
            var settings = provider.Load();

            Console.WriteLine("STATUS=OK");

            var enabled = settings.EnabledMetrics ?? new List<string>();
            foreach (var metric in enabled)
            {
                Console.WriteLine("ENABLED_METRIC=" + metric);
            }

            return 0;
        }

        private static int RunMetricsListSupported()
        {
            Console.WriteLine("STATUS=OK");

            foreach (var metric in SizerClient.SupportedMetricKeys.OrderBy(m => m, StringComparer.OrdinalIgnoreCase))
            {
                Console.WriteLine("SUPPORTED_METRIC=" + metric);
            }

            return 0;
        }

        private static int RunMetricsSet(string[] args)
        {
            var options = ParseKeyValueArgs(args);
            string value;
            if (!options.TryGetValue("metrics", out value) && !options.TryGetValue("enabled-metrics", out value))
            {
                Console.Error.WriteLine("ERROR: metrics set requires --metrics=metric1,metric2,...");
                return 1;
            }

            var provider = new CollectorSettingsProvider();
            var settings = provider.Load();

            var metrics = (value ?? string.Empty)
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(m => m.Trim())
                .Where(m => !string.IsNullOrWhiteSpace(m))
                .ToList();

            settings.EnabledMetrics = metrics;
            provider.Save(settings);

            Console.WriteLine("STATUS=OK");
            Console.WriteLine("MESSAGE=Enabled metrics updated.");
            return 0;
        }

        #endregion

        #region CLI: db

        private static int RunDbCommand(string[] args)
        {
            if (args == null || args.Length == 0)
            {
                Console.WriteLine("Usage: SizerDataCollector db [health|migrate|apply-sql] [options]");
                return 1;
            }

            var sub = args[0]?.Trim().ToLowerInvariant();
            var tail = args.Skip(1).ToArray();

            switch (sub)
            {
                case "health":
                    return RunDbHealth(tail);
                case "migrate":
                    return RunDbMigrate(tail);
                case "apply-sql":
                    return RunDbApplySql(tail);
                default:
                    Console.Error.WriteLine("ERROR: Unknown db subcommand '" + sub + "'.");
                    return 1;
            }
        }

        private static int RunDbHealth(string[] args)
        {
            var provider = new CollectorSettingsProvider();
            var settings = provider.Load();

            if (string.IsNullOrWhiteSpace(settings.TimescaleConnectionString))
            {
                Console.Error.WriteLine("ERROR: TimescaleDb connection string is empty. Configure it via:");
                Console.Error.WriteLine("  SizerDataCollector config set --timescale-connection-string=\"Host=...;Port=...;Username=...;Password=...;Database=...;\"");
                return 2;
            }

            var inspector = new DbIntrospector(settings.TimescaleConnectionString);
            var report = inspector.RunAsync(CancellationToken.None).GetAwaiter().GetResult();

            var options = ParseKeyValueArgs(args);
            string format;
            options.TryGetValue("format", out format);
            if (string.IsNullOrWhiteSpace(format))
            {
                format = "json";
            }

            if (string.Equals(format, "json", StringComparison.OrdinalIgnoreCase))
            {
                var json = JsonConvert.SerializeObject(report, Formatting.Indented);
                Console.WriteLine(json);
            }
            else
            {
                Console.WriteLine("STATUS=" + (report.Healthy ? "OK" : "UNHEALTHY"));
                Console.WriteLine("CAN_CONNECT=" + report.CanConnect);
                Console.WriteLine("TIMESCALE_INSTALLED=" + report.TimescaleInstalled);
                Console.WriteLine("APPLIED_MIGRATIONS=" + report.AppliedMigrationsCount);
                Console.WriteLine("MISSING_TABLES=" + string.Join(",", report.MissingTables ?? new List<string>()));
                Console.WriteLine("MISSING_FUNCTIONS=" + string.Join(",", report.MissingFunctions ?? new List<string>()));
                Console.WriteLine("MISSING_CAGGS=" + string.Join(",", report.MissingContinuousAggregates ?? new List<string>()));
                Console.WriteLine("MISSING_POLICIES=" + string.Join(",", report.MissingPolicies ?? new List<string>()));
                Console.WriteLine("SEED_PRESENT=" + report.SeedPresent);
            }

            if (report.Exception != null)
            {
                return 2;
            }

            return report.Healthy ? 0 : 2;
        }

        private static int RunDbMigrate(string[] args)
        {
            var options = ParseKeyValueArgs(args);

            var dryRun = options.ContainsKey("dry-run");
            var allowDestructive = options.ContainsKey("allow-destructive");

            var provider = new CollectorSettingsProvider();
            var settings = provider.Load();

            if (string.IsNullOrWhiteSpace(settings.TimescaleConnectionString))
            {
                Console.Error.WriteLine("ERROR: TimescaleDb connection string is empty. Configure it via:");
                Console.Error.WriteLine("  SizerDataCollector config set --timescale-connection-string=\"Host=...;Port=...;Username=...;Password=...;Database=...;\"");
                return 2;
            }

            var bootstrapper = new DbBootstrapper(settings.TimescaleConnectionString);

            if (dryRun)
            {
                var plan = bootstrapper.PlanAsync(CancellationToken.None).GetAwaiter().GetResult();

                Console.WriteLine("STATUS=OK");
                foreach (var item in plan)
                {
                    var line = string.Format(
                        "PLAN version={0} script={1} status={2} destructive={3} applied_at={4}",
                        item.Version,
                        item.ScriptName,
                        item.Status,
                        item.IsPotentiallyDestructive,
                        item.AppliedAt.HasValue ? item.AppliedAt.Value.ToString("u") : string.Empty);
                    Console.WriteLine(line);
                }

                return 0;
            }

            var result = bootstrapper.BootstrapAsync(allowDestructive, false, CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            foreach (var m in result.Migrations)
            {
                var message = (m.Message ?? string.Empty).Replace(Environment.NewLine, " ");
                var line = string.Format(
                    "MIGRATION version={0} script={1} status={2} message={3}",
                    m.Version,
                    m.ScriptName,
                    m.Status,
                    message);
                Console.WriteLine(line);
            }

            Console.WriteLine(
                "RESULT success={0} applied={1} skipped={2} checksum_mismatch={3} failed={4}",
                result.Success.ToString().ToLowerInvariant(),
                CountByStatus(result, MigrationStatus.Applied),
                CountByStatus(result, MigrationStatus.Skipped),
                CountByStatus(result, MigrationStatus.ChecksumMismatch),
                CountByStatus(result, MigrationStatus.Failed));

            return result.Success ? 0 : 2;
        }

        private static int RunDbApplySql(string[] args)
        {
            var options = ParseKeyValueArgs(args);

            string filePath;
            if (!options.TryGetValue("file", out filePath) || string.IsNullOrWhiteSpace(filePath))
            {
                Console.Error.WriteLine("STATUS=ERROR");
                Console.Error.WriteLine("ERROR_MESSAGE=db apply-sql requires --file=<path>.");
                return 1;
            }

            filePath = Path.GetFullPath(filePath);

            if (!File.Exists(filePath))
            {
                Console.Error.WriteLine("STATUS=ERROR");
                Console.Error.WriteLine("FILE=" + filePath);
                Console.Error.WriteLine("ERROR_MESSAGE=SQL file not found.");
                return 1;
            }

            string label;
            options.TryGetValue("label", out label);

            var dryRun = options.ContainsKey("dry-run");
            var allowDestructive = options.ContainsKey("allow-destructive");

            string sqlText;
            try
            {
                sqlText = File.ReadAllText(filePath);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("STATUS=ERROR");
                Console.Error.WriteLine("FILE=" + filePath);
                Console.Error.WriteLine("ERROR_MESSAGE=Failed to read SQL file: " + ex.Message);
                return 1;
            }

            var isDestructive = IsPotentiallyDestructiveScript(sqlText);

            if (dryRun)
            {
                Console.WriteLine("STATUS=OK");
                Console.WriteLine("FILE=" + filePath);
                if (!string.IsNullOrWhiteSpace(label))
                {
                    Console.WriteLine("LABEL=" + label);
                }
                Console.WriteLine("DRY_RUN=True");
                Console.WriteLine("POTENTIALLY_DESTRUCTIVE=" + isDestructive);

                if (isDestructive && !allowDestructive)
                {
                    Console.WriteLine("MESSAGE=Script would be treated as potentially destructive and requires --allow-destructive to execute.");
                }
                else
                {
                    Console.WriteLine("MESSAGE=Script validated (dry-run only).");
                }

                return 0;
            }

            if (isDestructive && !allowDestructive)
            {
                Console.Error.WriteLine("STATUS=ERROR");
                Console.Error.WriteLine("FILE=" + filePath);
                Console.Error.WriteLine("POTENTIALLY_DESTRUCTIVE=True");
                Console.Error.WriteLine("ERROR_MESSAGE=Script appears potentially destructive; rerun with --allow-destructive to execute.");
                return 2;
            }

            var provider2 = new CollectorSettingsProvider();
            var settings2 = provider2.Load();

            if (string.IsNullOrWhiteSpace(settings2.TimescaleConnectionString))
            {
                Console.Error.WriteLine("ERROR: TimescaleDb connection string is empty. Configure it via:");
                Console.Error.WriteLine("  SizerDataCollector config set --timescale-connection-string=\"Host=...;Port=...;Username=...;Password=...;Database=...;\"");
                return 2;
            }

            try
            {
                using (var connection = new NpgsqlConnection(settings2.TimescaleConnectionString))
                {
                    connection.Open();
                    using (var transaction = connection.BeginTransaction())
                    {
                        try
                        {
                            using (var command = new NpgsqlCommand(sqlText, connection, transaction))
                            {
                                command.CommandTimeout = 0;
                                command.ExecuteNonQuery();
                            }

                            transaction.Commit();
                        }
                        catch (Exception ex)
                        {
                            try { transaction.Rollback(); } catch { }
                            Console.Error.WriteLine("STATUS=ERROR");
                            Console.Error.WriteLine("FILE=" + filePath);
                            Console.Error.WriteLine("ERROR_MESSAGE=SQL execution failed: " + ex.Message);
                            return 2;
                        }
                    }
                }

                Console.WriteLine("STATUS=OK");
                Console.WriteLine("FILE=" + filePath);
                if (!string.IsNullOrWhiteSpace(label))
                {
                    Console.WriteLine("LABEL=" + label);
                }
                Console.WriteLine("DRY_RUN=False");
                Console.WriteLine("POTENTIALLY_DESTRUCTIVE=" + isDestructive);
                Console.WriteLine("MESSAGE=Applied SQL script successfully.");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("STATUS=ERROR");
                Console.Error.WriteLine("FILE=" + filePath);
                Console.Error.WriteLine("ERROR_MESSAGE=Failed to execute SQL script: " + ex.Message);
                return 2;
            }
        }

        #endregion

        #region CLI: collector

        private static int RunCollectorCommand(string[] args)
        {
            if (args == null || args.Length == 0)
            {
                Console.WriteLine("Usage: SizerDataCollector collector [probe|run-once|run-loop] [--force]");
                return 1;
            }

            var sub = args[0]?.Trim().ToLowerInvariant();
            var tail = args.Skip(1).ToArray();
            var force = HasForceFlag(args);

            var settingsProvider = new CollectorSettingsProvider();
            var runtimeSettings = settingsProvider.Load();
            var config = new CollectorConfig(runtimeSettings);
            LogEffectiveSettings(runtimeSettings);

            switch (sub)
            {
                case "probe":
                    RunProbe(config);
                    return 0;
                case "run-once":
                    RunCollectorOnceAsync(config, runtimeSettings, force).GetAwaiter().GetResult();
                    return 0;
                case "run-loop":
                    RunCollectorLoopAsync(config, runtimeSettings, force).GetAwaiter().GetResult();
                    return 0;
                default:
                    Console.Error.WriteLine("ERROR: Unknown collector subcommand '" + sub + "'.");
                    return 1;
            }
        }

        private static async Task RunCollectorOnceAsync(CollectorConfig config, CollectorRuntimeSettings runtimeSettings, bool force)
        {
            Logger.Log("Running collector for a single ingestion cycle...");

            var status = new CollectorStatus();
            var dataRoot = runtimeSettings?.SharedDataDirectory ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(dataRoot))
            {
                Directory.CreateDirectory(dataRoot);
            }

            var heartbeatPath = Path.Combine(dataRoot, "heartbeat.json");
            var heartbeatWriter = new HeartbeatWriter(heartbeatPath);
            var repository = new TimescaleRepository(config.TimescaleConnectionString);

            if (!force)
            {
                if (!IsCommissioningEnabled(config, runtimeSettings, status, heartbeatWriter))
                {
                    Logger.Log("Commissioning incomplete; ingestion disabled; run-once aborted.");
                    return;
                }
            }

            using (var sizerClient = new SizerClient(config))
            {
                var engine = new CollectorEngine(config, repository, sizerClient);
                await engine.RunSinglePollAsync(status, CancellationToken.None).ConfigureAwait(false);
            }

            WriteHeartbeat(status, heartbeatWriter);
        }

        private static async Task RunCollectorLoopAsync(CollectorConfig config, CollectorRuntimeSettings runtimeSettings, bool force)
        {
            Logger.Log("Running collector in continuous loop...");

            var status = new CollectorStatus();
            var dataRoot = runtimeSettings?.SharedDataDirectory ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(dataRoot))
            {
                Directory.CreateDirectory(dataRoot);
            }

            var heartbeatPath = Path.Combine(dataRoot, "heartbeat.json");
            var heartbeatWriter = new HeartbeatWriter(heartbeatPath);
            var repository = new TimescaleRepository(config.TimescaleConnectionString);

            if (!force)
            {
                if (!IsCommissioningEnabled(config, runtimeSettings, status, heartbeatWriter))
                {
                    Logger.Log("Commissioning incomplete; ingestion disabled; run-loop aborted.");
                    return;
                }
            }

            using (var sizerClient = new SizerClient(config))
            using (var cts = new CancellationTokenSource())
            {
                var engine = new CollectorEngine(config, repository, sizerClient);
                var runner = new CollectorRunner(config, engine, status, heartbeatWriter);
                await runner.RunAsync(cts.Token).ConfigureAwait(false);
            }
        }

        #endregion

        #region CLI: discovery

        private static int RunDiscoveryCommand(string[] args)
        {
            var settingsProvider = new CollectorSettingsProvider();
            var runtimeSettings = settingsProvider.Load();
            var config = new CollectorConfig(runtimeSettings);

            DiscoveryRunnerHarness.RunOnceAsync(config, CancellationToken.None).GetAwaiter().GetResult();
            return 0;
        }

        #endregion

        #region CLI: commissioning

        private static int RunCommissioningCommand(string[] args)
        {
            if (args == null || args.Length == 0)
            {
                Console.WriteLine("Usage: SizerDataCollector commissioning [status|ensure-row|mark-discovered|enable-ingestion|configure-machine|set-notes|reset] [options]");
                return 1;
            }

            var sub = args[0]?.Trim().ToLowerInvariant();
            var tail = args.Skip(1).ToArray();

            switch (sub)
            {
                case "status":
                    return RunCommissioningStatus(tail);
                case "ensure-row":
                    return RunCommissioningEnsureRow(tail);
                case "mark-discovered":
                    return RunCommissioningMarkDiscovered(tail);
                case "enable-ingestion":
                    return RunCommissioningEnableIngestion(tail);
                case "configure-machine":
                    return RunCommissioningConfigureMachine(tail);
                case "set-notes":
                    return RunCommissioningSetNotes(tail);
                case "reset":
                    return RunCommissioningReset(tail);
                default:
                    Console.Error.WriteLine("ERROR: Unknown commissioning subcommand '" + sub + "'.");
                    return 1;
            }
        }

        private static int RunCommissioningStatus(string[] args)
        {
            var options = ParseKeyValueArgs(args);
            string serial;
            if (!options.TryGetValue("serial", out serial) || string.IsNullOrWhiteSpace(serial))
            {
                Console.Error.WriteLine("ERROR: commissioning status requires --serial=<serial>.");
                return 1;
            }

            var provider = new CollectorSettingsProvider();
            var settings = provider.Load();
            if (string.IsNullOrWhiteSpace(settings.TimescaleConnectionString))
            {
                Console.Error.WriteLine("ERROR: TimescaleDb connection string is empty. Configure it via:");
                Console.Error.WriteLine("  SizerDataCollector config set --timescale-connection-string=\"Host=...;Port=...;Username=...;Password=...;Database=...;\"");
                return 2;
            }

            try
            {
                var repo = new CommissioningRepository(settings.TimescaleConnectionString);
                var introspector = new DbIntrospector(settings.TimescaleConnectionString);

                var runtimeConfig = new CollectorConfig(settings);
                Func<ISizerClient> sizerFactory = () => new SizerClient(runtimeConfig);

                var svc = new SizerDataCollector.Core.Commissioning.CommissioningService(
                    settings.TimescaleConnectionString,
                    repo,
                    introspector,
                    sizerFactory);

                var status = svc.BuildStatusAsync(serial, CancellationToken.None).GetAwaiter().GetResult();

                var thresholdsRepo = new ThresholdsRepository(settings.TimescaleConnectionString);
                var thresholds = thresholdsRepo.GetAsync(serial, CancellationToken.None).GetAwaiter().GetResult();

                var machineSettingsRepo = new MachineSettingsRepository(settings.TimescaleConnectionString);
                var machineSettings = machineSettingsRepo.GetSettingsAsync(serial, CancellationToken.None).GetAwaiter().GetResult();

                Console.WriteLine("STATUS=OK");
                Console.WriteLine("SERIAL=" + serial);
                Console.WriteLine("DB_BOOTSTRAPPED=" + status.DbBootstrapped);
                Console.WriteLine("SIZER_CONNECTED=" + status.SizerConnected);
                Console.WriteLine("THRESHOLDS_SET=" + status.ThresholdsSet);
                Console.WriteLine("MACHINE_DISCOVERED=" + status.MachineDiscovered);
                Console.WriteLine("GRADE_MAPPING_COMPLETED=" + status.GradeMappingCompleted);
                Console.WriteLine("CAN_ENABLE_INGESTION=" + status.CanEnableIngestion);

                var row = status.StoredRow;
                var ingestionEnabled = row != null && row.IngestionEnabledAt.HasValue;
                Console.WriteLine("INGESTION_ENABLED=" + ingestionEnabled);
                Console.WriteLine("DB_BOOTSTRAPPED_AT=" + FormatTimestamp(row?.DbBootstrappedAt));
                Console.WriteLine("SIZER_CONNECTED_AT=" + FormatTimestamp(row?.SizerConnectedAt));
                Console.WriteLine("MACHINE_DISCOVERED_AT=" + FormatTimestamp(row?.MachineDiscoveredAt));
                Console.WriteLine("GRADE_MAPPING_COMPLETED_AT=" + FormatTimestamp(row?.GradeMappingCompletedAt));
                Console.WriteLine("THRESHOLDS_SET_AT=" + FormatTimestamp(row?.ThresholdsSetAt));
                Console.WriteLine("INGESTION_ENABLED_AT=" + FormatTimestamp(row?.IngestionEnabledAt));

                if (thresholds != null)
                {
                    Console.WriteLine("THRESHOLDS_MIN_RPM=" + thresholds.MinRpm);
                    Console.WriteLine("THRESHOLDS_MIN_TOTAL_FPM=" + thresholds.MinTotalFpm);
                    Console.WriteLine("THRESHOLDS_UPDATED_AT=" + thresholds.UpdatedAt.ToString("u"));
                }

                if (machineSettings != null)
                {
                    Console.WriteLine("TARGET_MACHINE_SPEED=" + (machineSettings.TargetMachineSpeed.HasValue ? machineSettings.TargetMachineSpeed.Value.ToString() : string.Empty));
                    Console.WriteLine("LANE_COUNT=" + (machineSettings.LaneCount.HasValue ? machineSettings.LaneCount.Value.ToString() : string.Empty));
                    Console.WriteLine("TARGET_PERCENTAGE=" + (machineSettings.TargetPercentage.HasValue ? machineSettings.TargetPercentage.Value.ToString() : string.Empty));
                    Console.WriteLine("RECYCLE_OUTLET=" + (machineSettings.RecycleOutlet.HasValue ? machineSettings.RecycleOutlet.Value.ToString() : string.Empty));
                }

                if (status.BlockingReasons != null)
                {
                    foreach (var br in status.BlockingReasons)
                    {
                        Console.WriteLine("BLOCKING_REASON=" + br.Code + ":" + br.Message);
                    }
                }

                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("STATUS=ERROR");
                Console.Error.WriteLine("ERROR_MESSAGE=" + ex.Message);
                return 2;
            }
        }

        private static int RunCommissioningEnsureRow(string[] args)
        {
            var options = ParseKeyValueArgs(args);
            string serial;
            if (!options.TryGetValue("serial", out serial) || string.IsNullOrWhiteSpace(serial))
            {
                Console.Error.WriteLine("ERROR: commissioning ensure-row requires --serial=<serial>.");
                return 1;
            }

            var provider = new CollectorSettingsProvider();
            var settings = provider.Load();
            if (string.IsNullOrWhiteSpace(settings.TimescaleConnectionString))
            {
                Console.Error.WriteLine("ERROR: TimescaleDb connection string is empty. Configure it via:");
                Console.Error.WriteLine("  SizerDataCollector config set --timescale-connection-string=\"Host=...;Port=...;Username=...;Password=...;Database=...;\"");
                return 2;
            }

            try
            {
                var repo = new CommissioningRepository(settings.TimescaleConnectionString);
                repo.EnsureRowAsync(serial).GetAwaiter().GetResult();
                Console.WriteLine("STATUS=OK");
                Console.WriteLine("SERIAL=" + serial);
                Console.WriteLine("MESSAGE=Ensured commissioning_status row.");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("STATUS=ERROR");
                Console.Error.WriteLine("ERROR_MESSAGE=" + ex.Message);
                return 2;
            }
        }

        private static int RunCommissioningMarkDiscovered(string[] args)
        {
            var options = ParseKeyValueArgs(args);
            string serial;
            if (!options.TryGetValue("serial", out serial) || string.IsNullOrWhiteSpace(serial))
            {
                Console.Error.WriteLine("ERROR: commissioning mark-discovered requires --serial=<serial>.");
                return 1;
            }

            string tsRaw;
            var hasTimestamp = options.TryGetValue("timestamp", out tsRaw) && !string.IsNullOrWhiteSpace(tsRaw);
            var hasNowFlag = options.ContainsKey("now");

            if (!hasTimestamp && !hasNowFlag)
            {
                Console.Error.WriteLine("ERROR: commissioning mark-discovered requires --timestamp=<ISO8601> or --now.");
                return 1;
            }

            DateTimeOffset ts;
            if (hasNowFlag)
            {
                ts = DateTimeOffset.UtcNow;
            }
            else if (string.Equals(tsRaw, "now", StringComparison.OrdinalIgnoreCase))
            {
                ts = DateTimeOffset.UtcNow;
            }
            else if (!DateTimeOffset.TryParse(tsRaw, out ts))
            {
                Console.Error.WriteLine("ERROR: timestamp must be a valid ISO8601 value or 'now'.");
                return 1;
            }

            var provider = new CollectorSettingsProvider();
            var settings = provider.Load();
            if (string.IsNullOrWhiteSpace(settings.TimescaleConnectionString))
            {
                Console.Error.WriteLine("ERROR: TimescaleDb connection string is empty. Configure it via:");
                Console.Error.WriteLine("  SizerDataCollector config set --timescale-connection-string=\"Host=...;Port=...;Username=...;Password=...;Database=...;\"");
                return 2;
            }

            try
            {
                var repo = new CommissioningRepository(settings.TimescaleConnectionString);
                repo.MarkDiscoveredAsync(serial, ts, CancellationToken.None).GetAwaiter().GetResult();
                Console.WriteLine("STATUS=OK");
                Console.WriteLine("SERIAL=" + serial);
                Console.WriteLine("MACHINE_DISCOVERED_AT=" + ts.ToString("u"));
                Console.WriteLine("MESSAGE=Machine discovery timestamp recorded.");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("STATUS=ERROR");
                Console.Error.WriteLine("ERROR_MESSAGE=" + ex.Message);
                return 2;
            }
        }

        private static int RunCommissioningEnableIngestion(string[] args)
        {
            var options = ParseKeyValueArgs(args);
            string serial;
            if (!options.TryGetValue("serial", out serial) || string.IsNullOrWhiteSpace(serial))
            {
                Console.Error.WriteLine("ERROR: commissioning enable-ingestion requires --serial=<serial>.");
                return 1;
            }

            var provider = new CollectorSettingsProvider();
            var settings = provider.Load();
            if (string.IsNullOrWhiteSpace(settings.TimescaleConnectionString))
            {
                Console.Error.WriteLine("ERROR: TimescaleDb connection string is empty. Configure it via:");
                Console.Error.WriteLine("  SizerDataCollector config set --timescale-connection-string=\"Host=...;Port=...;Username=...;Password=...;Database=...;\"");
                return 2;
            }

            try
            {
                var repo = new CommissioningRepository(settings.TimescaleConnectionString);
                var now = DateTimeOffset.UtcNow;
                repo.SetTimestampAsync(serial, "ingestion_enabled_at", now).GetAwaiter().GetResult();
                Console.WriteLine("STATUS=OK");
                Console.WriteLine("SERIAL=" + serial);
                Console.WriteLine("INGESTION_ENABLED=True");
                Console.WriteLine("INGESTION_ENABLED_AT=" + now.ToString("u"));
                Console.WriteLine("MESSAGE=Ingestion enabled for serial.");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("STATUS=ERROR");
                Console.Error.WriteLine("ERROR_MESSAGE=" + ex.Message);
                return 2;
            }
        }

        private static int RunCommissioningSetNotes(string[] args)
        {
            var options = ParseKeyValueArgs(args);
            string serial;
            if (!options.TryGetValue("serial", out serial) || string.IsNullOrWhiteSpace(serial))
            {
                Console.Error.WriteLine("ERROR: commissioning set-notes requires --serial=<serial>.");
                return 1;
            }

            string notes;
            if (!options.TryGetValue("notes", out notes))
            {
                Console.Error.WriteLine("ERROR: commissioning set-notes requires --notes=<text>.");
                return 1;
            }

            var provider = new CollectorSettingsProvider();
            var settings = provider.Load();
            if (string.IsNullOrWhiteSpace(settings.TimescaleConnectionString))
            {
                Console.Error.WriteLine("ERROR: TimescaleDb connection string is empty. Configure it via:");
                Console.Error.WriteLine("  SizerDataCollector config set --timescale-connection-string=\"Host=...;Port=...;Username=...;Password=...;Database=...;\"");
                return 2;
            }

            try
            {
                var repo = new CommissioningRepository(settings.TimescaleConnectionString);
                repo.UpdateNotesAsync(serial, notes).GetAwaiter().GetResult();
                Console.WriteLine("STATUS=OK");
                Console.WriteLine("SERIAL=" + serial);
                Console.WriteLine("NOTES_SET=True");
                Console.WriteLine("MESSAGE=Commissioning notes updated.");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("STATUS=ERROR");
                Console.Error.WriteLine("ERROR_MESSAGE=" + ex.Message);
                return 2;
            }
        }

        private static int RunCommissioningReset(string[] args)
        {
            var options = ParseKeyValueArgs(args);
            string serial;
            if (!options.TryGetValue("serial", out serial) || string.IsNullOrWhiteSpace(serial))
            {
                Console.Error.WriteLine("ERROR: commissioning reset requires --serial=<serial>.");
                return 1;
            }

            var provider = new CollectorSettingsProvider();
            var settings = provider.Load();
            if (string.IsNullOrWhiteSpace(settings.TimescaleConnectionString))
            {
                Console.Error.WriteLine("ERROR: TimescaleDb connection string is empty. Configure it via:");
                Console.Error.WriteLine("  SizerDataCollector config set --timescale-connection-string=\"Host=...;Port=...;Username=...;Password=...;Database=...;\"");
                return 2;
            }

            try
            {
                var repo = new CommissioningRepository(settings.TimescaleConnectionString);
                repo.ResetAsync(serial, null).GetAwaiter().GetResult();
                Console.WriteLine("STATUS=OK");
                Console.WriteLine("SERIAL=" + serial);
                Console.WriteLine("MESSAGE=Commissioning status reset for serial.");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("STATUS=ERROR");
                Console.Error.WriteLine("ERROR_MESSAGE=" + ex.Message);
                return 2;
            }
        }

        private static int RunCommissioningConfigureMachine(string[] args)
        {
            var options = ParseKeyValueArgs(args);
            string serial;
            if (!options.TryGetValue("serial", out serial) || string.IsNullOrWhiteSpace(serial))
            {
                Console.Error.WriteLine("ERROR: commissioning configure-machine requires --serial=<serial>.");
                return 1;
            }

            string name;
            options.TryGetValue("name", out name);

            string targetSpeedRaw;
            string laneCountRaw;
            string targetPctRaw;
            string recycleOutletRaw;

            if (!options.TryGetValue("target-machine-speed", out targetSpeedRaw) ||
                !options.TryGetValue("lane-count", out laneCountRaw) ||
                !options.TryGetValue("target-percentage", out targetPctRaw) ||
                !options.TryGetValue("recycle-outlet", out recycleOutletRaw))
            {
                Console.Error.WriteLine("ERROR: commissioning configure-machine requires --target-machine-speed, --lane-count, --target-percentage and --recycle-outlet.");
                return 1;
            }

            double targetSpeed;
            int laneCount;
            double targetPct;
            int recycleOutlet;

            if (!double.TryParse(targetSpeedRaw, out targetSpeed))
            {
                Console.Error.WriteLine("ERROR: target-machine-speed must be a number.");
                return 1;
            }

            if (!int.TryParse(laneCountRaw, out laneCount))
            {
                Console.Error.WriteLine("ERROR: lane-count must be an integer.");
                return 1;
            }

            if (!double.TryParse(targetPctRaw, out targetPct))
            {
                Console.Error.WriteLine("ERROR: target-percentage must be a number.");
                return 1;
            }

            if (!int.TryParse(recycleOutletRaw, out recycleOutlet))
            {
                Console.Error.WriteLine("ERROR: recycle-outlet must be an integer.");
                return 1;
            }

            var provider = new CollectorSettingsProvider();
            var settings = provider.Load();
            if (string.IsNullOrWhiteSpace(settings.TimescaleConnectionString))
            {
                Console.Error.WriteLine("ERROR: TimescaleDb connection string is empty. Configure it via:");
                Console.Error.WriteLine("  SizerDataCollector config set --timescale-connection-string=\"Host=...;Port=...;Username=...;Password=...;Database=...;\"");
                return 2;
            }

            try
            {
                var machineSettingsRepo = new MachineSettingsRepository(settings.TimescaleConnectionString);
                machineSettingsRepo.UpsertSettingsAsync(serial, targetSpeed, laneCount, targetPct, recycleOutlet, CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();

                // Optionally ensure machines table reflects the desired name.
                if (!string.IsNullOrWhiteSpace(name))
                {
                    var cfg = new CollectorConfig(settings);
                    DatabaseTester.UpsertMachine(cfg, serial, name);
                }

                Console.WriteLine("STATUS=OK");
                Console.WriteLine("SERIAL=" + serial);
                if (!string.IsNullOrWhiteSpace(name))
                {
                    Console.WriteLine("MACHINE_NAME=" + name);
                }
                Console.WriteLine("TARGET_MACHINE_SPEED=" + targetSpeed);
                Console.WriteLine("LANE_COUNT=" + laneCount);
                Console.WriteLine("TARGET_PERCENTAGE=" + targetPct);
                Console.WriteLine("RECYCLE_OUTLET=" + recycleOutlet);
                Console.WriteLine("MESSAGE=Machine configuration saved.");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("STATUS=ERROR");
                Console.Error.WriteLine("ERROR_MESSAGE=" + ex.Message);
                return 2;
            }
        }

        #endregion

        #region CLI: grades

        private static readonly Dictionary<int, string> GradeCategoryLabels = new Dictionary<int, string>
        {
            { 0, "Good" },
            { 1, "Gate/Peddler" },
            { 2, "Bad" },
            { 3, "Recycle" }
        };

        private static int RunGradesCommand(string[] args)
        {
            if (args == null || args.Length == 0)
            {
                Console.WriteLine("Usage: SizerDataCollector grades [list-categories|resolve|dump-sql] [options]");
                return 1;
            }

            var sub = args[0]?.Trim().ToLowerInvariant();
            var tail = args.Skip(1).ToArray();

            switch (sub)
            {
                case "list-categories":
                    return RunGradesListCategories();
                case "resolve":
                    return RunGradesResolve(tail);
                case "dump-sql":
                    return RunGradesDumpSql(tail);
                default:
                    Console.Error.WriteLine("ERROR: Unknown grades subcommand '" + sub + "'.");
                    return 1;
            }
        }

        private static int RunGradesListCategories()
        {
            Console.WriteLine("STATUS=OK");
            foreach (var kvp in GradeCategoryLabels.OrderBy(kvp => kvp.Key))
            {
                Console.WriteLine("CATEGORY=" + kvp.Key + " NAME=" + kvp.Value);
            }
            return 0;
        }

        private static int RunGradesResolve(string[] args)
        {
            var options = ParseKeyValueArgs(args);
            string serial;
            string gradeKey;
            if (!options.TryGetValue("serial", out serial) || string.IsNullOrWhiteSpace(serial))
            {
                Console.Error.WriteLine("ERROR: grades resolve requires --serial=<serial>.");
                return 1;
            }

            if (!options.TryGetValue("grade-key", out gradeKey) || string.IsNullOrWhiteSpace(gradeKey))
            {
                Console.Error.WriteLine("ERROR: grades resolve requires --grade-key=<code>.");
                return 1;
            }

            var provider = new CollectorSettingsProvider();
            var settings = provider.Load();
            if (string.IsNullOrWhiteSpace(settings.TimescaleConnectionString))
            {
                Console.Error.WriteLine("ERROR: TimescaleDb connection string is empty. Configure it via:");
                Console.Error.WriteLine("  SizerDataCollector config set --timescale-connection-string=\"Host=...;Port=...;Username=...;Password=...;Database=...;\"");
                return 2;
            }

            try
            {
                var repo = new MachineSettingsRepository(settings.TimescaleConnectionString);
                var category = repo.ResolveCategoryAsync(serial, gradeKey, CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();

                if (!category.HasValue)
                {
                    Console.Error.WriteLine("STATUS=ERROR");
                    Console.Error.WriteLine("ERROR_MESSAGE=No category mapping found.");
                    return 2;
                }

                string label;
                GradeCategoryLabels.TryGetValue(category.Value, out label);

                Console.WriteLine("STATUS=OK");
                Console.WriteLine("SERIAL=" + serial);
                Console.WriteLine("GRADE_KEY=" + gradeKey);
                Console.WriteLine("CATEGORY=" + category.Value);
                Console.WriteLine("CATEGORY_NAME=" + (label ?? "UNKNOWN"));
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("STATUS=ERROR");
                Console.Error.WriteLine("ERROR_MESSAGE=" + ex.Message);
                return 2;
            }
        }

        private static int RunGradesDumpSql(string[] args)
        {
            var provider = new CollectorSettingsProvider();
            var settings = provider.Load();

            // Ensure migrations have been materialized to disk so we can read the grade-related scripts.
            var bootstrapper = new DbBootstrapper(settings.TimescaleConnectionString ?? string.Empty);
            bootstrapper.EnsureSqlFolderAsync(CancellationToken.None).GetAwaiter().GetResult();

            var basePath = DbBootstrapper.MigrationPath;
            var targets = new[]
            {
                "V012__grade_map_overrides.sql",
                "V015__serial_grade_to_cat.sql",
                "V016__grade_to_cat_suffix_overrides.sql"
            };

            try
            {
                Console.WriteLine("-- BEGIN grade_to_cat schema (from migrations V012/V015/V016)");
                foreach (var file in targets)
                {
                    var fullPath = Path.Combine(basePath, file);
                    if (File.Exists(fullPath))
                    {
                        Console.WriteLine();
                        Console.WriteLine("-- " + file);
                        Console.WriteLine(File.ReadAllText(fullPath));
                    }
                }

                Console.WriteLine("-- END grade_to_cat schema");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("STATUS=ERROR");
                Console.Error.WriteLine("ERROR_MESSAGE=" + ex.Message);
                return 2;
            }
        }

        #endregion

        #region CLI: targets

        private static int RunTargetsCommand(string[] args)
        {
            if (args == null || args.Length == 0)
            {
                Console.WriteLine("Usage: SizerDataCollector targets [get|set] [options]");
                return 1;
            }

            var sub = args[0]?.Trim().ToLowerInvariant();
            var tail = args.Skip(1).ToArray();

            switch (sub)
            {
                case "get":
                    return RunTargetsGet(tail);
                case "set":
                    return RunTargetsSet(tail);
                default:
                    Console.Error.WriteLine("ERROR: Unknown targets subcommand '" + sub + "'.");
                    return 1;
            }
        }

        private static int RunTargetsGet(string[] args)
        {
            var options = ParseKeyValueArgs(args);
            string serial;
            if (!options.TryGetValue("serial", out serial) || string.IsNullOrWhiteSpace(serial))
            {
                Console.Error.WriteLine("ERROR: targets get requires --serial=<serial>.");
                return 1;
            }

            var provider = new CollectorSettingsProvider();
            var settings = provider.Load();
            if (string.IsNullOrWhiteSpace(settings.TimescaleConnectionString))
            {
                Console.Error.WriteLine("ERROR: TimescaleDb connection string is empty. Configure it via:");
                Console.Error.WriteLine("  SizerDataCollector config set --timescale-connection-string=\"Host=...;Port=...;Username=...;Password=...;Database=...;\"");
                return 2;
            }

            try
            {
                var repo = new MachineSettingsRepository(settings.TimescaleConnectionString);
                var settingsRow = repo.GetSettingsAsync(serial, CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();

                if (settingsRow == null)
                {
                    Console.Error.WriteLine("STATUS=ERROR");
                    Console.Error.WriteLine("ERROR_MESSAGE=Machine settings not found for serial.");
                    return 2;
                }

                var throughput = repo.GetTargetThroughputAsync(serial, CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();

                Console.WriteLine("STATUS=OK");
                Console.WriteLine("SERIAL=" + serial);
                Console.WriteLine("TARGET_MACHINE_SPEED=" + (settingsRow.TargetMachineSpeed.HasValue ? settingsRow.TargetMachineSpeed.Value.ToString() : string.Empty));
                Console.WriteLine("LANE_COUNT=" + (settingsRow.LaneCount.HasValue ? settingsRow.LaneCount.Value.ToString() : string.Empty));
                Console.WriteLine("TARGET_PERCENTAGE=" + (settingsRow.TargetPercentage.HasValue ? settingsRow.TargetPercentage.Value.ToString() : string.Empty));
                Console.WriteLine("RECYCLE_OUTLET=" + (settingsRow.RecycleOutlet.HasValue ? settingsRow.RecycleOutlet.Value.ToString() : string.Empty));
                Console.WriteLine("TARGET_THROUGHPUT=" + (throughput.HasValue ? throughput.Value.ToString() : string.Empty));
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("STATUS=ERROR");
                Console.Error.WriteLine("ERROR_MESSAGE=" + ex.Message);
                return 2;
            }
        }

        private static int RunTargetsSet(string[] args)
        {
            var options = ParseKeyValueArgs(args);
            string serial;
            if (!options.TryGetValue("serial", out serial) || string.IsNullOrWhiteSpace(serial))
            {
                Console.Error.WriteLine("ERROR: targets set requires --serial=<serial>.");
                return 1;
            }

            string targetSpeedRaw;
            string laneCountRaw;
            string targetPctRaw;
            string recycleOutletRaw;

            if (!options.TryGetValue("target-machine-speed", out targetSpeedRaw) ||
                !options.TryGetValue("lane-count", out laneCountRaw) ||
                !options.TryGetValue("target-percentage", out targetPctRaw) ||
                !options.TryGetValue("recycle-outlet", out recycleOutletRaw))
            {
                Console.Error.WriteLine("ERROR: targets set requires --target-machine-speed, --lane-count, --target-percentage and --recycle-outlet.");
                return 1;
            }

            double targetSpeed;
            int laneCount;
            double targetPct;
            int recycleOutlet;

            if (!double.TryParse(targetSpeedRaw, out targetSpeed))
            {
                Console.Error.WriteLine("ERROR: target-machine-speed must be a number.");
                return 1;
            }

            if (!int.TryParse(laneCountRaw, out laneCount))
            {
                Console.Error.WriteLine("ERROR: lane-count must be an integer.");
                return 1;
            }

            if (!double.TryParse(targetPctRaw, out targetPct))
            {
                Console.Error.WriteLine("ERROR: target-percentage must be a number.");
                return 1;
            }

            if (!int.TryParse(recycleOutletRaw, out recycleOutlet))
            {
                Console.Error.WriteLine("ERROR: recycle-outlet must be an integer.");
                return 1;
            }

            var provider = new CollectorSettingsProvider();
            var settings = provider.Load();
            if (string.IsNullOrWhiteSpace(settings.TimescaleConnectionString))
            {
                Console.Error.WriteLine("ERROR: TimescaleDb connection string is empty. Configure it via:");
                Console.Error.WriteLine("  SizerDataCollector config set --timescale-connection-string=\"Host=...;Port=...;Username=...;Password=...;Database=...;\"");
                return 2;
            }

            try
            {
                var repo = new MachineSettingsRepository(settings.TimescaleConnectionString);
                repo.UpsertSettingsAsync(serial, targetSpeed, laneCount, targetPct, recycleOutlet, CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();

                var throughput = repo.GetTargetThroughputAsync(serial, CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();

                Console.WriteLine("STATUS=OK");
                Console.WriteLine("SERIAL=" + serial);
                Console.WriteLine("TARGET_MACHINE_SPEED=" + targetSpeed);
                Console.WriteLine("LANE_COUNT=" + laneCount);
                Console.WriteLine("TARGET_PERCENTAGE=" + targetPct);
                Console.WriteLine("RECYCLE_OUTLET=" + recycleOutlet);
                Console.WriteLine("TARGET_THROUGHPUT=" + (throughput.HasValue ? throughput.Value.ToString() : string.Empty));
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("STATUS=ERROR");
                Console.Error.WriteLine("ERROR_MESSAGE=" + ex.Message);
                return 2;
            }
        }

        #endregion

        #region Shared helpers

        private static Dictionary<string, string> ParseKeyValueArgs(string[] args)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (args == null)
            {
                return result;
            }

            foreach (var arg in args)
            {
                if (string.IsNullOrWhiteSpace(arg))
                {
                    continue;
                }

                var trimmed = arg.Trim();
                if (!trimmed.StartsWith("--", StringComparison.Ordinal))
                {
                    continue;
                }

                var withoutPrefix = trimmed.Substring(2);
                var equalsIndex = withoutPrefix.IndexOf('=');
                if (equalsIndex < 0)
                {
                    result[withoutPrefix] = "true";
                }
                else
                {
                    var key = withoutPrefix.Substring(0, equalsIndex);
                    var value = withoutPrefix.Substring(equalsIndex + 1);
                    result[key] = value;
                }
            }

            return result;
        }

        private static bool TryParseBool(string value, out bool result)
        {
            if (bool.TryParse(value, out result))
            {
                return true;
            }

            if (string.Equals(value, "1", StringComparison.OrdinalIgnoreCase))
            {
                result = true;
                return true;
            }

            if (string.Equals(value, "0", StringComparison.OrdinalIgnoreCase))
            {
                result = false;
                return true;
            }

            if (string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase))
            {
                result = true;
                return true;
            }

            if (string.Equals(value, "no", StringComparison.OrdinalIgnoreCase))
            {
                result = false;
                return true;
            }

            result = false;
            return false;
        }

        private static int CountByStatus(BootstrapResult result, MigrationStatus status)
        {
            if (result == null || result.Migrations == null)
            {
                return 0;
            }

            var count = 0;
            foreach (var m in result.Migrations)
            {
                if (m.Status == status)
                {
                    count++;
                }
            }

            return count;
        }

        private static bool IsPotentiallyDestructiveScript(string sql)
        {
            if (string.IsNullOrWhiteSpace(sql))
            {
                return false;
            }

            var text = sql.ToLowerInvariant();

            if (text.Contains("drop table ") || text.Contains("drop schema "))
            {
                return true;
            }

            if (text.Contains("truncate table "))
            {
                return true;
            }

            var alterIndex = text.IndexOf("alter table ", StringComparison.Ordinal);
            if (alterIndex >= 0)
            {
                var tail = text.Substring(alterIndex);
                if (tail.Contains(" drop column"))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsCommissioningEnabled(
            CollectorConfig config,
            CollectorRuntimeSettings runtimeSettings,
            CollectorStatus status,
            HeartbeatWriter heartbeatWriter)
        {
            status.CommissioningSerial = string.Empty;
            status.CommissioningBlockingReasons.Clear();

            if (runtimeSettings?.EnableIngestion != true)
            {
                status.CommissioningIngestionEnabled = false;
                status.CommissioningBlockingReasons.Add(new SizerDataCollector.Core.Commissioning.CommissioningReason("INGESTION_DISABLED", "Runtime setting EnableIngestion is false."));
                WriteHeartbeat(status, heartbeatWriter);
                return false;
            }

            try
            {
                using (var sizerClient = new SizerClient(config))
                {
                    var serial = sizerClient.GetSerialNoAsync(CancellationToken.None).GetAwaiter().GetResult();
                    if (string.IsNullOrWhiteSpace(serial))
                    {
                        Logger.Log("Commissioning check: Sizer serial number unavailable; disabling ingestion.");
                        status.CommissioningBlockingReasons.Add(new SizerDataCollector.Core.Commissioning.CommissioningReason("SIZER_UNAVAILABLE", "Sizer serial number unavailable."));
                        status.CommissioningIngestionEnabled = false;
                        WriteHeartbeat(status, heartbeatWriter);
                        return false;
                    }

                    status.CommissioningSerial = serial;
                    status.CommissioningIngestionEnabled = true;
                }

                return true;
            }
            catch (Exception ex)
            {
                Logger.Log("Commissioning check failed; disabling ingestion.", ex);
                status.CommissioningIngestionEnabled = false;
                status.CommissioningBlockingReasons.Add(new SizerDataCollector.Core.Commissioning.CommissioningReason("COMMISSIONING_CHECK_FAILED", "Commissioning check failed (see logs)."));
                WriteHeartbeat(status, heartbeatWriter);
                return false;
            }
        }

        private static void WriteHeartbeat(CollectorStatus status, HeartbeatWriter heartbeatWriter)
        {
            if (heartbeatWriter == null || status == null)
            {
                return;
            }

            var snapshot = status.CreateSnapshot();
            var payload = new HeartbeatPayload
            {
                MachineSerial = snapshot.MachineSerial,
                MachineName = snapshot.MachineName,
                LastPollUtc = snapshot.LastPollCompletedUtc ?? snapshot.LastPollStartedUtc,
                LastSuccessUtc = snapshot.LastSuccessUtc,
                LastErrorUtc = snapshot.LastErrorUtc,
                LastErrorMessage = snapshot.LastErrorMessage,
                LastRunId = snapshot.LastRunId,
                CommissioningIngestionEnabled = snapshot.CommissioningIngestionEnabled,
                CommissioningSerial = snapshot.CommissioningSerial,
                CommissioningBlockingReasons = snapshot.CommissioningBlockingReasons
            };

            heartbeatWriter.Write(payload);
        }

        private static void ShowUsage()
        {
            Console.WriteLine("SizerDataCollector CLI");
            Console.WriteLine();
            Console.WriteLine("Legacy harness (backwards compatible):");
            Console.WriteLine("  SizerDataCollector probe");
            Console.WriteLine("  SizerDataCollector single-poll");
            Console.WriteLine();
            Console.WriteLine("Configuration:");
            Console.WriteLine("  SizerDataCollector config show");
            Console.WriteLine("  SizerDataCollector config set --timescale-connection-string=... --enable-ingestion=true --enabled-metrics=m1,m2");
            Console.WriteLine();
            Console.WriteLine("Metrics:");
            Console.WriteLine("  SizerDataCollector metrics list");
            Console.WriteLine("  SizerDataCollector metrics list-supported");
            Console.WriteLine("  SizerDataCollector metrics set --metrics=m1,m2");
            Console.WriteLine();
            Console.WriteLine("Database:");
            Console.WriteLine("  SizerDataCollector db health [--format=json|text]");
            Console.WriteLine("  SizerDataCollector db migrate [--dry-run] [--allow-destructive]");
            Console.WriteLine("  SizerDataCollector db apply-sql --file <path> [--allow-destructive] [--dry-run] [--label=<string>]");
            Console.WriteLine();
            Console.WriteLine("Collector:");
            Console.WriteLine("  SizerDataCollector collector probe");
            Console.WriteLine("  SizerDataCollector collector run-once [--force]");
            Console.WriteLine("  SizerDataCollector collector run-loop [--force]");
            Console.WriteLine();
            Console.WriteLine("Discovery:");
            Console.WriteLine("  SizerDataCollector discovery run");
            Console.WriteLine();
            Console.WriteLine("Commissioning:");
            Console.WriteLine("  SizerDataCollector commissioning status --serial=<serial>");
            Console.WriteLine("  SizerDataCollector commissioning ensure-row --serial=<serial>");
            Console.WriteLine("  SizerDataCollector commissioning mark-discovered --serial=<serial> [--timestamp=<ISO8601>|--now]");
            Console.WriteLine("  SizerDataCollector commissioning enable-ingestion --serial=<serial>");
            Console.WriteLine("  SizerDataCollector commissioning configure-machine --serial=<serial> --name=... --target-machine-speed=... --lane-count=... --target-percentage=... --recycle-outlet=...");
            Console.WriteLine("  SizerDataCollector commissioning set-notes --serial=<serial> --notes=...");
            Console.WriteLine("  SizerDataCollector commissioning reset --serial=<serial>");
            Console.WriteLine();
            Console.WriteLine("Grades:");
            Console.WriteLine("  SizerDataCollector grades list-categories");
            Console.WriteLine("  SizerDataCollector grades resolve --serial=<serial> --grade-key=<code>");
            Console.WriteLine("  SizerDataCollector grades dump-sql");
            Console.WriteLine("  SizerDataCollector grades list-overrides --serial=<serial>");
            Console.WriteLine("  SizerDataCollector grades set-override --serial=<serial> --grade-key=<key> --category=<int>");
            Console.WriteLine("  SizerDataCollector grades remove-override --serial=<serial> --grade-key=<key>");
            Console.WriteLine();
            Console.WriteLine("Targets:");
            Console.WriteLine("  SizerDataCollector targets get --serial=<serial>");
            Console.WriteLine("  SizerDataCollector targets set --serial=<serial> --target-machine-speed=... --lane-count=... --target-percentage=... --recycle-outlet=...");
        }

        private static void LogEffectiveSettings(CollectorRuntimeSettings settings)
        {
            if (settings == null)
            {
                Logger.Log("Runtime settings: <null>");
                return;
            }

            Logger.Log("Runtime settings:");
            Logger.Log("  Sizer host: " + settings.SizerHost + ":" + settings.SizerPort);
            Logger.Log("  Open timeout: " + settings.OpenTimeoutSec + "s");
            Logger.Log("  Send timeout: " + settings.SendTimeoutSec + "s");
            Logger.Log("  Receive timeout: " + settings.ReceiveTimeoutSec + "s");
            Logger.Log("  Timescale connection string configured: " + !string.IsNullOrWhiteSpace(settings.TimescaleConnectionString));
            Logger.Log("  Poll interval: " + settings.PollIntervalSeconds + "s");
            Logger.Log("  Initial backoff: " + settings.InitialBackoffSeconds + "s");
            Logger.Log("  Max backoff: " + settings.MaxBackoffSeconds + "s");
            Logger.Log("  Enable ingestion: " + settings.EnableIngestion);
            Logger.Log("  Enabled metrics: " + (settings.EnabledMetrics == null ? 0 : settings.EnabledMetrics.Count));
        }

        private enum HarnessMode
        {
            Unknown,
            Probe,
            SinglePoll
        }

        #endregion
    }
}
