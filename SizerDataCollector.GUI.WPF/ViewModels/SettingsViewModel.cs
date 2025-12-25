using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows;
using System.Windows.Threading;
using SizerDataCollector.Core.Commissioning;
using SizerDataCollector.Core.Config;
using SizerDataCollector.Core.Db;
using SizerDataCollector.Core.Logging;
using SizerDataCollector.Core.Sizer;
using SizerDataCollector.GUI.WPF.Commands;

namespace SizerDataCollector.GUI.WPF.ViewModels
{
	public sealed class SettingsViewModel : INotifyPropertyChanged
	{
		private readonly CollectorSettingsProvider _settingsProvider;
		private readonly RelayCommand _initializeSqlFolderCommand;
		private readonly RelayCommand _bootstrapDatabaseCommand;
		private readonly RelayCommand _refreshStatusCommand;
		private readonly RelayCommand _refreshCommissioningCommand;
		private readonly RelayCommand _testSizerConnectionCommand;
		private readonly RelayCommand _enableIngestionCommand;
		private readonly RelayCommand _saveCommissioningNotesCommand;
		private readonly RelayCommand _resetCommissioningCommand;
		private readonly RelayCommand _discoverMachineCommand;

		private string _connectionString = string.Empty;
		private string _statusMessage = "Ready.";
		private bool _isBusy;

		private bool _postgresReachable;
		private bool _timescaleEnabled;
		private string _databaseName = "—";
		private int _appliedMigrations;
		private int _continuousAggregateCount;
		private long _bandDefinitionsCount;
		private long _machineThresholdsCount;
		private long _shiftCalendarCount;
		private DateTimeOffset _lastCheckedAt;

		private readonly ObservableCollection<string> _missingObjects = new ObservableCollection<string>();
		private readonly ObservableCollection<string> _commissioningBlockingReasons = new ObservableCollection<string>();

		private string _commissioningStatusMessage = "Commissioning not evaluated yet.";
		private string _commissioningNotes = string.Empty;
		private string _commissioningSerial = string.Empty;
		private CommissioningRow _commissioningStoredRow;

		private bool _commissioningDbBootstrapped;
		private bool _commissioningSizerConnected;
		private bool _commissioningThresholdsSet;
		private bool _commissioningMachineDiscovered;
		private bool _commissioningGradeMappingCompleted;
		private bool _commissioningCanEnableIngestion;
		private bool _commissioningIngestionEnabled;

		public SettingsViewModel(CollectorSettingsProvider settingsProvider)
		{
			_settingsProvider = settingsProvider ?? throw new ArgumentNullException(nameof(settingsProvider));
			_connectionString = LoadConnectionString();

			_initializeSqlFolderCommand = new RelayCommand(async _ => await InitializeSqlFolderAsync(), _ => CanRunActions);
			_bootstrapDatabaseCommand = new RelayCommand(async _ => await BootstrapDatabaseAsync(), _ => CanRunActions);
			_refreshStatusCommand = new RelayCommand(async _ => await RefreshStatusAsync(), _ => CanRunActions);
			_refreshCommissioningCommand = new RelayCommand(async _ => await RefreshCommissioningAsync(), _ => CanRunActions);
			_testSizerConnectionCommand = new RelayCommand(async _ => await TestSizerConnectionAsync(), _ => CanRunActions);
			_enableIngestionCommand = new RelayCommand(async _ => await EnableIngestionAsync(), _ => CommissioningCanEnableIngestion && CanRunActions);
			_saveCommissioningNotesCommand = new RelayCommand(async _ => await SaveCommissioningNotesAsync(), _ => CanRunActions);
			_resetCommissioningCommand = new RelayCommand(async _ => await ResetCommissioningAsync(), _ => CanRunActions);
			_discoverMachineCommand = new RelayCommand(async _ => await DiscoverMachineAsync(), _ => CanRunActions);
		}

		public event PropertyChangedEventHandler PropertyChanged;

