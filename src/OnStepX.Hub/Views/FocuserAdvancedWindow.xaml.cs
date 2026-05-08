using System.Windows;
using ASCOM.OnStepX.ViewModels;

namespace ASCOM.OnStepX.Views
{
    public partial class FocuserAdvancedWindow : Window
    {
        public FocuserAdvancedViewModel VM { get; }

        public FocuserAdvancedWindow(FocuserViewModel focuser)
        {
            InitializeComponent();
            VM = new FocuserAdvancedViewModel(focuser);
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
