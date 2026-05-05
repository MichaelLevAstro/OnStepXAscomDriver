using System;
using System.Text;
using System.Windows;
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
                if (e.OldValue is ConsoleViewModel oldVm)
                {
                    oldVm.AutoScrollRequested -= OnAutoScroll;
                    oldVm.CopySelectedRequested -= OnCopySelected;
                }
                if (e.NewValue is ConsoleViewModel newVm)
                {
                    newVm.AutoScrollRequested += OnAutoScroll;
                    newVm.CopySelectedRequested += OnCopySelected;
                }
            };
        }

        private void OnAutoScroll(object sender, EventArgs e)
        {
            if (LogList.Items.Count == 0) return;
            LogList.ScrollIntoView(LogList.Items[LogList.Items.Count - 1]);
        }

        // Invoked from VM via CopySelectedRequested. SelectedItems lives on
        // the view (ListBox); VM has no visibility into selection state.
        private void OnCopySelected(object sender, EventArgs e)
        {
            var sb = new StringBuilder();
            foreach (var item in LogList.SelectedItems)
                if (item is ConsoleEntry ce && !string.IsNullOrEmpty(ce.Raw))
                    sb.AppendLine(ce.Raw);
            if (sb.Length == 0) return;
            try { Clipboard.SetText(sb.ToString()); } catch { }
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
