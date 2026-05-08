using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace ASCOM.OnStepX.Controls
{
    // Custom tab strip styled to the app aesthetic. Items declared via
    // ItemsSource (ObservableCollection<TabItemDef>); selection driven by
    // SelectedId. Active tab gets the accent underline that animates
    // between selections.
    public partial class TabBar : UserControl
    {
        public TabBar()
        {
            InitializeComponent();
            Loaded += (s, e) => RebuildAndSync();
            SizeChanged += (s, e) => UpdateIndicator(false);
        }

        public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
            nameof(ItemsSource), typeof(ObservableCollection<TabItemDef>), typeof(TabBar),
            new PropertyMetadata(null, OnItemsSourceChanged));
        public ObservableCollection<TabItemDef> ItemsSource
        {
            get => (ObservableCollection<TabItemDef>)GetValue(ItemsSourceProperty);
            set => SetValue(ItemsSourceProperty, value);
        }

        public static readonly DependencyProperty SelectedIdProperty = DependencyProperty.Register(
            nameof(SelectedId), typeof(string), typeof(TabBar),
            new FrameworkPropertyMetadata(null,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedIdChanged));
        public string SelectedId
        {
            get => (string)GetValue(SelectedIdProperty);
            set => SetValue(SelectedIdProperty, value);
        }

        private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var tb = (TabBar)d;
            if (e.OldValue is ObservableCollection<TabItemDef> oldC)
            {
                oldC.CollectionChanged -= tb.OnCollectionChanged;
                foreach (var it in oldC) it.PropertyChanged -= tb.OnItemPropChanged;
            }
            if (e.NewValue is ObservableCollection<TabItemDef> newC)
            {
                newC.CollectionChanged += tb.OnCollectionChanged;
                foreach (var it in newC) it.PropertyChanged += tb.OnItemPropChanged;
            }
            tb.RebuildAndSync();
        }

        private void OnCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
                foreach (TabItemDef it in e.OldItems) it.PropertyChanged -= OnItemPropChanged;
            if (e.NewItems != null)
                foreach (TabItemDef it in e.NewItems) it.PropertyChanged += OnItemPropChanged;
            RebuildAndSync();
        }

        private void OnItemPropChanged(object sender, PropertyChangedEventArgs e)
        {
            // Visibility flips need to re-fade tab in/out and re-position indicator.
            if (e.PropertyName == nameof(TabItemDef.IsVisible))
            {
                RebuildAndSync();
            }
        }

        private static void OnSelectedIdChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((TabBar)d).UpdateActiveStyles();
        }

        private readonly Dictionary<string, Border> _buttonsById = new Dictionary<string, Border>();

        private void RebuildAndSync()
        {
            ItemsHost.Items.Clear();
            _buttonsById.Clear();
            if (ItemsSource == null) return;

            foreach (var def in ItemsSource)
            {
                var btn = BuildTabButton(def);
                _buttonsById[def.Id] = btn;
                ItemsHost.Items.Add(btn);
            }
            UpdateActiveStyles();
            UpdateIndicator(false);
        }

        private Border BuildTabButton(TabItemDef def)
        {
            var border = new Border
            {
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                Padding = new Thickness(16, 0, 16, 0),
                MinHeight = 40,
                Tag = def.Id,
                Visibility = def.IsVisible ? Visibility.Visible : Visibility.Collapsed,
                SnapsToDevicePixels = true
            };

            var sp = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };

            var icon = new Path
            {
                Width = 14,
                Height = 14,
                Margin = new Thickness(0, 0, 8, 0),
                Stretch = Stretch.Uniform,
                StrokeThickness = 1.6,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                Data = def.IconGeometry,
                Stroke = (Brush)FindResource("Brush.TextDim"),
                Tag = "icon"
            };
            sp.Children.Add(icon);

            var label = new TextBlock
            {
                Text = def.Label,
                FontSize = 12.5,
                FontWeight = FontWeights.Medium,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = (Brush)FindResource("Brush.TextDim"),
                Tag = "label"
            };
            sp.Children.Add(label);

            border.Child = sp;

            border.MouseLeftButtonUp += (s, e) =>
            {
                SelectedId = def.Id;
                e.Handled = true;
            };
            border.MouseEnter += (s, e) => HoverPaint(border, true);
            border.MouseLeave += (s, e) => HoverPaint(border, false);

            // Fade-in animation when "appearing" flag set (PA tab arriving).
            if (def.IsAppearing)
            {
                border.Opacity = 0;
                var fade = new DoubleAnimation
                {
                    From = 0, To = 1, Duration = TimeSpan.FromMilliseconds(220),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                border.BeginAnimation(OpacityProperty, fade);
            }
            return border;
        }

        private void HoverPaint(Border btn, bool hovering)
        {
            if (!(btn.Child is StackPanel sp)) return;
            string id = btn.Tag as string;
            bool active = id != null && id == SelectedId;
            foreach (var c in sp.Children)
            {
                if (c is FrameworkElement fe && fe.Tag is string tag)
                {
                    if (tag == "icon" && c is Path p)
                        p.Stroke = (Brush)FindResource(active ? "Brush.Accent" : (hovering ? "Brush.Text" : "Brush.TextDim"));
                    else if (tag == "label" && c is TextBlock t)
                        t.Foreground = (Brush)FindResource(active || hovering ? "Brush.Text" : "Brush.TextDim");
                }
            }
        }

        private void UpdateActiveStyles()
        {
            foreach (var kv in _buttonsById)
            {
                if (!(kv.Value.Child is StackPanel sp)) continue;
                bool active = kv.Key == SelectedId;
                foreach (var c in sp.Children)
                {
                    if (c is FrameworkElement fe && fe.Tag is string tag)
                    {
                        if (tag == "icon" && c is Path p)
                            p.Stroke = (Brush)FindResource(active ? "Brush.Accent" : "Brush.TextDim");
                        else if (tag == "label" && c is TextBlock t)
                            t.Foreground = (Brush)FindResource(active ? "Brush.Text" : "Brush.TextDim");
                    }
                }
            }
            UpdateIndicator(true);
        }

        // Re-position the underline. animate=false → snap (e.g. on initial load).
        private void UpdateIndicator(bool animate)
        {
            if (ItemsSource == null || string.IsNullOrEmpty(SelectedId))
            {
                Indicator.Width = 0;
                return;
            }
            if (!_buttonsById.TryGetValue(SelectedId, out var btn) || btn.Visibility != Visibility.Visible)
            {
                Indicator.Width = 0;
                return;
            }
            // Resolve target X relative to the host items panel + 8px host padding.
            ItemsHost.UpdateLayout();
            var transform = btn.TransformToAncestor(this);
            var origin = transform.Transform(new Point(0, 0));
            double targetX = origin.X;
            double targetW = btn.ActualWidth;

            if (animate && Indicator.Width > 0)
            {
                var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
                var moveAnim = new DoubleAnimation
                {
                    To = targetX, Duration = TimeSpan.FromMilliseconds(250), EasingFunction = ease
                };
                var widthAnim = new DoubleAnimation
                {
                    To = targetW, Duration = TimeSpan.FromMilliseconds(250), EasingFunction = ease
                };
                IndicatorTx.BeginAnimation(TranslateTransform.XProperty, moveAnim);
                Indicator.BeginAnimation(WidthProperty, widthAnim);
            }
            else
            {
                Indicator.BeginAnimation(WidthProperty, null);
                IndicatorTx.BeginAnimation(TranslateTransform.XProperty, null);
                Indicator.Width = targetW;
                IndicatorTx.X = targetX;
            }
        }
    }

    // Tab descriptor. ObservableCollection<TabItemDef> is the ItemsSource;
    // toggling IsVisible on PA hides/shows that tab in real time.
    public sealed class TabItemDef : INotifyPropertyChanged
    {
        public string Id { get; }
        public string Label { get; }
        public Geometry IconGeometry { get; }

        private bool _isVisible = true;
        public bool IsVisible
        {
            get => _isVisible;
            set
            {
                if (_isVisible == value) return;
                _isVisible = value;
                IsAppearing = value; // animate fade-in next rebuild
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsVisible)));
            }
        }

        public bool IsAppearing { get; set; }

        public TabItemDef(string id, string label, Geometry icon)
        {
            Id = id;
            Label = label;
            IconGeometry = icon;
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }
}