		public ICommand InitializeSqlFolderCommand => _initializeSqlFolderCommand;
		public ICommand BootstrapDatabaseCommand => _bootstrapDatabaseCommand;
		public ICommand RefreshStatusCommand => _refreshStatusCommand;
		public ICommand RefreshCommissioningCommand => _refreshCommissioningCommand;
		public ICommand TestSizerConnectionCommand => _testSizerConnectionCommand;
		public ICommand EnableIngestionCommand => _enableIngestionCommand;
		public ICommand SaveCommissioningNotesCommand => _saveCommissioningNotesCommand;
		public ICommand ResetCommissioningCommand => _resetCommissioningCommand;
		public ICommand DiscoverMachineCommand => _discoverMachineCommand;

		public string ConnectionString
		{
			get => _connectionString;
			private set
			{
				if (SetProperty(ref _connectionString, value))
				{
					OnPropertyChanged(nameof(CanRunActions));
					RaiseCommandStates();
				}
			}
		}

		public string StatusMessage
		{
			get => _statusMessage;
			private set => SetProperty(ref _statusMessage, value);
		}

		public bool IsBusy
		{
			get => _isBusy;
			private set
			{
				if (SetProperty(ref _isBusy, value))
				{
					OnPropertyChanged(nameof(CanRunActions));
					RaiseCommandStates();
				}
			}
		}

		public bool PostgresReachable
		{
			get => _postgresReachable;
			private set => SetProperty(ref _postgresReachable, value);
		}

		public bool TimescaleEnabled
		{
			get => _timescaleEnabled;
			private set => SetProperty(ref _timescaleEnabled, value);
		}

		public string DatabaseName
		{
			get => _databaseName;
			private set => SetProperty(ref _databaseName, value);
		}

		public int AppliedMigrations
		{
			get => _appliedMigrations;
			private set => SetProperty(ref _appliedMigrations, value);
		}

		public int ContinuousAggregateCount
		{
			get => _continuousAggregateCount;
			private set => SetProperty(ref _continuousAggregateCount, value);
		}

		public long BandDefinitionsCount
		{
			get => _bandDefinitionsCount;
			private set => SetProperty(ref _bandDefinitionsCount, value);
		}

		public long MachineThresholdsCount
		{
			get => _machineThresholdsCount;
			private set => SetProperty(ref _machineThresholdsCount, value);
		}

		public long ShiftCalendarCount
		{
			get => _shiftCalendarCount;
			private set => SetProperty(ref _shiftCalendarCount, value);
		}

		public DateTimeOffset LastCheckedAt
		{
			get => _lastCheckedAt;
			private set => SetProperty(ref _lastCheckedAt, value);
		}

		public ObservableCollection<string> MissingObjects => _missingObjects;

		private bool _hasMissingObjects;
		public bool HasMissingObjects => _hasMissingObjects;

		// band_definitions may be empty without failing health; keep count visible.
		public bool SeedPresent => MachineThresholdsCount > 0 && ShiftCalendarCount > 0;

		public bool CanRunActions => !IsBusy && !string.IsNullOrWhiteSpace(ConnectionString);

		public string CommissioningSerial
		{
			get => _commissioningSerial;
			private set => SetProperty(ref _commissioningSerial, value);
		}

		public CommissioningRow CommissioningStoredRow
		{
			get => _commissioningStoredRow;
			private set
			{
				_commissioningStoredRow = value;
				OnPropertyChanged(nameof(CommissioningDbBootstrappedAtText));
				OnPropertyChanged(nameof(CommissioningSizerConnectedAtText));
				OnPropertyChanged(nameof(CommissioningThresholdsSetAtText));
				OnPropertyChanged(nameof(CommissioningIngestionEnabledAtText));
			}
		}

		public string CommissioningStatusMessage
		{
			get => _commissioningStatusMessage;
			private set => SetProperty(ref _commissioningStatusMessage, value);
		}

		public string CommissioningNotes
		{
			get => _commissioningNotes;
			set => SetProperty(ref _commissioningNotes, value);
		}

		public bool CommissioningDbBootstrapped
		{
			get => _commissioningDbBootstrapped;
			private set
			{
				if (SetProperty(ref _commissioningDbBootstrapped, value))
				{
					OnPropertyChanged(nameof(CommissioningDbBootstrappedText));
				}
			}
		}

		public bool CommissioningSizerConnected
		{
			get => _commissioningSizerConnected;
			private set
			{
				if (SetProperty(ref _commissioningSizerConnected, value))
				{
					OnPropertyChanged(nameof(CommissioningSizerConnectedText));
				}
			}
		}

