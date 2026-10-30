using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using ASCOM.OnStepX.ViewModels;

namespace ASCOM.OnStepX.Views.Tabs
{
    public partial class SetupTab : UserControl
    {
        public SetupTab() => AvaloniaXamlLoader.Load(this);

        // WPF refreshed the port list when the combo's dropdown opened.
        // AutoCompleteBox has no such event, so focus is the trigger instead —
        // same purpose, keeping the list current when an adapter is plugged in
        // after the hub started.
        private void ComPortFocused(object sender, RoutedEventArgs e)
        {
            (DataContext as MainViewModel)?.Connection?.RefreshSerialPorts();
        }
    }
}
