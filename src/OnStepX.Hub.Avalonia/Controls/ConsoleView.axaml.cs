using System;
using System.Text;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using ASCOM.OnStepX.Platform;
using ASCOM.OnStepX.ViewModels;

namespace ASCOM.OnStepX.Controls
{
    public partial class ConsoleView : UserControl
    {
        private ListBox _log;

        public ConsoleView()
        {
            AvaloniaXamlLoader.Load(this);
            _log = this.FindControl<ListBox>("LogList");

            DataContextChanged += (s, e) =>
            {
                if (_current != null)
                {
                    _current.AutoScrollRequested -= OnAutoScroll;
                    _current.CopySelectedRequested -= OnCopySelected;
                }
                _current = DataContext as ConsoleViewModel;
                if (_current != null)
                {
                    _current.AutoScrollRequested += OnAutoScroll;
                    _current.CopySelectedRequested += OnCopySelected;
                }
            };
        }

        // Avalonia's DataContextChanged carries no old/new values, so the
        // previous one has to be remembered to unsubscribe from it.
        private ConsoleViewModel _current;

        private void OnAutoScroll(object sender, EventArgs e)
        {
            if (_log?.ItemCount is not > 0) return;
            _log.ScrollIntoView(_log.ItemCount - 1);
        }

        // Raised by the view model: selection lives on the ListBox, which the
        // view model cannot see.
        private void OnCopySelected(object sender, EventArgs e)
        {
            if (_log?.SelectedItems == null) return;

            var sb = new StringBuilder();
            foreach (var item in _log.SelectedItems)
                if (item is ConsoleEntry ce && !string.IsNullOrEmpty(ce.Raw))
                    sb.AppendLine(ce.Raw);

            if (sb.Length == 0) return;
            Ui.Shell.SetClipboardText(sb.ToString());
        }

        private void OnCmdInputKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            if (DataContext is ConsoleViewModel vm && vm.SendCommand.CanExecute(null))
            {
                vm.SendCommand.Execute(null);
                e.Handled = true;
            }
        }
    }
}
