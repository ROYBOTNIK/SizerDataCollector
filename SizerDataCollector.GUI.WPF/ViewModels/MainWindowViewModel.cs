using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using SizerDataCollector;
using SizerDataCollector.Collector;
using SizerDataCollector.Config;
using SizerDataCollector.Db;
using SizerDataCollector.GUI.WPF.Commands;
using SizerDataCollector.Sizer;
using Logger = SizerDataCollector.Logger;

namespace SizerDataCollector.GUI.WPF.ViewModels
{
	public sealed class MainWindowViewModel : INotifyPropertyChanged
	{
		private readonly Dispatcher _dispatcher;
		private readonly CollectorSettingsProvider _settingsProvider;

		private readonly object _statusLock = new object();
		private CollectorRuntimeSettings _currentSettings;
		private CancellationTokenSource _cts;
		private Task _runnerTask;
		private CollectorStatus _collectorStatus;
		private CancellationTokenSource _statusLoopCts;
		private Task _statusLoopTask;

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

		private bool _isCollectorRunning;
		private readonly RelayCommand _startCommand;
		private readonly RelayCommand _stopCommand;
		private readonly RelayCommand _saveSettingsCommand;

		public MainWindowViewModel(CollectorSettingsProvider settingsProvider)
		{
			_dispatcher = Application.Current != null ? Application.Current.Dispatcher : Dispatcher.CurrentDispatcher;
			_settingsProvider = settingsProvider ?? throw new ArgumentNullException(nameof(settingsProvider));

			_startCommand = new RelayCommand(_ => StartCollector(), _ => CanStartCollector());
			_stopCommand = new RelayCommand(_ => StopCollector(), _ => CanStopCollector());
			_saveSettingsCommand = new RelayCommand(_ => SaveSettings(), _ => CanSaveSettings());
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

		public bool IsCollectorRunning
		{
			get => _isCollectorRunning;
			private set
			{
				if (SetProperty(ref _isCollectorRunning, value))
				{
					UpdateRunnerCommandStates();
				}
			}
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
				UpdateStatus(_collectorStatus?.CreateSnapshot());
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

		private bool SaveSettingsInternal(bool showNotification)
		{
			try
			{
				var updatedSettings = ToRuntimeSettings(_currentSettings);
				_settingsProvider.Save(updatedSettings);
				_currentSettings = updatedSettings;
				Logger.Log("MainWindowViewModel: Settings saved to collector_config.json.");
				if (showNotification)
				{
					ShowMessage?.Invoke("Settings saved successfully.", "Settings", MessageBoxImage.Information);
				}
				return true;
			}
			catch (Exception ex)
			{
				Logger.Log("MainWindowViewModel: Failed to save settings in WPF UI.", ex);
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
		}

		private void UpdateStatus(CollectorStatusSnapshot snapshot)
		{
			if (snapshot == null)
			{
				LastPollStartDisplay = "--";
				LastPollEndDisplay = "--";
				LastPollError = "--";
				TotalPollsStartedDisplay = "0";
				TotalPollsSucceededDisplay = "0";
				TotalPollsFailedDisplay = "0";
				return;
			}

			LastPollStartDisplay = FormatTimestamp(snapshot.LastPollStartUtc);
			LastPollEndDisplay = FormatTimestamp(snapshot.LastPollEndUtc);
			LastPollError = string.IsNullOrWhiteSpace(snapshot.LastPollError) ? "--" : snapshot.LastPollError;
			TotalPollsStartedDisplay = snapshot.TotalPollsStarted.ToString(CultureInfo.InvariantCulture);
			TotalPollsSucceededDisplay = snapshot.TotalPollsSucceeded.ToString(CultureInfo.InvariantCulture);
			TotalPollsFailedDisplay = snapshot.TotalPollsFailed.ToString(CultureInfo.InvariantCulture);
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
				EnabledMetrics = source.EnabledMetrics != null ? new List<string>(source.EnabledMetrics) : new List<string>()
			};
		}

		private async void StartCollector()
		{
			if (!CanStartCollector())
			{
				ShowMessage?.Invoke("Collector is already running.", "Collector", MessageBoxImage.Information);
				return;
			}

			if (!EnableIngestion)
			{
				ShowMessage?.Invoke("Enable ingestion before starting the collector loop.", "Collector", MessageBoxImage.Information);
				return;
			}

			if (!SaveSettingsInternal(false))
			{
				ShowMessage?.Invoke("Failed to persist settings. Check logs for details.", "Error", MessageBoxImage.Error);
				return;
			}

			try
			{
				var runtimeSettings = _currentSettings ?? _settingsProvider.Load();
				if (runtimeSettings == null)
				{
					runtimeSettings = new CollectorRuntimeSettings();
				}

				var config = new CollectorConfig(runtimeSettings);

				await Task.Run(() => DatabaseTester.TestAndInitialize(config));

				_collectorStatus = new CollectorStatus();
				UpdateStatus(_collectorStatus.CreateSnapshot());

				_cts = new CancellationTokenSource();
				var token = _cts.Token;
				IsCollectorRunning = true;

				_runnerTask = Task.Run(async () =>
				{
					try
					{
						using (var sizerClient = new SizerClient(config))
						{
							var repository = new TimescaleRepository(config.TimescaleConnectionString);
							var engine = new CollectorEngine(config, repository, sizerClient);
							var runner = new CollectorRunner(config, engine, _collectorStatus);
							await runner.RunAsync(token).ConfigureAwait(false);
						}
					}
					catch (OperationCanceledException) when (token.IsCancellationRequested)
					{
					}
					catch (Exception ex)
					{
						Logger.Log("Collector loop terminated unexpectedly in WPF host.", ex);
						if (_collectorStatus != null)
						{
							lock (_statusLock)
							{
								_collectorStatus.LastPollError = ex.Message;
							}
						}
					}
					finally
					{
						StopStatusRefreshLoop();
						var _ = _dispatcher.BeginInvoke(new Action(() =>
						{
							UpdateStatus(_collectorStatus?.CreateSnapshot());
							CleanupRunnerState();
						}));
					}
				}, token);

				StartStatusRefreshLoop();
				Logger.Log("MainWindowViewModel: Collector started from GUI.");
			}
			catch (Exception ex)
			{
				Logger.Log("MainWindowViewModel: Failed to start collector.", ex);
				StopStatusRefreshLoop();
				CleanupRunnerState();
				ShowMessage?.Invoke($"Failed to start collector: {ex.Message}", "Error", MessageBoxImage.Error);
			}

			UpdateRunnerCommandStates();
		}

		private async void StopCollector()
		{
			if (!CanStopCollector())
			{
				ShowMessage?.Invoke("Collector is not currently running.", "Collector", MessageBoxImage.Information);
				return;
			}

			try
			{
				_cts?.Cancel();
				var task = _runnerTask;
				if (task != null)
				{
					try
					{
						await task.ConfigureAwait(false);
					}
					catch (OperationCanceledException)
					{
					}
				}
			}
			catch (Exception ex)
			{
				Logger.Log("MainWindowViewModel: Error while stopping collector.", ex);
			}
			finally
			{
				StopStatusRefreshLoop();
				CleanupRunnerState();
				UpdateStatus(_collectorStatus?.CreateSnapshot());
				Logger.Log("MainWindowViewModel: Collector stopped from GUI.");
			}

			UpdateRunnerCommandStates();
		}

		private void StartStatusRefreshLoop()
		{
			StopStatusRefreshLoop();

			if (_collectorStatus == null)
			{
				return;
			}

			_statusLoopCts = new CancellationTokenSource();
			var loopToken = _statusLoopCts.Token;

			_statusLoopTask = Task.Run(async () =>
			{
				while (!loopToken.IsCancellationRequested)
				{
					try
					{
						var snapshot = _collectorStatus?.CreateSnapshot();
						var _ = _dispatcher.BeginInvoke(new Action(() => UpdateStatus(snapshot)));
						await Task.Delay(TimeSpan.FromSeconds(1), loopToken).ConfigureAwait(false);
					}
					catch (OperationCanceledException)
					{
						break;
					}
					catch (Exception ex)
					{
						Logger.Log("MainWindowViewModel: Status refresh loop error.", ex);
					}
				}
			}, loopToken);
		}

		private void StopStatusRefreshLoop()
		{
			if (_statusLoopCts != null)
			{
				_statusLoopCts.Cancel();
				_statusLoopCts.Dispose();
				_statusLoopCts = null;
			}

			_statusLoopTask = null;
		}

		private void CleanupRunnerState()
		{
			_cts?.Dispose();
			_cts = null;
			_runnerTask = null;
			IsCollectorRunning = false;
			UpdateRunnerCommandStates();
		}

		private bool CanStartCollector()
		{
			return _runnerTask == null || _runnerTask.IsCompleted;
		}

		private bool CanStopCollector()
		{
			return _runnerTask != null && !_runnerTask.IsCompleted;
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

		private static string FormatTimestamp(DateTime? timestamp)
		{
			return timestamp.HasValue ? timestamp.Value.ToString("u", CultureInfo.InvariantCulture) : "--";
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

