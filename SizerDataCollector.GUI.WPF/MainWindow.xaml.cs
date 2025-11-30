using System.Windows;
using SizerDataCollector.GUI.WPF.ViewModels;
using SizerDataCollector.Config;

namespace SizerDataCollector.GUI.WPF
{
	public partial class MainWindow : Window
	{
		public MainWindowViewModel ViewModel { get; }

		public MainWindow()
		{
			InitializeComponent();
			var settingsProvider = new CollectorSettingsProvider();
			ViewModel = new MainWindowViewModel(settingsProvider)
			{
				ShowMessage = ShowMessage
			};
			DataContext = ViewModel;
			Loaded += (_, __) => ViewModel.Initialize();
		}

		private void ShowMessage(string message, string caption, MessageBoxImage image)
		{
			MessageBox.Show(this, message, caption, MessageBoxButton.OK, image);
		}
	}
}
