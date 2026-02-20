using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
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
                ConfigureLoggerFromRuntimeSettings();

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
                    case "status":
                        return RunStatusCommand(tail);
                    case "preflight":
                        return RunPreflightCommand(tail);
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
            Logger.Configure(runtimeSettings);
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

        private static void ConfigureLoggerFromRuntimeSettings()
        {
            try
            {
                var provider = new CollectorSettingsProvider();
                var settings = provider.Load();
                Logger.Configure(settings);
            }
            catch
            {
                // Continue with bootstrap logger settings from app.config.
            }
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
            Console.WriteLine("LOG_LEVEL=" + (settings.LogLevel ?? "Info"));
            Console.WriteLine("DIAGNOSTIC_MODE=" + settings.DiagnosticMode);
            Console.WriteLine("DIAGNOSTIC_UNTIL_UTC=" + FormatTimestamp(settings.DiagnosticUntilUtc));
            Console.WriteLine("LOG_AS_JSON=" + settings.LogAsJson);
            Console.WriteLine("LOG_MAX_FILE_BYTES=" + settings.LogMaxFileBytes);
            Console.WriteLine("LOG_RETENTION_DAYS=" + settings.LogRetentionDays);
            Console.WriteLine("LOG_MAX_FILES=" + settings.LogMaxFiles);
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

            if (options.TryGetValue("log-level", out value))
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    Console.Error.WriteLine("ERROR: log-level cannot be empty.");
                    hasError = true;
                }
                else
                {
                    settings.LogLevel = value.Trim();
                    changed = true;
                }
            }

            if (options.TryGetValue("log-as-json", out value))
            {
                bool asJson;
                if (!TryParseBool(value, out asJson))
                {
                    Console.Error.WriteLine("ERROR: log-as-json must be true/false/1/0.");
                    hasError = true;
                }
                else
                {
                    settings.LogAsJson = asJson;
                    changed = true;
                }
            }

            if (options.TryGetValue("log-max-file-bytes", out value))
            {
                long parsed;
                if (!long.TryParse(value, out parsed) || parsed < 1024)
                {
                    Console.Error.WriteLine("ERROR: log-max-file-bytes must be an integer >= 1024.");
                    hasError = true;
                }
                else
                {
                    settings.LogMaxFileBytes = parsed;
                    changed = true;
                }
            }

            if (options.TryGetValue("log-retention-days", out value))
            {
                int parsed;
                if (!int.TryParse(value, out parsed) || parsed < 1 || parsed > 3650)
                {
                    Console.Error.WriteLine("ERROR: log-retention-days must be an integer between 1 and 3650.");
                    hasError = true;
                }
                else
                {
                    settings.LogRetentionDays = parsed;
                    changed = true;
                }
            }

            if (options.TryGetValue("log-max-files", out value))
            {
                int parsed;
                if (!int.TryParse(value, out parsed) || parsed < 1 || parsed > 10000)
                {
                    Console.Error.WriteLine("ERROR: log-max-files must be an integer between 1 and 10000.");
                    hasError = true;
                }
                else
                {
                    settings.LogMaxFiles = parsed;
                    changed = true;
                }
            }

            if (options.TryGetValue("diagnostic-mode", out value))
            {
                bool diagnosticMode;
                if (!TryParseBool(value, out diagnosticMode))
                {
                    Console.Error.WriteLine("ERROR: diagnostic-mode must be true/false/1/0.");
                    hasError = true;
                }
                else
                {
                    settings.DiagnosticMode = diagnosticMode;
                    if (!diagnosticMode)
                    {
                        settings.DiagnosticUntilUtc = null;
                    }
                    changed = true;
                }
            }

            if (options.TryGetValue("diagnostic-until-utc", out value))
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    settings.DiagnosticUntilUtc = null;
                    changed = true;
                }
                else
                {
                    DateTimeOffset parsedUntil;
                    if (!DateTimeOffset.TryParse(value, out parsedUntil))
                    {
                        Console.Error.WriteLine("ERROR: diagnostic-until-utc must be a valid ISO-8601 timestamp.");
                        hasError = true;
                    }
                    else
                    {
                        settings.DiagnosticUntilUtc = parsedUntil;
                        settings.DiagnosticMode = true;
                        changed = true;
                    }
                }
            }

            if (options.TryGetValue("diagnostic-duration-minutes", out value))
            {
                int minutes;
                if (!int.TryParse(value, out minutes) || minutes <= 0 || minutes > 10080)
                {
                    Console.Error.WriteLine("ERROR: diagnostic-duration-minutes must be an integer between 1 and 10080.");
                    hasError = true;
                }
                else
                {
                    settings.DiagnosticMode = true;
                    settings.DiagnosticUntilUtc = DateTimeOffset.UtcNow.AddMinutes(minutes);
                    changed = true;
                }
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
            Logger.Configure(settings);
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
            if (args == null || args.Length == 0)
            {
                Console.WriteLine("Usage: SizerDataCollector discovery [run|probe|scan|apply] [options]");
                return 1;
            }

            var sub = args[0]?.Trim().ToLowerInvariant();
            var tail = args.Skip(1).ToArray();

            switch (sub)
            {
                case "run":
                    return RunDiscoveryRun(tail);
                case "probe":
                    return RunDiscoveryProbe(tail);
                case "scan":
                    return RunDiscoveryScan(tail);
                case "apply":
                    return RunDiscoveryApply(tail);
                default:
                    Console.Error.WriteLine("ERROR: Unknown discovery subcommand '" + sub + "'.");
                    return 1;
            }
        }

        private static int RunDiscoveryRun(string[] args)
        {
            var options = ParseKeyValueArgs(args);
            var format = GetOutputFormat(options, "json");

            var settingsProvider = new CollectorSettingsProvider();
            var runtimeSettings = settingsProvider.Load();
            var config = new CollectorConfig(runtimeSettings);
            var snapshot = new DiscoveryRunner().RunAsync(config, CancellationToken.None).GetAwaiter().GetResult();
            var result = BuildProbeResult(
                config.SizerHost,
                config.SizerPort,
                snapshot.SerialNo,
                snapshot.MachineName,
                null,
                snapshot.DurationMs,
                true,
                true,
                true);

            if (string.Equals(format, "text", StringComparison.OrdinalIgnoreCase))
            {
                WriteProbeResultText(result);
                return result.CandidateFound ? 0 : 2;
            }

            Console.WriteLine(JsonConvert.SerializeObject(result, Formatting.Indented));
            return result.CandidateFound ? 0 : 2;
        }

        private static int RunDiscoveryProbe(string[] args)
        {
            var options = ParseKeyValueArgs(args);

            string host;
            if (!options.TryGetValue("host", out host) || string.IsNullOrWhiteSpace(host))
            {
                Console.Error.WriteLine("ERROR: discovery probe requires --host=<ip-or-hostname>.");
                return 1;
            }

            var settingsProvider = new CollectorSettingsProvider();
            var runtimeSettings = settingsProvider.Load();

            var port = runtimeSettings.SizerPort > 0 ? runtimeSettings.SizerPort : 8001;
            string value;
            if (options.TryGetValue("port", out value))
            {
                int parsedPort;
                if (!int.TryParse(value, out parsedPort) || parsedPort <= 0 || parsedPort > 65535)
                {
                    Console.Error.WriteLine("ERROR: port must be an integer between 1 and 65535.");
                    return 1;
                }

                port = parsedPort;
            }

            var timeoutMs = 1500;
            if (options.TryGetValue("timeout-ms", out value))
            {
                int parsedTimeout;
                if (!int.TryParse(value, out parsedTimeout) || parsedTimeout < 100 || parsedTimeout > 120000)
                {
                    Console.Error.WriteLine("ERROR: timeout-ms must be an integer between 100 and 120000.");
                    return 1;
                }

                timeoutMs = parsedTimeout;
            }

            var requireSerial = true;
            if (options.TryGetValue("require-serial", out value) && !TryParseBool(value, out requireSerial))
            {
                Console.Error.WriteLine("ERROR: require-serial must be true/false/1/0.");
                return 1;
            }

            var requireMachineName = true;
            if (options.TryGetValue("require-machine-name", out value) && !TryParseBool(value, out requireMachineName))
            {
                Console.Error.WriteLine("ERROR: require-machine-name must be true/false/1/0.");
                return 1;
            }

            var format = GetOutputFormat(options, "json");
            var result = ProbeEndpoint(runtimeSettings, host.Trim(), port, timeoutMs, requireSerial, requireMachineName);

            if (string.Equals(format, "text", StringComparison.OrdinalIgnoreCase))
            {
                WriteProbeResultText(result);
            }
            else
            {
                Console.WriteLine(JsonConvert.SerializeObject(result, Formatting.Indented));
            }

            return result.CandidateFound ? 0 : 2;
        }

        private static int RunDiscoveryApply(string[] args)
        {
            var options = ParseKeyValueArgs(args);
            string host;
            if (!options.TryGetValue("host", out host) || string.IsNullOrWhiteSpace(host))
            {
                Console.Error.WriteLine("ERROR: discovery apply requires --host=<ip-or-hostname>.");
                return 1;
            }

            var provider = new CollectorSettingsProvider();
            var settings = provider.Load();

            var port = settings.SizerPort > 0 ? settings.SizerPort : 8001;
            string value;
            if (options.TryGetValue("port", out value))
            {
                int parsedPort;
                if (!int.TryParse(value, out parsedPort) || parsedPort <= 0 || parsedPort > 65535)
                {
                    Console.Error.WriteLine("ERROR: port must be an integer between 1 and 65535.");
                    return 1;
                }

                port = parsedPort;
            }

            settings.SizerHost = host.Trim();
            settings.SizerPort = port;
            provider.Save(settings);

            Console.WriteLine("STATUS=OK");
            Console.WriteLine("MESSAGE=Discovery endpoint applied to runtime settings.");
            Console.WriteLine("SIZER_HOST=" + settings.SizerHost);
            Console.WriteLine("SIZER_PORT=" + settings.SizerPort);
            return 0;
        }

        private static int RunDiscoveryScan(string[] args)
        {
            var options = ParseKeyValueArgs(args);
            string value;

            var baseSettings = new CollectorSettingsProvider().Load();
            var port = baseSettings.SizerPort > 0 ? baseSettings.SizerPort : 8001;
            if (options.TryGetValue("port", out value))
            {
                int parsedPort;
                if (!int.TryParse(value, out parsedPort) || parsedPort <= 0 || parsedPort > 65535)
                {
                    Console.Error.WriteLine("ERROR: port must be an integer between 1 and 65535.");
                    return 1;
                }

                port = parsedPort;
            }

            var timeoutMs = 1500;
            if (options.TryGetValue("timeout-ms", out value))
            {
                int parsedTimeout;
                if (!int.TryParse(value, out parsedTimeout) || parsedTimeout < 100 || parsedTimeout > 120000)
                {
                    Console.Error.WriteLine("ERROR: timeout-ms must be an integer between 100 and 120000.");
                    return 1;
                }

                timeoutMs = parsedTimeout;
            }

            var concurrency = 32;
            if (options.TryGetValue("concurrency", out value))
            {
                int parsedConcurrency;
                if (!int.TryParse(value, out parsedConcurrency) || parsedConcurrency < 1 || parsedConcurrency > 128)
                {
                    Console.Error.WriteLine("ERROR: concurrency must be an integer between 1 and 128.");
                    return 1;
                }

                concurrency = parsedConcurrency;
            }

            var maxFound = 5;
            if (options.TryGetValue("max-found", out value))
            {
                int parsedMaxFound;
                if (!int.TryParse(value, out parsedMaxFound) || parsedMaxFound < 1 || parsedMaxFound > 1000)
                {
                    Console.Error.WriteLine("ERROR: max-found must be an integer between 1 and 1000.");
                    return 1;
                }

                maxFound = parsedMaxFound;
            }

            var requireSerial = true;
            if (options.TryGetValue("require-serial", out value) && !TryParseBool(value, out requireSerial))
            {
                Console.Error.WriteLine("ERROR: require-serial must be true/false/1/0.");
                return 1;
            }

            var requireMachineName = true;
            if (options.TryGetValue("require-machine-name", out value) && !TryParseBool(value, out requireMachineName))
            {
                Console.Error.WriteLine("ERROR: require-machine-name must be true/false/1/0.");
                return 1;
            }

            var includeAll = false;
            if (options.TryGetValue("include-all", out value) && !TryParseBool(value, out includeAll))
            {
                Console.Error.WriteLine("ERROR: include-all must be true/false/1/0.");
                return 1;
            }

            var allowLargeScan = false;
            if (options.TryGetValue("allow-large-scan", out value) && !TryParseBool(value, out allowLargeScan))
            {
                Console.Error.WriteLine("ERROR: allow-large-scan must be true/false/1/0.");
                return 1;
            }

            var format = GetOutputFormat(options, "json");
            var hosts = ResolveScanHosts(options, allowLargeScan);
            if (hosts == null || hosts.Count == 0)
            {
                Console.Error.WriteLine("ERROR: discovery scan requires one of --subnet=<CIDR>, --range=<start-end>, or --hosts=<h1,h2,...>.");
                return 1;
            }

            var scanStartedUtc = DateTimeOffset.UtcNow;
            var results = ScanHosts(baseSettings, hosts, port, timeoutMs, concurrency, requireSerial, requireMachineName, maxFound);
            var scanFinishedUtc = DateTimeOffset.UtcNow;

            var candidates = results.Where(r => r.CandidateFound).ToList();
            var response = new DiscoveryScanResponse
            {
                Status = "ok",
                StartedAtUtc = scanStartedUtc,
                FinishedAtUtc = scanFinishedUtc,
                Input = new DiscoveryScanInput
                {
                    Port = port,
                    TimeoutMs = timeoutMs,
                    Concurrency = concurrency,
                    RequireSerial = requireSerial,
                    RequireMachineName = requireMachineName,
                    MaxFound = maxFound,
                    HostCount = hosts.Count
                },
                Summary = new DiscoveryScanSummary
                {
                    HostsTotal = hosts.Count,
                    HostsProbed = results.Count,
                    HostsReachable = results.Count(r => r.Reachable),
                    CandidatesFound = candidates.Count
                },
                Candidates = candidates,
                Results = includeAll ? results : new List<DiscoveryProbeResult>()
            };

            if (string.Equals(format, "text", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("STATUS=OK");
                Console.WriteLine("HOSTS_TOTAL=" + response.Summary.HostsTotal);
                Console.WriteLine("HOSTS_PROBED=" + response.Summary.HostsProbed);
                Console.WriteLine("HOSTS_REACHABLE=" + response.Summary.HostsReachable);
                Console.WriteLine("CANDIDATES_FOUND=" + response.Summary.CandidatesFound);
                foreach (var candidate in response.Candidates)
                {
                    Console.WriteLine("CANDIDATE=" + candidate.Host + ":" + candidate.Port + "|" + (candidate.SerialNo ?? string.Empty) + "|" + (candidate.MachineName ?? string.Empty) + "|" + candidate.Confidence);
                }
            }
            else
            {
                Console.WriteLine(JsonConvert.SerializeObject(response, Formatting.Indented));
            }

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

        #region CLI: preflight

        private static int RunPreflightCommand(string[] args)
        {
            var options = ParseKeyValueArgs(args);
            var format = GetOutputFormat(options, "json");
            string raw;

            var checkSizer = true;
            if (options.TryGetValue("check-sizer", out raw) && !TryParseBool(raw, out checkSizer))
            {
                Console.Error.WriteLine("ERROR: check-sizer must be true/false/1/0.");
                return 1;
            }

            var checkDb = true;
            if (options.TryGetValue("check-db", out raw) && !TryParseBool(raw, out checkDb))
            {
                Console.Error.WriteLine("ERROR: check-db must be true/false/1/0.");
                return 1;
            }

            var timeoutMs = 1500;
            if (options.TryGetValue("timeout-ms", out raw))
            {
                int parsed;
                if (!int.TryParse(raw, out parsed) || parsed < 100 || parsed > 120000)
                {
                    Console.Error.WriteLine("ERROR: timeout-ms must be an integer between 100 and 120000.");
                    return 1;
                }

                timeoutMs = parsed;
            }

            var startedAtUtc = DateTimeOffset.UtcNow;
            var checks = new List<PreflightCheckResult>();
            var provider = new CollectorSettingsProvider();
            var settings = provider.Load();

            checks.Add(new PreflightCheckResult
            {
                Name = "runtime_settings_load",
                Passed = settings != null,
                Severity = "required",
                Details = settings == null ? "Failed to load runtime settings." : "Runtime settings loaded."
            });

            checks.Add(new PreflightCheckResult
            {
                Name = "sizer_config",
                Passed = settings != null &&
                         !string.IsNullOrWhiteSpace(settings.SizerHost) &&
                         settings.SizerPort > 0 &&
                         settings.SizerPort <= 65535,
                Severity = "required",
                Details = settings == null
                    ? "Settings unavailable."
                    : "Host=" + (settings.SizerHost ?? string.Empty) + ", Port=" + settings.SizerPort
            });

            var sharedDataDirectory = settings?.SharedDataDirectory;
            if (string.IsNullOrWhiteSpace(sharedDataDirectory))
            {
                sharedDataDirectory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "Opti-Fresh",
                    "SizerCollector");
            }

            checks.Add(TestDirectoryWritable("shared_data_directory_writable", sharedDataDirectory, "required"));

            var configDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Opti-Fresh",
                "SizerDataCollector");
            checks.Add(TestDirectoryWritable("runtime_config_directory_writable", configDirectory, "required"));

            if (checkSizer)
            {
                var probe = ProbeEndpoint(
                    settings,
                    settings?.SizerHost ?? string.Empty,
                    settings?.SizerPort ?? 0,
                    timeoutMs,
                    true,
                    true);

                checks.Add(new PreflightCheckResult
                {
                    Name = "sizer_connectivity",
                    Passed = probe.CandidateFound,
                    Severity = "required",
                    Details = string.IsNullOrWhiteSpace(probe.Error)
                        ? "Probe succeeded. Serial=" + (probe.SerialNo ?? string.Empty) + ", Machine=" + (probe.MachineName ?? string.Empty)
                        : probe.Error
                });
            }
            else
            {
                checks.Add(new PreflightCheckResult
                {
                    Name = "sizer_connectivity",
                    Passed = true,
                    Severity = "optional",
                    Details = "Skipped by option (--check-sizer=false)."
                });
            }

            if (checkDb)
            {
                if (string.IsNullOrWhiteSpace(settings?.TimescaleConnectionString))
                {
                    checks.Add(new PreflightCheckResult
                    {
                        Name = "db_health",
                        Passed = false,
                        Severity = "required",
                        Details = "Timescale connection string is not configured."
                    });
                }
                else
                {
                    var report = new DbIntrospector(settings.TimescaleConnectionString)
                        .RunAsync(CancellationToken.None)
                        .GetAwaiter()
                        .GetResult();

                    checks.Add(new PreflightCheckResult
                    {
                        Name = "db_health",
                        Passed = report.Healthy,
                        Severity = "required",
                        Details = report.Healthy
                            ? "DB health check passed."
                            : "Healthy=false; CanConnect=" + report.CanConnect + "; TimescaleInstalled=" + report.TimescaleInstalled + "; Error=" + (report.Error ?? string.Empty)
                    });
                }
            }
            else
            {
                checks.Add(new PreflightCheckResult
                {
                    Name = "db_health",
                    Passed = true,
                    Severity = "optional",
                    Details = "Skipped by option (--check-db=false)."
                });
            }

            var healthy = checks
                .Where(c => string.Equals(c.Severity, "required", StringComparison.OrdinalIgnoreCase))
                .All(c => c.Passed);

            var result = new PreflightResult
            {
                Status = healthy ? "ok" : "error",
                StartedAtUtc = startedAtUtc,
                FinishedAtUtc = DateTimeOffset.UtcNow,
                Healthy = healthy,
                Checks = checks
            };

            if (string.Equals(format, "text", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("STATUS=" + (healthy ? "OK" : "ERROR"));
                Console.WriteLine("HEALTHY=" + healthy);
                foreach (var check in checks)
                {
                    Console.WriteLine(
                        "CHECK name=" + check.Name +
                        " severity=" + check.Severity +
                        " passed=" + check.Passed +
                        " details=" + (check.Details ?? string.Empty).Replace(Environment.NewLine, " "));
                }
            }
            else
            {
                Console.WriteLine(JsonConvert.SerializeObject(result, Formatting.Indented));
            }

            return healthy ? 0 : 2;
        }

        private static PreflightCheckResult TestDirectoryWritable(string name, string directory, string severity)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                return new PreflightCheckResult
                {
                    Name = name,
                    Passed = false,
                    Severity = severity,
                    Details = "Directory path is empty."
                };
            }

            try
            {
                Directory.CreateDirectory(directory);
                var probeFile = Path.Combine(directory, ".write_probe_" + Guid.NewGuid().ToString("N") + ".tmp");
                File.WriteAllText(probeFile, DateTime.UtcNow.ToString("O"));
                File.Delete(probeFile);
                return new PreflightCheckResult
                {
                    Name = name,
                    Passed = true,
                    Severity = severity,
                    Details = directory
                };
            }
            catch (Exception ex)
            {
                return new PreflightCheckResult
                {
                    Name = name,
                    Passed = false,
                    Severity = severity,
                    Details = directory + " -> " + ex.Message
                };
            }
        }

        #endregion

        #region CLI: status

        private static int RunStatusCommand(string[] args)
        {
            var options = ParseKeyValueArgs(args);
            var format = GetOutputFormat(options, "json");

            var provider = new CollectorSettingsProvider();
            var settings = provider.Load();

            string heartbeatPath;
            if (!options.TryGetValue("heartbeat-file", out heartbeatPath) || string.IsNullOrWhiteSpace(heartbeatPath))
            {
                var dataRoot = settings.SharedDataDirectory;
                if (string.IsNullOrWhiteSpace(dataRoot))
                {
                    dataRoot = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                        "Opti-Fresh",
                        "SizerCollector");
                }

                heartbeatPath = Path.Combine(dataRoot, "heartbeat.json");
            }

            HeartbeatPayload heartbeat = null;
            var heartbeatExists = File.Exists(heartbeatPath);
            string heartbeatReadError = string.Empty;
            DateTimeOffset? heartbeatLastWriteUtc = null;
            if (heartbeatExists)
            {
                try
                {
                    var json = File.ReadAllText(heartbeatPath);
                    heartbeat = JsonConvert.DeserializeObject<HeartbeatPayload>(json);
                    heartbeatLastWriteUtc = File.GetLastWriteTimeUtc(heartbeatPath);
                }
                catch (Exception ex)
                {
                    heartbeatReadError = ex.Message;
                }
            }

            if (string.Equals(format, "text", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("STATUS=OK");
                Console.WriteLine("SIZER_HOST=" + settings.SizerHost);
                Console.WriteLine("SIZER_PORT=" + settings.SizerPort);
                Console.WriteLine("ENABLE_INGESTION=" + settings.EnableIngestion);
                Console.WriteLine("LOG_LEVEL=" + (settings.LogLevel ?? "Info"));
                Console.WriteLine("DIAGNOSTIC_MODE=" + settings.DiagnosticMode);
                Console.WriteLine("DIAGNOSTIC_UNTIL_UTC=" + FormatTimestamp(settings.DiagnosticUntilUtc));
                Console.WriteLine("LOG_AS_JSON=" + settings.LogAsJson);
                Console.WriteLine("LOG_MAX_FILE_BYTES=" + settings.LogMaxFileBytes);
                Console.WriteLine("LOG_RETENTION_DAYS=" + settings.LogRetentionDays);
                Console.WriteLine("LOG_MAX_FILES=" + settings.LogMaxFiles);
                Console.WriteLine("HEARTBEAT_PATH=" + heartbeatPath);
                Console.WriteLine("HEARTBEAT_EXISTS=" + heartbeatExists);
                Console.WriteLine("HEARTBEAT_LAST_WRITE_UTC=" + FormatTimestamp(heartbeatLastWriteUtc));
                Console.WriteLine("HEARTBEAT_READ_ERROR=" + heartbeatReadError);
                if (heartbeat != null)
                {
                    Console.WriteLine("MACHINE_SERIAL=" + (heartbeat.MachineSerial ?? string.Empty));
                    Console.WriteLine("MACHINE_NAME=" + (heartbeat.MachineName ?? string.Empty));
                    Console.WriteLine("LAST_POLL_UTC=" + FormatTimestamp(heartbeat.LastPollUtc));
                    Console.WriteLine("LAST_SUCCESS_UTC=" + FormatTimestamp(heartbeat.LastSuccessUtc));
                    Console.WriteLine("LAST_ERROR_UTC=" + FormatTimestamp(heartbeat.LastErrorUtc));
                    Console.WriteLine("LAST_ERROR_MESSAGE=" + (heartbeat.LastErrorMessage ?? string.Empty));
                    Console.WriteLine("LAST_RUN_ID=" + (heartbeat.LastRunId ?? string.Empty));
                    Console.WriteLine("COMMISSIONING_INGESTION_ENABLED=" + (heartbeat.CommissioningIngestionEnabled.HasValue ? heartbeat.CommissioningIngestionEnabled.Value.ToString() : string.Empty));
                    Console.WriteLine("COMMISSIONING_SERIAL=" + (heartbeat.CommissioningSerial ?? string.Empty));
                    Console.WriteLine("SERVICE_STATE=" + (heartbeat.ServiceState ?? string.Empty));
                    Console.WriteLine("SERVICE_STATE_REASON=" + (heartbeat.ServiceStateReason ?? string.Empty));
                }
                return 0;
            }

            Console.WriteLine(JsonConvert.SerializeObject(new
            {
                status = "ok",
                runtime = new
                {
                    sizerHost = settings.SizerHost,
                    sizerPort = settings.SizerPort,
                    enableIngestion = settings.EnableIngestion,
                    logLevel = settings.LogLevel,
                    diagnosticMode = settings.DiagnosticMode,
                    diagnosticUntilUtc = settings.DiagnosticUntilUtc,
                    logAsJson = settings.LogAsJson,
                    logMaxFileBytes = settings.LogMaxFileBytes,
                    logRetentionDays = settings.LogRetentionDays,
                    logMaxFiles = settings.LogMaxFiles
                },
                heartbeat = new
                {
                    path = heartbeatPath,
                    exists = heartbeatExists,
                    lastWriteUtc = heartbeatLastWriteUtc,
                    readError = heartbeatReadError,
                    payload = heartbeat
                }
            }, Formatting.Indented));
            return 0;
        }

        #endregion

        #region Shared helpers

        private static string GetOutputFormat(Dictionary<string, string> options, string defaultValue)
        {
            string format;
            if (!options.TryGetValue("format", out format) || string.IsNullOrWhiteSpace(format))
            {
                return defaultValue;
            }

            return format.Trim().ToLowerInvariant();
        }

        private static List<DiscoveryProbeResult> ScanHosts(
            CollectorRuntimeSettings baseSettings,
            List<string> hosts,
            int port,
            int timeoutMs,
            int concurrency,
            bool requireSerial,
            bool requireMachineName,
            int maxFound)
        {
            var results = new List<DiscoveryProbeResult>();
            var sync = new object();
            var candidateCount = 0;

            using (var gate = new SemaphoreSlim(concurrency))
            {
                var tasks = hosts.Select(async host =>
                {
                    await gate.WaitAsync().ConfigureAwait(false);
                    try
                    {
                        lock (sync)
                        {
                            if (candidateCount >= maxFound)
                            {
                                return;
                            }
                        }

                        var probe = ProbeEndpoint(baseSettings, host, port, timeoutMs, requireSerial, requireMachineName);
                        lock (sync)
                        {
                            results.Add(probe);
                            if (probe.CandidateFound)
                            {
                                candidateCount++;
                            }
                        }
                    }
                    finally
                    {
                        gate.Release();
                    }
                }).ToArray();

                Task.WhenAll(tasks).GetAwaiter().GetResult();
            }

            return results
                .OrderBy(r => r.Host, StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.Port)
                .ToList();
        }

        private static DiscoveryProbeResult ProbeEndpoint(
            CollectorRuntimeSettings baseSettings,
            string host,
            int port,
            int timeoutMs,
            bool requireSerial,
            bool requireMachineName)
        {
            var started = DateTimeOffset.UtcNow;
            var serial = string.Empty;
            var machineName = string.Empty;
            var error = string.Empty;
            var reachable = false;

            var settings = CloneRuntimeSettings(baseSettings);
            settings.SizerHost = host;
            settings.SizerPort = port;

            var timeoutSeconds = (int)Math.Ceiling(timeoutMs / 1000.0);
            if (timeoutSeconds < 1)
            {
                timeoutSeconds = 1;
            }

            settings.OpenTimeoutSec = timeoutSeconds;
            settings.SendTimeoutSec = timeoutSeconds;
            settings.ReceiveTimeoutSec = timeoutSeconds;

            try
            {
                var config = new CollectorConfig(settings);
                using (var client = new SizerClient(config))
                {
                    string serialError;
                    serial = ProbeValueWithTimeout(ct => client.GetSerialNoAsync(ct), timeoutMs, out serialError);
                    if (string.IsNullOrWhiteSpace(serialError))
                    {
                        reachable = true;
                    }
                    else
                    {
                        error = "serial_no: " + serialError;
                    }

                    string machineError;
                    machineName = ProbeValueWithTimeout(ct => client.GetMachineNameAsync(ct), timeoutMs, out machineError);
                    if (string.IsNullOrWhiteSpace(machineError))
                    {
                        reachable = true;
                    }
                    else
                    {
                        error = string.IsNullOrWhiteSpace(error)
                            ? "machine_name: " + machineError
                            : error + "; machine_name: " + machineError;
                    }
                }
            }
            catch (Exception ex)
            {
                error = string.IsNullOrWhiteSpace(error) ? ex.Message : error + "; " + ex.Message;
            }

            var finished = DateTimeOffset.UtcNow;
            var elapsed = (int)Math.Round((finished - started).TotalMilliseconds);
            return BuildProbeResult(host, port, serial, machineName, error, elapsed, reachable, requireSerial, requireMachineName);
        }

        private static DiscoveryProbeResult BuildProbeResult(
            string host,
            int port,
            string serial,
            string machineName,
            string error,
            int? latencyMs,
            bool reachable,
            bool requireSerial,
            bool requireMachineName)
        {
            var hasSerial = !string.IsNullOrWhiteSpace(serial);
            var hasMachineName = !string.IsNullOrWhiteSpace(machineName);
            var candidateFound = (!requireSerial || hasSerial) && (!requireMachineName || hasMachineName);

            string confidence;
            if (hasSerial && hasMachineName)
            {
                confidence = "high";
            }
            else if (hasSerial || hasMachineName)
            {
                confidence = "medium";
            }
            else
            {
                confidence = "low";
            }

            return new DiscoveryProbeResult
            {
                Status = candidateFound ? "ok" : "no-match",
                Host = host,
                Port = port,
                Reachable = reachable,
                CandidateFound = candidateFound,
                SerialNo = serial ?? string.Empty,
                MachineName = machineName ?? string.Empty,
                Confidence = confidence,
                LatencyMs = latencyMs,
                Error = error ?? string.Empty
            };
        }

        private static void WriteProbeResultText(DiscoveryProbeResult result)
        {
            Console.WriteLine("STATUS=" + (result.CandidateFound ? "OK" : "NO_MATCH"));
            Console.WriteLine("HOST=" + result.Host);
            Console.WriteLine("PORT=" + result.Port);
            Console.WriteLine("REACHABLE=" + result.Reachable);
            Console.WriteLine("CANDIDATE_FOUND=" + result.CandidateFound);
            Console.WriteLine("SERIAL_NO=" + (result.SerialNo ?? string.Empty));
            Console.WriteLine("MACHINE_NAME=" + (result.MachineName ?? string.Empty));
            Console.WriteLine("CONFIDENCE=" + result.Confidence);
            Console.WriteLine("LATENCY_MS=" + (result.LatencyMs.HasValue ? result.LatencyMs.Value.ToString() : string.Empty));
            if (!string.IsNullOrWhiteSpace(result.Error))
            {
                Console.WriteLine("ERROR=" + result.Error);
            }
        }

        private static CollectorRuntimeSettings CloneRuntimeSettings(CollectorRuntimeSettings source)
        {
            if (source == null)
            {
                return new CollectorRuntimeSettings();
            }

            return new CollectorRuntimeSettings
            {
                SizerHost = source.SizerHost,
                SizerPort = source.SizerPort,
                OpenTimeoutSec = source.OpenTimeoutSec,
                SendTimeoutSec = source.SendTimeoutSec,
                ReceiveTimeoutSec = source.ReceiveTimeoutSec,
                TimescaleConnectionString = source.TimescaleConnectionString,
                EnabledMetrics = source.EnabledMetrics == null ? new List<string>() : source.EnabledMetrics.ToList(),
                EnableIngestion = source.EnableIngestion,
                PollIntervalSeconds = source.PollIntervalSeconds,
                InitialBackoffSeconds = source.InitialBackoffSeconds,
                MaxBackoffSeconds = source.MaxBackoffSeconds,
                SharedDataDirectory = source.SharedDataDirectory
            };
        }

        private static string ProbeValueWithTimeout(Func<CancellationToken, Task<string>> operation, int timeoutMs, out string error)
        {
            using (var cts = new CancellationTokenSource(timeoutMs))
            {
                try
                {
                    error = string.Empty;
                    var value = operation(cts.Token).GetAwaiter().GetResult();
                    return value;
                }
                catch (OperationCanceledException)
                {
                    error = "timeout";
                    return string.Empty;
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                    return string.Empty;
                }
            }
        }

        private static List<string> ResolveScanHosts(Dictionary<string, string> options, bool allowLargeScan)
        {
            string subnet;
            string range;
            string hosts;
            var hasSubnet = options.TryGetValue("subnet", out subnet) && !string.IsNullOrWhiteSpace(subnet);
            var hasRange = options.TryGetValue("range", out range) && !string.IsNullOrWhiteSpace(range);
            var hasHosts = options.TryGetValue("hosts", out hosts) && !string.IsNullOrWhiteSpace(hosts);

            var sourceCount = (hasSubnet ? 1 : 0) + (hasRange ? 1 : 0) + (hasHosts ? 1 : 0);
            if (sourceCount == 0)
            {
                return new List<string>();
            }

            if (sourceCount > 1)
            {
                Console.Error.WriteLine("ERROR: Specify only one of --subnet, --range, or --hosts.");
                return null;
            }

            var maxHosts = allowLargeScan ? 65536 : 4096;
            List<string> resolved;
            string error;

            if (hasSubnet)
            {
                if (!TryExpandSubnet(subnet.Trim(), maxHosts, out resolved, out error))
                {
                    Console.Error.WriteLine("ERROR: " + error);
                    return null;
                }
            }
            else if (hasRange)
            {
                if (!TryExpandRange(range.Trim(), maxHosts, out resolved, out error))
                {
                    Console.Error.WriteLine("ERROR: " + error);
                    return null;
                }
            }
            else
            {
                resolved = hosts
                    .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(h => h.Trim())
                    .Where(h => !string.IsNullOrWhiteSpace(h))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (resolved.Count > maxHosts)
                {
                    Console.Error.WriteLine("ERROR: hosts list exceeds scan limit of " + maxHosts + ". Use --allow-large-scan to increase the limit.");
                    return null;
                }
            }

            return resolved;
        }

        private static bool TryExpandSubnet(string subnetCidr, int maxHosts, out List<string> hosts, out string error)
        {
            hosts = new List<string>();
            error = string.Empty;

            var parts = subnetCidr.Split('/');
            if (parts.Length != 2)
            {
                error = "subnet must be in CIDR format, e.g. 10.155.155.0/24.";
                return false;
            }

            IPAddress ip;
            if (!IPAddress.TryParse(parts[0], out ip) || ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
            {
                error = "subnet base address must be a valid IPv4 address.";
                return false;
            }

            int prefix;
            if (!int.TryParse(parts[1], out prefix) || prefix < 0 || prefix > 32)
            {
                error = "subnet prefix must be an integer between 0 and 32.";
                return false;
            }

            var count = 1UL << (32 - prefix);
            if (count > (ulong)maxHosts)
            {
                error = "subnet expands to " + count + " hosts which exceeds scan limit of " + maxHosts + ". Use --allow-large-scan to increase the limit.";
                return false;
            }

            var baseValue = ToUInt32(ip);
            var mask = prefix == 0 ? 0u : uint.MaxValue << (32 - prefix);
            var network = baseValue & mask;
            for (ulong i = 0; i < count; i++)
            {
                hosts.Add(ToIPv4String(network + (uint)i));
            }

            return true;
        }

        private static bool TryExpandRange(string rangeValue, int maxHosts, out List<string> hosts, out string error)
        {
            hosts = new List<string>();
            error = string.Empty;

            var parts = rangeValue.Split('-');
            if (parts.Length != 2)
            {
                error = "range must be in format start-end, e.g. 10.155.155.1-10.155.155.254.";
                return false;
            }

            IPAddress startIp;
            IPAddress endIp;
            if (!IPAddress.TryParse(parts[0], out startIp) || startIp.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
            {
                error = "range start must be a valid IPv4 address.";
                return false;
            }

            if (!IPAddress.TryParse(parts[1], out endIp) || endIp.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
            {
                error = "range end must be a valid IPv4 address.";
                return false;
            }

            var start = ToUInt32(startIp);
            var end = ToUInt32(endIp);
            if (start > end)
            {
                error = "range start must be <= range end.";
                return false;
            }

            var count = (ulong)(end - start) + 1UL;
            if (count > (ulong)maxHosts)
            {
                error = "range expands to " + count + " hosts which exceeds scan limit of " + maxHosts + ". Use --allow-large-scan to increase the limit.";
                return false;
            }

            for (var value = start; value <= end; value++)
            {
                hosts.Add(ToIPv4String(value));
            }

            return true;
        }

        private static uint ToUInt32(IPAddress ip)
        {
            var bytes = ip.GetAddressBytes();
            return ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
        }

        private static string ToIPv4String(uint value)
        {
            return string.Format(
                "{0}.{1}.{2}.{3}",
                (value >> 24) & 255,
                (value >> 16) & 255,
                (value >> 8) & 255,
                value & 255);
        }

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
                CommissioningBlockingReasons = snapshot.CommissioningBlockingReasons,
                ServiceState = snapshot.ServiceState,
                ServiceStateReason = snapshot.ServiceStateReason
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
            Console.WriteLine("  SizerDataCollector config set --timescale-connection-string=... --enable-ingestion=true --enabled-metrics=m1,m2 --log-level=Info --diagnostic-mode=false --log-as-json=false --log-max-file-bytes=10485760 --log-retention-days=14 --log-max-files=100");
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
            Console.WriteLine("  SizerDataCollector discovery run [--format=json|text]");
            Console.WriteLine("  SizerDataCollector discovery probe --host=<ip-or-hostname> [--port=8001] [--timeout-ms=1500] [--format=json|text]");
            Console.WriteLine("  SizerDataCollector discovery scan --subnet=<CIDR>|--range=<start-end>|--hosts=<h1,h2,...> [--port=8001] [--timeout-ms=1500] [--concurrency=32] [--max-found=5] [--format=json|text]");
            Console.WriteLine("  SizerDataCollector discovery apply --host=<ip-or-hostname> [--port=8001]");
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
            Console.WriteLine();
            Console.WriteLine("Runtime status:");
            Console.WriteLine("  SizerDataCollector status [--format=json|text] [--heartbeat-file=<path>]");
            Console.WriteLine("  SizerDataCollector preflight [--format=json|text] [--check-sizer=true|false] [--check-db=true|false] [--timeout-ms=1500]");
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
            Logger.Log("  Log level: " + (settings.LogLevel ?? "Info"));
            Logger.Log("  Diagnostic mode: " + settings.DiagnosticMode);
            Logger.Log("  Diagnostic until (UTC): " + FormatTimestamp(settings.DiagnosticUntilUtc));
            Logger.Log("  Log as JSON: " + settings.LogAsJson);
            Logger.Log("  Log max file bytes: " + settings.LogMaxFileBytes);
            Logger.Log("  Log retention days: " + settings.LogRetentionDays);
            Logger.Log("  Log max files: " + settings.LogMaxFiles);
        }

        private static string FormatTimestamp(DateTimeOffset? value)
        {
            return value.HasValue ? value.Value.ToString("u") : string.Empty;
        }

        private static string FormatTimestamp(DateTime? value)
        {
            return value.HasValue ? value.Value.ToString("u") : string.Empty;
        }

        private sealed class DiscoveryProbeResult
        {
            public string Status { get; set; }
            public string Host { get; set; }
            public int Port { get; set; }
            public bool Reachable { get; set; }
            public bool CandidateFound { get; set; }
            public string SerialNo { get; set; }
            public string MachineName { get; set; }
            public string Confidence { get; set; }
            public int? LatencyMs { get; set; }
            public string Error { get; set; }
        }

        private sealed class DiscoveryScanResponse
        {
            public string Status { get; set; }
            public DateTimeOffset StartedAtUtc { get; set; }
            public DateTimeOffset FinishedAtUtc { get; set; }
            public DiscoveryScanInput Input { get; set; }
            public DiscoveryScanSummary Summary { get; set; }
            public List<DiscoveryProbeResult> Candidates { get; set; } = new List<DiscoveryProbeResult>();
            public List<DiscoveryProbeResult> Results { get; set; } = new List<DiscoveryProbeResult>();
        }

        private sealed class DiscoveryScanInput
        {
            public int Port { get; set; }
            public int TimeoutMs { get; set; }
            public int Concurrency { get; set; }
            public bool RequireSerial { get; set; }
            public bool RequireMachineName { get; set; }
            public int MaxFound { get; set; }
            public int HostCount { get; set; }
        }

        private sealed class DiscoveryScanSummary
        {
            public int HostsTotal { get; set; }
            public int HostsProbed { get; set; }
            public int HostsReachable { get; set; }
            public int CandidatesFound { get; set; }
        }

        private sealed class PreflightResult
        {
            public string Status { get; set; }
            public DateTimeOffset StartedAtUtc { get; set; }
            public DateTimeOffset FinishedAtUtc { get; set; }
            public bool Healthy { get; set; }
            public List<PreflightCheckResult> Checks { get; set; } = new List<PreflightCheckResult>();
        }

        private sealed class PreflightCheckResult
        {
            public string Name { get; set; }
            public bool Passed { get; set; }
            public string Severity { get; set; }
            public string Details { get; set; }
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
