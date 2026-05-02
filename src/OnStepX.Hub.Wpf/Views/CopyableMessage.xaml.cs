using System;
using System.Windows;

namespace ASCOM.OnStepX.Views
{
    // WPF replacement for Ui/CopyableMessage.cs. Static Show factory mirrors
    // the legacy hub's call sites.
    public partial class CopyableMessage : Window
    {
        public CopyableMessage(string title, string body)
        {
            InitializeComponent();
            TitleText.Text = title ?? "OnStepX";
            Title = title ?? "OnStepX";
            BodyText.Text = body ?? "";
            try { Icon = WindowIconLoader.LoadImageSource(); } catch { }
        }

        public static void Show(string title, string body)
        {
            try
            {
                var w = new CopyableMessage(title, body)
                {
                    Owner = Application.Current?.MainWindow
                };
                w.ShowDialog();
            }
            catch
            {
                // Fallback to plain MessageBox if for any reason the WPF
                // dispatcher isn't running yet.
                MessageBox.Show(body, title);
            }
        }

        private void OnCopy(object sender, RoutedEventArgs e)
        {
            try { Clipboard.SetText(BodyText.Text); } catch { }
        }

        private void OnClose(object sender, RoutedEventArgs e) => Close();
    }
}
