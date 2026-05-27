using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace MTGProxyBuilder.UI.Converters;

public class InverseBoolToVisibilityConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b ? !b : (object?)true;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b ? !b : (object?)false;
}
