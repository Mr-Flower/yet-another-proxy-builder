using MTGProxyBuilder.Core.Services;
using SkiaSharp;

namespace MTGProxyBuilder.Tests.Services;

public class YgoCardBackProviderTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), $"ygoback_{Guid.NewGuid():N}");

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    [Fact]
    public void GetOrCreate_RendersDecodablePngWithCardRatio()
    {
        var path = YgoCardBackProvider.GetOrCreate(_dir);

        Assert.True(File.Exists(path));
        Assert.EndsWith(".png", path);

        using var bmp = SKBitmap.Decode(path);
        Assert.NotNull(bmp);
        Assert.True(bmp!.Width > 0 && bmp.Height > 0);
        Assert.True(bmp.Height > bmp.Width, "card back should be taller than it is wide");
    }

    [Fact]
    public void GetOrCreate_IsIdempotent_ReturnsSamePath()
    {
        var first = YgoCardBackProvider.GetOrCreate(_dir);
        var firstWriteTime = File.GetLastWriteTimeUtc(first);

        var second = YgoCardBackProvider.GetOrCreate(_dir);

        Assert.Equal(first, second);
        // Second call must reuse the cached file, not re-render it.
        Assert.Equal(firstWriteTime, File.GetLastWriteTimeUtc(second));
    }
}
