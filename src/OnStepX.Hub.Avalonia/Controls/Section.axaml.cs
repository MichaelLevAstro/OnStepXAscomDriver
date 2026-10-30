using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;

using ASCOM.OnStepX.Config;

namespace ASCOM.OnStepX.Controls
{
    // Collapsible card, ported from the WPF Section.
    //
    // Avalonia styled properties replace WPF dependency properties, and
    // [Content] replaces [ContentProperty] so a tab can still write the body
    // as the element's children rather than naming Body explicitly.
    public partial class Section : UserControl
    {
        private bool _loaded;
        private Border _bodyBorder;
        private RotateTransform _chevron;

        public Section()
        {
            AvaloniaXamlLoader.Load(this);
            _bodyBorder = this.FindControl<Border>("BodyBorder");
            _chevron = this.FindControl<Path>("Chevron")?.RenderTransform as RotateTransform;
            UpdateVisuals();
        }

        public static readonly StyledProperty<string> TitleProperty =
            AvaloniaProperty.Register<Section, string>(nameof(Title), "");
        public string Title
        {
            get => GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
        }

        public static readonly StyledProperty<Geometry> IconGeometryProperty =
            AvaloniaProperty.Register<Section, Geometry>(nameof(IconGeometry));
        public Geometry IconGeometry
        {
            get => GetValue(IconGeometryProperty);
            set => SetValue(IconGeometryProperty, value);
        }

        public static readonly StyledProperty<object> HeaderRightProperty =
            AvaloniaProperty.Register<Section, object>(nameof(HeaderRight));
        public object HeaderRight
        {
            get => GetValue(HeaderRightProperty);
            set => SetValue(HeaderRightProperty, value);
        }

        public static readonly StyledProperty<object> BodyProperty =
            AvaloniaProperty.Register<Section, object>(nameof(Body));

        // Deliberately NOT [Content]. Marking it so redirects every XAML
        // child of a Section into Body — including the Border in this
        // control's own .axaml, which then never reaches Content and the card
        // renders as nothing at all. Callers write <c:Section.Body> instead,
        // which is also what the WPF markup does.
        public object Body
        {
            get => GetValue(BodyProperty);
            set => SetValue(BodyProperty, value);
        }

        public static readonly StyledProperty<bool> IsExpandedProperty =
            AvaloniaProperty.Register<Section, bool>(nameof(IsExpanded), true);
        public bool IsExpanded
        {
            get => GetValue(IsExpandedProperty);
            set => SetValue(IsExpandedProperty, value);
        }

        // Settings key used to remember expanded/collapsed between sessions.
        // The value written in markup is the first-run default.
        public static readonly StyledProperty<string> PersistKeyProperty =
            AvaloniaProperty.Register<Section, string>(nameof(PersistKey));
        public string PersistKey
        {
            get => GetValue(PersistKeyProperty);
            set => SetValue(PersistKeyProperty, value);
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property != IsExpandedProperty) return;

            UpdateVisuals();
            if (!_loaded || string.IsNullOrEmpty(PersistKey)) return;
            try { DriverSettings.SetSectionExpanded(PersistKey, IsExpanded); } catch { }
        }

        protected override void OnLoaded(Avalonia.Interactivity.RoutedEventArgs e)
        {
            base.OnLoaded(e);

            if (!string.IsNullOrEmpty(PersistKey))
            {
                bool restored = IsExpanded;
                try { restored = DriverSettings.GetSectionExpanded(PersistKey, IsExpanded); } catch { }
                if (restored != IsExpanded) IsExpanded = restored;
            }
            _loaded = true;
            UpdateVisuals();
        }

        private void UpdateVisuals()
        {
            if (_bodyBorder != null) _bodyBorder.IsVisible = IsExpanded;
            if (_chevron != null) _chevron.Angle = IsExpanded ? 0 : -90;
        }

        private void OnHeaderClick(object sender, PointerReleasedEventArgs e)
        {
            if (e.InitialPressMouseButton != MouseButton.Left) return;
            IsExpanded = !IsExpanded;
            e.Handled = true;
        }
    }
}
