using MTGProxyBuilder.Core.Services;
using SkiaSharp;

namespace MTGProxyBuilder.Tests.Services;

public class BleedProcessorTests
{
    [Fact]
    public void GetBleedExtendedImage_NullPath_ReturnsNull()
    {
        var proc = new BleedProcessor();
        Assert.Null(proc.GetBleedExtendedImage(null!, 10));
    }

    [Fact]
    public void GetBleedExtendedImage_EmptyPath_ReturnsEmpty()
    {
        var proc = new BleedProcessor();
        Assert.Equal(string.Empty, proc.GetBleedExtendedImage(string.Empty, 10));
    }

    [Fact]
    public void GetBleedExtendedImage_ZeroBleed_ReturnsOriginal()
    {
        var proc = new BleedProcessor();
        Assert.Equal("/some/path.jpg", proc.GetBleedExtendedImage("/some/path.jpg", 0));
    }

    [Fact]
    public void GetBleedExtendedImage_NegativeBleed_ReturnsOriginal()
    {
        var proc = new BleedProcessor();
        Assert.Equal("/some/path.jpg", proc.GetBleedExtendedImage("/some/path.jpg", -5));
    }

    [Fact]
    public void GetBleedExtendedImage_NonexistentFile_ReturnsOriginal()
    {
        var proc = new BleedProcessor();
        string fakePath = $"/nonexistent/{Guid.NewGuid():N}.png";
        Assert.Equal(fakePath, proc.GetBleedExtendedImage(fakePath, 10));
    }

    [Fact]
    public void ClearCache_DoesNotThrow()
    {
        var proc = new BleedProcessor();
        var ex = Record.Exception(() => proc.ClearCache());
        Assert.Null(ex);
    }

    [Fact]
    public void GetBleedExtendedImage_ValidImage_ReturnsProcessedPath()
    {
        // Create a tiny valid PNG-like image to test processing
        var proc = new BleedProcessor();
        var tmpDir = Path.Combine(Path.GetTempPath(), $"bleed_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);

        try
        {
            string inputPath = Path.Combine(tmpDir, "test_card.png");
            CreateTestImage(inputPath, 10, 10);

            var result = proc.GetBleedExtendedImage(inputPath, 2);

            Assert.NotNull(result);
            Assert.NotEqual(inputPath, result); // Should return a different (processed) path
            Assert.True(File.Exists(result), "Processed file should exist on disk");
            Assert.EndsWith(".jpg", result!); // Should be JPEG
        }
        finally
        {
            try { Directory.Delete(tmpDir, true); } catch { }
            proc.ClearCache();
        }
    }

    [Fact]
    public void GetBleedExtendedImage_SameInputTwice_ReturnsCached()
    {
        var proc = new BleedProcessor();
        var tmpDir = Path.Combine(Path.GetTempPath(), $"bleed_test2_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);

        try
        {
            string inputPath = Path.Combine(tmpDir, "test_card2.png");
            CreateTestImage(inputPath, 10, 10);

            var result1 = proc.GetBleedExtendedImage(inputPath, 2);
            var result2 = proc.GetBleedExtendedImage(inputPath, 2);

            Assert.Equal(result1, result2); // Same path from cache
        }
        finally
        {
            try { Directory.Delete(tmpDir, true); } catch { }
            proc.ClearCache();
        }
    }

    private static void CreateTestImage(string path, int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.CornflowerBlue);
        using var stream = File.OpenWrite(path);
        bitmap.Encode(stream, SKEncodedImageFormat.Png, 100);
    }
}
