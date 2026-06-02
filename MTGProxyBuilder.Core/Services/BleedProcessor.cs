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

        // Per-instance memo so one processor's ClearCache() can never wipe another's state. The on-disk
        // cache (the File.Exists checks below) is still shared between instances using the same folder,
        // so cross-instance reuse keeps working — only the in-memory fast path is instance-scoped.
        private readonly ConcurrentDictionary<string, string> _processedCache = new();

        /// <param name="cacheDir">Folder for processed-image files. Defaults to the per-user AppData
        /// cache; tests pass a unique temp folder so parallel runs stay fully isolated.</param>
        public BleedProcessor(string? cacheDir = null)
        {
            _cacheDir = cacheDir ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "YetAnotherProxyBuilder", "BleedCache");
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
                string hash = $"{Path.GetFileNameWithoutExtension(sourcePath)}_{StableHash.Hex(sourcePath)}_b{bleedPixels}_v6";
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

        /// <summary>
        /// Resolves the image to draw filling a full (card + 2*bleed) cell, matching the PDF output.
        /// The cut line always sits on the card edge; <paramref name="bleedMm"/> is just the trim margin
        /// around it (0 .. <see cref="Constants.MpcBleedMm"/>, the 1/8" MPC max). The card region ends up
        /// the SAME size for both sources at any setting, so the same card never changes zoom:
        /// - MPCFill art carries a baked 1/8" bleed; it is CROPPED down to <paramref name="bleedMm"/>
        ///   (lossless — only the outer bleed is trimmed, the card is untouched). At the full 1/8" it is
        ///   used whole.
        /// - Scryfall scans are the bare card, so <paramref name="bleedMm"/> of bleed is ADDED by
        ///   duplicating the edge pixels outward (sized as a fraction of this image's own pixels, so any
        ///   scan resolution gets the right border).
        /// <paramref name="cardWmm"/> is the card width the bare scan represents.
        /// </summary>
        public string? GetDisplayImage(string sourcePath, double bleedMm, double cardWmm)
        {
            if (string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath) || cardWmm <= 0)
                return sourcePath;

            bleedMm = Math.Clamp(bleedMm, 0, Constants.MpcBleedMm);

            if (ImageAlreadyHasBleed(sourcePath))
            {
                // MPCFill: at the full 1/8" there's nothing to trim; otherwise crop the native bleed down.
                if (bleedMm >= Constants.MpcBleedMm - 0.005) return sourcePath;
                return GetMpcCroppedImage(sourcePath, bleedMm) ?? sourcePath;
            }

            // Scryfall: extend the bare card by the requested bleed.
            if (bleedMm <= 0) return sourcePath;
            int bleedPx = ScryfallBleedPixels(sourcePath, bleedMm, cardWmm);
            return bleedPx > 0 ? GetBleedExtendedImage(sourcePath, bleedPx) : sourcePath;
        }

        /// <summary>
        /// Crops an MPCFill image's baked 1/8" bleed down to <paramref name="bleedMm"/> per side (0..1/8"),
        /// keeping the card centered and untouched. The trim is derived from the MPC poker template
        /// (full 822x1122, trimmed card 750x1050 => 36 px/side bleed) as a fraction of the image, so it is
        /// resolution-independent. Cached on disk.
        /// </summary>
        private string? GetMpcCroppedImage(string sourcePath, double bleedMm)
        {
            double keep = Math.Clamp(bleedMm / Constants.MpcBleedMm, 0, 1); // fraction of native bleed kept
            double cropFracW = (36.0 / 822.0) * (1 - keep);                 // trim per side (of width)
            double cropFracH = (36.0 / 1122.0) * (1 - keep);                // trim per side (of height)

            int bleedKey = (int)Math.Round(bleedMm * 1000);
            string cacheKey = $"{sourcePath}|mpccrop|{bleedKey}";
            if (_processedCache.TryGetValue(cacheKey, out var cached) && File.Exists(cached)) return cached;

            try
            {
                string hash = $"crop_{Path.GetFileNameWithoutExtension(sourcePath)}_{StableHash.Hex(sourcePath)}_b{bleedKey}_v6";
                string outputPath = Path.Combine(_cacheDir, $"{hash}.jpg");
                if (File.Exists(outputPath)) { _processedCache[cacheKey] = outputPath; return outputPath; }

                using var source = SKBitmap.Decode(sourcePath);
                if (source == null) return null;

                int cropX = (int)Math.Round(source.Width * cropFracW);
                int cropY = (int)Math.Round(source.Height * cropFracH);
                int w = source.Width - 2 * cropX;
                int h = source.Height - 2 * cropY;
                if (w <= 0 || h <= 0) return null;

                using var output = new SKBitmap(w, h);
                using (var canvas = new SKCanvas(output))
                    canvas.DrawBitmap(source,
                        new SKRect(cropX, cropY, cropX + w, cropY + h),
                        new SKRect(0, 0, w, h));

                using var stream = File.OpenWrite(outputPath);
                output.Encode(stream, SKEncodedImageFormat.Jpeg, 95);
                _processedCache[cacheKey] = outputPath;
                return outputPath;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"MPCFill crop error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Bleed width in SOURCE-image pixels for a bare Scryfall scan: the physical bleed (mm) is the
        /// same fraction of the card as it is of the image's pixels (square pixels), so a 700 px and a
        /// 1500 px scan both get a correct ~1/8" border. (The old code added a fixed 14 px regardless
        /// of resolution, so the bleed was far too thin on real scans.) Reads only the image header.
        /// </summary>
        private static int ScryfallBleedPixels(string sourcePath, double bleedMm, double cardWmm)
        {
            try
            {
                using var codec = SKCodec.Create(sourcePath);
                int w = codec?.Info.Width ?? 0;
                return w > 0 ? Math.Max(1, (int)Math.Round(w * bleedMm / cardWmm)) : 0;
            }
            catch { return 0; }
        }

        /// <summary>
        /// Builds a bleed-extended bitmap from <paramref name="source"/> (the bare card): squares off any
        /// white corners, draws the card centered, and stretches the edge pixels outward into the bleed.
        /// The caller owns and disposes the returned bitmap. NOTE: mutates <paramref name="source"/>'s
        /// corner pixels (square-off).
        /// </summary>
        private static SKBitmap RenderBleed(SKBitmap source, int bleedPixels)
        {
            SquareOffAllWhiteCorners(source);

            var output = new SKBitmap(source.Width + 2 * bleedPixels, source.Height + 2 * bleedPixels);
            using (var canvas = new SKCanvas(output))
            {
                canvas.DrawBitmap(source, bleedPixels, bleedPixels);
                StretchEdgesOutward(canvas, source, bleedPixels);
                FillCornerSquares(canvas, source, bleedPixels);
            }
            return output;
        }

        /// <summary>Squares off all four white corners of a (rounded) Scryfall-style scan. No-op for
        /// coloured/full-art corners and for full-bleed MPCFill art.</summary>
        private static void SquareOffAllWhiteCorners(SKBitmap source)
        {
            int w = source.Width, h = source.Height;
            SquareOffWhiteCorner(source, w, h, 0,     0,      1,  1);
            SquareOffWhiteCorner(source, w, h, w - 1, 0,     -1,  1);
            SquareOffWhiteCorner(source, w, h, 0,     h - 1,  1, -1);
            SquareOffWhiteCorner(source, w, h, w - 1, h - 1, -1, -1);
        }

        /// <summary>Fills the four bleed margins by stretching the card's outermost edge pixels outward.</summary>
        private static void StretchEdgesOutward(SKCanvas canvas, SKBitmap source, int bleed)
        {
            int w = source.Width, h = source.Height;

            using var topStrip = RowStrip(source, 0);
            canvas.DrawBitmap(topStrip, new SKRect(0, 0, w, 1), new SKRect(bleed, 0, bleed + w, bleed));

            using var bottomStrip = RowStrip(source, h - 1);
            canvas.DrawBitmap(bottomStrip, new SKRect(0, 0, w, 1), new SKRect(bleed, bleed + h, bleed + w, h + 2 * bleed));

            using var leftStrip = ColumnStrip(source, 0);
            canvas.DrawBitmap(leftStrip, new SKRect(0, 0, 1, h), new SKRect(0, bleed, bleed, bleed + h));

            using var rightStrip = ColumnStrip(source, w - 1);
            canvas.DrawBitmap(rightStrip, new SKRect(0, 0, 1, h), new SKRect(bleed + w, bleed, w + 2 * bleed, bleed + h));
        }

        /// <summary>Copies row <paramref name="y"/> of <paramref name="src"/> into a 1px-tall strip bitmap.</summary>
        private static SKBitmap RowStrip(SKBitmap src, int y)
        {
            var strip = new SKBitmap(src.Width, 1);
            for (int x = 0; x < src.Width; x++) strip.SetPixel(x, 0, src.GetPixel(x, y));
            return strip;
        }

        /// <summary>Copies column <paramref name="x"/> of <paramref name="src"/> into a 1px-wide strip bitmap.</summary>
        private static SKBitmap ColumnStrip(SKBitmap src, int x)
        {
            var strip = new SKBitmap(1, src.Height);
            for (int y = 0; y < src.Height; y++) strip.SetPixel(0, y, src.GetPixel(x, y));
            return strip;
        }

        /// <summary>Fills the four corner bleed squares with the (squared-off) corner pixel colour.</summary>
        private static void FillCornerSquares(SKCanvas canvas, SKBitmap source, int bleed)
        {
            int w = source.Width, h = source.Height;
            using var paint = new SKPaint();
            FillSquare(canvas, paint, 0,         0,         bleed, source.GetPixel(0,     0));
            FillSquare(canvas, paint, bleed + w, 0,         bleed, source.GetPixel(w - 1, 0));
            FillSquare(canvas, paint, 0,         bleed + h, bleed, source.GetPixel(0,     h - 1));
            FillSquare(canvas, paint, bleed + w, bleed + h, bleed, source.GetPixel(w - 1, h - 1));
        }

        private static void FillSquare(SKCanvas canvas, SKPaint paint, float x, float y, int size, SKColor color)
        {
            paint.Color = color;
            canvas.DrawRect(x, y, size, size, paint);
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

            var border = FindCornerBorderColor(bmp, cx, cy, dx, dy, band, out int borderLuma);
            if (border == null) return; // diagonal stayed white -> white/light border, leave it

            FillCornerFringe(bmp, w, h, cx, cy, dx, dy, band, border.Value, borderLuma);
        }

        /// <summary>
        /// Scans diagonally inward from a corner to where the white ends, then returns the DARKEST pixel
        /// in the next few (the card's black edge, skipping the anti-aliased fringe). Returns null if the
        /// diagonal stays near-white across the whole band (a white/light-bordered card).
        /// </summary>
        private static SKColor? FindCornerBorderColor(SKBitmap bmp, int cx, int cy, int dx, int dy,
            int band, out int borderLuma)
        {
            borderLuma = int.MaxValue;
            for (int d = 1; d <= band; d++)
            {
                if (IsNearWhite(bmp.GetPixel(cx + dx * d, cy + dy * d))) continue;

                SKColor? darkest = null;
                for (int e = 0; e <= 4 && d + e <= band; e++)
                {
                    var q = bmp.GetPixel(cx + dx * (d + e), cy + dy * (d + e));
                    int lu = Luma(q);
                    if (lu < borderLuma) { borderLuma = lu; darkest = q; }
                }
                return darkest;
            }
            return null;
        }

        /// <summary>
        /// Fills each row from the corner inward with the border colour while pixels are clearly LIGHTER
        /// than the border — swallowing the white triangle and the grey anti-alias fringe — stopping at
        /// the dark border so the card art is never touched.
        /// </summary>
        private static void FillCornerFringe(SKBitmap bmp, int w, int h, int cx, int cy, int dx, int dy,
            int band, SKColor border, int borderLuma)
        {
            int fillAbove = Math.Min(borderLuma + 50, 200);
            for (int iy = 0; iy <= band; iy++)
            {
                int y = cy + dy * iy;
                if (y < 0 || y >= h) break;
                for (int ix = 0; ix <= band; ix++)
                {
                    int x = cx + dx * ix;
                    if (x < 0 || x >= w) break;
                    if (Luma(bmp.GetPixel(x, y)) <= fillAbove) break; // reached the dark border
                    bmp.SetPixel(x, y, border);
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
