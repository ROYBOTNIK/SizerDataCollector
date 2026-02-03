using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.ServiceProcess;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Newtonsoft.Json;
using SizerDataCollector.Core.Collector;
using SizerDataCollector.GUI.WPF.Commands;
using SizerDataCollector.Core.Config;
using SizerDataCollector.Core.Logging;
using SizerDataCollector.GUI.WPF.Services;

namespace SizerDataCollector.GUI.WPF.ViewModels
{
	public sealed class CollectorStatusViewModel : INotifyPropertyChanged
	{
		private readonly CollectorSettingsProvider _settingsProvider;
		private readonly WindowsServiceManager _serviceManager;
		private readonly HeartbeatReader _heartbeatReader;

		private CollectorRuntimeSettings _currentSettings;
		private DispatcherTimer _heartbeatTimer;
		private bool _isServiceOperationInProgress;
		private ServiceControllerStatus? _serviceStatus;
		private bool _isServiceInstalled;

		private string _sizerHost = string.Empty;
		private string _sizerPort = "0";
		private string _pollIntervalSeconds = "0";
		private string _initialBackoffSeconds = "0";
		private string _maxBackoffSeconds = "0";
		private string _timescaleConnectionString = string.Empty;
		private bool _enableIngestion;

		private string _lastPollStartDisplay = "--";
		private string _lastPollEndDisplay = "--";
		private string _lastPollError = "--";
		private string _totalPollsStartedDisplay = "0";
		private string _totalPollsSucceededDisplay = "0";
		private string _totalPollsFailedDisplay = "0";

		private string _serviceStatusDisplay = "Unknown";
		private string _lastPollTimeDisplay = "--";
		private string _lastErrorMessage = "--";
		private string _lastPollDisplay = "—";
		private string _lastSuccessDisplay = "—";
		private string _lastErrorDisplay = "—";
		private string _sharedDataDirectory = string.Empty;
		private readonly RelayCommand _startCommand;
		private readonly RelayCommand _stopCommand;
		private readonly RelayCommand _saveSettingsCommand;

		public CollectorStatusViewModel(CollectorSettingsProvider settingsProvider)
		{
			_settingsProvider = settingsProvider ?? throw new ArgumentNullException(nameof(settingsProvider));
			_serviceManager = new WindowsServiceManager();

			var runtimeSettings = _settingsProvider.Load();
			_currentSettings = runtimeSettings;
			SharedDataDirectory = runtimeSettings?.SharedDataDirectory ?? string.Empty;

			var dataRoot = NormalizeDataRoot(SharedDataDirectory);
			if (!string.IsNullOrWhiteSpace(dataRoot))
			{
				Directory.CreateDirectory(dataRoot);
			}
			var heartbeatPath = string.IsNullOrWhiteSpace(dataRoot)
				? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "heartbeat.json")
				: Path.Combine(dataRoot, "heartbeat.json");
			_heartbeatReader = new HeartbeatReader(heartbeatPath);

			Logger.Log("CollectorStatusViewModel created. Checking service installation status...");
			try
			{
				var installed = _serviceManager.IsInstalled();
				var status = installed ? _serviceManager.GetStatus() : (ServiceControllerStatus?)null;
				Logger.Log($"CollectorStatusViewModel: Service installed = {installed}, status = {status?.ToString() ?? "Unknown"}");
				IsServiceInstalled = installed;
				ServiceStatus = status?.ToString() ?? "Unknown";
			}
			catch (Exception ex)
			{
				Logger.Log("CollectorStatusViewModel: Failed to query Windows service status during construction.", ex);
			}

			_startCommand = new RelayCommand(
				async _ => await StartServiceAsync(),
				_ =>
				{
					Logger.Log($"[StartCommand.CanExecute] IsServiceInstalled={IsServiceInstalled}, ServiceStatus={ServiceStatus}, IsBusy={IsBusy}");
					return IsServiceInstalled && !IsBusy;
				});
			_stopCommand = new RelayCommand(
				async _ => await StopServiceAsync(),
				_ =>
				{
					Logger.Log($"[StopCommand.CanExecute] IsServiceInstalled={IsServiceInstalled}, ServiceStatus={ServiceStatus}, IsBusy={IsBusy}");
					return IsServiceInstalled && !IsBusy;
				});
			_saveSettingsCommand = new RelayCommand(_ => SaveSettings(), _ => CanSaveSettings());

