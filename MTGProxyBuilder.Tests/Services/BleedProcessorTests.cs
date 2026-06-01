using MTGProxyBuilder.Core;
using MTGProxyBuilder.Core.Services;
using SkiaSharp;

namespace MTGProxyBuilder.Tests.Services;

public class BleedProcessorTests : IDisposable
{
    // A unique cache folder per test instance (xUnit news up the class per test method) keeps the
    // shared on-disk/in-memory cache from racing across parallel tests — see the flaky-test fix.
    private readonly string _cacheDir =
        Path.Combine(Path.GetTempPath(), $"bleedcache_{Guid.NewGuid():N}");

    private BleedProcessor NewProc() => new(_cacheDir);

    public void Dispose()
    {
        try { Directory.Delete(_cacheDir, true); } catch { }
    }

    [Fact]
    public void GetBleedExtendedImage_NullPath_ReturnsNull()
    {
        var proc = NewProc();
        Assert.Null(proc.GetBleedExtendedImage(null!, 10));
    }

    [Fact]
    public void GetBleedExtendedImage_EmptyPath_ReturnsEmpty()
    {
        var proc = NewProc();
        Assert.Equal(string.Empty, proc.GetBleedExtendedImage(string.Empty, 10));
    }

    [Fact]
    public void GetBleedExtendedImage_ZeroBleed_ReturnsOriginal()
    {
        var proc = NewProc();
        Assert.Equal("/some/path.jpg", proc.GetBleedExtendedImage("/some/path.jpg", 0));
    }

    [Fact]
    public void GetBleedExtendedImage_NegativeBleed_ReturnsOriginal()
    {
        var proc = NewProc();
        Assert.Equal("/some/path.jpg", proc.GetBleedExtendedImage("/some/path.jpg", -5));
    }

    [Fact]
    public void GetBleedExtendedImage_NonexistentFile_ReturnsOriginal()
    {
        var proc = NewProc();
        string fakePath = $"/nonexistent/{Guid.NewGuid():N}.png";
        Assert.Equal(fakePath, proc.GetBleedExtendedImage(fakePath, 10));
    }

    [Fact]
    public void ClearCache_DoesNotThrow()
    {
        var proc = NewProc();
        var ex = Record.Exception(() => proc.ClearCache());
        Assert.Null(ex);
    }

