using System;
using System.IO;
using MTGProxyBuilder.Core.Models;
using MTGProxyBuilder.Core.Services;
using SkiaSharp;

namespace MTGProxyBuilder.Tests.Services;

public class ImageAdjustmentProcessorTests
{
    private static SKBitmap SolidBitmap(byte v, int w = 16, int h = 16)
    {
        var bmp = new SKBitmap(w, h);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(new SKColor(v, v, v));
        return bmp;
    }

    /// <summary>A bright bitmap with a dark frame of <paramref name="border"/> px on every side.</summary>
    private static SKBitmap BorderedBitmap(int size = 20, int border = 4, byte borderVal = 26, byte centerVal = 200)
    {
        var bmp = new SKBitmap(size, size);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(new SKColor(centerVal, centerVal, centerVal));
        using var paint = new SKPaint { Color = new SKColor(borderVal, borderVal, borderVal) };
        canvas.DrawRect(0, 0, size, border, paint);              // top
        canvas.DrawRect(0, size - border, size, border, paint);  // bottom
        canvas.DrawRect(0, 0, border, size, paint);              // left
        canvas.DrawRect(size - border, 0, border, size, paint);  // right
        return bmp;
    }

    private static ImageAdjustmentSettings Blacken(int threshold = 64) =>
        new() { BlackenBorder = true, BorderThreshold = threshold };

    [Fact]
    public void Apply_NoOpSettings_LeavesPixelsUnchanged()
    {
        var proc = new ImageAdjustmentProcessor();
        using var src = SolidBitmap(26);
        using var outp = proc.Apply(src, new ImageAdjustmentSettings()); // BlackenBorder off => no-op

        var p = outp.GetPixel(0, 0);
        Assert.Equal(26, p.Red);
    }

    [Fact]
    public void Apply_Disabled_LeavesPixelsUnchanged()
    {
        var proc = new ImageAdjustmentProcessor();
        using var src = SolidBitmap(26);
        using var outp = proc.Apply(src, new ImageAdjustmentSettings { Enabled = false, BlackenBorder = true, BorderThreshold = 200 });

        Assert.Equal(26, outp.GetPixel(0, 0).Red); // disabled => no change
    }

    [Fact]
    public void Apply_BlackenBorder_DarkEdgeBecomesBlack_BrightCenterKept()
    {
        var proc = new ImageAdjustmentProcessor();
        using var src = BorderedBitmap();
        using var outp = proc.Apply(src, Blacken(64));

        var corner = outp.GetPixel(0, 0);   // in the dark frame
        var center = outp.GetPixel(10, 10); // bright interior
        Assert.Equal(0, corner.Red);
        Assert.Equal(0, corner.Green);
        Assert.Equal(0, corner.Blue);
        Assert.True(center.Red > 0, "Bright interior should be untouched");
    }

    [Fact]
    public void Apply_BlackenBorder_LeavesIsolatedInteriorDarkSpotUntouched()
    {
        // A dark spot in the middle, surrounded by bright pixels, is NOT connected to the edge through
        // dark pixels — the flood must not reach it (proves this is border-only, not a global crush).
        using var src = SolidBitmap(200, 21, 21);
        using (var canvas = new SKCanvas(src))
        using (var paint = new SKPaint { Color = new SKColor(20, 20, 20) })
            canvas.DrawRect(9, 9, 3, 3, paint); // central dark spot, away from all edges

        var proc = new ImageAdjustmentProcessor();
        using var outp = proc.Apply(src, Blacken(64));

        var spot = outp.GetPixel(10, 10);
        Assert.Equal(20, spot.Red); // unchanged — interior art is preserved
    }

    [Fact]
    public void Apply_BlackenBorder_BrightImageUnchanged()
    {
        var proc = new ImageAdjustmentProcessor();
        using var src = SolidBitmap(200);
        using var outp = proc.Apply(src, Blacken(64));

        Assert.Equal(200, outp.GetPixel(0, 0).Red); // no dark border to fill
    }

    [Fact]
    public void GetAdjustedImage_NoOp_ReturnsOriginalPath()
    {
        var proc = new ImageAdjustmentProcessor();
        var tmpDir = Path.Combine(Path.GetTempPath(), $"adj_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);
        try
        {
            string input = Path.Combine(tmpDir, "card.png");
            SaveBitmap(SolidBitmap(100), input);

            var result = proc.GetAdjustedImage(input, new ImageAdjustmentSettings());
            Assert.Equal(input, result); // no-op returns the original unchanged
        }
        finally { try { Directory.Delete(tmpDir, true); } catch { } }
    }

    [Fact]
    public void GetAdjustedImage_RealSettings_WritesNewJpeg()
    {
        var proc = new ImageAdjustmentProcessor();
        var tmpDir = Path.Combine(Path.GetTempPath(), $"adj_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);
        try
        {
            string input = Path.Combine(tmpDir, "card.png");
            SaveBitmap(BorderedBitmap(), input);

            var s = Blacken(64);
            var result = proc.GetAdjustedImage(input, s);

            Assert.NotNull(result);
            Assert.NotEqual(input, result);
            Assert.True(File.Exists(result));
            Assert.EndsWith(".jpg", result!);

            // Same input + settings should hit the cache and return the same path.
            var again = proc.GetAdjustedImage(input, s);
            Assert.Equal(result, again);
        }
        finally { try { Directory.Delete(tmpDir, true); } catch { } }
    }

    [Fact]
    public void GetAdjustedImage_MissingFile_ReturnsInput()
    {
        var proc = new ImageAdjustmentProcessor();
        string fake = Path.Combine(Path.GetTempPath(), $"nope_{Guid.NewGuid():N}.png");
        var result = proc.GetAdjustedImage(fake, Blacken(64));
        Assert.Equal(fake, result);
    }

    private static void SaveBitmap(SKBitmap bmp, string path)
    {
        using (bmp)
        using (var stream = File.OpenWrite(path))
            bmp.Encode(stream, SKEncodedImageFormat.Png, 100);
    }
}
