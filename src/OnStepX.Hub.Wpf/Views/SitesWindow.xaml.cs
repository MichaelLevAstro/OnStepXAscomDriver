using System.Windows;
using ASCOM.OnStepX.Config;
using ASCOM.OnStepX.ViewModels;

namespace ASCOM.OnStepX.Views
{
    public partial class SitesWindow : Window
    {
        public SitesWindowViewModel VM { get; }
        internal Site AppliedSite => VM.AppliedSite;

        public SitesWindow(MainViewModel main, bool connected)
        {
            InitializeComponent();
            VM = new SitesWindowViewModel(main, connected) { CloseAction = ok => CloseWith(ok) };
            DataContext = VM;
            try { Icon = WindowIconLoader.LoadImageSource(); } catch { }
        }

        private void CloseWith(bool ok)
        {
            DialogResult = ok;
            Close();
        }

        private void OnCancelClose(object sender, RoutedEventArgs e) => CloseWith(false);
    }
}
