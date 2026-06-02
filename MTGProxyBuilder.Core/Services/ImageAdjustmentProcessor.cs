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
                System.Diagnostics.Debug.WriteLine($"Image adjustment error: {ex.Message}");
                return sourcePath; // Fall back to original
            }
        }

        /// <summary>
        /// Produces a new bitmap with the adjustments applied. Caller owns the result.
        /// Used directly by the adjustment dialog for live preview on a downscaled copy.
        /// </summary>
        public SKBitmap Apply(SKBitmap source, ImageAdjustmentSettings settings)
        {
            var result = source.Copy();
            if (settings.IsNoOp)
                return result;

            // Precompute the channel transfer pieces once.
            float brightnessOffset = settings.Brightness / 100f * 255f;     // additive
            float c = settings.Contrast * 2.55f;                            // -255..255
            float contrastFactor = (259f * (c + 255f)) / (255f * (259f - c));
            float satMult = 1f + settings.Saturation / 100f;               // 0..2
            int blackPoint = Math.Clamp(settings.BlackPoint, 0, 254);
            float blackScale = blackPoint > 0 ? 255f / (255f - blackPoint) : 1f;

            var pixels = result.Pixels;
            for (int i = 0; i < pixels.Length; i++)
            {
                var px = pixels[i];
                float r = px.Red;
                float g = px.Green;
                float b = px.Blue;

                // 1. Brightness (additive offset)
                if (brightnessOffset != 0f)
                {
                    r += brightnessOffset;
                    g += brightnessOffset;
                    b += brightnessOffset;
                }

                // 2. Contrast (around mid-grey)
                if (settings.Contrast != 0)
                {
                    r = contrastFactor * (r - 128f) + 128f;
                    g = contrastFactor * (g - 128f) + 128f;
                    b = contrastFactor * (b - 128f) + 128f;
                }

                // 3. Saturation (lerp around perceived luminance)
                if (settings.Saturation != 0)
                {
                    float gray = 0.299f * r + 0.587f * g + 0.114f * b;
                    r = gray + (r - gray) * satMult;
                    g = gray + (g - gray) * satMult;
                    b = gray + (b - gray) * satMult;
                }

                // 4. Black point: crush everything <= threshold to black, stretch the rest.
                if (blackPoint > 0)
                {
                    r = (r - blackPoint) * blackScale;
                    g = (g - blackPoint) * blackScale;
                    b = (b - blackPoint) * blackScale;
                }

                pixels[i] = new SKColor(ClampByte(r), ClampByte(g), ClampByte(b), px.Alpha);
            }

            result.Pixels = pixels;
            return result;
        }

        private static byte ClampByte(float v) =>
            v <= 0f ? (byte)0 : v >= 255f ? (byte)255 : (byte)(v + 0.5f);
    }
}
