using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace SizerDataCollector.GUI.WPF.Commands
{
	public sealed class RelayCommand : ICommand
	{
		private readonly Action<object> _execute;
		private readonly Func<object, bool> _canExecute;

		public RelayCommand(Action<object> execute, Func<object, bool> canExecute)
		{
			_execute = execute ?? throw new ArgumentNullException(nameof(execute));
			_canExecute = canExecute ?? (_ => true);
		}

		public event EventHandler CanExecuteChanged;

		public bool CanExecute(object parameter)
		{
			return _canExecute(parameter);
		}

		public void Execute(object parameter)
		{
			_execute(parameter);
		}

		public void RaiseCanExecuteChanged()
		{
			var handler = CanExecuteChanged;
			if (handler == null)
			{
				return;
			}

			var dispatcher = Application.Current != null ? Application.Current.Dispatcher : null;
			if (dispatcher != null && !dispatcher.CheckAccess())
			{
				dispatcher.BeginInvoke(
					DispatcherPriority.Normal,
					new Action(() => handler(this, EventArgs.Empty)));
			}
			else
			{
				handler(this, EventArgs.Empty);
			}
		}
	}
}

