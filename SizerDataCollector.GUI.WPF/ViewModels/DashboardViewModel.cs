using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SizerDataCollector.GUI.WPF.ViewModels
{
	public sealed class DashboardViewModel : INotifyPropertyChanged
	{
		private CollectorStatusViewModel _collector;

		public event PropertyChangedEventHandler PropertyChanged;

		public DashboardViewModel()
		{
		}

		public DashboardViewModel(CollectorStatusViewModel collector)
		{
			AttachCollector(collector);
		}

		public string OverviewTitle => "Dashboard";

		public string OverviewDescription => "High-level view of collector health and navigation shortcuts.";

		public string ServiceStatus => _collector?.ServiceStatus ?? "Unknown";

		public string LastPoll => _collector?.LastPollDisplay ?? "—";

		public string LastError => _collector?.LastErrorDisplay ?? "—";

		public void AttachCollector(CollectorStatusViewModel collector)
		{
			if (collector == null)
			{
				throw new ArgumentNullException(nameof(collector));
			}

			if (_collector == collector)
			{
				return;
			}

			if (_collector != null)
			{
				_collector.PropertyChanged -= CollectorOnPropertyChanged;
			}

			_collector = collector;
			_collector.PropertyChanged += CollectorOnPropertyChanged;
			RaiseSnapshotChanges();
		}

		public void Refresh()
		{
			RaiseSnapshotChanges();
		}

		private void CollectorOnPropertyChanged(object sender, PropertyChangedEventArgs e)
		{
			if (e.PropertyName == nameof(CollectorStatusViewModel.ServiceStatus))
			{
				OnPropertyChanged(nameof(ServiceStatus));
			}

			if (e.PropertyName == nameof(CollectorStatusViewModel.LastPollDisplay))
			{
				OnPropertyChanged(nameof(LastPoll));
			}

			if (e.PropertyName == nameof(CollectorStatusViewModel.LastErrorDisplay))
			{
				OnPropertyChanged(nameof(LastError));
			}
		}

		private void RaiseSnapshotChanges()
		{
			OnPropertyChanged(nameof(ServiceStatus));
			OnPropertyChanged(nameof(LastPoll));
			OnPropertyChanged(nameof(LastError));
		}

		private void OnPropertyChanged(string propertyName)
		{
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
		}
	}
}

