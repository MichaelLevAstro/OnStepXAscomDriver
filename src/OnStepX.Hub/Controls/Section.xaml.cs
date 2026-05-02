using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;

namespace ASCOM.OnStepX.Controls
{
    // Collapsible section card. Replaces SectionPanel + section-header from
    // the design HTML. Default state is expanded; clicking the header (icon
    // + title row) toggles. Chevron rotates -90deg when collapsed.
    [ContentProperty(nameof(Body))]
    public partial class Section : UserControl
    {
        public Section() { InitializeComponent(); UpdateVisuals(); }

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
            new PropertyMetadata(true, (d, e) => ((Section)d).UpdateVisuals()));
        public bool IsExpanded { get => (bool)GetValue(IsExpandedProperty); set => SetValue(IsExpandedProperty, value); }

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
