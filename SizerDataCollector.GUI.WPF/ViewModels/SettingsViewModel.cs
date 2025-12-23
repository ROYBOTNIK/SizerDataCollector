using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows;
using System.Windows.Threading;
using SizerDataCollector.Core.Config;
using SizerDataCollector.Core.Db;
using SizerDataCollector.GUI.WPF.Commands;

namespace SizerDataCollector.GUI.WPF.ViewModels
{
	public sealed class SettingsViewModel : INotifyPropertyChanged
	{
		private readonly CollectorSettingsProvider _settingsProvider;
		private readonly RelayCommand _initializeSqlFolderCommand;
		private readonly RelayCommand _bootstrapDatabaseCommand;
		private readonly RelayCommand _refreshStatusCommand;

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

		public SettingsViewModel(CollectorSettingsProvider settingsProvider)
		{
			_settingsProvider = settingsProvider ?? throw new ArgumentNullException(nameof(settingsProvider));
			_connectionString = LoadConnectionString();

			_initializeSqlFolderCommand = new RelayCommand(async _ => await InitializeSqlFolderAsync(), _ => CanRunActions);
			_bootstrapDatabaseCommand = new RelayCommand(async _ => await BootstrapDatabaseAsync(), _ => CanRunActions);
			_refreshStatusCommand = new RelayCommand(async _ => await RefreshStatusAsync(), _ => CanRunActions);
		}

		public event PropertyChangedEventHandler PropertyChanged;

		public ICommand InitializeSqlFolderCommand => _initializeSqlFolderCommand;
		public ICommand BootstrapDatabaseCommand => _bootstrapDatabaseCommand;
		public ICommand RefreshStatusCommand => _refreshStatusCommand;

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

		public bool HasMissingObjects => _missingObjects.Count > 0;

		public bool SeedPresent => BandDefinitionsCount > 0 && MachineThresholdsCount > 0 && ShiftCalendarCount > 0;

		public bool CanRunActions => !IsBusy && !string.IsNullOrWhiteSpace(ConnectionString);

		public async Task InitializeAsync()
		{
			await RefreshStatusAsync().ConfigureAwait(false);
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

					var missingItems = BuildMissingList(report);
					UpdateMissingCollection(missingItems);

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

		private static System.Collections.Generic.List<string> BuildMissingList(DbHealthReport report)
		{
			var list = new System.Collections.Generic.List<string>();
			void add(System.Collections.Generic.IEnumerable<string> items, string prefix)
			{
				if (items == null) return;
				foreach (var item in items)
				{
					list.Add($"{prefix}: {item}");
				}
			}

			add(report.MissingTables, "Table");
			add(report.MissingFunctions, "Function");
			add(report.MissingContinuousAggregates, "Continuous Aggregate");
			add(report.MissingPolicies, "Refresh Policy");
			return list;
		}

		private void UpdateMissingCollection(System.Collections.Generic.IEnumerable<string> items)
		{
			var dispatcher = Application.Current?.Dispatcher;
			if (dispatcher != null && !dispatcher.CheckAccess())
			{
				dispatcher.Invoke(() => ApplyMissing(items));
			}
			else
			{
				ApplyMissing(items);
			}
		}

		private void ApplyMissing(System.Collections.Generic.IEnumerable<string> items)
		{
			_missingObjects.Clear();
			if (items != null)
			{
				foreach (var item in items)
				{
					_missingObjects.Add(item);
				}
			}
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

