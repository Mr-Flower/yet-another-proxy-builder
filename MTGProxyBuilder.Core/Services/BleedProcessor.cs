using SkiaSharp;
using System.Collections.Concurrent;

namespace MTGProxyBuilder.Core.Services
{
    /// <summary>
    /// Generates bleed-extended images by stretching edge pixels outward.
    /// Results are cached to disk so each source image is only processed once per bleed size.
    /// </summary>
    public class BleedProcessor
    {
        private readonly string _cacheDir;
        private static readonly ConcurrentDictionary<string, string> _processedCache = new();

        public BleedProcessor()
        {
            _cacheDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MTGProxyBuilder", "BleedCache");
            Directory.CreateDirectory(_cacheDir);
        }

        /// <summary>
        /// True if the image already includes its own bleed margin and must NOT be bleed-extended.
        /// MPCFill art is authored full-bleed (cached with an "mpc_" filename prefix), unlike Scryfall
        /// scans which are the bare card and need bleed added. Such images are drawn straight into the
        /// full cell instead of being extended (which would double the border).
        /// </summary>
        public static bool ImageAlreadyHasBleed(string? path) =>
            !string.IsNullOrEmpty(path) &&
            Path.GetFileName(path).StartsWith("mpc_", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Returns the path to a bleed-extended version of the source image.
        /// The bleed area is filled by stretching the outermost edge pixels outward.
        /// </summary>
        /// <param name="sourcePath">Path to the original card image.</param>
        /// <param name="bleedPixels">How many pixels of bleed to add on each side.</param>
        public string? GetBleedExtendedImage(string sourcePath, int bleedPixels)
        {
            if (string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath) || bleedPixels <= 0)
                return sourcePath; // No bleed needed, return original

            string cacheKey = $"{sourcePath}|{bleedPixels}";
            if (_processedCache.TryGetValue(cacheKey, out var cached) && File.Exists(cached))
                return cached;

            try
            {
                // The trailing _vN is a processing-logic version: bump it whenever the bleed
                // algorithm changes so stale cached results (e.g. the broken coloured corners) are
                // regenerated instead of reused. v5 = corner square-off that also swallows the
                // anti-aliased grey fringe (luma-based fill to the dark border).
                string hash = $"{Path.GetFileNameWithoutExtension(sourcePath)}_{sourcePath.GetHashCode():X8}_b{bleedPixels}_v5";
                string outputPath = Path.Combine(_cacheDir, $"{hash}.jpg");

                if (File.Exists(outputPath))
                {
                    _processedCache[cacheKey] = outputPath;
                    return outputPath;
                }

                using var source = SKBitmap.Decode(sourcePath);
                if (source == null) return sourcePath;

                using var output = RenderBleed(source, bleedPixels);

                // Save as JPEG (much faster than PNG, fine for print)
                using var stream = File.OpenWrite(outputPath);
                output.Encode(stream, SKEncodedImageFormat.Jpeg, 95);

                _processedCache[cacheKey] = outputPath;
                return outputPath;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Bleed processing error: {ex.Message}");
                return sourcePath; // Fall back to original
            }
        }

        // MakePlayingCards / MPCFill standard bleed per side (the margin baked into MPCFill art).
        private const double MpcBleedMm = 3.0;

        /// <summary>
        /// Resolves the image to draw filling a full (card + 2*bleed) cell, matching the PDF output.
        /// Scryfall scans are bleed-extended (and corner-squared). MPCFill art is authored full-bleed,
        /// so it's first cropped back to the bare card (removing its built-in ~3 mm MPC bleed) and then
        /// extended to the USER's bleed — otherwise its larger built-in bleed sits inside the cut line
        /// and the card looks "under-cut". Returns the original path when no bleed is needed.
        /// </summary>
        public string? GetDisplayImage(string sourcePath, int bleedPixels, double cardWmm, double cardHmm)
        {
            if (string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath) || bleedPixels <= 0)
                return sourcePath;
            if (!ImageAlreadyHasBleed(sourcePath))
                return GetBleedExtendedImage(sourcePath, bleedPixels);

            string cacheKey = $"{sourcePath}|{bleedPixels}|{cardWmm}x{cardHmm}|mpc";
            if (_processedCache.TryGetValue(cacheKey, out var cached) && File.Exists(cached))
                return cached;