		public bool CommissioningThresholdsSet
		{
			get => _commissioningThresholdsSet;
			private set
			{
				if (SetProperty(ref _commissioningThresholdsSet, value))
				{
					OnPropertyChanged(nameof(CommissioningThresholdsSetText));
				}
			}
		}

		public bool CommissioningMachineDiscovered
		{
			get => _commissioningMachineDiscovered;
			private set
			{
				if (SetProperty(ref _commissioningMachineDiscovered, value))
				{
					OnPropertyChanged(nameof(CommissioningMachineDiscoveredText));
				}
			}
		}

		public bool CommissioningGradeMappingCompleted
		{
			get => _commissioningGradeMappingCompleted;
			private set
			{
				if (SetProperty(ref _commissioningGradeMappingCompleted, value))
				{
					OnPropertyChanged(nameof(CommissioningGradeMappingCompletedText));
				}
			}
		}

		public bool CommissioningCanEnableIngestion
		{
			get => _commissioningCanEnableIngestion;
			private set
			{
				if (SetProperty(ref _commissioningCanEnableIngestion, value))
				{
					OnPropertyChanged(nameof(CommissioningCanEnableIngestionText));
					_enableIngestionCommand?.RaiseCanExecuteChanged();
				}
			}
		}

		public bool CommissioningIngestionEnabled
		{
			get => _commissioningIngestionEnabled;
			private set
			{
				if (SetProperty(ref _commissioningIngestionEnabled, value))
				{
					OnPropertyChanged(nameof(CommissioningIngestionEnabledText));
				}
			}
		}

		public string CommissioningDbBootstrappedText => AsStatusText(CommissioningDbBootstrapped);
		public string CommissioningSizerConnectedText => AsStatusText(CommissioningSizerConnected);
		public string CommissioningThresholdsSetText => AsStatusText(CommissioningThresholdsSet);
		public string CommissioningMachineDiscoveredText => AsStatusText(CommissioningMachineDiscovered);
		public string CommissioningGradeMappingCompletedText => AsStatusText(CommissioningGradeMappingCompleted);
		public string CommissioningCanEnableIngestionText => AsStatusText(CommissioningCanEnableIngestion);
		public string CommissioningIngestionEnabledText => AsStatusText(CommissioningIngestionEnabled);

		public string CommissioningDbBootstrappedAtText => FormatTimestamp(CommissioningStoredRow?.DbBootstrappedAt);
		public string CommissioningSizerConnectedAtText => FormatTimestamp(CommissioningStoredRow?.SizerConnectedAt);
		public string CommissioningThresholdsSetAtText => FormatTimestamp(CommissioningStoredRow?.ThresholdsSetAt);
		public string CommissioningIngestionEnabledAtText => FormatTimestamp(CommissioningStoredRow?.IngestionEnabledAt);

		public ObservableCollection<string> CommissioningBlockingReasons => _commissioningBlockingReasons;

		public async Task InitializeAsync()
		{
			await RefreshStatusAsync().ConfigureAwait(false);
			await RefreshCommissioningAsync().ConfigureAwait(false);
		}

		private async Task InitializeSqlFolderAsync()
		{
			if (!EnsureConnectionStringPresent()) return;
			using (new BusyScope(this))
			{
				try
				{
					var bootstrapper = new DbBootstrapper(ConnectionString);
					await bootstrapper.EnsureSqlFolderAsync(CancellationToken.None).ConfigureAwait(false);
					StatusMessage = "SQL folder initialized (if missing).";
				}
				catch (Exception ex)
				{
					StatusMessage = $"Failed to initialize SQL folder: {ex.Message}";
				}
			}
		}

