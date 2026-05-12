using MTGProxyBuilder.Core.Services;

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
            // Create a minimal 2x2 pixel BMP (valid image for SkiaSharp)
            string inputPath = Path.Combine(tmpDir, "test_card.bmp");
            CreateMinimalBmp(inputPath, 10, 10);

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
            string inputPath = Path.Combine(tmpDir, "test_card2.bmp");
            CreateMinimalBmp(inputPath, 10, 10);

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

    /// <summary>Creates a minimal valid BMP file that SkiaSharp can decode.</summary>
    private static void CreateMinimalBmp(string path, int width, int height)
    {
        int rowSize = ((width * 3 + 3) / 4) * 4; // BMP rows padded to 4 bytes
        int pixelDataSize = rowSize * height;
        int fileSize = 54 + pixelDataSize;

        using var stream = File.Create(path);
        using var bw = new BinaryWriter(stream);

        // BMP header
        bw.Write((byte)'B'); bw.Write((byte)'M');
        bw.Write(fileSize);
        bw.Write(0); // reserved
        bw.Write(54); // pixel data offset

        // DIB header (BITMAPINFOHEADER)
        bw.Write(40); // header size
        bw.Write(width);
        bw.Write(height);
        bw.Write((short)1); // color planes
        bw.Write((short)24); // bits per pixel
        bw.Write(0); // compression
        bw.Write(pixelDataSize);
        bw.Write(2835); // horizontal resolution
        bw.Write(2835); // vertical resolution
        bw.Write(0); // colors in palette
        bw.Write(0); // important colors

        // Pixel data (blue/red gradient)
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bw.Write((byte)(x * 25)); // B
                bw.Write((byte)(y * 25)); // G
                bw.Write((byte)128);       // R
            }
            // Pad row to 4-byte boundary
            for (int p = width * 3; p < rowSize; p++)
                bw.Write((byte)0);
        }
    }
}
