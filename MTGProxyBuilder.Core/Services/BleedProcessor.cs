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
                // v7: lossless PNG output. The Scryfall source is now a lossless "png" (745x1040), so
                // re-encoding as JPEG would throw away quality for no reason — PNG keeps it pixel-exact.
                string hash = $"{Path.GetFileNameWithoutExtension(sourcePath)}_{StableHash.Hex(sourcePath)}_b{bleedPixels}_v8";
                string outputPath = Path.Combine(_cacheDir, $"{hash}.png");

                if (File.Exists(outputPath))
                {
                    _processedCache[cacheKey] = outputPath;
                    return outputPath;
                }

                using var source = SKBitmap.Decode(sourcePath);
                if (source == null) return sourcePath;

                using var output = RenderBleed(source, bleedPixels);

                using var stream = File.OpenWrite(outputPath);
                output.Encode(stream, SKEncodedImageFormat.Png, 100);

                _processedCache[cacheKey] = outputPath;
                return outputPath;
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "Bleed processing error");
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

            // Scryfall: square off the rounded corners (consistent border colour), then extend the bleed.
            // GetBleedExtendedImage normalizes corners as part of RenderBleed; with no bleed we still need
            // the corner fix on its own so the transparent png / black-jpg corners don't show through.
            int bleedPx = bleedMm > 0 ? ScryfallBleedPixels(sourcePath, bleedMm, cardWmm) : 0;
            return bleedPx > 0
                ? GetBleedExtendedImage(sourcePath, bleedPx)
                : (GetCornerNormalizedImage(sourcePath) ?? sourcePath);
        }

        /// <summary>
        /// Returns a corner-normalized copy of a bare Scryfall scan (no bleed): the rounded corners are
        /// squared off to the border colour. Cached on disk. Used when bleed is 0, so corners are still
        /// consistent even without a bleed margin.
        /// </summary>
        public string? GetCornerNormalizedImage(string sourcePath)
        {
            if (string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath))
                return sourcePath;

            string cacheKey = $"{sourcePath}|corners";
            if (_processedCache.TryGetValue(cacheKey, out var cached) && File.Exists(cached))
                return cached;

            try
            {
                string hash = $"corners_{Path.GetFileNameWithoutExtension(sourcePath)}_{StableHash.Hex(sourcePath)}_v8";
                string outputPath = Path.Combine(_cacheDir, $"{hash}.png");
                if (File.Exists(outputPath)) { _processedCache[cacheKey] = outputPath; return outputPath; }

                using var source = SKBitmap.Decode(sourcePath);
                if (source == null) return sourcePath;

                NormalizeCorners(source);

                using var stream = File.OpenWrite(outputPath);
                source.Encode(stream, SKEncodedImageFormat.Png, 100);
                _processedCache[cacheKey] = outputPath;
                return outputPath;
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "Corner normalization error");
                return sourcePath;
            }
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
                // v7: JPEG quality 100. The MPCFill source is already a JPEG, so PNG would only bloat the
                // file without recovering detail; q100 re-encodes the crop with no visible generational loss.
                string hash = $"crop_{Path.GetFileNameWithoutExtension(sourcePath)}_{StableHash.Hex(sourcePath)}_b{bleedKey}_v7";
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
                output.Encode(stream, SKEncodedImageFormat.Jpeg, 100);
                _processedCache[cacheKey] = outputPath;
                return outputPath;
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "MPCFill crop error");
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
            NormalizeCorners(source);

            var output = new SKBitmap(source.Width + 2 * bleedPixels, source.Height + 2 * bleedPixels);
            using (var canvas = new SKCanvas(output))
            {
                canvas.DrawBitmap(source, bleedPixels, bleedPixels);
                StretchEdgesOutward(canvas, source, bleedPixels);
                FillCornerSquares(canvas, source, bleedPixels);
            }
            return output;
        }

        // The rounded-corner radius is a small % of the card. Bound the corner flood to a box this big so
        // it can never run along the whole border (e.g. a black corner-fill on a black-bordered card).
        private const double CornerBoxFraction = 0.14;

        /// <summary>
        /// Squares off all four rounded corners so the border reaches the corner consistently. Keys on
        /// TRANSPARENCY (Scryfall's png corners, including the anti-aliased rounding fringe) and repaints
        /// that region with the EXACT colour of the card border sampled just inside it — black border →
        /// black corner, white → white, coloured/silver kept. Opaque corners (full-art/borderless, or the
        /// flat-colour jpg scans) are left untouched: matching them by colour would risk eating a border
        /// of the same colour, and the printed output uses the transparent png anyway.
        /// </summary>
        private static void NormalizeCorners(SKBitmap source)
        {
            int w = source.Width, h = source.Height;
            if (w < 8 || h < 8) return;

            var px = source.Pixels;
            int box = Math.Max(4, (int)(Math.Min(w, h) * CornerBoxFraction));

            NormalizeCorner(px, w, h, box, 0,     0);
            NormalizeCorner(px, w, h, box, w - 1, 0);
            NormalizeCorner(px, w, h, box, 0,     h - 1);
            NormalizeCorner(px, w, h, box, w - 1, h - 1);

            source.Pixels = px;
        }

        private static bool Transparent(SKColor c) => c.Alpha < 250;

        private static void NormalizeCorner(SKColor[] px, int w, int h, int box, int cx, int cy)
        {
            // Only act on a transparent (rounded png) corner. Opaque corners are left as-is.
            if (!Transparent(px[cy * w + cx])) return;

            // The corner-fill is the transparent region (plus the semi-transparent rounding fringe).
            bool IsFill(SKColor c) => Transparent(c);

            var inRegion = new HashSet<int>();
            var stack = new Stack<int>();
            var region = new List<int>();
            long sr = 0, sg = 0, sb = 0; int sn = 0; // border-colour accumulator

            void Visit(int x, int y)
            {
                if (x < 0 || x >= w || y < 0 || y >= h) return;
                if (Math.Abs(x - cx) > box || Math.Abs(y - cy) > box) return; // stay in the corner box
                int idx = y * w + x;
                if (!inRegion.Add(idx)) return;
                if (IsFill(px[idx])) { stack.Push(idx); }
                else if (px[idx].Alpha >= 250)
                {
                    // solid pixel just inside the fill = the card border; sample its colour
                    var c = px[idx]; sr += c.Red; sg += c.Green; sb += c.Blue; sn++;
                }
            }

            int seedIdx = cy * w + cx;
            inRegion.Add(seedIdx);
            stack.Push(seedIdx); // seed is transparent (checked above)
            while (stack.Count > 0)
            {
                int idx = stack.Pop();
                region.Add(idx);
                int x = idx % w, y = idx / w;
                Visit(x - 1, y); Visit(x + 1, y); Visit(x, y - 1); Visit(x, y + 1);
            }

            if (region.Count == 0 || sn == 0) return; // nothing to fill, or no border found to match

            var border = new SKColor((byte)(sr / sn), (byte)(sg / sn), (byte)(sb / sn), 255);
            foreach (int idx in region) px[idx] = border;
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

        public void ClearCache()
        {
            _processedCache.Clear();
            if (Directory.Exists(_cacheDir))
                foreach (var f in Directory.GetFiles(_cacheDir))
                    try { File.Delete(f); } catch { }
        }
    }
}
