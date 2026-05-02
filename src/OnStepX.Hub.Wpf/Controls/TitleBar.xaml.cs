using System.Windows.Controls;
using System.Windows.Shell;

namespace ASCOM.OnStepX.Controls
{
    public partial class TitleBar : UserControl
    {
        public TitleBar()
        {
            InitializeComponent();
            // Buttons sit inside WindowChrome's caption strip — opt them out of
            // chrome hit-testing so clicks reach the Button rather than the
            // window-drag handler.
            Loaded += (s, e) =>
            {
                foreach (var b in FindButtonsRecursive(this))
                    b.SetValue(WindowChrome.IsHitTestVisibleInChromeProperty, true);
            };
        }

        private static System.Collections.Generic.IEnumerable<Button> FindButtonsRecursive(System.Windows.DependencyObject root)
        {
            int n = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < n; i++)
            {
                var c = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
                if (c is Button btn) yield return btn;
                foreach (var r in FindButtonsRecursive(c)) yield return r;
            }
        }
    }
}
