using System;
using System.Windows.Input;

namespace ASCOM.OnStepX.ViewModels
{
    public sealed class RelayCommand : ICommand
    {
        private readonly Action<object> _execute;
        private readonly Predicate<object> _canExecute;

        public RelayCommand(Action execute, Func<bool> canExecute = null)
            : this(_ => execute(), canExecute == null ? (Predicate<object>)null : _ => canExecute()) { }

        public RelayCommand(Action<object> execute, Predicate<object> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object parameter) => _canExecute == null || _canExecute(parameter);
        public void Execute(object parameter) => _execute(parameter);

        // Was wired straight to WPF's CommandManager.RequerySuggested. It still
        // is on Windows — the WPF host installs a requery source that forwards
        // to it, so buttons keep re-evaluating CanExecute after input the way
        // they always have. See CommandRequery.
        public event EventHandler CanExecuteChanged
        {
            add { CommandRequery.Source.Subscribe(value); }
            remove { CommandRequery.Source.Unsubscribe(value); }
        }

        public void RaiseCanExecuteChanged() => CommandRequery.Invalidate();
    }
}
