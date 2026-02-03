using System.ComponentModel;

namespace SizerDataCollector.GUI.WPF.ViewModels
{
	public class LaneToolsHomeViewModel : INotifyPropertyChanged
	{
		public event PropertyChangedEventHandler PropertyChanged;

		public string Title { get; } = "Lane Tools – Home";
	}
}