			UpdateRunnerCommandStates();
		}

		public event PropertyChangedEventHandler PropertyChanged;

		public Action<string, string, MessageBoxImage> ShowMessage { get; set; }

		public string SizerHost
		{
			get => _sizerHost;
			set
			{
				if (SetProperty(ref _sizerHost, value))
				{
					UpdateSaveCommandState();
				}
			}
		}

		public string SizerPort
		{
			get => _sizerPort;
			set
			{
				if (SetProperty(ref _sizerPort, value))
				{
					UpdateSaveCommandState();
				}
			}
		}

		public string PollIntervalSeconds
		{
			get => _pollIntervalSeconds;
			set => SetProperty(ref _pollIntervalSeconds, value);
		}

		public string InitialBackoffSeconds
		{
			get => _initialBackoffSeconds;
			set => SetProperty(ref _initialBackoffSeconds, value);
		}

		public string MaxBackoffSeconds
		{
			get => _maxBackoffSeconds;
			set => SetProperty(ref _maxBackoffSeconds, value);
		}

		public string TimescaleConnectionString
		{
			get => _timescaleConnectionString;
			set => SetProperty(ref _timescaleConnectionString, value);
		}

		public bool EnableIngestion
		{
			get => _enableIngestion;
			set => SetProperty(ref _enableIngestion, value);
		}

		public string LastPollStartDisplay
		{
			get => _lastPollStartDisplay;
			private set => SetProperty(ref _lastPollStartDisplay, value);
		}

		public string LastPollEndDisplay
		{
			get => _lastPollEndDisplay;
			private set => SetProperty(ref _lastPollEndDisplay, value);
		}

		public string LastPollError
		{
			get => _lastPollError;
			private set => SetProperty(ref _lastPollError, value);
		}

		public string TotalPollsStartedDisplay
		{
			get => _totalPollsStartedDisplay;
			private set => SetProperty(ref _totalPollsStartedDisplay, value);
		}

		public string TotalPollsSucceededDisplay
		{
			get => _totalPollsSucceededDisplay;
			private set => SetProperty(ref _totalPollsSucceededDisplay, value);
		}

		public string TotalPollsFailedDisplay
		{
			get => _totalPollsFailedDisplay;
			private set => SetProperty(ref _totalPollsFailedDisplay, value);
		}

		public bool IsServiceInstalled
		{
			get => _isServiceInstalled;
			private set
			{
				if (SetProperty(ref _isServiceInstalled, value))
				{
					UpdateRunnerCommandStates();
				}
			}
		}

		public bool IsBusy
		{
			get => _isServiceOperationInProgress;
			private set
			{
				if (SetProperty(ref _isServiceOperationInProgress, value))
				{
					UpdateRunnerCommandStates();
				}
			}
		}

		public string ServiceStatus
		{
			get => _serviceStatusDisplay;
			private set
			{
				if (SetProperty(ref _serviceStatusDisplay, value))
				{
					UpdateRunnerCommandStates();
				}
			}
		}

		public string LastPollTime
		{
			get => _lastPollTimeDisplay;
			private set => SetProperty(ref _lastPollTimeDisplay, value);
		}

		public string LastErrorMessage
		{
			get => _lastErrorMessage;
			private set => SetProperty(ref _lastErrorMessage, value);
		}

		public string LastPollDisplay
		{
			get => _lastPollDisplay;
			private set => SetProperty(ref _lastPollDisplay, value);
		}

		public string LastSuccessDisplay
		{
			get => _lastSuccessDisplay;
			private set => SetProperty(ref _lastSuccessDisplay, value);
		}

		public string LastErrorDisplay
		{
			get => _lastErrorDisplay;
			private set => SetProperty(ref _lastErrorDisplay, value);
		}

		public string SharedDataDirectory
		{
			get => _sharedDataDirectory;
			set => SetProperty(ref _sharedDataDirectory, value);
		}