    [Fact]
    public void GetBleedExtendedImage_ValidImage_ReturnsProcessedPath()
    {
        // Create a tiny valid PNG-like image to test processing
        var proc = NewProc();
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
        var proc = NewProc();
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

    // ---- MPCFill art already has bleed (must not be extended) ----

    [Theory]
    [InlineData("/cache/mpc_abc123.jpg", true)]
    [InlineData("/cache/MPC_Upper.png", true)]
    [InlineData("/cache/3f2a9b-scryfall.jpg", false)]
    [InlineData("/lib/abcdef123456.jpg", false)]
    [InlineData("", false)]
    public void ImageAlreadyHasBleed_DetectsMpcFillByPrefix(string path, bool expected)
        => Assert.Equal(expected, BleedProcessor.ImageAlreadyHasBleed(path));

    [Fact]
    public void GetDisplayImage_MpcFillArt_ReturnedWholeUnprocessed()
    {
        // MPCFill art is already full-bleed -> drawn whole (no crop, no extend), so GetDisplayImage
        // returns the original path unchanged; the cut line trims the built-in 1/8".
        var proc = NewProc();
        var tmpDir = Path.Combine(Path.GetTempPath(), $"disp_mpc_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);
        try
        {
            string input = Path.Combine(tmpDir, "mpc_full.png"); // mpc_ prefix => already full-bleed
            CreateTestImage(input, 80, 112);
            var result = proc.GetDisplayImage(input, Constants.MpcBleedMm, 63);
            Assert.Equal(input, result);
        }
        finally { try { Directory.Delete(tmpDir, true); } catch { } proc.ClearCache(); }
    }

    [Fact]
    public void GetDisplayImage_ScryfallScan_DelegatesToBleedExtend()
    {
        var proc = NewProc();
        var tmpDir = Path.Combine(Path.GetTempPath(), $"disp_scry_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);
        try
        {
            string input = Path.Combine(tmpDir, "scryfall_card.png"); // no mpc_ prefix
            CreateTestImage(input, 400, 560); // realistic-ish scan; 1/8" => a multi-px border
            var result = proc.GetDisplayImage(input, Constants.MpcBleedMm, 63);
            Assert.NotNull(result);
            Assert.NotEqual(input, result);
            Assert.True(File.Exists(result));
        }
        finally { try { Directory.Delete(tmpDir, true); } catch { } proc.ClearCache(); }
    }

    [Fact]
    public void GetDisplayImage_ZeroBleed_ReturnsOriginal()
        => Assert.Equal("/x/mpc_a.jpg", NewProc().GetDisplayImage("/x/mpc_a.jpg", 0, 63));

    [Fact]
    public void GetDisplayImage_MpcFillReducedBleed_CropsTowardCard()
    {
        // Reducing the bleed below 1/8" trims MPCFill's native bleed: the output is smaller than the
        // full image (outer margin removed) but still larger than the bare card.
        var proc = NewProc();
        var tmpDir = Path.Combine(Path.GetTempPath(), $"mpccrop_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);
        try
        {
            string input = Path.Combine(tmpDir, "mpc_full.png");
            CreateTestImage(input, 822, 1122); // MPC poker template (card 750x1050 + 36px bleed/side)
            var result = proc.GetDisplayImage(input, Constants.MpcBleedMm / 2, 63);
            Assert.NotNull(result);
            Assert.NotEqual(input, result);
            using var bmp = SKBitmap.Decode(result);
            Assert.InRange(bmp.Width, 751, 821); // between card (750) and full (822)
        }
        finally { try { Directory.Delete(tmpDir, true); } catch { } proc.ClearCache(); }
    }

    [Fact]
    public void GetDisplayImage_MpcFillZeroBleed_CropsToCard()
    {
        // At bleed 0 the native 1/8" bleed is fully trimmed, leaving just the ~750x1050 card region.
        var proc = NewProc();
        var tmpDir = Path.Combine(Path.GetTempPath(), $"mpccrop0_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);
        try
        {
            string input = Path.Combine(tmpDir, "mpc_full.png");
            CreateTestImage(input, 822, 1122);
            var result = proc.GetDisplayImage(input, 0, 63);
            using var bmp = SKBitmap.Decode(result);
            Assert.InRange(bmp.Width, 748, 752);
            Assert.InRange(bmp.Height, 1048, 1052);
        }
        finally { try { Directory.Delete(tmpDir, true); } catch { } proc.ClearCache(); }
    }

    [Theory]
    [InlineData(630)]
    [InlineData(1260)]
    public void GetDisplayImage_ScryfallBleed_ScalesWithResolution(int srcW)
    {
        // The added bleed must be a fixed FRACTION of the card (≈ MpcBleedMm/cardWmm per side),
        // independent of scan resolution — so a low- and a high-res scan of the same card get the same
        // proportional 1/8" border, matching MPCFill. (The old code added a fixed 14 px regardless of
        // resolution, so the bleed was far too thin on high-res scans and the zoom diverged.)
        var proc = NewProc();
        int srcH = (int)(srcW * 88.0 / 63.0);
        var tmpDir = Path.Combine(Path.GetTempPath(), $"disp_res_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);
        try
        {
            string input = Path.Combine(tmpDir, "scryfall_card.png"); // no mpc_ prefix
            CreateTestImage(input, srcW, srcH);
            var result = proc.GetDisplayImage(input, Constants.MpcBleedMm, 63);
            using var bmp = SKBitmap.Decode(result);
            double bleedFractionPerSide = (bmp.Width - srcW) / 2.0 / srcW;
            double expected = Constants.MpcBleedMm / 63.0; // ≈ 0.0484 of the card width per side
            Assert.InRange(bleedFractionPerSide, expected - 0.005, expected + 0.005);
        }
        finally { try { Directory.Delete(tmpDir, true); } catch { } proc.ClearCache(); }
    }

    // ---- corner square-off (white triangle removal) ----

    [Fact]
    public void GetBleedExtendedImage_BlackBorderWhiteCorners_FillsCornersDark()
    {
        // White background with a black rounded "card" -> the 4 rectangle corners are white triangles.
        var proc = NewProc();
        var tmpDir = Path.Combine(Path.GetTempPath(), $"bleed_corner_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);
        try
        {
            string input = Path.Combine(tmpDir, "blackcard.png");
            CreateRoundedCardImage(input, 60, 84, radius: 5, card: SKColors.Black, bg: SKColors.White);

            var result = proc.GetBleedExtendedImage(input, 6);
            Assert.NotNull(result);
            using var outBmp = SKBitmap.Decode(result);

            // Every extreme output corner (in the bleed) must be dark, not the original white.
            AssertDark(outBmp.GetPixel(0, 0));
            AssertDark(outBmp.GetPixel(outBmp.Width - 1, 0));
            AssertDark(outBmp.GetPixel(0, outBmp.Height - 1));
            AssertDark(outBmp.GetPixel(outBmp.Width - 1, outBmp.Height - 1));
        }
        finally { try { Directory.Delete(tmpDir, true); } catch { } proc.ClearCache(); }
    }

    [Fact]
    public void GetBleedExtendedImage_AntiAliasedBlackBorder_NoLightFringeNearCorner()
    {
        // Anti-aliased rounded black card on white: the white->black arc has grey fringe pixels.
        // After square-off the whole corner region (bleed + the former white triangle) must be dark,
        // with no light/grey fringe left (the "white artefacts near the corner" the user reported).
        var proc = NewProc();
        var tmpDir = Path.Combine(Path.GetTempPath(), $"bleed_aa_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);
        try
        {
            string input = Path.Combine(tmpDir, "aacard.png");
            CreateRoundedCardImage(input, 120, 168, radius: 12, card: SKColors.Black, bg: SKColors.White, antiAlias: true);

            int bleed = 8;
            var result = proc.GetBleedExtendedImage(input, bleed);
            using var outBmp = SKBitmap.Decode(result);

            // Scan the top-left corner region of the output (bleed + ~the rounding radius).
            int region = bleed + 16;
            for (int y = 0; y < region; y++)
                for (int x = 0; x < region; x++)
                {
                    var c = outBmp.GetPixel(x, y);
                    int luma = (c.Red * 30 + c.Green * 59 + c.Blue * 11) / 100;
                    Assert.True(luma < 140,
                        $"light fringe left at ({x},{y}) = ({c.Red},{c.Green},{c.Blue})");
                }
        }
        finally { try { Directory.Delete(tmpDir, true); } catch { } proc.ClearCache(); }
    }

    [Fact]
    public void GetBleedExtendedImage_WhiteCard_KeepsCornersWhite()
    {
        // An all-white card: a white border must stay white (no recolouring to art).
        var proc = NewProc();
        var tmpDir = Path.Combine(Path.GetTempPath(), $"bleed_white_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);
        try
        {
            string input = Path.Combine(tmpDir, "whitecard.png");
            CreateRoundedCardImage(input, 60, 84, radius: 5, card: SKColors.White, bg: SKColors.White);

            var result = proc.GetBleedExtendedImage(input, 6);
            using var outBmp = SKBitmap.Decode(result);
            AssertNearWhite(outBmp.GetPixel(0, 0));
            AssertNearWhite(outBmp.GetPixel(outBmp.Width - 1, outBmp.Height - 1));
        }
        finally { try { Directory.Delete(tmpDir, true); } catch { } proc.ClearCache(); }
    }

    [Fact]
    public void GetBleedExtendedImage_FullArtColouredCorners_NotRecoloured()
    {
        // Full-art card (corners already coloured, no white triangle): corners must NOT be repainted.
        var proc = NewProc();
        var tmpDir = Path.Combine(Path.GetTempPath(), $"bleed_red_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);
        try
        {
            string input = Path.Combine(tmpDir, "redcard.png");
            using (var bmp = new SKBitmap(60, 84))
            {
                using var canvas = new SKCanvas(bmp);
                canvas.Clear(SKColors.Red);
                using var stream = File.OpenWrite(input);
                bmp.Encode(stream, SKEncodedImageFormat.Png, 100);
            }

            var result = proc.GetBleedExtendedImage(input, 6);
            using var outBmp = SKBitmap.Decode(result);
            var px = outBmp.GetPixel(0, 0);
            Assert.True(px.Red > 150 && px.Green < 120 && px.Blue < 120,
                $"corner should stay red-ish, was ({px.Red},{px.Green},{px.Blue})");
        }
        finally { try { Directory.Delete(tmpDir, true); } catch { } proc.ClearCache(); }
    }

    private static void AssertDark(SKColor c) =>
        Assert.True(c.Red < 80 && c.Green < 80 && c.Blue < 80,
            $"expected dark corner, was ({c.Red},{c.Green},{c.Blue})");

    private static void AssertNearWhite(SKColor c) =>
        Assert.True(c.Red >= 230 && c.Green >= 230 && c.Blue >= 230,
            $"expected white corner, was ({c.Red},{c.Green},{c.Blue})");

    private static void CreateRoundedCardImage(string path, int w, int h, float radius, SKColor card, SKColor bg, bool antiAlias = false)
    {
        using var bitmap = new SKBitmap(w, h);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(bg);
        using var paint = new SKPaint { Color = card, IsAntialias = antiAlias, Style = SKPaintStyle.Fill };
        canvas.DrawRoundRect(new SKRect(0, 0, w, h), radius, radius, paint);
        canvas.Flush();
        using var stream = File.OpenWrite(path);
        bitmap.Encode(stream, SKEncodedImageFormat.Png, 100);
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
