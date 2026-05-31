using System;
using System.Collections.Concurrent;
using System.IO;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace MTGProxyBuilder.UI.Controls
{
    /// <summary>
    /// Builds art option tiles and action tiles used across art selector and library dialogs.
    /// Centralizes tile sizing, colors, and layout for visual consistency.
    /// </summary>
    public static class ArtTileBuilder
    {
        public const double TileWidth = 200;
        public const double TileHeight = 295;
        public const double ImageHeight = 235;
        public const double LabelFontSize = 11;
        public const double DetailFontSize = 9;

        // Process-wide cache of tile-sized thumbnails, keyed by source path. Each image is decoded once
        // (downscaled to tile width) and reused — so reopening a selector for the same/identical card is
        // instant instead of re-decoding every option from disk. Images themselves stay on disk, so a
        // program restart re-decodes (once) but never re-downloads.
        private static readonly ConcurrentDictionary<string, Bitmap?> _thumbCache = new();

        /// <summary>A cached, downscaled bitmap for a tile (decoded once per path per session), or null.</summary>
        public static Bitmap? GetThumbnail(string? path, int decodeWidth = 220)
        {
            if (string.IsNullOrEmpty(path)) return null;
            return _thumbCache.GetOrAdd(path, p =>
            {
                try
                {
                    using var stream = File.OpenRead(p);
                    return Bitmap.DecodeToWidth(stream, decodeWidth);
                }
                catch { return null; }
            });
        }

        /// <summary>Creates an art option tile with image, label, and detail text.</summary>
        public static Border CreateOptionTile(string label, string imagePath, bool isCurrent, string detail,
            int decodePixelWidth = 220)
        {
            var border = new Border
            {
                Width = TileWidth, Height = TileHeight, Margin = new Avalonia.Thickness(4),
                Background = AppBrushes.TileBg,
                CornerRadius = new Avalonia.CornerRadius(4), Cursor = new Cursor(StandardCursorType.Hand),
                BorderThickness = new Avalonia.Thickness(2),
                BorderBrush = isCurrent ? Brushes.DodgerBlue : Brushes.Transparent,
                ClipToBounds = true
            };
            ToolTip.SetTip(border, $"{label}\n{detail}");

            var stack = new StackPanel();

            var imgBorder = new Border
            {
                Height = ImageHeight, Background = Brushes.Black,
                CornerRadius = new Avalonia.CornerRadius(3, 3, 0, 0), ClipToBounds = true
            };
            var bmp = GetThumbnail(imagePath, decodePixelWidth);
            if (bmp != null)
                imgBorder.Child = new Image { Source = bmp, Stretch = Stretch.UniformToFill };
            else
                imgBorder.Child = new TextBlock
                {
                    Text = "?", Foreground = Brushes.Gray, FontSize = 24,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
            stack.Children.Add(imgBorder);

            var lbl = new TextBlock
            {
                Text = label + (isCurrent ? " *" : ""),
                Foreground = AppBrushes.TextSecondary,
                FontSize = LabelFontSize, TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Avalonia.Thickness(3, 4, 3, 0)
            };
            stack.Children.Add(lbl);

            var detailLbl = new TextBlock
            {
                Text = detail, Foreground = AppBrushes.TextMuted,
                FontSize = DetailFontSize, TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Avalonia.Thickness(3, 0, 3, 2)
            };
            stack.Children.Add(detailLbl);

            border.Child = stack;
            return border;
        }

        /// <summary>Creates an option tile with deferred image loading (Image control returned for later assignment).</summary>
        public static (Border Border, Image ImageControl) CreateDeferredTile(string label, string detail,
            IBrush? labelForeground = null, IBrush? borderBrush = null)
        {
            var border = new Border
            {
                Width = TileWidth, Height = TileHeight, Margin = new Avalonia.Thickness(4),
                Background = AppBrushes.TileBg,
                CornerRadius = new Avalonia.CornerRadius(4), Cursor = new Cursor(StandardCursorType.Hand),
                BorderThickness = new Avalonia.Thickness(2),
                BorderBrush = borderBrush ?? Brushes.Transparent,
                ClipToBounds = true
            };
            ToolTip.SetTip(border, $"{label}\n{detail}");

            var stack = new StackPanel();

            var imgBorder = new Border
            {
                Height = ImageHeight, Background = Brushes.Black,
                CornerRadius = new Avalonia.CornerRadius(3, 3, 0, 0), ClipToBounds = true
            };
            var img = new Image { Stretch = Stretch.UniformToFill };
            imgBorder.Child = img;
            stack.Children.Add(imgBorder);

            var lbl = new TextBlock
            {
                Text = label,
                Foreground = labelForeground ?? AppBrushes.TextSecondary,
                FontSize = LabelFontSize, TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Avalonia.Thickness(3, 4, 3, 0)
            };
            stack.Children.Add(lbl);

            var detailLbl = new TextBlock
            {
                Text = detail, Foreground = AppBrushes.TextMuted,
                FontSize = DetailFontSize, HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Avalonia.Thickness(3, 0, 3, 2)
            };
            stack.Children.Add(detailLbl);

            border.Child = stack;
            return (border, img);
        }

        /// <summary>Creates an action tile ("+Add to Library", "Browse File...", etc.).</summary>
        public static Border CreateActionTile(string label)
        {
            var border = new Border
            {
                Width = TileWidth, Height = TileHeight, Margin = new Avalonia.Thickness(4),
                Background = AppBrushes.ActionTileBg,
                CornerRadius = new Avalonia.CornerRadius(4), Cursor = new Cursor(StandardCursorType.Hand),
                BorderThickness = new Avalonia.Thickness(1),
                BorderBrush = AppBrushes.Border
            };

            var stack = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            stack.Children.Add(new TextBlock
            {
                Text = "+", FontSize = 28, Foreground = Brushes.Gray,
                HorizontalAlignment = HorizontalAlignment.Center
            });
            stack.Children.Add(new TextBlock
            {
                Text = label, FontSize = 10,
                Foreground = AppBrushes.Label,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = Avalonia.Media.TextAlignment.Center
            });
            border.Child = stack;
            return border;
        }
    }
}
