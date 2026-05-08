using System.Windows.Controls;
using ASCOM.OnStepX.ViewModels;

namespace ASCOM.OnStepX.Views.Tabs
{
    public partial class SetupTab : UserControl
    {
        public SetupTab() { InitializeComponent(); }

        // Refresh COM port list when the dropdown opens. Cheap call; keeps the
        // list current when adapters are hot-plugged after the hub launched.
        private void ComPortDropDownOpened(object sender, System.EventArgs e)
        {
            (DataContext as MainViewModel)?.Connection?.RefreshSerialPorts();
        }
    }
}
