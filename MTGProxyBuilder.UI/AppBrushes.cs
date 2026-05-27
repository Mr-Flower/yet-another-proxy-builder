using Avalonia.Media;

namespace MTGProxyBuilder.UI;

public static class AppBrushes
{
    // Backgrounds
    public static readonly SolidColorBrush WindowBg = new(Color.FromRgb(0x1E, 0x1E, 0x1E));
    public static readonly SolidColorBrush PanelBg = new(Color.FromRgb(0x25, 0x25, 0x26));
    public static readonly SolidColorBrush TileBg = new(Color.FromRgb(0x3E, 0x3E, 0x42));
    public static readonly SolidColorBrush ActionTileBg = new(Color.FromRgb(0x33, 0x33, 0x38));

    // Text
    public static readonly SolidColorBrush TextPrimary = new(Color.FromRgb(0xFF, 0xFF, 0xFF));
    public static readonly SolidColorBrush TextSecondary = new(Color.FromRgb(0xCC, 0xCC, 0xCC));
    public static readonly SolidColorBrush TextMuted = new(Color.FromRgb(0x88, 0x88, 0x88));
    public static readonly SolidColorBrush TextDim = new(Color.FromRgb(0x66, 0x66, 0x66));
    public static readonly SolidColorBrush Label = new(Color.FromRgb(0xAA, 0xAA, 0xAA));

    // Accents
    public static readonly SolidColorBrush AccentBlue = new(Color.FromRgb(0x00, 0x78, 0xD4));
    public static readonly SolidColorBrush AccentGreen = new(Color.FromRgb(0x4C, 0xAF, 0x50));
    public static readonly SolidColorBrush AccentRed = new(Color.FromRgb(0xE0, 0x60, 0x60));
    public static readonly SolidColorBrush SelectionBlue = new(Color.FromRgb(0x1E, 0x90, 0xFF));

    // Borders
    public static readonly SolidColorBrush Border = new(Color.FromRgb(0x55, 0x55, 0x55));
    public static readonly SolidColorBrush BorderSubtle = new(Color.FromRgb(0x33, 0x33, 0x33));
}