            try
            {
                string hash = $"{Path.GetFileNameWithoutExtension(sourcePath)}_{sourcePath.GetHashCode():X8}" +
                              $"_b{bleedPixels}_{(int)cardWmm}x{(int)cardHmm}_mpc_v5";
                string outputPath = Path.Combine(_cacheDir, $"{hash}.jpg");
                if (File.Exists(outputPath)) { _processedCache[cacheKey] = outputPath; return outputPath; }

                using var src = SKBitmap.Decode(sourcePath);
                if (src == null) return sourcePath;

                // Crop the built-in MPC bleed: per-side fraction = mpcBleed / (card + 2*mpcBleed).
                double fracW = cardWmm > 0 ? MpcBleedMm / (cardWmm + 2 * MpcBleedMm) : 0;
                double fracH = cardHmm > 0 ? MpcBleedMm / (cardHmm + 2 * MpcBleedMm) : 0;
                int cropX = (int)Math.Round(src.Width * fracW);
                int cropY = (int)Math.Round(src.Height * fracH);
                int bareW = Math.Max(1, src.Width - 2 * cropX);
                int bareH = Math.Max(1, src.Height - 2 * cropY);

                using var bare = new SKBitmap(bareW, bareH);
                using (var c = new SKCanvas(bare))
                    c.DrawBitmap(src,
                        new SKRect(cropX, cropY, src.Width - cropX, src.Height - cropY),
                        new SKRect(0, 0, bareW, bareH));

                using var output = RenderBleed(bare, bleedPixels);
                using var stream = File.OpenWrite(outputPath);
                output.Encode(stream, SKEncodedImageFormat.Jpeg, 95);

                _processedCache[cacheKey] = outputPath;
                return outputPath;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"MPC bleed processing error: {ex.Message}");
                return sourcePath;
            }
        }

        /// <summary>
        /// Builds a bleed-extended bitmap from <paramref name="source"/> (the bare card): squares off any
        /// white corners, draws the card centered, and stretches the edge pixels outward into the bleed.
        /// The caller owns and disposes the returned bitmap. NOTE: mutates <paramref name="source"/>'s
        /// corner pixels (square-off).
        /// </summary>
        private static SKBitmap RenderBleed(SKBitmap source, int bleedPixels)
        {
            int srcW = source.Width;
            int srcH = source.Height;

            // Square off white corners of Scryfall-style scans (no-op for full-bleed/MPC art).
            SquareOffWhiteCorner(source, srcW, srcH, 0,        0,         1,  1);
            SquareOffWhiteCorner(source, srcW, srcH, srcW - 1, 0,        -1,  1);
            SquareOffWhiteCorner(source, srcW, srcH, 0,        srcH - 1,  1, -1);
            SquareOffWhiteCorner(source, srcW, srcH, srcW - 1, srcH - 1, -1, -1);

            int outW = srcW + 2 * bleedPixels;
            int outH = srcH + 2 * bleedPixels;

            var output = new SKBitmap(outW, outH);
            using (var canvas = new SKCanvas(output))
            {
                canvas.DrawBitmap(source, bleedPixels, bleedPixels);

                // Top edge
                using (var strip = new SKBitmap(srcW, 1))
                {
                    for (int x = 0; x < srcW; x++) strip.SetPixel(x, 0, source.GetPixel(x, 0));
                    canvas.DrawBitmap(strip, new SKRect(0, 0, srcW, 1),
                        new SKRect(bleedPixels, 0, bleedPixels + srcW, bleedPixels));
                }
                // Bottom edge
                using (var strip = new SKBitmap(srcW, 1))
                {
                    for (int x = 0; x < srcW; x++) strip.SetPixel(x, 0, source.GetPixel(x, srcH - 1));
                    canvas.DrawBitmap(strip, new SKRect(0, 0, srcW, 1),
                        new SKRect(bleedPixels, bleedPixels + srcH, bleedPixels + srcW, outH));
                }
                // Left edge
                using (var strip = new SKBitmap(1, srcH))
                {
                    for (int y = 0; y < srcH; y++) strip.SetPixel(0, y, source.GetPixel(0, y));
                    canvas.DrawBitmap(strip, new SKRect(0, 0, 1, srcH),
                        new SKRect(0, bleedPixels, bleedPixels, bleedPixels + srcH));
                }
                // Right edge
                using (var strip = new SKBitmap(1, srcH))
                {
                    for (int y = 0; y < srcH; y++) strip.SetPixel(0, y, source.GetPixel(srcW - 1, y));
                    canvas.DrawBitmap(strip, new SKRect(0, 0, 1, srcH),
                        new SKRect(bleedPixels + srcW, bleedPixels, outW, bleedPixels + srcH));
                }

                // Corners: fill the bleed squares with the (squared-off) corner colour.
                using var paint = new SKPaint();
                paint.Color = source.GetPixel(0, 0);
                canvas.DrawRect(0, 0, bleedPixels, bleedPixels, paint);
                paint.Color = source.GetPixel(srcW - 1, 0);
                canvas.DrawRect(bleedPixels + srcW, 0, bleedPixels, bleedPixels, paint);
                paint.Color = source.GetPixel(0, srcH - 1);
                canvas.DrawRect(0, bleedPixels + srcH, bleedPixels, bleedPixels, paint);
                paint.Color = source.GetPixel(srcW - 1, srcH - 1);
                canvas.DrawRect(bleedPixels + srcW, bleedPixels + srcH, bleedPixels, bleedPixels, paint);
            }
            return output;
        }

        private static bool IsNearWhite(SKColor c) => c.Red >= 235 && c.Green >= 235 && c.Blue >= 235;

        private static int Luma(SKColor c) => (c.Red * 30 + c.Green * 59 + c.Blue * 11) / 100;

        /// <summary>
        /// Squares off one rectangular corner of a (rounded) card scan by replacing the white triangle
        /// (and the anti-aliased grey fringe along the rounding) outside the card with the card's border
        /// colour, so the border extends all the way to the corner — exactly like the straight edges do.
        /// (cx,cy) is the extreme corner pixel; (dx,dy) point inward (±1). No-op when the corner isn't
        /// near-white (coloured/full-art) or when the border itself is white/light (white-bordered cards
        /// keep their white corners).
        /// </summary>
        private static void SquareOffWhiteCorner(SKBitmap bmp, int w, int h, int cx, int cy, int dx, int dy)
        {
            if (!IsNearWhite(bmp.GetPixel(cx, cy))) return;

            // Generous band — the rounded-corner radius is only a few % of the card, so 1/6 of the
            // shorter side covers it comfortably regardless of image resolution.
            int band = Math.Max(4, Math.Min(w, h) / 6);

            // Find the border colour: scan diagonally inward to where the white ends, then take the
            // DARKEST pixel in the next few (the card's black edge), skipping the anti-aliased fringe.
            // If the diagonal stays white across the whole band, it's a white/light border -> leave it.
            SKColor? border = null;
            int bestLuma = int.MaxValue;
            for (int d = 1; d <= band; d++)
            {
                if (!IsNearWhite(bmp.GetPixel(cx + dx * d, cy + dy * d)))
                {
                    for (int e = 0; e <= 4 && d + e <= band; e++)
                    {
                        var q = bmp.GetPixel(cx + dx * (d + e), cy + dy * (d + e));
                        int lu = Luma(q);
                        if (lu < bestLuma) { bestLuma = lu; border = q; }
                    }
                    break;
                }
            }
            if (border == null) return;

            // Fill, per row, from the edge inward while the pixel is clearly LIGHTER than the border —
            // this swallows the white triangle AND the grey anti-alias fringe along the rounding, and
            // stops once it reaches the dark border, so the card art is never touched.
            int fillAbove = Math.Min(bestLuma + 50, 200);
            for (int iy = 0; iy <= band; iy++)
            {
                int y = cy + dy * iy;
                if (y < 0 || y >= h) break;
                for (int ix = 0; ix <= band; ix++)
                {
                    int x = cx + dx * ix;
                    if (x < 0 || x >= w) break;
                    if (Luma(bmp.GetPixel(x, y)) <= fillAbove) break; // reached the dark border
                    bmp.SetPixel(x, y, border.Value);
                }
            }
        }

        public void ClearCache()
        {
            _processedCache.Clear();
            if (Directory.Exists(_cacheDir))
                foreach (var f in Directory.GetFiles(_cacheDir))
                    try { File.Delete(f); } catch { }
        }
    }
}
