using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using ASCOM.OnStepX.ViewModels;

namespace ASCOM.OnStepX.Converters
{
    // Maps ConsoleEntryKind → response-cell brush (uses theme-resolved brushes
    // so dark/light swap recolors live).
    public sealed class ConsoleResponseBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var kind = value is ConsoleEntryKind k ? k : ConsoleEntryKind.Pair;
            string brushKey;
            switch (kind)
            {
                case ConsoleEntryKind.Invalid: brushKey = "Brush.Danger"; break;
                case ConsoleEntryKind.Note:    brushKey = "Brush.ColMeta"; break;
                default:                       brushKey = "Brush.ColResp"; break;
            }
            return Application.Current?.TryFindResource(brushKey) as Brush ?? Brushes.LightGreen;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    public sealed class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool v = value is bool b && b;
            if ("Invert".Equals(parameter)) v = !v;
            return v ? Visibility.Visible : Visibility.Collapsed;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool v = value is Visibility vis && vis == Visibility.Visible;
            if ("Invert".Equals(parameter)) v = !v;
            return v;
        }
    }

    // Inverts a bool. Used to drive IsEnabled when a "Busy" flag is true.
    public sealed class InverseBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => !(value is bool b && b);
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => !(value is bool b && b);
    }
}
