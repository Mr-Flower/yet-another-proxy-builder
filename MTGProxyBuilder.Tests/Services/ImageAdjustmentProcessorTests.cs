using System;
using System.IO;
using MTGProxyBuilder.Core.Models;
using MTGProxyBuilder.Core.Services;
using SkiaSharp;

namespace MTGProxyBuilder.Tests.Services;

public class ImageAdjustmentProcessorTests
{
    private static SKBitmap SolidBitmap(byte r, byte g, byte b, int w = 8, int h = 8)
    {
        var bmp = new SKBitmap(w, h);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(new SKColor(r, g, b));
        return bmp;
    }

    [Fact]
    public void Apply_NoOpSettings_LeavesPixelsUnchanged()
    {
        var proc = new ImageAdjustmentProcessor();
        using var src = SolidBitmap(120, 130, 140);
        using var outp = proc.Apply(src, new ImageAdjustmentSettings()); // all neutral

        var p = outp.GetPixel(0, 0);
        Assert.Equal(120, p.Red);
        Assert.Equal(130, p.Green);
        Assert.Equal(140, p.Blue);
    }

    [Fact]
    public void Apply_Disabled_LeavesPixelsUnchanged()
    {
        var proc = new ImageAdjustmentProcessor();
        using var src = SolidBitmap(26, 26, 26);
        var s = new ImageAdjustmentSettings { Enabled = false, BlackPoint = 200 };
        using var outp = proc.Apply(src, s);

        var p = outp.GetPixel(0, 0);
        Assert.Equal(26, p.Red); // disabled => no change despite black point
    }

    [Fact]
    public void Apply_BlackPoint_CrushesDarkGreyToBlack()
    {
        var proc = new ImageAdjustmentProcessor();
        using var src = SolidBitmap(26, 26, 26); // typical Scryfall border grey
        var s = new ImageAdjustmentSettings { BlackPoint = 40 };
        using var outp = proc.Apply(src, s);

        var p = outp.GetPixel(0, 0);
        Assert.Equal(0, p.Red);
        Assert.Equal(0, p.Green);
        Assert.Equal(0, p.Blue);
    }

    [Fact]
    public void Apply_BlackPoint_KeepsBrightPixelNonBlack()
    {
        var proc = new ImageAdjustmentProcessor();
        using var src = SolidBitmap(200, 200, 200);
        var s = new ImageAdjustmentSettings { BlackPoint = 40 };
        using var outp = proc.Apply(src, s);

        var p = outp.GetPixel(0, 0);
        Assert.True(p.Red > 0, "A bright pixel should not be crushed to black");
    }

    [Fact]
    public void Apply_PositiveBrightness_IncreasesChannels()
    {
        var proc = new ImageAdjustmentProcessor();
        using var src = SolidBitmap(100, 100, 100);
        var s = new ImageAdjustmentSettings { Brightness = 50 };
        using var outp = proc.Apply(src, s);

        var p = outp.GetPixel(0, 0);
        Assert.True(p.Red > 100, "Positive brightness should raise channel values");
    }

    [Fact]
    public void Apply_FullNegativeSaturation_ProducesGrey()
    {
        var proc = new ImageAdjustmentProcessor();
        using var src = SolidBitmap(200, 50, 50);
        var s = new ImageAdjustmentSettings { Saturation = -100 };
        using var outp = proc.Apply(src, s);

        var p = outp.GetPixel(0, 0);
        Assert.Equal(p.Red, p.Green);
        Assert.Equal(p.Green, p.Blue); // grayscale => all channels equal
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
            SaveBitmap(SolidBitmap(100, 100, 100), input);

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
            SaveBitmap(SolidBitmap(26, 26, 26), input);

            var s = new ImageAdjustmentSettings { BlackPoint = 40 };
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
        var result = proc.GetAdjustedImage(fake, new ImageAdjustmentSettings { BlackPoint = 40 });
        Assert.Equal(fake, result);
    }

    private static void SaveBitmap(SKBitmap bmp, string path)
    {
        using (bmp)
        using (var stream = File.OpenWrite(path))
            bmp.Encode(stream, SKEncodedImageFormat.Png, 100);
    }
}
