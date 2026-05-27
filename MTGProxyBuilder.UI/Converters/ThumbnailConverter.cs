using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;

namespace MTGProxyBuilder.UI.Converters;

public class ThumbnailConverter : IMultiValueConverter
{
    private static readonly ConcurrentDictionary<string, Bitmap?> _cache = new(StringComparer.OrdinalIgnoreCase);
    private static Services.ThumbnailService? _thumbnailService;

    public static void SetThumbnailService(Services.ThumbnailService? service) => _thumbnailService = service;

    public static void ClearCache()
    {
        foreach (var bmp in _cache.Values)
            bmp?.Dispose();
        _cache.Clear();
    }

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count < 2 || values[0] is not string path || values[1] is not string entryId)
            return null;
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return null;

        if (_cache.TryGetValue(entryId, out var cached))
            return cached;

        try
        {
            string loadPath = _thumbnailService?.GetOrCreate(entryId, path) ?? path;
            var bmp = new Bitmap(loadPath);
            _cache[entryId] = bmp;
            return bmp;
        }
        catch
        {
            _cache[entryId] = null;
            return null;
        }
    }
}
