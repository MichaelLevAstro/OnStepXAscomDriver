using System.Windows;
using ASCOM.OnStepX.ViewModels;

namespace ASCOM.OnStepX.Views
{
    public partial class UpdateDialog : Window
    {
        public UpdateDialog(UpdateViewModel vm)
        {
            InitializeComponent();
            DataContext = vm;
            try { Icon = WindowIconLoader.LoadImageSource(); } catch { }
        }

        private void OnClose(object sender, RoutedEventArgs e) => Close();
    }
}
