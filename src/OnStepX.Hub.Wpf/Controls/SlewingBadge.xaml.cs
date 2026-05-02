using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace ASCOM.OnStepX.Controls
{
    public partial class SlewingBadge : UserControl
    {
        public SlewingBadge()
        {
            InitializeComponent();
            Loaded += (s, e) =>
            {
                ((Storyboard)Resources["Radar1"]).Begin(this, true);
                ((Storyboard)Resources["Radar2"]).Begin(this, true);
            };
            Unloaded += (s, e) =>
            {
                try { ((Storyboard)Resources["Radar1"]).Stop(this); } catch { }
                try { ((Storyboard)Resources["Radar2"]).Stop(this); } catch { }
            };
        }

        public static readonly DependencyProperty CoordProperty = DependencyProperty.Register(
            nameof(Coord), typeof(string), typeof(SlewingBadge), new PropertyMetadata(""));

        public string Coord { get => (string)GetValue(CoordProperty); set => SetValue(CoordProperty, value); }
    }
}
