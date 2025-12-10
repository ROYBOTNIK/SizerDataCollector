using System.ComponentModel;

namespace SizerDataCollector.GUI.WPF.ViewModels
{
	public sealed class SettingsViewModel : INotifyPropertyChanged
	{
		public event PropertyChangedEventHandler PropertyChanged;

		public string Placeholder => "Settings and configuration tools will be added here.";
	}
}

