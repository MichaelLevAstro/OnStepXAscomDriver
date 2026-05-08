using System.Windows;
using ASCOM.OnStepX.ViewModels;

namespace ASCOM.OnStepX.Views
{
    public partial class RotatorAdvancedWindow : Window
    {
        public RotatorAdvancedViewModel VM { get; }

        public RotatorAdvancedWindow(RotatorViewModel rotator)
        {
            InitializeComponent();
            VM = new RotatorAdvancedViewModel(rotator);
            DataContext = VM;
            try { Icon = WindowIconLoader.LoadImageSource(); } catch { }
        }

        private void OnOk(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }
    }
}
