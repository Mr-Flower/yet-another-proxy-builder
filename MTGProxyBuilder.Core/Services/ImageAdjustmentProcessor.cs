using System.Collections.Concurrent;
using MTGProxyBuilder.Core.Models;
using SkiaSharp;

namespace MTGProxyBuilder.Core.Services
{
    /// <summary>
    /// Applies brightness / contrast / saturation / black-point adjustments to card
    /// artwork using SkiaSharp. Mirrors the BleedProcessor pattern: a path-to-path
    /// transform whose results are cached on disk, so each (image, settings) pair is
    /// only computed once and both the on-screen preview and the PDF pick it up simply
    /// by following the card's ArtworkPath.
    ///
    /// Fork-specific file — self-contained, never touched by upstream.
    /// </summary>
    public class ImageAdjustmentProcessor
    {
        private readonly string _cacheDir;
        private static readonly ConcurrentDictionary<string, string> _processedCache = new();

        public ImageAdjustmentProcessor()
        {
            _cacheDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "YetAnotherProxyBuilder", "AdjustCache");
            Directory.CreateDirectory(_cacheDir);
        }

        public string CacheDirectory => _cacheDir;

        /// <summary>
        /// Returns the path to an adjusted version of the source image. If the settings
        /// are a no-op (or the source is missing) the original path is returned unchanged.
        /// Results are cached on disk keyed by source path + settings signature.
        /// </summary>
        public string? GetAdjustedImage(string? sourcePath, ImageAdjustmentSettings settings)
        {
            if (string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath))
                return sourcePath;
            if (settings.IsNoOp)
                return sourcePath;

            string cacheKey = $"{sourcePath}|{settings.Signature()}";
            if (_processedCache.TryGetValue(cacheKey, out var cached) && File.Exists(cached))
                return cached;

            try
            {
                string hash = $"{Path.GetFileNameWithoutExtension(sourcePath)}_{sourcePath.GetHashCode():X8}_{settings.Signature()}";
                string outputPath = Path.Combine(_cacheDir, $"{hash}.jpg");

                if (File.Exists(outputPath))
                {
                    _processedCache[cacheKey] = outputPath;
                    return outputPath;
                }

                using var source = SKBitmap.Decode(sourcePath);
                if (source == null) return sourcePath;

                using var adjusted = Apply(source, settings);

                using (var stream = File.OpenWrite(outputPath))
                    adjusted.Encode(stream, SKEncodedImageFormat.Jpeg, 95);

                _processedCache[cacheKey] = outputPath;
                return outputPath;
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "Image adjustment error");
                return sourcePath; // Fall back to original
            }
        }

        /// <summary>
        /// Produces a new bitmap with the card's border blackened. Caller owns the result.
        /// Used directly by the adjustment dialog for live preview on a downscaled copy.
        /// </summary>
        public SKBitmap Apply(SKBitmap source, ImageAdjustmentSettings settings)
        {
            var result = source.Copy();
            if (settings.IsNoOp)
                return result;

            BlackenBorder(result, Math.Clamp(settings.BorderThreshold, 0, 255));
            return result;
        }

        /// <summary>
        /// Floods inward from the four edges over connected pixels whose luminance is at or below
        /// <paramref name="threshold"/>, setting each to pure black. This blackens only the dark border
        /// that touches the image edge — it stops at the lighter card frame/art, so dark areas inside the
        /// artwork (which aren't edge-connected through dark pixels) are left untouched.
        /// </summary>
        private static void BlackenBorder(SKBitmap bmp, int threshold)
        {
            int w = bmp.Width, h = bmp.Height;
            if (w == 0 || h == 0) return;

            var px = bmp.Pixels;
            var visited = new bool[px.Length];
            var queue = new Queue<int>();

            bool IsBorderDark(int idx)
            {
                var c = px[idx];
                // Rec.601 luma; cheap and matches how the eye weights the channels.
                float luma = 0.299f * c.Red + 0.587f * c.Green + 0.114f * c.Blue;
                return luma <= threshold;
            }

            void Seed(int idx)
            {
                if (!visited[idx] && IsBorderDark(idx))
                {
                    visited[idx] = true;
                    queue.Enqueue(idx);
                }
            }

            // Seed from every edge pixel.
            for (int x = 0; x < w; x++) { Seed(x); Seed((h - 1) * w + x); }
            for (int y = 0; y < h; y++) { Seed(y * w); Seed(y * w + (w - 1)); }

            while (queue.Count > 0)
            {
                int idx = queue.Dequeue();
                int x = idx % w, y = idx / w;
                var a = px[idx].Alpha;
                px[idx] = new SKColor(0, 0, 0, a); // pure black, keep alpha

                if (x > 0)     Seed(idx - 1);
                if (x < w - 1) Seed(idx + 1);
                if (y > 0)     Seed(idx - w);
                if (y < h - 1) Seed(idx + w);
            }

            bmp.Pixels = px;
        }
    }
}
