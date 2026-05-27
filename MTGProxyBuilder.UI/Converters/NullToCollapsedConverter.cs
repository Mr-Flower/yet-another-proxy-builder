using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace MTGProxyBuilder.UI.Converters;

public class NullToCollapsedConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool result = value != null && !(value is string s && string.IsNullOrEmpty(s));
        return parameter is string p && p == "inverse" ? !result : result;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
