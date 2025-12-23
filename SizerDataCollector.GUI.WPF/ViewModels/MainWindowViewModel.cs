using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using SizerDataCollector.GUI.WPF.Commands;

namespace SizerDataCollector.GUI.WPF.ViewModels
{
	public sealed class MainWindowViewModel : INotifyPropertyChanged
	{
		private object _currentPage;

		public MainWindowViewModel(
			CollectorStatusViewModel collectorStatusViewModel,
			DashboardViewModel dashboardViewModel,
			SettingsViewModel settingsViewModel,
			LaneToolsHomeViewModel laneToolsHomeViewModel,
			LaneConsistencyViewModel laneConsistencyViewModel,
			GradeComparisonViewModel gradeComparisonViewModel)
		{
			CollectorStatusPage = collectorStatusViewModel ?? throw new ArgumentNullException(nameof(collectorStatusViewModel));
			DashboardPage = dashboardViewModel ?? throw new ArgumentNullException(nameof(dashboardViewModel));
			SettingsPage = settingsViewModel ?? throw new ArgumentNullException(nameof(settingsViewModel));
			LaneToolsHomePage = laneToolsHomeViewModel ?? throw new ArgumentNullException(nameof(laneToolsHomeViewModel));
			LaneConsistencyPage = laneConsistencyViewModel ?? throw new ArgumentNullException(nameof(laneConsistencyViewModel));
			GradeComparisonPage = gradeComparisonViewModel ?? throw new ArgumentNullException(nameof(gradeComparisonViewModel));

			DashboardPage.AttachCollector(CollectorStatusPage);

			ShowDashboardCommand = new RelayCommand(_ => CurrentPage = DashboardPage);
			ShowCollectorStatusCommand = new RelayCommand(_ => CurrentPage = CollectorStatusPage);
			ShowSettingsCommand = new RelayCommand(_ => CurrentPage = SettingsPage);
			ShowLaneToolsHomeCommand = new RelayCommand(_ => CurrentPage = LaneToolsHomePage);
			ShowLaneConsistencyCommand = new RelayCommand(_ => CurrentPage = LaneConsistencyPage);
			ShowGradeComparisonCommand = new RelayCommand(_ => CurrentPage = GradeComparisonPage);

			CurrentPage = DashboardPage;

			CollectorStatusPage.PropertyChanged += CollectorStatusPropertyChanged;
		}

		public event PropertyChangedEventHandler PropertyChanged;

		public string Title => "Sizer Data Collector";

		public string HeaderMachineName => Environment.MachineName;

		public string HeaderServiceStatus => CollectorStatusPage?.ServiceStatus ?? "Unknown";

		public string HeaderLastPoll => CollectorStatusPage?.LastPollDisplay ?? "—";

		public string LastErrorSummary => CollectorStatusPage?.LastErrorDisplay ?? "—";

		public string ServiceStatusSummary => HeaderServiceStatus;

		public string LastPollSummary => HeaderLastPoll;

		public object CurrentPage
		{
			get => _currentPage;
			private set => SetProperty(ref _currentPage, value);
		}

		public CollectorStatusViewModel CollectorStatusPage { get; }

		public DashboardViewModel DashboardPage { get; }

		public SettingsViewModel SettingsPage { get; }

		public LaneToolsHomeViewModel LaneToolsHomePage { get; }

		public LaneConsistencyViewModel LaneConsistencyPage { get; }

		public GradeComparisonViewModel GradeComparisonPage { get; }

		public ICommand ShowDashboardCommand { get; }

		public ICommand ShowCollectorStatusCommand { get; }

		public ICommand ShowSettingsCommand { get; }

		public ICommand ShowLaneToolsHomeCommand { get; }

		public ICommand ShowLaneConsistencyCommand { get; }

		public ICommand ShowGradeComparisonCommand { get; }

		public void Initialize()
		{
			CollectorStatusPage?.Initialize();
			DashboardPage?.Refresh();
			if (SettingsPage != null)
			{
				_ = SettingsPage.InitializeAsync();
			}
		}

		public void Shutdown()
		{
			CollectorStatusPage?.Shutdown();
		}

		private void CollectorStatusPropertyChanged(object sender, PropertyChangedEventArgs e)
		{
			if (e.PropertyName == nameof(CollectorStatusViewModel.ServiceStatus))
			{
				OnPropertyChanged(nameof(HeaderServiceStatus));
				OnPropertyChanged(nameof(ServiceStatusSummary));
			}

			if (e.PropertyName == nameof(CollectorStatusViewModel.LastPollDisplay))
			{
				OnPropertyChanged(nameof(HeaderLastPoll));
				OnPropertyChanged(nameof(LastPollSummary));
			}

			if (e.PropertyName == nameof(CollectorStatusViewModel.LastErrorDisplay))
			{
				OnPropertyChanged(nameof(LastErrorSummary));
			}
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

		private void OnPropertyChanged(string propertyName)
		{
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
		}
	}
}

