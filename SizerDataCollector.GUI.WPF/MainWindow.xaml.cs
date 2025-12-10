using System.Windows;
using SizerDataCollector.Core.Config;
using SizerDataCollector.GUI.WPF.ViewModels;

namespace SizerDataCollector.GUI.WPF
{
	public partial class MainWindow : Window
	{
		public MainWindowViewModel ViewModel { get; }

		public MainWindow()
		{
			InitializeComponent();
			var settingsProvider = new CollectorSettingsProvider();
			var collectorStatusViewModel = new CollectorStatusViewModel(settingsProvider)
			{
				ShowMessage = ShowMessage
			};

			var dashboardViewModel = new DashboardViewModel(collectorStatusViewModel);
			var settingsViewModel = new SettingsViewModel();
			var laneToolsHomeViewModel = new LaneToolsHomeViewModel();
			var laneConsistencyViewModel = new LaneConsistencyViewModel();
			var gradeComparisonViewModel = new GradeComparisonViewModel();

			ViewModel = new MainWindowViewModel(
				collectorStatusViewModel,
				dashboardViewModel,
				settingsViewModel,
				laneToolsHomeViewModel,
				laneConsistencyViewModel,
				gradeComparisonViewModel);
			DataContext = ViewModel;

			Loaded += (_, __) => ViewModel.Initialize();
			Closed += (_, __) => ViewModel.Shutdown();
		}

		private void ShowMessage(string message, string caption, MessageBoxImage image)
		{
			MessageBox.Show(this, message, caption, MessageBoxButton.OK, image);
		}
	}
}