		private async Task BootstrapDatabaseAsync()
		{
			if (!EnsureConnectionStringPresent()) return;
			using (new BusyScope(this))
			{
				try
				{
					var bootstrapper = new DbBootstrapper(ConnectionString);
					var result = await bootstrapper.BootstrapAsync(CancellationToken.None).ConfigureAwait(false);
					var applied = 0;
					var skipped = 0;
					var failed = 0;
					foreach (var m in result.Migrations)
					{
						if (m.Status == MigrationStatus.Applied) applied++;
						else if (m.Status == MigrationStatus.Skipped) skipped++;
						else failed++;
					}

					if (result.Success)
					{
						StatusMessage = $"Bootstrap complete. Applied {applied}, skipped {skipped}.";
					}
					else
					{
						StatusMessage = $"Bootstrap completed with issues. Applied {applied}, skipped {skipped}, failed {failed}. {result.ErrorMessage}";
					}
				}
				catch (Exception ex)
				{
					StatusMessage = $"Bootstrap failed: {ex.Message}";
				}
			}

			await RefreshStatusAsync().ConfigureAwait(false);
		}

		private async Task RefreshStatusAsync()
		{
			if (!EnsureConnectionStringPresent()) return;
			using (new BusyScope(this))
			{
				try
				{
					var inspector = new DbIntrospector(ConnectionString);
					var report = await inspector.RunAsync(CancellationToken.None).ConfigureAwait(false);

					PostgresReachable = report.CanConnect;
					TimescaleEnabled = report.TimescaleInstalled;
					DatabaseName = string.IsNullOrWhiteSpace(report.DatabaseName) ? "—" : report.DatabaseName;
					AppliedMigrations = report.AppliedMigrationsCount;
					ContinuousAggregateCount = report.ContinuousAggregateCount;
					BandDefinitionsCount = report.BandDefinitionsCount;
					MachineThresholdsCount = report.MachineThresholdsCount;
					ShiftCalendarCount = report.ShiftCalendarCount;
					LastCheckedAt = report.CheckedAt;

					var missingItems = BuildMissingList(report, out bool hasMissing);
					UpdateMissingCollection(missingItems, hasMissing);

					if (report.Exception != null)
					{
						StatusMessage = $"Health check error: {report.Error}";
					}
					else if (report.Healthy)
					{
						StatusMessage = "Health check passed.";
					}
					else
					{
						StatusMessage = "Health check completed with missing items.";
					}
				}
				catch (Exception ex)
				{
					StatusMessage = $"Refresh failed: {ex.Message}";
				}
			}
		}

		private async Task RefreshCommissioningAsync()
		{
			if (!EnsureConnectionStringPresent()) return;
			using (new BusyScope(this))
			{
				try
				{
					var runtimeSettings = _settingsProvider.Load();
					var config = new CollectorConfig(runtimeSettings);
					var sizerClientFactory = new Func<ISizerClient>(() => new SizerClient(config));

					string serialNo = null;
					try
					{
						using (var probeClient = sizerClientFactory())
						{
							serialNo = await probeClient.GetSerialNoAsync(CancellationToken.None).ConfigureAwait(false);
						}
					}
					catch (Exception ex)
					{
						Logger.Log("Commissioning: failed to retrieve serial number from Sizer.", ex);
						ApplyCommissioningFailure("Failed to retrieve serial number from Sizer.");
						return;
					}

					if (string.IsNullOrWhiteSpace(serialNo))
					{
						ApplyCommissioningFailure("Sizer returned an empty serial number.");
						return;
					}

					var repository = new CommissioningRepository(ConnectionString);
					var thresholdsRepository = new ThresholdsRepository(ConnectionString);
					var introspector = new DbIntrospector(ConnectionString);

					await repository.EnsureRowAsync(serialNo).ConfigureAwait(false);

					var health = await introspector.RunAsync(CancellationToken.None).ConfigureAwait(false);
					if (health?.Healthy == true)
					{
						var existing = await repository.GetAsync(serialNo).ConfigureAwait(false);
						if (existing?.DbBootstrappedAt == null)
						{
							await repository.SetTimestampAsync(serialNo, "db_bootstrapped_at", DateTimeOffset.UtcNow).ConfigureAwait(false);
						}
					}

					var thresholds = await thresholdsRepository.GetAsync(serialNo, CancellationToken.None).ConfigureAwait(false);
					if (thresholds != null)
					{
						var existing = await repository.GetAsync(serialNo).ConfigureAwait(false);
						if (existing?.ThresholdsSetAt == null)
						{
							await repository.SetTimestampAsync(serialNo, "thresholds_set_at", DateTimeOffset.UtcNow).ConfigureAwait(false);
						}
					}

					var service = new CommissioningService(ConnectionString, repository, introspector, sizerClientFactory);
					var status = await service.BuildStatusAsync(serialNo, CancellationToken.None).ConfigureAwait(false);
					ApplyCommissioningStatus(status);
				}
				catch (Exception ex)
				{
					Logger.Log("Commissioning refresh failed.", ex);
					ApplyCommissioningFailure($"Commissioning refresh failed: {ex.Message}");
				}
			}
		}

