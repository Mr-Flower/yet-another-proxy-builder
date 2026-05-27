using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using SkiaSharp;

namespace MTGProxyBuilder.UI.Services;

public class ThumbnailService
{
    private readonly string _thumbnailDirectory;
    private const int ThumbnailWidth = 200;
    private const int JpegQuality = 85;

    public ThumbnailService(string libraryDirectory)
    {
        _thumbnailDirectory = Path.Combine(libraryDirectory, "Thumbnails");
        Directory.CreateDirectory(_thumbnailDirectory);
    }

    public string GetThumbnailPath(string entryId)
        => Path.Combine(_thumbnailDirectory, $"{entryId}.jpg");

    public bool HasThumbnail(string entryId)
        => File.Exists(GetThumbnailPath(entryId));

    public string? GetOrCreate(string entryId, string sourceFilePath)
    {
        var thumbPath = GetThumbnailPath(entryId);
        if (File.Exists(thumbPath))
            return thumbPath;
        return Generate(entryId, sourceFilePath);
    }

    public string? Generate(string entryId, string sourceFilePath)
    {
        if (!File.Exists(sourceFilePath))
            return null;

        try
        {
            var thumbPath = GetThumbnailPath(entryId);

            using var original = SKBitmap.Decode(sourceFilePath);
            if (original == null) return null;

            int targetHeight = (int)Math.Round(original.Height * (double)ThumbnailWidth / original.Width);
            using var scaled = original.Resize(new SKImageInfo(ThumbnailWidth, targetHeight), SKFilterQuality.Medium);
            if (scaled == null) return null;

            using var data = scaled.Encode(SKEncodedImageFormat.Jpeg, JpegQuality);
            using var stream = File.Create(thumbPath);
            data.SaveTo(stream);

            return thumbPath;
        }
        catch
        {
            return null;
        }
    }

    public void Delete(string entryId)
    {
        var path = GetThumbnailPath(entryId);
        if (File.Exists(path))
            try { File.Delete(path); } catch { }
    }

    public void DeleteAll()
    {
        if (!Directory.Exists(_thumbnailDirectory)) return;
        foreach (var file in Directory.GetFiles(_thumbnailDirectory, "*.jpg"))
            try { File.Delete(file); } catch { }
    }

    public int RegenerateAll(IReadOnlyList<(string Id, string FilePath)> entries,
        Action<int, int>? onProgress = null, CancellationToken ct = default)
    {
        DeleteAll();
        int generated = 0;
        for (int i = 0; i < entries.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var (id, path) = entries[i];
            if (Generate(id, path) != null)
                generated++;
            onProgress?.Invoke(i + 1, entries.Count);
        }
        return generated;
    }
}
