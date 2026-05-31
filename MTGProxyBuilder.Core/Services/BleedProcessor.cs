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
                // regenerated instead of reused. v3 = square-off white corners (border colour) then bleed.
                string hash = $"{Path.GetFileNameWithoutExtension(sourcePath)}_{sourcePath.GetHashCode():X8}_b{bleedPixels}_v3";
                string outputPath = Path.Combine(_cacheDir, $"{hash}.jpg");

                if (File.Exists(outputPath))
                {
                    _processedCache[cacheKey] = outputPath;
                    return outputPath;
                }

                using var source = SKBitmap.Decode(sourcePath);
                if (source == null) return sourcePath;

                int srcW = source.Width;
                int srcH = source.Height;

                // Square off white corners of Scryfall-style scans: those images show a rounded card
                // with a WHITE triangle filling each rectangular corner (outside the rounding). Replace
                // ONLY those near-white corner pixels with the card's border colour, so the corner — and
                // the bleed stretched from it — matches the border: black border -> black corners, white
                // border -> left white. Coloured/full-art/MPCFill images (no white corner) are untouched.
                // This makes Scryfall bleed behave like MPCFill's full-bleed art.
                SquareOffWhiteCorner(source, srcW, srcH, 0,        0,         1,  1);
                SquareOffWhiteCorner(source, srcW, srcH, srcW - 1, 0,        -1,  1);
                SquareOffWhiteCorner(source, srcW, srcH, 0,        srcH - 1,  1, -1);
                SquareOffWhiteCorner(source, srcW, srcH, srcW - 1, srcH - 1, -1, -1);

                int outW = srcW + 2 * bleedPixels;
                int outH = srcH + 2 * bleedPixels;

                using var output = new SKBitmap(outW, outH);
                using var canvas = new SKCanvas(output);

                // Draw original (now squared-off) image centered
                canvas.DrawBitmap(source, bleedPixels, bleedPixels);

                // Extend edges: take a 1-pixel strip from each edge and stretch it outward

                // Top edge: stretch top row upward
                using (var topStrip = new SKBitmap(srcW, 1))
                {
                    for (int x = 0; x < srcW; x++)
                        topStrip.SetPixel(x, 0, source.GetPixel(x, 0));
                    canvas.DrawBitmap(topStrip, new SKRect(0, 0, srcW, 1),
                        new SKRect(bleedPixels, 0, bleedPixels + srcW, bleedPixels));
                }

                // Bottom edge: stretch bottom row downward
                using (var bottomStrip = new SKBitmap(srcW, 1))
                {
                    for (int x = 0; x < srcW; x++)
                        bottomStrip.SetPixel(x, 0, source.GetPixel(x, srcH - 1));
                    canvas.DrawBitmap(bottomStrip, new SKRect(0, 0, srcW, 1),
                        new SKRect(bleedPixels, bleedPixels + srcH, bleedPixels + srcW, outH));
                }

                // Left edge: stretch left column leftward
                using (var leftStrip = new SKBitmap(1, srcH))
                {
                    for (int y = 0; y < srcH; y++)
                        leftStrip.SetPixel(0, y, source.GetPixel(0, y));
                    canvas.DrawBitmap(leftStrip, new SKRect(0, 0, 1, srcH),
                        new SKRect(0, bleedPixels, bleedPixels, bleedPixels + srcH));
                }

                // Right edge: stretch right column rightward
                using (var rightStrip = new SKBitmap(1, srcH))
                {
                    for (int y = 0; y < srcH; y++)
                        rightStrip.SetPixel(0, y, source.GetPixel(srcW - 1, y));
                    canvas.DrawBitmap(rightStrip, new SKRect(0, 0, 1, srcH),
                        new SKRect(bleedPixels + srcW, bleedPixels, outW, bleedPixels + srcH));
                }

                // Corners: fill the bleed-region squares with the corner pixel colour. The source
                // corners were already squared off above, so this is the correct border colour
                // (black/white/coloured) — never white-on-a-black-border.
                var topLeft     = source.GetPixel(0, 0);
                var topRight    = source.GetPixel(srcW - 1, 0);
                var bottomLeft  = source.GetPixel(0, srcH - 1);
                var bottomRight = source.GetPixel(srcW - 1, srcH - 1);

                using var paint = new SKPaint();
                paint.Color = topLeft;
                canvas.DrawRect(0, 0, bleedPixels, bleedPixels, paint);
                paint.Color = topRight;
                canvas.DrawRect(bleedPixels + srcW, 0, bleedPixels, bleedPixels, paint);
                paint.Color = bottomLeft;
                canvas.DrawRect(0, bleedPixels + srcH, bleedPixels, bleedPixels, paint);
                paint.Color = bottomRight;
                canvas.DrawRect(bleedPixels + srcW, bleedPixels + srcH, bleedPixels, bleedPixels, paint);

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

        private static bool IsNearWhite(SKColor c) => c.Red >= 235 && c.Green >= 235 && c.Blue >= 235;

        /// <summary>
        /// Fills the near-white triangle in one rectangular corner of a (rounded) card scan with the
        /// card's border colour, squaring off the corner. (cx,cy) is the extreme corner pixel and
        /// (dx,dy) points inward (±1). No-op if that corner isn't near-white (coloured/full-art) or if
        /// the border itself is white/light (so white-bordered cards keep white corners).
        /// </summary>
        private static void SquareOffWhiteCorner(SKBitmap bmp, int w, int h, int cx, int cy, int dx, int dy)
        {
            if (!IsNearWhite(bmp.GetPixel(cx, cy))) return;

            int maxScan = Math.Max(2, Math.Min(w, h) / 10); // covers the rounded-corner radius with margin

            // Border colour = first non-near-white pixel scanning diagonally inward.
            SKColor? border = null;
            for (int d = 1; d <= maxScan; d++)
            {
                var p = bmp.GetPixel(cx + dx * d, cy + dy * d);
                if (!IsNearWhite(p)) { border = p; break; }
            }
            if (border == null) return; // white/light border -> leave the corner white

            // Recolour only the near-white pixels in the corner box (the triangle), never the
            // card's actual frame/art pixels.
            for (int ix = 0; ix <= maxScan; ix++)
                for (int iy = 0; iy <= maxScan; iy++)
                {
                    int x = cx + dx * ix, y = cy + dy * iy;
                    if (x < 0 || x >= w || y < 0 || y >= h) continue;
                    if (IsNearWhite(bmp.GetPixel(x, y)))
                        bmp.SetPixel(x, y, border.Value);
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