		private async Task TestSizerConnectionAsync()
		{
			if (!EnsureConnectionStringPresent()) return;

			using (new BusyScope(this))
			{
				try
				{
					var runtimeSettings = _settingsProvider.Load();
					var config = new CollectorConfig(runtimeSettings);
					using (var client = new SizerClient(config))
					{
						var serial = await client.GetSerialNoAsync(CancellationToken.None).ConfigureAwait(false);
						var machineName = await client.GetMachineNameAsync(CancellationToken.None).ConfigureAwait(false);

						if (string.IsNullOrWhiteSpace(serial))
						{
							ApplyCommissioningFailure("Sizer connection succeeded but serial number was empty.");
							return;
						}

						if (string.IsNullOrWhiteSpace(machineName))
						{
							ApplyCommissioningFailure("Sizer connection succeeded but machine name was empty.");
							return;
						}

						var repository = new CommissioningRepository(ConnectionString);
						await repository.EnsureRowAsync(serial).ConfigureAwait(false);
						await repository.SetTimestampAsync(serial, "sizer_connected_at", DateTimeOffset.UtcNow).ConfigureAwait(false);
					}

					await RefreshCommissioningAsync().ConfigureAwait(false);
				}
				catch (Exception ex)
				{
					Logger.Log("Commissioning: Sizer connection test failed.", ex);
					ApplyCommissioningFailure($"Sizer connection test failed: {ex.Message}");
				}
			}
		}

		private async Task EnableIngestionAsync()
		{
			if (!CommissioningCanEnableIngestion || string.IsNullOrWhiteSpace(CommissioningSerial))
			{
				return;
			}

			if (!EnsureConnectionStringPresent()) return;

			using (new BusyScope(this))
			{
				try
				{
					var repository = new CommissioningRepository(ConnectionString);
					await repository.SetTimestampAsync(CommissioningSerial, "ingestion_enabled_at", DateTimeOffset.UtcNow).ConfigureAwait(false);

					var runtimeSettings = _settingsProvider.Load() ?? new CollectorRuntimeSettings();
					runtimeSettings.EnableIngestion = true;
					_settingsProvider.Save(runtimeSettings);

					await RefreshCommissioningAsync().ConfigureAwait(false);
				}
				catch (Exception ex)
				{
					Logger.Log("Commissioning: failed to enable ingestion.", ex);
					ApplyCommissioningFailure($"Enable ingestion failed: {ex.Message}");
				}
			}
		}

		private async Task SaveCommissioningNotesAsync()
		{
			if (string.IsNullOrWhiteSpace(CommissioningSerial))
			{
				ApplyCommissioningFailure("Cannot save notes without a resolved serial number.");
				return;
			}

			if (!EnsureConnectionStringPresent()) return;

			using (new BusyScope(this))
			{
				try
				{
					var repository = new CommissioningRepository(ConnectionString);
					await repository.UpdateNotesAsync(CommissioningSerial, CommissioningNotes ?? string.Empty).ConfigureAwait(false);
					await RefreshCommissioningAsync().ConfigureAwait(false);
				}
				catch (Exception ex)
				{
					Logger.Log("Commissioning: failed to save notes.", ex);
					ApplyCommissioningFailure($"Save notes failed: {ex.Message}");
				}
			}
		}

		private async Task ResetCommissioningAsync()
		{
			if (string.IsNullOrWhiteSpace(CommissioningSerial))
			{
				ApplyCommissioningFailure("Cannot reset commissioning without a resolved serial number.");
				return;
			}

			if (!EnsureConnectionStringPresent()) return;

			using (new BusyScope(this))
			{
				try
				{
					var repository = new CommissioningRepository(ConnectionString);
					var note = $"Reset by {Environment.UserName} at {DateTimeOffset.UtcNow:u}";
					await repository.ResetAsync(CommissioningSerial, note).ConfigureAwait(false);
					await RefreshCommissioningAsync().ConfigureAwait(false);
				}
				catch (Exception ex)
				{
					Logger.Log("Commissioning: failed to reset commissioning.", ex);
					ApplyCommissioningFailure($"Reset commissioning failed: {ex.Message}");
				}
			}
		}

