using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;
using ASCOM.OnStepX.ViewModels;

namespace ASCOM.OnStepX.Converters
{
    // The WPF hub had five converters. Two of them are gone rather than ported:
    //
    //   BoolToVisibilityConverter  - Avalonia has no Visibility. A control is
    //                                shown or not through IsVisible, which is
    //                                already a bool, and the inverted case is
    //                                spelled {Binding !Something} in markup.
    //   InverseBoolConverter       - same "!" operator.
    //
    // What is left genuinely needs code.

    // ConsoleEntryKind -> the brush its response cell is drawn in. Resolved
    // from the application's resources rather than hard-coded so a dark/light
    // swap recolours the console with everything else.
    public sealed class ConsoleResponseBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var kind = value is ConsoleEntryKind k ? k : ConsoleEntryKind.Pair;
            string key = kind switch
            {
                ConsoleEntryKind.Invalid => "Brush.Danger",
                ConsoleEntryKind.Note => "Brush.ColMeta",
                _ => "Brush.ColResp",
            };

            if (Application.Current != null
                && Application.Current.TryFindResource(key, out var res)
                && res is IBrush brush)
                return brush;

            return Brushes.LightGreen;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    // true -> one star, false -> zero. Collapses the third column when the
    // board reports no focuser or rotator.
    public sealed class BoolToGridLengthConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => (value is bool b && b) ? new GridLength(1, GridUnitType.Star) : new GridLength(0);

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    // Drives which tab's panel is showing: ActiveTab compared against the tab
    // id passed as the parameter. Returns a bool for IsVisible, where the WPF
    // one returned a Visibility.
    //
    // Hidden tabs stay loaded rather than being torn down, so the view models
    // behind them keep polling — same as the WPF hub.
    public sealed class StringEqualsConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => string.Equals(value as string, parameter as string, StringComparison.Ordinal);

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
