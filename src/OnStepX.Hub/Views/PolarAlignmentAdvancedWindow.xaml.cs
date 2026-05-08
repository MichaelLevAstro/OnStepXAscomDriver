using System.Windows;
using ASCOM.OnStepX.ViewModels;

namespace ASCOM.OnStepX.Views
{
    public partial class PolarAlignmentAdvancedWindow : Window
    {
        public PolarAlignmentAdvancedViewModel VM { get; }

        public PolarAlignmentAdvancedWindow(PolarAlignmentViewModel paVm)
        {
            InitializeComponent();
            VM = new PolarAlignmentAdvancedViewModel(paVm);
            DataContext = VM;
            try { Icon = WindowIconLoader.LoadImageSource(); } catch { }
        }

        private void OnOk(object sender, RoutedEventArgs e)
        {
            VM.Apply();
            DialogResult = true;
            Close();
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