		private async Task DiscoverMachineAsync()
		{
			if (!EnsureConnectionStringPresent()) return;

			using (new BusyScope(this))
			{
				try
				{
					var runtimeSettings = _settingsProvider.Load();
					var config = new CollectorConfig(runtimeSettings);
					using (var client = new SizerClient(config))
					{
						var serial = await client.GetSerialNoAsync(CancellationToken.None).ConfigureAwait(false);
						if (string.IsNullOrWhiteSpace(serial))
						{
							ApplyCommissioningFailure("Discovery failed: Sizer returned an empty serial number.");
							return;
						}

						var repository = new CommissioningRepository(ConnectionString);
						await repository.EnsureRowAsync(serial).ConfigureAwait(false);
						CommissioningSerial = serial;
					}

					await RefreshCommissioningAsync().ConfigureAwait(false);
				}
				catch (Exception ex)
				{
					Logger.Log("Commissioning: machine discovery failed.", ex);
					ApplyCommissioningFailure($"Machine discovery failed: {ex.Message}");
				}
			}
		}

		private void ApplyCommissioningStatus(CommissioningStatus status)
		{
			if (status == null)
			{
				ApplyCommissioningFailure("Commissioning status is unavailable.");
				return;
			}

			CommissioningSerial = status.SerialNo;
			CommissioningStoredRow = status.StoredRow;
			CommissioningDbBootstrapped = status.DbBootstrapped;
			CommissioningSizerConnected = status.SizerConnected;
			CommissioningThresholdsSet = status.ThresholdsSet;
			CommissioningMachineDiscovered = status.MachineDiscovered;
			CommissioningGradeMappingCompleted = status.GradeMappingCompleted;
			CommissioningCanEnableIngestion = status.CanEnableIngestion;
			CommissioningIngestionEnabled = status.StoredRow?.IngestionEnabledAt != null;
			CommissioningNotes = status.StoredRow?.Notes ?? string.Empty;

			UpdateCommissioningBlockingReasons(status.BlockingReasons);

			CommissioningStatusMessage = status.CanEnableIngestion
				? "Commissioning prerequisites satisfied. Ingestion can be enabled."
				: (status.BlockingReasons.Count > 0
					? string.Join("; ", status.BlockingReasons)
					: "Commissioning prerequisites not yet satisfied.");
		}

		private void ApplyCommissioningFailure(string message)
		{
			CommissioningStoredRow = null;
			CommissioningSerial = string.Empty;
			CommissioningDbBootstrapped = false;
			CommissioningSizerConnected = false;
			CommissioningThresholdsSet = false;
			CommissioningMachineDiscovered = false;
			CommissioningGradeMappingCompleted = false;
			CommissioningCanEnableIngestion = false;
			CommissioningIngestionEnabled = false;
			CommissioningNotes = string.Empty;
			CommissioningStatusMessage = message;
			UpdateCommissioningBlockingReasons(new[] { new CommissioningReason("UNKNOWN", message) });
		}

		private void UpdateCommissioningBlockingReasons(System.Collections.Generic.IEnumerable<CommissioningReason> reasons)
		{
			var dispatcher = Application.Current?.Dispatcher;
			if (dispatcher != null && !dispatcher.CheckAccess())
			{
				dispatcher.Invoke(() => ApplyBlockingReasons(reasons));
			}
			else
			{
				ApplyBlockingReasons(reasons);
			}
		}

		private void ApplyBlockingReasons(System.Collections.Generic.IEnumerable<CommissioningReason> reasons)
		{
			_commissioningBlockingReasons.Clear();
			if (reasons != null)
			{
				foreach (var r in reasons)
				{
					if (r != null && (!string.IsNullOrWhiteSpace(r.Code) || !string.IsNullOrWhiteSpace(r.Message)))
					{
						var display = string.IsNullOrWhiteSpace(r.Code)
							? r.Message
							: $"{r.Code}: {r.Message}";
						_commissioningBlockingReasons.Add(display);
					}
				}
			}
			OnPropertyChanged(nameof(CommissioningBlockingReasons));
		}

