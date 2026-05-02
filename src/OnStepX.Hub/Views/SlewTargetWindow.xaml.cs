using System.Windows;
using ASCOM.OnStepX.ViewModels;

namespace ASCOM.OnStepX.Views
{
    public partial class SlewTargetWindow : Window
    {
        public SlewTargetViewModel VM { get; }

        public SlewTargetWindow(MainViewModel main)
        {
            InitializeComponent();
            VM = new SlewTargetViewModel(main) { CloseAction = Close };
            DataContext = VM;
            try { Icon = WindowIconLoader.LoadImageSource(); } catch { }
            Closed += (s, e) => VM.Detach();
        }

        private void OnClose(object sender, RoutedEventArgs e) => Close();
    }
}
