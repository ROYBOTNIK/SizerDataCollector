using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows;
using System.Windows.Threading;
using Newtonsoft.Json;
using SizerDataCollector.Core.Commissioning;
using SizerDataCollector.Core.Config;
using SizerDataCollector.Core.Db;
using SizerDataCollector.Core.Logging;
using SizerDataCollector.Core.Sizer;
using SizerDataCollector.Core.Sizer.Discovery;
using SizerDataCollector.GUI.WPF.Commands;
using System.Linq;

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
		private readonly RelayCommand _copyDiscoveryJsonCommand;
		private readonly RelayCommand _cancelDiscoveryCommand;
		private readonly RelayCommand _refreshMachinesCommand;
		private readonly RelayCommand _saveMachineSettingsCommand;
		private readonly RelayCommand _saveGradeOverridesCommand;

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
		private long _discoverySnapshotCount;
		private DateTimeOffset? _latestDiscoveryAt;
		private readonly ObservableCollection<DiscoverySnapshotRecord> _discoveryHistory = new ObservableCollection<DiscoverySnapshotRecord>();
		private DiscoverySnapshotRecord _selectedDiscoverySnapshot;

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

		private long? _discoverySnapshotId;
		private DateTimeOffset? _discoveryDiscoveredAt;
		private string _discoverySerial = string.Empty;
		private string _discoveryMachineName = string.Empty;
		private int? _discoveryOutletCount;
		private string _discoveryOutletNames = string.Empty;
		private int? _discoveryLaneViewCount;
		private int? _discoveryGradeKeyCount;
		private int? _discoverySizeKeyCount;
		private string _discoveryStatusMessage = "Discovery not run yet.";
		private string _discoveryRawJson = string.Empty;
		private string _discoverySummaryJson = string.Empty;
		private CancellationTokenSource _discoveryCts;

		private readonly ObservableCollection<MachineOption> _machines = new ObservableCollection<MachineOption>();
		private MachineOption _selectedMachine;
		private readonly ObservableCollection<OutletOption> _outletOptions = new ObservableCollection<OutletOption>();
		private OutletOption _selectedRecycleOutlet;
		private double? _targetMachineSpeed;
		private int? _laneCountSetting;
		private double? _targetPercentage;
		private double? _targetThroughputPreview;
		private readonly ObservableCollection<GradeRow> _gradeRows = new ObservableCollection<GradeRow>();
		private readonly ObservableCollection<CategoryOption> _categoryOptions = new ObservableCollection<CategoryOption>
		{
			new CategoryOption(0, "Good / Export"),
			new CategoryOption(1, "Peddler / Test"),
			new CategoryOption(2, "Bad / Green / Cull"),
			new CategoryOption(3, "Recycle")
		};

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
			_discoverMachineCommand = new RelayCommand(async _ => await RunDiscoveryAsync(), _ => CanRunActions);
			_copyDiscoveryJsonCommand = new RelayCommand(_ => CopyDiscoveryJson(), _ => HasDiscoveryRawJson);
			_cancelDiscoveryCommand = new RelayCommand(_ => CancelDiscovery(), _ => _discoveryCts != null && !_discoveryCts.IsCancellationRequested);
			_refreshMachinesCommand = new RelayCommand(async _ => await RefreshMachinesAsync(), _ => CanRunActions);
			_saveMachineSettingsCommand = new RelayCommand(async _ => await SaveMachineSettingsAsync(), _ => SelectedMachine != null && CanRunActions);
			_saveGradeOverridesCommand = new RelayCommand(async _ => await SaveGradeOverridesAsync(), _ => SelectedMachine != null && CanRunActions && _gradeRows.Count > 0);
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
		public ICommand CopyDiscoveryJsonCommand => _copyDiscoveryJsonCommand;
		public ICommand CancelDiscoveryCommand => _cancelDiscoveryCommand;
		public ICommand RefreshMachinesCommand => _refreshMachinesCommand;
		public ICommand SaveMachineSettingsCommand => _saveMachineSettingsCommand;
		public ICommand SaveGradeOverridesCommand => _saveGradeOverridesCommand;

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

		public long DiscoverySnapshotCount
		{
			get => _discoverySnapshotCount;
			private set => SetProperty(ref _discoverySnapshotCount, value);
		}

		public DateTimeOffset? LatestDiscoveryAt
		{
			get => _latestDiscoveryAt;
			private set => SetProperty(ref _latestDiscoveryAt, value);
		}

		public DateTimeOffset LastCheckedAt
		{
			get => _lastCheckedAt;
			private set => SetProperty(ref _lastCheckedAt, value);
		}

		public ObservableCollection<string> MissingObjects => _missingObjects;

		private bool _hasMissingObjects;
		public bool HasMissingObjects => _hasMissingObjects;

		public ObservableCollection<DiscoverySnapshotRecord> DiscoveryHistory => _discoveryHistory;

		public DiscoverySnapshotRecord SelectedDiscoverySnapshot
		{
			get => _selectedDiscoverySnapshot;
			set
			{
				if (SetProperty(ref _selectedDiscoverySnapshot, value))
				{
					ApplyDiscoveryRecord(value);
				}
			}
		}

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

		public long? DiscoverySnapshotId
		{
			get => _discoverySnapshotId;
			private set => SetProperty(ref _discoverySnapshotId, value);
		}

		public DateTimeOffset? DiscoveryDiscoveredAt
		{
			get => _discoveryDiscoveredAt;
			private set => SetProperty(ref _discoveryDiscoveredAt, value);
		}

		public string DiscoverySerial
		{
			get => _discoverySerial;
			private set => SetProperty(ref _discoverySerial, value ?? string.Empty);
		}

		public string DiscoveryMachineName
		{
			get => _discoveryMachineName;
			private set => SetProperty(ref _discoveryMachineName, value ?? string.Empty);
		}

		public int? DiscoveryOutletCount
		{
			get => _discoveryOutletCount;
			private set => SetProperty(ref _discoveryOutletCount, value);
		}

		public string DiscoveryOutletNames
		{
			get => _discoveryOutletNames;
			private set => SetProperty(ref _discoveryOutletNames, value ?? string.Empty);
		}

		public int? DiscoveryLaneViewCount
		{
			get => _discoveryLaneViewCount;
			private set => SetProperty(ref _discoveryLaneViewCount, value);
		}

		public int? DiscoveryGradeKeyCount
		{
			get => _discoveryGradeKeyCount;
			private set => SetProperty(ref _discoveryGradeKeyCount, value);
		}

		public int? DiscoverySizeKeyCount
		{
			get => _discoverySizeKeyCount;
			private set => SetProperty(ref _discoverySizeKeyCount, value);
		}

		public string DiscoveryStatusMessage
		{
			get => _discoveryStatusMessage;
			private set => SetProperty(ref _discoveryStatusMessage, value);
		}

		public string DiscoveryRawJson
		{
			get => _discoveryRawJson;
			private set
			{
				if (SetProperty(ref _discoveryRawJson, value ?? string.Empty))
				{
					_copyDiscoveryJsonCommand?.RaiseCanExecuteChanged();
					OnPropertyChanged(nameof(HasDiscoveryRawJson));
				}
			}
		}

		public string DiscoverySummaryJson
		{
			get => _discoverySummaryJson;
			private set => SetProperty(ref _discoverySummaryJson, value ?? string.Empty);
		}

		public bool HasDiscoveryRawJson => !string.IsNullOrWhiteSpace(DiscoveryRawJson);

		public ObservableCollection<string> CommissioningBlockingReasons => _commissioningBlockingReasons;

		public ObservableCollection<MachineOption> Machines => _machines;

		public MachineOption SelectedMachine
		{
			get => _selectedMachine;
			set
			{
				if (SetProperty(ref _selectedMachine, value))
				{
					_ = LoadSelectedMachineAsync();
				}
			}
		}

		public ObservableCollection<OutletOption> OutletOptions => _outletOptions;

		public OutletOption SelectedRecycleOutlet
		{
			get => _selectedRecycleOutlet;
			set => SetProperty(ref _selectedRecycleOutlet, value);
		}

		public double? TargetMachineSpeed
		{
			get => _targetMachineSpeed;
			set => SetProperty(ref _targetMachineSpeed, value);
		}

		public int? LaneCountSetting
		{
			get => _laneCountSetting;
			set => SetProperty(ref _laneCountSetting, value);
		}

		public double? TargetPercentage
		{
			get => _targetPercentage;
			set => SetProperty(ref _targetPercentage, value);
		}

		public double? TargetThroughputPreview
		{
			get => _targetThroughputPreview;
			private set => SetProperty(ref _targetThroughputPreview, value);
		}

		public ObservableCollection<GradeRow> GradeRows => _gradeRows;
		public ObservableCollection<CategoryOption> CategoryOptions => _categoryOptions;

		public async Task InitializeAsync()
		{
			await RefreshStatusAsync();
			await RefreshCommissioningAsync();
			await RefreshDiscoveryHistoryAsync();
			await RefreshMachinesAsync();
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
					DiscoverySnapshotCount = report.DiscoverySnapshotCount;
					LatestDiscoveryAt = report.LatestDiscoveryAt;
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

					await RefreshDiscoveryHistoryAsync(serialNo);
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

		private async Task RunDiscoveryAsync()
		{
			if (!EnsureConnectionStringPresent()) return;

			using (new BusyScope(this))
			{
				try
				{
					var runtimeSettings = _settingsProvider.Load();
					var config = new CollectorConfig(runtimeSettings);

					_discoveryCts = new CancellationTokenSource();
					_cancelDiscoveryCommand.RaiseCanExecuteChanged();

					var runner = new DiscoveryRunner();
					var snapshot = await runner.RunAsync(config, _discoveryCts.Token);

					if (snapshot == null || string.IsNullOrWhiteSpace(snapshot.SerialNo))
					{
						DiscoveryStatusMessage = "Discovery failed: serial number unavailable; snapshot not stored.";
						_discoveryCts = null;
						_cancelDiscoveryCommand.RaiseCanExecuteChanged();
						return;
					}

					var discoveryRepo = new MachineDiscoveryRepository(ConnectionString);
					var insertResult = await discoveryRepo.InsertSnapshotAsync(snapshot, _discoveryCts.Token);

					var commissioningRepo = new CommissioningRepository(ConnectionString);
					await commissioningRepo.MarkDiscoveredAsync(snapshot.SerialNo, insertResult.DiscoveredAt, _discoveryCts.Token);

					ApplyDiscoveryResult(snapshot, insertResult);

					await RefreshDiscoveryHistoryAsync(snapshot.SerialNo);
					await RefreshCommissioningAsync();

					_discoveryCts = null;
					_cancelDiscoveryCommand.RaiseCanExecuteChanged();
				}
				catch (Exception ex)
				{
					Logger.Log("Discovery run failed.", ex);
					DiscoveryStatusMessage = $"Discovery failed: {ex.Message}";
					_discoveryCts = null;
					_cancelDiscoveryCommand.RaiseCanExecuteChanged();
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
			DiscoveryStatusMessage = "Discovery not run yet.";
		}

		private void ApplyDiscoveryResult(MachineDiscoverySnapshot snapshot, InsertSnapshotResult insertResult)
		{
			DiscoverySnapshotId = insertResult?.Id;
			DiscoveryDiscoveredAt = insertResult?.DiscoveredAt;
			DiscoverySerial = snapshot?.SerialNo ?? string.Empty;
			DiscoveryMachineName = snapshot?.MachineName ?? string.Empty;

			var summary = snapshot?.Summary;
			DiscoveryOutletCount = summary?.OutletCount;
			DiscoveryOutletNames = summary?.OutletNames != null
				? string.Join(", ", LimitList(summary.OutletNames, 8))
				: string.Empty;
			DiscoveryLaneViewCount = summary?.LaneViewCount;
			DiscoveryGradeKeyCount = summary?.DistinctGradeKeys?.Count;
			DiscoverySizeKeyCount = summary?.DistinctSizeKeys?.Count;

			var tsText = DiscoveryDiscoveredAt.HasValue ? DiscoveryDiscoveredAt.Value.ToString("u") : "n/a";
			DiscoveryStatusMessage = snapshot?.Success == true
				? $"Discovery stored (ID {insertResult?.Id}) at {tsText}"
				: $"Discovery stored with errors (ID {insertResult?.Id}) at {tsText}";

			DiscoveryRawJson = snapshot != null
				? JsonConvert.SerializeObject(snapshot, Formatting.Indented)
				: string.Empty;
			DiscoverySummaryJson = snapshot?.Summary != null
				? JsonConvert.SerializeObject(snapshot.Summary, Formatting.Indented)
				: string.Empty;
		}

		private static System.Collections.Generic.IEnumerable<string> LimitList(System.Collections.Generic.IEnumerable<string> source, int max)
		{
			if (source == null) yield break;
			int count = 0;
			foreach (var item in source)
			{
				yield return item;
				count++;
				if (count >= max) yield break;
			}
		}

		private void CopyDiscoveryJson()
		{
			if (string.IsNullOrWhiteSpace(DiscoveryRawJson))
			{
				return;
			}

			try
			{
				Clipboard.SetText(DiscoveryRawJson);
				StatusMessage = "Raw discovery JSON copied to clipboard.";
			}
			catch (Exception ex)
			{
				StatusMessage = $"Failed to copy JSON: {ex.Message}";
			}
		}

		private void CancelDiscovery()
		{
			try
			{
				_discoveryCts?.Cancel();
			}
			finally
			{
				_cancelDiscoveryCommand.RaiseCanExecuteChanged();
			}
		}

		private async Task RefreshDiscoveryHistoryAsync(string serialOverride = null)
		{
			if (!EnsureConnectionStringPresent()) return;

			try
			{
				var repo = new MachineDiscoveryRepository(ConnectionString);
				var serial = !string.IsNullOrWhiteSpace(serialOverride)
					? serialOverride
					: (!string.IsNullOrWhiteSpace(CommissioningSerial) ? CommissioningSerial : null);

				var items = await repo.GetRecentSnapshotsAsync(serial, 10, CancellationToken.None);
				UpdateDiscoveryHistory(items);

				if (SelectedDiscoverySnapshot == null && _discoveryHistory.Count > 0)
				{
					SelectedDiscoverySnapshot = _discoveryHistory[0];
				}
			}
			catch (Exception ex)
			{
				StatusMessage = $"Discovery history refresh failed: {ex.Message}";
			}
		}

		private void UpdateDiscoveryHistory(System.Collections.Generic.IEnumerable<DiscoverySnapshotRecord> items)
		{
			var dispatcher = Application.Current?.Dispatcher;
			if (dispatcher != null && !dispatcher.CheckAccess())
			{
				dispatcher.Invoke(() => ApplyHistory(items));
			}
			else
			{
				ApplyHistory(items);
			}
		}

		private void ApplyHistory(System.Collections.Generic.IEnumerable<DiscoverySnapshotRecord> items)
		{
			_discoveryHistory.Clear();
			if (items != null)
			{
				foreach (var item in items)
				{
					_discoveryHistory.Add(item);
				}
			}
			OnPropertyChanged(nameof(DiscoveryHistory));
		}

		private void ApplyDiscoveryRecord(DiscoverySnapshotRecord record)
		{
			if (record == null)
			{
				return;
			}

			DiscoverySnapshotId = record.Id;
			DiscoveryDiscoveredAt = record.DiscoveredAt;
			DiscoverySerial = record.SerialNo ?? string.Empty;
			DiscoveryMachineName = record.MachineName ?? string.Empty;
			DiscoveryOutletCount = record.Summary?.OutletCount;
			DiscoveryOutletNames = record.Summary?.OutletNames != null
				? string.Join(", ", LimitList(record.Summary.OutletNames, 8))
				: string.Empty;
			DiscoveryLaneViewCount = record.Summary?.LaneViewCount;
			DiscoveryGradeKeyCount = record.Summary?.DistinctGradeKeys?.Count;
			DiscoverySizeKeyCount = record.Summary?.DistinctSizeKeys?.Count;

			DiscoveryStatusMessage = record.Success
				? $"Snapshot {record.Id} at {record.DiscoveredAt:u} (success)"
				: $"Snapshot {record.Id} at {record.DiscoveredAt:u} (failed): {record.ErrorText}";

			DiscoveryRawJson = string.IsNullOrWhiteSpace(record.RawPayloadJson)
				? string.Empty
				: record.RawPayloadJson;

			DiscoverySummaryJson = record.Summary != null
				? JsonConvert.SerializeObject(record.Summary, Formatting.Indented)
				: (string.IsNullOrWhiteSpace(record.RawSummaryJson) ? string.Empty : record.RawSummaryJson);
		}

		private async Task RefreshMachinesAsync()
		{
			if (!EnsureConnectionStringPresent()) return;
			try
			{
				var repo = new MachineSettingsRepository(ConnectionString);
				var machines = await repo.GetMachinesAsync(CancellationToken.None).ConfigureAwait(false);
				ApplyCollection(_machines, machines.Select(m => new MachineOption(m.SerialNo, m.Name)));
				if (SelectedMachine == null && _machines.Count > 0)
				{
					SelectedMachine = _machines[0];
				}
			}
			catch (Exception ex)
			{
				StatusMessage = $"Failed to load machines: {ex.Message}";
			}
		}

		private async Task LoadSelectedMachineAsync()
		{
			if (SelectedMachine == null || !EnsureConnectionStringPresent()) return;
			using (new BusyScope(this))
			{
				try
				{
					await LoadMachineSettingsAsync(SelectedMachine.SerialNo).ConfigureAwait(false);
					await LoadDiscoveryAssistAsync(SelectedMachine.SerialNo).ConfigureAwait(false);
					await LoadGradeOverridesAsync(SelectedMachine.SerialNo).ConfigureAwait(false);
					await RefreshThroughputPreviewAsync().ConfigureAwait(false);
				}
				catch (Exception ex)
				{
					StatusMessage = $"Failed to load machine data: {ex.Message}";
				}
			}
		}

		private async Task LoadMachineSettingsAsync(string serialNo)
		{
			var repo = new MachineSettingsRepository(ConnectionString);
			var settings = await repo.GetSettingsAsync(serialNo, CancellationToken.None).ConfigureAwait(false);
			TargetMachineSpeed = settings?.TargetMachineSpeed;
			LaneCountSetting = settings?.LaneCount;
			TargetPercentage = settings?.TargetPercentage;
			if (settings?.RecycleOutlet != null)
			{
				SelectedRecycleOutlet = _outletOptions.FirstOrDefault(o => o.Id == settings.RecycleOutlet) ?? new OutletOption(settings.RecycleOutlet.Value, $"Outlet {settings.RecycleOutlet.Value}");
			}
		}

		private async Task LoadDiscoveryAssistAsync(string serialNo)
		{
			var discoveryRepo = new MachineDiscoveryRepository(ConnectionString);
			var snapshot = await discoveryRepo.GetLatestSnapshotAsync(serialNo, CancellationToken.None).ConfigureAwait(false);

			ApplyCollection(_outletOptions, ParseOutlets(snapshot));

			var gradeKeys = ParseGradeKeys(snapshot);
			var repo = new MachineSettingsRepository(ConnectionString);
			var overrides = await repo.GetGradeOverridesAsync(serialNo, CancellationToken.None).ConfigureAwait(false);
			var rows = new List<GradeRow>();
			foreach (var key in gradeKeys.Union(overrides.Select(o => o.GradeKey), StringComparer.OrdinalIgnoreCase))
			{
				var resolved = await repo.ResolveCategoryAsync(serialNo, key, CancellationToken.None).ConfigureAwait(false);
				var existing = overrides.FirstOrDefault(o => string.Equals(o.GradeKey, key, StringComparison.OrdinalIgnoreCase));
				rows.Add(new GradeRow
				{
					GradeKey = key,
					ResolvedCategory = resolved,
					OverrideCategory = existing?.DesiredCat,
					IsActive = existing?.IsActive ?? true,
					HasExistingOverride = existing != null,
					OriginalDesiredCat = existing?.DesiredCat,
					OriginalIsActive = existing?.IsActive ?? false
				});
			}
			ApplyCollection(_gradeRows, rows.OrderBy(r => r.GradeKey, StringComparer.OrdinalIgnoreCase));
		}

		private async Task RefreshThroughputPreviewAsync()
		{
			if (SelectedMachine == null) return;
			var repo = new MachineSettingsRepository(ConnectionString);
			TargetThroughputPreview = await repo.GetTargetThroughputAsync(SelectedMachine.SerialNo, CancellationToken.None).ConfigureAwait(false);
		}

		private async Task SaveMachineSettingsAsync()
		{
			if (SelectedMachine == null)
			{
				StatusMessage = "Select a machine first.";
				return;
			}

			if (!TargetMachineSpeed.HasValue || !LaneCountSetting.HasValue || !TargetPercentage.HasValue || SelectedRecycleOutlet == null)
			{
				StatusMessage = "Fill target speed, lane count, target %, and recycle outlet.";
				return;
			}

			if (!EnsureConnectionStringPresent()) return;

			using (new BusyScope(this))
			{
				try
				{
					var repo = new MachineSettingsRepository(ConnectionString);
					await repo.UpsertSettingsAsync(
						SelectedMachine.SerialNo,
						TargetMachineSpeed.Value,
						LaneCountSetting.Value,
						TargetPercentage.Value,
						SelectedRecycleOutlet.Id,
						CancellationToken.None).ConfigureAwait(false);

					await RefreshThroughputPreviewAsync().ConfigureAwait(false);
					StatusMessage = "Machine settings saved.";
				}
				catch (Exception ex)
				{
					StatusMessage = $"Save machine settings failed: {ex.Message}";
				}
			}
		}

		private async Task SaveGradeOverridesAsync()
		{
			if (SelectedMachine == null)
			{
				StatusMessage = "Select a machine first.";
				return;
			}

			if (!EnsureConnectionStringPresent()) return;

			using (new BusyScope(this))
			{
				try
				{
					var repo = new MachineSettingsRepository(ConnectionString);
					foreach (var row in _gradeRows)
					{
						if (row.OverrideCategory.HasValue || row.HasExistingOverride)
						{
							var desired = row.OverrideCategory ?? row.OriginalDesiredCat ?? row.ResolvedCategory ?? 2;
							await repo.UpsertGradeOverrideAsync(
								SelectedMachine.SerialNo,
								row.GradeKey,
								desired,
								row.IsActive,
								Environment.UserName,
								CancellationToken.None).ConfigureAwait(false);
						}
					}

					await LoadGradeOverridesAsync(SelectedMachine.SerialNo).ConfigureAwait(false);
					StatusMessage = "Grade overrides saved.";
				}
				catch (Exception ex)
				{
					StatusMessage = $"Save grade overrides failed: {ex.Message}";
				}
			}
		}

		private async Task LoadGradeOverridesAsync(string serialNo)
		{
			var repo = new MachineSettingsRepository(ConnectionString);
			var overrides = await repo.GetGradeOverridesAsync(serialNo, CancellationToken.None).ConfigureAwait(false);

			foreach (var row in _gradeRows)
			{
				var existing = overrides.FirstOrDefault(o => string.Equals(o.GradeKey, row.GradeKey, StringComparison.OrdinalIgnoreCase));
				row.OverrideCategory = existing?.DesiredCat;
				row.IsActive = existing?.IsActive ?? row.IsActive;
				row.HasExistingOverride = existing != null;
				row.OriginalDesiredCat = existing?.DesiredCat;
				row.OriginalIsActive = existing?.IsActive ?? false;
				row.ResolvedCategory = await repo.ResolveCategoryAsync(serialNo, row.GradeKey, CancellationToken.None).ConfigureAwait(false);
			}

			OnPropertyChanged(nameof(GradeRows));
		}

		private static IList<OutletOption> ParseOutlets(MachineDiscoverySnapshot snapshot)
		{
			var results = new List<OutletOption>();
			try
			{
				var token = snapshot?.Payloads != null && snapshot.Payloads.TryGetValue("outlets_details", out var outlets) ? outlets : null;
				var array = NormalizeToken(token) as Newtonsoft.Json.Linq.JArray;
				if (array == null) return results;
				foreach (var item in array)
				{
					var id = item?["Id"]?.ToObject<int?>();
					var name = item?["Name"]?.ToObject<string>();
					if (id.HasValue)
					{
						results.Add(new OutletOption(id.Value, string.IsNullOrWhiteSpace(name) ? $"Outlet {id}" : name));
					}
				}
			}
			catch { }
			return results;
		}

		private static HashSet<string> ParseGradeKeys(MachineDiscoverySnapshot snapshot)
		{
			var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			try
			{
				var token = snapshot?.Payloads != null && snapshot.Payloads.TryGetValue("lanes_grade_fpm", out var grades) ? grades : null;
				var array = NormalizeToken(token) as Newtonsoft.Json.Linq.JArray;
				if (array == null) return keys;
				foreach (var lane in array)
				{
					if (lane is Newtonsoft.Json.Linq.JObject obj)
					{
						foreach (var prop in obj.Properties())
						{
							if (!string.IsNullOrWhiteSpace(prop.Name))
							{
								keys.Add(prop.Name);
							}
						}
					}
				}
			}
			catch { }
			return keys;
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
			_copyDiscoveryJsonCommand.RaiseCanExecuteChanged();
			_refreshMachinesCommand.RaiseCanExecuteChanged();
			_saveMachineSettingsCommand.RaiseCanExecuteChanged();
			_saveGradeOverridesCommand.RaiseCanExecuteChanged();
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

		private static void ApplyCollection<T>(ObservableCollection<T> target, System.Collections.Generic.IEnumerable<T> source)
		{
			target.Clear();
			if (source == null) return;
			foreach (var item in source)
			{
				target.Add(item);
			}
		}

		private static Newtonsoft.Json.Linq.JToken NormalizeToken(Newtonsoft.Json.Linq.JToken token)
		{
			if (token is Newtonsoft.Json.Linq.JValue val && val.Type == Newtonsoft.Json.Linq.JTokenType.String)
			{
				var s = val.ToObject<string>();
				if (!string.IsNullOrWhiteSpace(s))
				{
					try { return Newtonsoft.Json.Linq.JToken.Parse(s); }
					catch { return token; }
				}
			}
			return token;
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

		public sealed class MachineOption
		{
			public MachineOption(string serialNo, string name)
			{
				SerialNo = serialNo;
				Name = name;
			}

			public string SerialNo { get; }
			public string Name { get; }
			public string Display => string.IsNullOrWhiteSpace(Name) ? SerialNo : $"{SerialNo} — {Name}";
		}

		public sealed class OutletOption
		{
			public OutletOption(int id, string name)
			{
				Id = id;
				Name = name;
			}

			public int Id { get; }
			public string Name { get; }
			public string Display => $"{Id} {Name}";
		}

		public sealed class CategoryOption
		{
			public CategoryOption(int id, string name)
			{
				Id = id;
				Name = name;
			}

			public int Id { get; }
			public string Name { get; }
		}

		public sealed class GradeRow : INotifyPropertyChanged
		{
			private int? _overrideCategory;
			private bool _isActive;
			private int? _resolvedCategory;

			public string GradeKey { get; set; }
			public int? ResolvedCategory
			{
				get => _resolvedCategory;
				set
				{
					if (_resolvedCategory != value)
					{
						_resolvedCategory = value;
						OnPropertyChanged(nameof(ResolvedCategory));
					}
				}
			}
			public int? OverrideCategory
			{
				get => _overrideCategory;
				set
				{
					if (_overrideCategory != value)
					{
						_overrideCategory = value;
						OnPropertyChanged(nameof(OverrideCategory));
					}
				}
			}
			public bool IsActive
			{
				get => _isActive;
				set
				{
					if (_isActive != value)
					{
						_isActive = value;
						OnPropertyChanged(nameof(IsActive));
					}
				}
			}
			public bool HasExistingOverride { get; set; }
			public int? OriginalDesiredCat { get; set; }
			public bool OriginalIsActive { get; set; }

			public event PropertyChangedEventHandler PropertyChanged;
			private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
		}
	}
}

