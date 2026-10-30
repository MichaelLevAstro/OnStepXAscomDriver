using System;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using ASCOM.OnStepX.ViewModels;

namespace ASCOM.OnStepX.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            AvaloniaXamlLoader.Load(this);
#if DEBUG
            this.AttachDevTools();
#endif
        }

        // The WPF hub does this from its Loaded handler. Opened is Avalonia's
        // equivalent: the window exists and the dispatcher is running, so a
        // connect attempt can raise a dialog if it needs to.
        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);
            if (DataContext is MainViewModel vm) vm.TryAutoConnect();
        }
    }
}
