using System.Collections.Concurrent;
using MTGProxyBuilder.Core.Models;
using MTGProxyBuilder.Resources;
using SkiaSharp;

namespace MTGProxyBuilder.Core.Services
{
    public class CardCompositor
    {
        private readonly ConcurrentDictionary<string, SKTypeface> _fontCache = new();
        private readonly ConcurrentDictionary<string, SKBitmap?> _imageCache = new();

        /// <summary>
        /// Renders the full card at native resolution.
        /// </summary>
        public SKBitmap RenderCard(CustomCardProject project)
        {
            var bitmap = new SKBitmap(project.CardWidthPx, project.CardHeightPx);
            using var canvas = new SKCanvas(bitmap);
            RenderToCanvas(canvas, project, project.CardWidthPx, project.CardHeightPx, 1f);
            return bitmap;
        }

        /// <summary>
        /// Renders a scaled preview that fits within maxWidth x maxHeight.
        /// </summary>
        public SKBitmap RenderPreview(CustomCardProject project, int maxWidth, int maxHeight)
        {
            float scaleX = (float)maxWidth / project.CardWidthPx;
            float scaleY = (float)maxHeight / project.CardHeightPx;
            float scale = Math.Min(scaleX, scaleY);

            int w = (int)(project.CardWidthPx * scale);
            int h = (int)(project.CardHeightPx * scale);
            if (w < 1) w = 1;
            if (h < 1) h = 1;

            var bitmap = new SKBitmap(w, h);
            using var canvas = new SKCanvas(bitmap);
            RenderToCanvas(canvas, project, w, h, scale);
            return bitmap;
        }

        private void RenderToCanvas(SKCanvas canvas, CustomCardProject project, int width, int height, float scale)
        {
            canvas.Clear(ParseColor(project.BackgroundColor));

            canvas.Save();
            canvas.Scale(scale);

            var sortedLayers = project.Layers
                .Where(l => l.IsVisible)
                .OrderBy(l => l.ZOrder);

            foreach (var layer in sortedLayers)
            {
                RenderLayer(canvas, layer);
            }

            canvas.Restore();
        }

        private void RenderLayer(SKCanvas canvas, LayerBase layer)
        {
            canvas.Save();

            // Translate to layer position
            canvas.Translate(layer.X, layer.Y);

            // Rotate around layer center
            if (layer.Rotation != 0)
            {
                canvas.Translate(layer.Width / 2, layer.Height / 2);
                canvas.RotateDegrees(layer.Rotation);
                canvas.Translate(-layer.Width / 2, -layer.Height / 2);
            }

            using var paint = new SKPaint();
            paint.Color = paint.Color.WithAlpha((byte)(layer.Opacity * 255));
            paint.IsAntialias = true;

            switch (layer)
            {
                case ImageLayer imageLayer:
                    RenderImageLayer(canvas, imageLayer, paint);
                    break;
                case TextLayer textLayer:
                    RenderTextLayer(canvas, textLayer, paint);
                    break;
            }

            canvas.Restore();
        }

        private void RenderImageLayer(SKCanvas canvas, ImageLayer layer, SKPaint paint)
        {
            var bitmap = LoadImage(layer);
            if (bitmap == null) return;

            var destRect = new SKRect(0, 0, layer.Width, layer.Height);
            canvas.DrawBitmap(bitmap, destRect, paint);
        }

        private void RenderTextLayer(SKCanvas canvas, TextLayer layer, SKPaint basePaint)
        {
            if (string.IsNullOrEmpty(layer.Text)) return;

            var typeface = GetTypeface(layer.FontFamily, layer.IsBold, layer.IsItalic);

            using var font = new SKFont(typeface, layer.FontSize);
            font.Edging = SKFontEdging.SubpixelAntialias;

            // Draw stroke first (behind fill) if configured
            if (!string.IsNullOrEmpty(layer.StrokeColor) && layer.StrokeWidth > 0)
            {
                using var strokePaint = new SKPaint();
                strokePaint.Color = ParseColor(layer.StrokeColor).WithAlpha((byte)(layer.Opacity * 255));
                strokePaint.Style = SKPaintStyle.Stroke;
                strokePaint.StrokeWidth = layer.StrokeWidth;
                strokePaint.IsAntialias = true;

                DrawTextBlock(canvas, layer, font, strokePaint);
            }

            // Draw fill
            using var fillPaint = new SKPaint();
            fillPaint.Color = ParseColor(layer.FontColor).WithAlpha((byte)(layer.Opacity * 255));
            fillPaint.Style = SKPaintStyle.Fill;
            fillPaint.IsAntialias = true;

            DrawTextBlock(canvas, layer, font, fillPaint);
        }

        private void DrawTextBlock(SKCanvas canvas, TextLayer layer, SKFont font, SKPaint paint)
        {
            var lines = layer.Text.Split('\n');
            float lineHeight = layer.FontSize * layer.LineSpacing;
            float y = font.Size; // baseline offset

            foreach (var line in lines)
            {
                float x = 0;
                float textWidth = font.MeasureText(line);

                switch (layer.TextAlignment)
                {
                    case CardTextAlignment.Center:
                        x = (layer.Width - textWidth) / 2;
                        break;
                    case CardTextAlignment.Right:
                        x = layer.Width - textWidth;
                        break;
                }

                canvas.DrawText(line, x, y, font, paint);
                y += lineHeight;
            }
        }

        private SKBitmap? LoadImage(ImageLayer layer)
        {
            if (string.IsNullOrEmpty(layer.ImageSource))
                return null;

            return _imageCache.GetOrAdd(layer.ImageSource, source =>
            {
                // Try loading from cached bytes first
                if (layer.ImageBytes != null)
                    return SKBitmap.Decode(layer.ImageBytes);

                // Try loading from file path
                if (File.Exists(source))
                {
                    var bytes = File.ReadAllBytes(source);
                    layer.ImageBytes = bytes;
                    return SKBitmap.Decode(bytes);
                }

                return null;
            });
        }

        private SKTypeface GetTypeface(string fontFamily, bool bold, bool italic)
        {
            var style = bold && italic ? SKFontStyle.BoldItalic
                      : bold ? SKFontStyle.Bold
                      : italic ? SKFontStyle.Italic
                      : SKFontStyle.Normal;

            string cacheKey = $"{fontFamily}_{style}";

            return _fontCache.GetOrAdd(cacheKey, _ =>
            {
                // Try embedded font from Resources
                var fontBytes = FontProvider.GetFontBytes(fontFamily);
                if (fontBytes != null)
                {
                    var tf = SKTypeface.FromData(SKData.CreateCopy(fontBytes));
                    if (tf != null) return tf;
                }

                // Fall back to system font
                return SKTypeface.FromFamilyName(fontFamily, style) ?? SKTypeface.Default;
            });
        }

        /// <summary>
        /// Invalidates a cached image so it will be reloaded on next render.
        /// </summary>
        public void InvalidateImage(string imageSource)
        {
            _imageCache.TryRemove(imageSource, out _);
        }

        /// <summary>
        /// Clears all caches.
        /// </summary>
        public void ClearCaches()
        {
            foreach (var kvp in _imageCache)
                kvp.Value?.Dispose();
            _imageCache.Clear();

            foreach (var kvp in _fontCache)
                kvp.Value?.Dispose();
            _fontCache.Clear();
        }

        private static SKColor ParseColor(string hex)
        {
            if (SKColor.TryParse(hex, out var color))
                return color;
            return SKColors.Black;
        }
    }
}
