using System.ComponentModel;

namespace SizerDataCollector.GUI.WPF.ViewModels
{
	public class LaneConsistencyViewModel : INotifyPropertyChanged
	{
		public event PropertyChangedEventHandler PropertyChanged;

		public string Title { get; } = "Lane Consistency Analysis";
	}
}