		public ICommand StartCommand => _startCommand;

		public ICommand StopCommand => _stopCommand;

		public ICommand SaveSettingsCommand => _saveSettingsCommand;

		public void Initialize()
		{
			try
			{
				_currentSettings = _settingsProvider.Load();
				LoadFromSettings(_currentSettings);
				RefreshServiceStatus();
				StartHeartbeatMonitoring();
				UpdateSaveCommandState();
			}
			catch (Exception ex)
			{
				Logger.Log("Failed to load settings in WPF UI.", ex);
				ShowMessage?.Invoke($"Failed to load settings: {ex.Message}", "Error", MessageBoxImage.Error);
			}
		}

		public void SaveSettings()
		{
			SaveSettingsInternal(true);
		}

		public void Shutdown()
		{
			StopHeartbeatMonitoring();
		}

		private bool SaveSettingsInternal(bool showNotification)
		{
			try
			{
				var updatedSettings = ToRuntimeSettings(_currentSettings);
				_settingsProvider.Save(updatedSettings);
				_currentSettings = updatedSettings;
				Logger.Log("CollectorStatusViewModel: Settings saved to collector_config.json.");
				if (showNotification)
				{
					ShowMessage?.Invoke("Settings saved successfully.", "Settings", MessageBoxImage.Information);
				}
				return true;
			}
			catch (Exception ex)
			{
				Logger.Log("CollectorStatusViewModel: Failed to save settings in WPF UI.", ex);
				if (showNotification)
				{
					ShowMessage?.Invoke($"Failed to save settings: {ex.Message}", "Error", MessageBoxImage.Error);
				}
				return false;
			}
			finally
			{
				UpdateSaveCommandState();
			}
		}

		private void LoadFromSettings(CollectorRuntimeSettings settings)
		{
			if (settings == null)
			{
				return;
			}

			SizerHost = settings.SizerHost ?? string.Empty;
			SizerPort = settings.SizerPort.ToString(CultureInfo.InvariantCulture);
			PollIntervalSeconds = settings.PollIntervalSeconds.ToString(CultureInfo.InvariantCulture);
			InitialBackoffSeconds = settings.InitialBackoffSeconds.ToString(CultureInfo.InvariantCulture);
			MaxBackoffSeconds = settings.MaxBackoffSeconds.ToString(CultureInfo.InvariantCulture);
			TimescaleConnectionString = settings.TimescaleConnectionString ?? string.Empty;
			EnableIngestion = settings.EnableIngestion;
			SharedDataDirectory = settings.SharedDataDirectory ?? string.Empty;
		}

		private CollectorRuntimeSettings ToRuntimeSettings(CollectorRuntimeSettings baseline)
		{
			var source = baseline ?? new CollectorRuntimeSettings();

			return new CollectorRuntimeSettings
			{
				SizerHost = SizerHost?.Trim() ?? string.Empty,
				SizerPort = ParseInt(SizerPort, source.SizerPort),
				PollIntervalSeconds = ParseInt(PollIntervalSeconds, source.PollIntervalSeconds),
				InitialBackoffSeconds = ParseInt(InitialBackoffSeconds, source.InitialBackoffSeconds),
				MaxBackoffSeconds = ParseInt(MaxBackoffSeconds, source.MaxBackoffSeconds),
				TimescaleConnectionString = TimescaleConnectionString ?? string.Empty,
				EnableIngestion = EnableIngestion,
				OpenTimeoutSec = source.OpenTimeoutSec,
				SendTimeoutSec = source.SendTimeoutSec,
				ReceiveTimeoutSec = source.ReceiveTimeoutSec,
				SharedDataDirectory = string.IsNullOrWhiteSpace(SharedDataDirectory) ? source.SharedDataDirectory : SharedDataDirectory,
				EnabledMetrics = source.EnabledMetrics != null
					? new List<string>(new HashSet<string>(source.EnabledMetrics, StringComparer.OrdinalIgnoreCase))
					: new List<string>()
			};
		}

