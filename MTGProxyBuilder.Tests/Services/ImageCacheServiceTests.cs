using MTGProxyBuilder.Core.Services;

namespace MTGProxyBuilder.Tests.Services;

public class ImageCacheServiceTests
{
    [Fact]
    public void CacheDirectory_Exists()
    {
        var svc = new ImageCacheService();
        Assert.True(Directory.Exists(svc.CacheDirectory));
    }

    [Fact]
    public void IsImageCached_ReturnsFalse_WhenNotCached()
    {
        var svc = new ImageCacheService();
        Assert.False(svc.IsImageCached($"nonexistent_{Guid.NewGuid():N}"));
    }

    [Fact]
    public void GetCachedImagePath_ReturnsNull_WhenNotCached()
    {
        var svc = new ImageCacheService();
        Assert.Null(svc.GetCachedImagePath($"nonexistent_{Guid.NewGuid():N}"));
    }

    [Fact]
    public void ClearCache_DoesNotThrow()
    {
        var svc = new ImageCacheService();
        var ex = Record.Exception(() => svc.ClearCache());
        Assert.Null(ex);
    }

    [Fact]
    public async Task CacheImageFromUrlAsync_ReturnsNull_ForInvalidUrl()
    {
        var svc = new ImageCacheService();
        using var http = new HttpClient();
        var result = await svc.CacheImageFromUrlAsync(http, "https://invalid.example.com/no-image.jpg", $"test_{Guid.NewGuid():N}");
        Assert.Null(result);
    }
}
