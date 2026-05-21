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

    // --- Metadata Tests ---

    [Fact]
    public void SetMetadata_StoresAndRetrieves()
    {
        var svc = new ImageCacheService();
        string key = $"mpc_meta_test_{Guid.NewGuid():N}";

        // Write a file so the key appears in the index
        string testFile = Path.Combine(svc.CacheDirectory, $"{key}.png");
        File.WriteAllBytes(testFile, new byte[] { 0x89, 0x50, 0x4E, 0x47 });

        try
        {
            // Re-create to pick up the new file
            var svc2 = new ImageCacheService();
            svc2.SetMetadata(key, "Lightning Bolt", "Chilli_Axe");

            var cached = svc2.GetCachedByPrefix(key);
            Assert.Single(cached);
            Assert.Equal("Lightning Bolt", cached[0].Name);
            Assert.Equal("Chilli_Axe", cached[0].Source);
        }
        finally
        {
            try { File.Delete(testFile); } catch { }
        }
    }

    [Fact]
    public void GetCachedByPrefix_ReturnsFilenameWhenNoMetadata()
    {
        var svc = new ImageCacheService();
        string key = $"mpc_nometa_{Guid.NewGuid():N}";

        string testFile = Path.Combine(svc.CacheDirectory, $"{key}.jpg");
        File.WriteAllBytes(testFile, new byte[] { 0xFF, 0xD8 });

        try
        {
            var svc2 = new ImageCacheService();
            var cached = svc2.GetCachedByPrefix(key);
            Assert.Single(cached);
            Assert.Equal(key, cached[0].Name); // Falls back to filename
            Assert.Equal("", cached[0].Source);
        }
        finally
        {
            try { File.Delete(testFile); } catch { }
        }
    }

    [Fact]
    public void GetCachedByPrefix_ReturnsEmptyForNoMatch()
    {
        var svc = new ImageCacheService();
        var result = svc.GetCachedByPrefix($"nonexistent_prefix_{Guid.NewGuid():N}");
        Assert.Empty(result);
    }

    // --- Remove Tests ---

    [Fact]
    public void Remove_DeletesFileAndIndex()
    {
        var svc = new ImageCacheService();
        string key = $"mpc_remove_test_{Guid.NewGuid():N}";
        string testFile = Path.Combine(svc.CacheDirectory, $"{key}.png");
        File.WriteAllBytes(testFile, new byte[] { 0x89, 0x50, 0x4E, 0x47 });

        var svc2 = new ImageCacheService(); // pick up the new file
        Assert.True(svc2.IsImageCached(key));

        bool removed = svc2.Remove(key);
        Assert.True(removed);
        Assert.False(File.Exists(testFile));
        Assert.False(svc2.IsImageCached(key));
        Assert.Null(svc2.GetCachedImagePath(key));
    }

    [Fact]
    public void Remove_NonexistentKey_ReturnsFalse()
    {
        var svc = new ImageCacheService();
        Assert.False(svc.Remove($"nonexistent_{Guid.NewGuid():N}"));
    }

    [Fact]
    public void Remove_AlsoDeletesMetadata()
    {
        var svc = new ImageCacheService();
        string key = $"mpc_remove_meta_{Guid.NewGuid():N}";
        string testFile = Path.Combine(svc.CacheDirectory, $"{key}.png");
        File.WriteAllBytes(testFile, new byte[] { 0x89, 0x50, 0x4E, 0x47 });

        var svc2 = new ImageCacheService();
        svc2.SetMetadata(key, "Test Card", "TestSource");

        bool removed = svc2.Remove(key);
        Assert.True(removed);

        var cached = svc2.GetCachedByPrefix(key);
        Assert.Empty(cached);
    }

    [Fact]
    public void ClearCache_AlsoDeletesMetadata()
    {
        var svc = new ImageCacheService();
        svc.SetMetadata("test_clear", "Card", "Source");
        svc.ClearCache();

        var cached = svc.GetCachedByPrefix("test_clear");
        Assert.Empty(cached);
    }
}
