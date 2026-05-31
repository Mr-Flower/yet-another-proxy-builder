using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;

namespace MTGProxyBuilder.UI.Converters;

/// <summary>
/// Attached property that asynchronously loads an image into an <see cref="Image"/> from either
/// a remote http(s) URL (e.g. Scryfall thumbnails) or a local file path. Decoded bitmaps are
/// cached in-memory keyed by the source string, so the same card art is only fetched once.
/// Usage in XAML: <c>&lt;Image converters:ImageLoader.Source="{Binding SmallImageUrl}"/&gt;</c>.
/// </summary>
public class ImageLoader
{
    private ImageLoader() { }

    private static readonly HttpClient _http = CreateClient();
    private static readonly ConcurrentDictionary<string, Bitmap?> _cache =
        new(StringComparer.OrdinalIgnoreCase);

    public static readonly AttachedProperty<string?> SourceProperty =
        AvaloniaProperty.RegisterAttached<ImageLoader, Image, string?>("Source");

    static ImageLoader()
    {
        SourceProperty.Changed.AddClassHandler<Image>((img, _) => OnSourceChanged(img));
    }

    public static void SetSource(Image element, string? value) => element.SetValue(SourceProperty, value);
    public static string? GetSource(Image element) => element.GetValue(SourceProperty);

    private static HttpClient CreateClient()
    {
        var client = new HttpClient();
        // Scryfall asks API consumers to identify themselves; harmless for image CDN requests too.
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "MTGProxyBuilder/1.0");
        return client;
    }

    private static async void OnSourceChanged(Image img)
    {
        var requested = GetSource(img);
        img.Source = null;
        if (string.IsNullOrWhiteSpace(requested))
            return;

        var bitmap = await LoadAsync(requested);

        // Guard against control recycling (ListBox virtualization): only assign the bitmap if the
        // image control is still asking for the same source we just finished loading.
        if (bitmap != null && GetSource(img) == requested)
            img.Source = bitmap;
    }

    private static async Task<Bitmap?> LoadAsync(string source)
    {
        if (_cache.TryGetValue(source, out var cached))
            return cached;

        Bitmap? bitmap = null;
        try
        {
            if (source.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                var bytes = await _http.GetByteArrayAsync(source);
                using var ms = new MemoryStream(bytes);
                bitmap = new Bitmap(ms);
            }
            else if (File.Exists(source))
            {
                bitmap = await Task.Run(() => new Bitmap(source));
            }
        }
        catch
        {
            bitmap = null;
        }

        _cache[source] = bitmap;
        return bitmap;
    }
}