		private async Task StartServiceAsync()
		{
			if (!EnableIngestion)
			{
				ShowMessage?.Invoke("Enable ingestion before starting the collector service.", "Collector", MessageBoxImage.Information);
				return;
			}

			if (!SaveSettingsInternal(false))
			{
				ShowMessage?.Invoke("Failed to persist settings. Check logs for details.", "Error", MessageBoxImage.Error);
				return;
			}

			IsBusy = true;

			try
			{
				Logger.Log("CollectorStatusViewModel: Starting service via WindowsServiceManager...");
				await _serviceManager.StartAsync(TimeSpan.FromSeconds(30));
				Logger.Log("CollectorStatusViewModel: Service start request completed.");
			}
			catch (InvalidOperationException ex)
			{
				Logger.Log("CollectorStatusViewModel: Failed to start service.", ex);
				MessageBox.Show(
					"Could not start the service. This usually means you need to run this tool as Administrator.",
					"Service Start Failed",
					MessageBoxButton.OK,
					MessageBoxImage.Warning);
			}
			catch (Exception ex)
			{
				Logger.Log("CollectorStatusViewModel: Failed to start service.", ex);
				ShowMessage?.Invoke($"Failed to start service: {ex.Message}", "Error", MessageBoxImage.Error);
			}
			finally
			{
				try
				{
					IsServiceInstalled = _serviceManager.IsInstalled();
					var status = IsServiceInstalled ? _serviceManager.GetStatus() : (ServiceControllerStatus?)null;
					ServiceStatus = status?.ToString() ?? "Unknown";
					Logger.Log($"CollectorStatusViewModel: After Start, installed = {IsServiceInstalled}, status = {ServiceStatus}");
				}
				catch (Exception statusEx)
				{
					Logger.Log("CollectorStatusViewModel: Failed to refresh service status after Start.", statusEx);
				}

				IsBusy = false;
			}
		}

		private async Task StopServiceAsync()
		{
			IsBusy = true;

			try
			{
				Logger.Log("CollectorStatusViewModel: Stopping service via WindowsServiceManager...");
				await _serviceManager.StopAsync(TimeSpan.FromSeconds(30));
				Logger.Log("CollectorStatusViewModel: Service stop request completed.");
			}
			catch (InvalidOperationException ex)
			{
				Logger.Log("CollectorStatusViewModel: Failed to stop service.", ex);
				MessageBox.Show(
					"Could not stop the service. This usually means you need to run this tool as Administrator.",
					"Service Stop Failed",
					MessageBoxButton.OK,
					MessageBoxImage.Warning);
			}
			catch (Exception ex)
			{
				Logger.Log("CollectorStatusViewModel: Failed to stop service.", ex);
				ShowMessage?.Invoke($"Failed to stop service: {ex.Message}", "Error", MessageBoxImage.Error);
			}
			finally
			{
				try
				{
					IsServiceInstalled = _serviceManager.IsInstalled();
					var status = IsServiceInstalled ? _serviceManager.GetStatus() : (ServiceControllerStatus?)null;
					ServiceStatus = status?.ToString() ?? "Unknown";
					Logger.Log($"CollectorStatusViewModel: After Stop, installed = {IsServiceInstalled}, status = {ServiceStatus}");
				}
				catch (Exception statusEx)
				{
					Logger.Log("CollectorStatusViewModel: Failed to refresh service status after Stop.", statusEx);
				}

				IsBusy = false;
			}
		}

		private void UpdateRunnerCommandStates()
		{
			if (_startCommand != null)
			{
				_startCommand.RaiseCanExecuteChanged();
			}

			if (_stopCommand != null)
			{
				_stopCommand.RaiseCanExecuteChanged();
			}
		}

		private void RefreshServiceStatus()
		{
			try
			{
				if (!_serviceManager.IsInstalled())
				{
					IsServiceInstalled = false;
					_serviceStatus = null;
					ServiceStatus = "Not Installed";
					return;
				}

				IsServiceInstalled = true;
				_serviceStatus = _serviceManager.GetStatus();
				ServiceStatus = _serviceStatus?.ToString() ?? "Unknown";
			}
			catch (Exception ex)
			{
				_serviceStatus = null;
				ServiceStatus = "Unknown";
				Logger.Log("CollectorStatusViewModel: Failed to query service status.", ex);
			}
		}

