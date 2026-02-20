using System;
using System.Diagnostics;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using SizerDataCollector.Core.Collector;
using SizerDataCollector.Core.Config;
using SizerDataCollector.Core.Db;
using SizerDataCollector.Core.Logging;
using SizerDataCollector.Core.Sizer;
using SizerDataCollector.Core.Commissioning;

namespace SizerDataCollector.Service
{
    public class SizerCollectorService : ServiceBase
    {
        private const string EventSourceName = "SizerDataCollectorService";
        private const string EventLogName = "Application";
        private CancellationTokenSource _cts;
        private Task _runnerTask;
        private CollectorStatus _status;
        private HeartbeatWriter _heartbeatWriter;

        public SizerCollectorService()
        {
            ServiceName = "SizerDataCollectorService";
        }

        protected override void OnStart(string[] args)
        {
            Logger.Log("Service starting...");

            try
            {
                var settingsProvider = new CollectorSettingsProvider();
                var runtimeSettings = settingsProvider.Load();
                Logger.Configure(runtimeSettings);
                var config = new CollectorConfig(runtimeSettings);

                _status = new CollectorStatus();
                SetServiceState("starting", "Service initialization in progress.", false);

                // 1) Normalize any configured SharedDataDirectory
                var dataRoot = NormalizeDataRoot(runtimeSettings?.SharedDataDirectory);

                // 2) If nothing configured, default to ProgramData (recommended for services)
                if (string.IsNullOrWhiteSpace(dataRoot))
                {
                    dataRoot = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                        "Opti-Fresh",
                        "SizerDataCollector");
                }

                // 3) Ensure directory exists
                Directory.CreateDirectory(dataRoot);

                // 4) Heartbeat file lives in data root
                var heartbeatPath = Path.Combine(dataRoot, "heartbeat.json");

                _heartbeatWriter = new HeartbeatWriter(heartbeatPath);
                _cts = new CancellationTokenSource();

                if (!IsIngestionEnabled(runtimeSettings, config, _status, _heartbeatWriter))
                {
                    Logger.Log("Commissioning incomplete; ingestion disabled; service idle.");
                    SetServiceState("blocked", "Commissioning or ingestion settings prevent startup.", true);
                    return;
                }

                _runnerTask = Task.Run(() => RunSupervisedLoopAsync(config, _cts.Token));

                Logger.Log("Service started.");
                SetServiceState("running", "Collector service running.", true);
            }
            catch (Exception ex)
            {
                Logger.Log("Service failed to start.", ex);
                TryWriteEventLog("Service failed to start: " + ex.Message, EventLogEntryType.Error, 1001);
                _cts?.Cancel();
                _cts?.Dispose();
                _cts = null;
                _runnerTask = null;
                throw;
            }
        }

        protected override void OnStop()
        {
            Logger.Log("Service stopping...");
            SetServiceState("stopping", "Service shutdown requested.", true);

            try
            {
                _cts?.Cancel();

                if (_runnerTask != null)
                {
                    var timeout = TimeSpan.FromSeconds(30);
                    if (!_runnerTask.Wait(timeout))
                    {
                        Logger.Log($"Service runner did not stop within {timeout.TotalSeconds} seconds.");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log("Error while stopping service.", ex);
            }
            finally
            {
                _runnerTask = null;

                _cts?.Dispose();
                _cts = null;

                Logger.Log("Service stopped.");
                SetServiceState("stopped", "Service stopped.", true);
            }
        }

        private async Task RunSupervisedLoopAsync(CollectorConfig config, CancellationToken cancellationToken)
        {
            var restartDelay = TimeSpan.FromSeconds(15);
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    SetServiceState("running", "Collector loop active.", true);
                    DatabaseTester.TestAndInitialize(config);
                    var repository = new TimescaleRepository(config.TimescaleConnectionString);

                    using (var sizerClient = new SizerClient(config))
                    {
                        var engine = new CollectorEngine(config, repository, sizerClient);
                        var runner = new CollectorRunner(config, engine, _status, _heartbeatWriter);
                        await runner.RunAsync(cancellationToken).ConfigureAwait(false);
                    }

                    if (!cancellationToken.IsCancellationRequested)
                    {
                        Logger.Log("Collector runner exited unexpectedly; restarting supervised loop.");
                        SetServiceState("degraded", "Collector loop exited unexpectedly; retrying.", true);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Logger.Log("Service runner faulted; entering degraded retry mode.", ex, LogLevel.Error);
                    RecordServiceFault(ex);
                    SetServiceState("degraded", "Service runner faulted; retrying.", true);
                    TryWriteEventLog("Service runner faulted; retrying. " + ex.Message, EventLogEntryType.Warning, 1002);
                    WriteHeartbeat(_status, _heartbeatWriter);

                    try
                    {
                        await Task.Delay(restartDelay, cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }
                }
            }
        }

        private void RecordServiceFault(Exception ex)
        {
            if (_status == null)
            {
                return;
            }

            _status.LastErrorUtc = DateTime.UtcNow;
            _status.LastErrorMessage = ex?.Message ?? "Unknown service fault";
        }

        private void SetServiceState(string state, string reason, bool emitHeartbeat)
        {
            if (_status == null)
            {
                return;
            }

            _status.ServiceState = state;
            _status.ServiceStateReason = reason;
            if (emitHeartbeat && _heartbeatWriter != null)
            {
                WriteHeartbeat(_status, _heartbeatWriter);
            }
        }

        private static void TryWriteEventLog(string message, EventLogEntryType entryType, int eventId)
        {
            try
            {
                if (!EventLog.SourceExists(EventSourceName))
                {
                    EventLog.CreateEventSource(EventSourceName, EventLogName);
                }

                EventLog.WriteEntry(EventSourceName, message, entryType, eventId);
            }
            catch
            {
                // Avoid failing service path if EventLog registration/write fails.
            }
        }

        private static bool IsIngestionEnabled(CollectorRuntimeSettings runtimeSettings, CollectorConfig config, CollectorStatus status, HeartbeatWriter heartbeatWriter)
        {
            status.CommissioningSerial = string.Empty;
            status.CommissioningBlockingReasons.Clear();

            if (runtimeSettings?.EnableIngestion != true)
            {
                status.CommissioningIngestionEnabled = false;
                status.CommissioningBlockingReasons.Add(new CommissioningReason("INGESTION_DISABLED", "Runtime setting EnableIngestion is false."));
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
                        status.CommissioningBlockingReasons.Add(new CommissioningReason("SIZER_UNAVAILABLE", "Sizer serial number unavailable."));
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
                status.CommissioningBlockingReasons.Add(new CommissioningReason("COMMISSIONING_CHECK_FAILED", "Commissioning check failed (see logs)."));
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

        private static string NormalizeDataRoot(string candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                return string.Empty;
            }

            // If a file path was stored by mistake (e.g., ends with .json), use its directory
            if (candidate.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                return Path.GetDirectoryName(candidate);
            }

            // If a file already exists at that path, fall back to its directory
            if (File.Exists(candidate))
            {
                return Path.GetDirectoryName(candidate);
            }

            return candidate;
        }
    }
}
