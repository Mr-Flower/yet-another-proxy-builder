using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace MTGProxyBuilder.UI.Converters
{
    /// <summary>
    /// Converts a (FilePath, Id) pair to a BitmapImage for display in virtualized lists.
    /// Uses a ThumbnailService for fast pre-generated thumbnails, with an in-memory
    /// cache so re-scrolling doesn't reload from disk.
    /// </summary>
    public class ThumbnailConverter : IMultiValueConverter
    {
        private static readonly ConcurrentDictionary<string, BitmapImage?> _cache = new(StringComparer.OrdinalIgnoreCase);
        private static Services.ThumbnailService? _thumbnailService;

        public static void SetThumbnailService(Services.ThumbnailService? service) => _thumbnailService = service;
        public static void ClearCache() => _cache.Clear();

        public object? Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture)
        {
            if (values.Length < 2 || values[0] is not string path || values[1] is not string entryId)
                return null;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return null;

            string cacheKey = entryId;
            if (_cache.TryGetValue(cacheKey, out var cached))
                return cached;

            try
            {
                string loadPath = _thumbnailService?.GetOrCreate(entryId, path) ?? path;
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(loadPath, UriKind.Absolute);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.DecodePixelWidth = 150;
                bmp.EndInit();
                bmp.Freeze();
                _cache[cacheKey] = bmp;
                return bmp;
            }
            catch
            {
                _cache[cacheKey] = null;
                return null;
            }
        }

        public object?[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
