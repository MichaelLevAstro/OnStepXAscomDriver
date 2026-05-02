using System.Windows;
using ASCOM.OnStepX.ViewModels;

namespace ASCOM.OnStepX.Views
{
    public partial class AdvancedSettingsWindow : Window
    {
        public AdvancedSettingsViewModel VM { get; }

        public AdvancedSettingsWindow(MainViewModel main)
        {
            InitializeComponent();
            VM = new AdvancedSettingsViewModel(main);
            DataContext = VM;
            try { Icon = WindowIconLoader.LoadImageSource(); } catch { }
        }

        private void OnOk(object sender, RoutedEventArgs e)
        {
            if (VM.Apply()) { DialogResult = true; Close(); }
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
