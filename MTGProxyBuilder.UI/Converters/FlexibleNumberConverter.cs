using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace MTGProxyBuilder.UI.Converters;

/// <summary>
/// Two-way converter for numeric TextBoxes that accepts both ',' and '.' as the
/// decimal separator regardless of system culture. Display always uses '.'.
/// Bind with Converter={StaticResource FlexibleNumber}; works with float/double/int
/// target properties.
/// </summary>
public class FlexibleNumberConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // Model -> text: always show with '.' so editing is predictable.
        return value switch
        {
            float f => f.ToString(CultureInfo.InvariantCulture),
            double d => d.ToString(CultureInfo.InvariantCulture),
            int i => i.ToString(CultureInfo.InvariantCulture),
            _ => value?.ToString() ?? string.Empty
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var text = (value as string)?.Trim();
        if (string.IsNullOrEmpty(text))
            return BindingNothingFor(targetType);

        // Accept both separators by normalizing ',' to '.'.
        text = text.Replace(',', '.');

        var nullableUnderlying = Nullable.GetUnderlyingType(targetType);
        var t = nullableUnderlying ?? targetType;

        if (t == typeof(float))
            return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var f)
                ? f : Avalonia.Data.BindingOperations.DoNothing;
        if (t == typeof(double))
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
                ? d : Avalonia.Data.BindingOperations.DoNothing;
        if (t == typeof(int))
            return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)
                ? i : Avalonia.Data.BindingOperations.DoNothing;

        return Avalonia.Data.BindingOperations.DoNothing;
    }

    private static object BindingNothingFor(Type targetType)
        => Nullable.GetUnderlyingType(targetType) != null ? null! : Avalonia.Data.BindingOperations.DoNothing;
}
