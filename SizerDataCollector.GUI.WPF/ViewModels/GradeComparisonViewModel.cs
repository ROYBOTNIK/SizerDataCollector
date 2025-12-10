using System.ComponentModel;

namespace SizerDataCollector.GUI.WPF.ViewModels
{
	public class GradeComparisonViewModel : INotifyPropertyChanged
	{
		public event PropertyChangedEventHandler PropertyChanged;

		public string Title { get; } = "Lane Grade Comparison";
	}
}