		private void StartHeartbeatMonitoring()
		{
			if (_heartbeatTimer != null)
			{
				return;
			}

			_heartbeatTimer = new DispatcherTimer
			{
				Interval = TimeSpan.FromSeconds(5)
			};
			_heartbeatTimer.Tick += HeartbeatTimerOnTick;
			_heartbeatTimer.Start();

			RefreshHeartbeat();
		}

		private void StopHeartbeatMonitoring()
		{
			if (_heartbeatTimer == null)
			{
				return;
			}

			_heartbeatTimer.Tick -= HeartbeatTimerOnTick;
			_heartbeatTimer.Stop();
			_heartbeatTimer = null;
		}

		private void HeartbeatTimerOnTick(object sender, EventArgs e)
		{
			RefreshHeartbeat();
			RefreshServiceStatus();
		}

		private void RefreshHeartbeat()
		{
			try
			{
				var payload = _heartbeatReader.ReadOrNull();
				if (payload == null)
				{
					LastPollDisplay = "—";
					LastSuccessDisplay = "—";
					LastErrorDisplay = "—";
					LastPollTime = "--";
					LastErrorMessage = "--";
					LastPollStartDisplay = "—";
					LastPollEndDisplay = "—";
					LastPollError = "—";
					return;
				}

				var localZone = TimeZoneInfo.Local;

				string FormatLocal(DateTime? utc)
				{
					if (!utc.HasValue)
					{
						return null;
					}

					var utcValue = DateTime.SpecifyKind(utc.Value, DateTimeKind.Utc);
					var localTime = TimeZoneInfo.ConvertTimeFromUtc(utcValue, localZone);
					return localTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
				}

				LastPollDisplay = FormatLocal(payload.LastPollUtc) ?? "—";
				LastSuccessDisplay = FormatLocal(payload.LastSuccessUtc) ?? "—";

				var errorTime = FormatLocal(payload.LastErrorUtc);
				LastErrorDisplay = errorTime != null
					? $"{errorTime} – {payload.LastErrorMessage}"
					: "—";

				LastPollTime = LastPollDisplay;
				LastErrorMessage = string.IsNullOrWhiteSpace(payload.LastErrorMessage) ? "--" : payload.LastErrorMessage;

				// Fill the UTC-oriented fields with what we have
				LastPollStartDisplay = payload.LastPollUtc.HasValue
					? DateTime.SpecifyKind(payload.LastPollUtc.Value, DateTimeKind.Utc).ToString("u", CultureInfo.InvariantCulture)
					: "—";

				var endUtc = payload.LastSuccessUtc ?? payload.LastErrorUtc;
				LastPollEndDisplay = endUtc.HasValue
					? DateTime.SpecifyKind(endUtc.Value, DateTimeKind.Utc).ToString("u", CultureInfo.InvariantCulture)
					: "—";

				LastPollError = string.IsNullOrWhiteSpace(payload.LastErrorMessage) ? "—" : payload.LastErrorMessage;
			}
			catch (Exception ex)
			{
				Logger.Log("CollectorStatusViewModel: Failed to read heartbeat.", ex);
			}
		}

		private static int ParseInt(string value, int fallback)
		{
			if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
			{
				return parsed;
			}

			return fallback;
		}

		private bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string propertyName = "")
		{
			if (EqualityComparer<T>.Default.Equals(storage, value))
			{
				return false;
			}

			storage = value;
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
			return true;
		}
		
		private static string NormalizeDataRoot(string candidate)
		{
			if (string.IsNullOrWhiteSpace(candidate))
			{
				return string.Empty;
			}

			// If a file path was stored by mistake (e.g. ends with .json), use its directory
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

		private bool CanSaveSettings()
		{
			if (string.IsNullOrWhiteSpace(SizerHost))
			{
				return false;
			}

			int parsedPort;
			if (!int.TryParse(SizerPort, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedPort))
			{
				return false;
			}

			return parsedPort > 0;
		}

		private void UpdateSaveCommandState()
		{
			if (_saveSettingsCommand != null)
			{
				_saveSettingsCommand.RaiseCanExecuteChanged();
			}
		}
	}
}

