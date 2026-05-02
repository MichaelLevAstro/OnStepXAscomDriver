using System;
using System.Windows.Controls;
using System.Windows.Input;
using ASCOM.OnStepX.ViewModels;

namespace ASCOM.OnStepX.Controls
{
    public partial class ConsoleView : UserControl
    {
        public ConsoleView()
        {
            InitializeComponent();
            DataContextChanged += (s, e) =>
            {
                if (e.OldValue is ConsoleViewModel oldVm) oldVm.AutoScrollRequested -= OnAutoScroll;
                if (e.NewValue is ConsoleViewModel newVm) newVm.AutoScrollRequested += OnAutoScroll;
            };
        }

        private void OnAutoScroll(object sender, EventArgs e) => LogScroll.ScrollToBottom();

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