		private static System.Collections.Generic.List<string> BuildMissingList(DbHealthReport report, out bool hasMissing)
		{
			var list = new System.Collections.Generic.List<string>();
			bool missing = false;

			if (report.MissingTables != null)
			{
				foreach (var item in report.MissingTables)
				{
					list.Add($"Table: {item}");
					missing = true;
				}
			}

			if (report.MissingFunctions != null)
			{
				foreach (var item in report.MissingFunctions)
				{
					list.Add($"Function: {item}");
					missing = true;
				}
			}

			if (report.MissingContinuousAggregates != null)
			{
				foreach (var item in report.MissingContinuousAggregates)
				{
					list.Add($"Continuous Aggregate: {item}");
					missing = true;
				}
			}

			if (!string.IsNullOrWhiteSpace(report.PolicyCheckError))
			{
				list.Add($"Refresh policy check failed: {report.PolicyCheckError}");
				missing = true;
			}
			else if (report.ExpectedPolicies > 0)
			{
				if (report.MissingPolicies != null && report.MissingPolicies.Count == 0)
				{
					list.Add($"Refresh policies: OK ({report.FoundPolicies}/{report.ExpectedPolicies})");
				}
				else
				{
					list.Add("Refresh policies missing for: " + string.Join(", ", report.MissingPolicies ?? new System.Collections.Generic.List<string>()));
					missing = true;
				}
			}

			hasMissing = missing;
			return list;
		}

		private static string AsStatusText(bool value) => value ? "✓" : "✕";
		private static string FormatTimestamp(DateTimeOffset? value) => value.HasValue ? value.Value.ToString("u") : "—";

		private void UpdateMissingCollection(System.Collections.Generic.IEnumerable<string> items, bool hasMissing)
		{
			var dispatcher = Application.Current?.Dispatcher;
			if (dispatcher != null && !dispatcher.CheckAccess())
			{
				dispatcher.Invoke(() => ApplyMissing(items, hasMissing));
			}
			else
			{
				ApplyMissing(items, hasMissing);
			}
		}

		private void ApplyMissing(System.Collections.Generic.IEnumerable<string> items, bool hasMissing)
		{
			_missingObjects.Clear();
			if (items != null)
			{
				foreach (var item in items)
				{
					_missingObjects.Add(item);
				}
			}
			_hasMissingObjects = hasMissing;
			OnPropertyChanged(nameof(HasMissingObjects));
		}

		private bool EnsureConnectionStringPresent()
		{
			if (!string.IsNullOrWhiteSpace(ConnectionString))
			{
				return true;
			}

			StatusMessage = "Timescale connection string is missing. Update configuration.";
			return false;
		}

		private string LoadConnectionString()
		{
			var settings = _settingsProvider.Load();
			return settings?.TimescaleConnectionString ?? string.Empty;
		}

		private void RaiseCommandStates()
		{
			_initializeSqlFolderCommand.RaiseCanExecuteChanged();
			_bootstrapDatabaseCommand.RaiseCanExecuteChanged();
			_refreshStatusCommand.RaiseCanExecuteChanged();
			_refreshCommissioningCommand.RaiseCanExecuteChanged();
			_testSizerConnectionCommand.RaiseCanExecuteChanged();
			_enableIngestionCommand.RaiseCanExecuteChanged();
			_saveCommissioningNotesCommand.RaiseCanExecuteChanged();
			_resetCommissioningCommand.RaiseCanExecuteChanged();
			_discoverMachineCommand.RaiseCanExecuteChanged();
		}

		private bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string propertyName = "")
		{
			if (Equals(storage, value))
			{
				return false;
			}

			storage = value;
			OnPropertyChanged(propertyName);
			return true;
		}

		private void OnPropertyChanged([CallerMemberName] string propertyName = "")
		{
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
		}

		private sealed class BusyScope : IDisposable
		{
			private readonly SettingsViewModel _owner;

			public BusyScope(SettingsViewModel owner)
			{
				_owner = owner;
				_owner.IsBusy = true;
			}

			public void Dispose()
			{
				_owner.IsBusy = false;
			}
		}
	}
}

