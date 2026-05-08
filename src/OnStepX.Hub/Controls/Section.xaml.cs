using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using ASCOM.OnStepX.Config;

namespace ASCOM.OnStepX.Controls
{
    [ContentProperty(nameof(Body))]
    public partial class Section : UserControl
    {
        private bool _loaded;

        public Section()
        {
            InitializeComponent();
            UpdateVisuals();
            Loaded += OnLoaded;
        }

        public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
            nameof(Title), typeof(string), typeof(Section), new PropertyMetadata(""));
        public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }

        public static readonly DependencyProperty IconGeometryProperty = DependencyProperty.Register(
            nameof(IconGeometry), typeof(Geometry), typeof(Section), new PropertyMetadata(null));
        public Geometry IconGeometry { get => (Geometry)GetValue(IconGeometryProperty); set => SetValue(IconGeometryProperty, value); }

        public static readonly DependencyProperty HeaderRightProperty = DependencyProperty.Register(
            nameof(HeaderRight), typeof(object), typeof(Section), new PropertyMetadata(null));
        public object HeaderRight { get => GetValue(HeaderRightProperty); set => SetValue(HeaderRightProperty, value); }

        public static readonly DependencyProperty BodyProperty = DependencyProperty.Register(
            nameof(Body), typeof(object), typeof(Section), new PropertyMetadata(null));
        public object Body { get => GetValue(BodyProperty); set => SetValue(BodyProperty, value); }

        public static readonly DependencyProperty IsExpandedProperty = DependencyProperty.Register(
            nameof(IsExpanded), typeof(bool), typeof(Section),
            new PropertyMetadata(true, OnIsExpandedChanged));
        public bool IsExpanded { get => (bool)GetValue(IsExpandedProperty); set => SetValue(IsExpandedProperty, value); }

        // Optional registry key used to persist expanded/collapsed state across
        // sessions. XAML IsExpanded acts as the first-run default.
        public static readonly DependencyProperty PersistKeyProperty = DependencyProperty.Register(
            nameof(PersistKey), typeof(string), typeof(Section), new PropertyMetadata(null));
        public string PersistKey { get => (string)GetValue(PersistKeyProperty); set => SetValue(PersistKeyProperty, value); }

        private static void OnIsExpandedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var s = (Section)d;
            s.UpdateVisuals();
            if (!s._loaded) return;
            if (string.IsNullOrEmpty(s.PersistKey)) return;
            try { DriverSettings.SetSectionExpanded(s.PersistKey, (bool)e.NewValue); } catch { }
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(PersistKey))
            {
                bool restored = IsExpanded;
                try { restored = DriverSettings.GetSectionExpanded(PersistKey, IsExpanded); } catch { }
                if (restored != IsExpanded) IsExpanded = restored;
            }
            _loaded = true;
        }

        private void UpdateVisuals()
        {
            if (BodyBorder == null) return;
            BodyBorder.Visibility = IsExpanded ? Visibility.Visible : Visibility.Collapsed;
            ChevRot.Angle = IsExpanded ? 0 : -90;
        }

        private void OnHeaderClick(object sender, MouseButtonEventArgs e)
        {
            IsExpanded = !IsExpanded;
            e.Handled = true;
        }
    }
}
