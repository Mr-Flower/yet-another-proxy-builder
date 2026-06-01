using SkiaSharp;

namespace MTGProxyBuilder.Core.Services
{
    /// <summary>
    /// Provides a default card back for Yu-Gi-Oh! cards. YGOPRODeck serves no generic back, and the
    /// real Konami back is trademarked, so this renders an ORIGINAL, neutral brown/orange back (a
    /// diamond motif, no logos or text) once and caches it. Users can replace it via the art selector.
    /// </summary>
    public static class YgoCardBackProvider
    {
        private const int Width = 750;
        private const int Height = 1086; // 59:86 card ratio

        /// <summary>Returns the cached back image path, rendering it on first use.
        /// <paramref name="cacheDir"/> defaults to the per-user AppData cache (tests pass a temp dir).</summary>
        public static string GetOrCreate(string? cacheDir = null)
        {
            string dir = cacheDir ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MTGProxyBuilder", "Generated");
            Directory.CreateDirectory(dir);

            string path = Path.Combine(dir, "ygo_back_v1.png");
            if (File.Exists(path)) return path;

            Render(path);
            return path;
        }

        private static void Render(string path)
        {
            using var bitmap = new SKBitmap(Width, Height);
            using (var canvas = new SKCanvas(bitmap))
            {
                PaintBackground(canvas);
                PaintBorder(canvas);
                PaintDiamondMotif(canvas);
            }

            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            using var stream = File.OpenWrite(path);
            data.SaveTo(stream);
        }

        /// <summary>A warm brown-to-orange radial wash, evoking a classic TCG back without copying one.</summary>
        private static void PaintBackground(SKCanvas canvas)
        {
            var center = new SKPoint(Width / 2f, Height / 2f);
            using var shader = SKShader.CreateRadialGradient(
                center, Width * 0.75f,
                new[] { new SKColor(0xC6, 0x7A, 0x32), new SKColor(0x7A, 0x44, 0x16) },
                new[] { 0f, 1f }, SKShaderTileMode.Clamp);
            using var paint = new SKPaint { Shader = shader, IsAntialias = true };
            canvas.DrawRect(new SKRect(0, 0, Width, Height), paint);
        }

        /// <summary>A tan inset frame just in from the card edge.</summary>
        private static void PaintBorder(SKCanvas canvas)
        {
            float inset = Width * 0.06f;
            var rect = new SKRect(inset, inset, Width - inset, Height - inset);
            using var paint = new SKPaint
            {
                Color = new SKColor(0xE8, 0xC9, 0x9A),
                Style = SKPaintStyle.Stroke,
                StrokeWidth = Width * 0.018f,
                IsAntialias = true
            };
            canvas.DrawRoundRect(rect, 18, 18, paint);
        }

        /// <summary>A set of concentric centred diamonds — an original ornamental motif.</summary>
        private static void PaintDiamondMotif(SKCanvas canvas)
        {
            var center = new SKPoint(Width / 2f, Height / 2f);
            using var paint = new SKPaint
            {
                Color = new SKColor(0xF1, 0xD8, 0xAE),
                Style = SKPaintStyle.Stroke,
                StrokeWidth = Width * 0.012f,
                IsAntialias = true
            };

            foreach (float radius in new[] { Width * 0.34f, Width * 0.24f, Width * 0.14f })
            {
                using var path = new SKPath();
                path.MoveTo(center.X, center.Y - radius);
                path.LineTo(center.X + radius, center.Y);
                path.LineTo(center.X, center.Y + radius);
                path.LineTo(center.X - radius, center.Y);
                path.Close();
                canvas.DrawPath(path, paint);
            }
        }
    }
}
