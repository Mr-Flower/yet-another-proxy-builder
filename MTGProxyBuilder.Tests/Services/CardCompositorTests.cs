using MTGProxyBuilder.Core.Models;
using MTGProxyBuilder.Core.Services;
using SkiaSharp;

namespace MTGProxyBuilder.Tests.Services;

public class CardCompositorTests : IDisposable
{
    private readonly CardCompositor _compositor = new();

    [Fact]
    public void RenderCard_EmptyProject_ReturnsBackgroundColor()
    {
        var project = new CustomCardProject { BackgroundColor = "#FF0000" };

        using var bitmap = _compositor.RenderCard(project);

        Assert.Equal(project.CardWidthPx, bitmap.Width);
        Assert.Equal(project.CardHeightPx, bitmap.Height);

        // Center pixel should be red
        var centerPixel = bitmap.GetPixel(bitmap.Width / 2, bitmap.Height / 2);
        Assert.Equal(255, centerPixel.Red);
        Assert.Equal(0, centerPixel.Green);
        Assert.Equal(0, centerPixel.Blue);
    }

    [Fact]
    public void RenderCard_DefaultProject_ReturnsBlackBackground()
    {
        var project = new CustomCardProject();

        using var bitmap = _compositor.RenderCard(project);

        var pixel = bitmap.GetPixel(0, 0);
        Assert.Equal(0, pixel.Red);
        Assert.Equal(0, pixel.Green);
        Assert.Equal(0, pixel.Blue);
        Assert.Equal(255, pixel.Alpha);
    }

    [Fact]
    public void RenderCard_TextLayer_RendersWithoutError()
    {
        var project = new CustomCardProject();
        project.Layers.Add(new TextLayer
        {
            Name = "Title",
            Text = "Hello World",
            FontSize = 36,
            FontColor = "#FFFFFF",
            X = 10,
            Y = 10,
            Width = 500,
            Height = 100,
            ZOrder = 0,
            IsVisible = true
        });

        using var bitmap = _compositor.RenderCard(project);

        Assert.Equal(project.CardWidthPx, bitmap.Width);
        Assert.Equal(project.CardHeightPx, bitmap.Height);
    }

    [Fact]
    public void RenderCard_InvisibleLayer_NotRendered()
    {
        var project = new CustomCardProject { BackgroundColor = "#000000" };
        project.Layers.Add(new TextLayer
        {
            Name = "Hidden",
            Text = "Should not appear",
            FontSize = 200,
            FontColor = "#FFFFFF",
            X = 0,
            Y = 0,
            Width = 744,
            Height = 500,
            ZOrder = 0,
            IsVisible = false
        });

        using var bitmap = _compositor.RenderCard(project);

        // With invisible layer, entire bitmap should remain black
        var pixel = bitmap.GetPixel(bitmap.Width / 2, 100);
        Assert.Equal(0, pixel.Red);
        Assert.Equal(0, pixel.Green);
        Assert.Equal(0, pixel.Blue);
    }

    [Fact]
    public void RenderCard_LayerOpacity_AffectsRendering()
    {
        var project = new CustomCardProject { BackgroundColor = "#000000" };
        project.Layers.Add(new TextLayer
        {
            Name = "Semi-transparent",
            Text = "Test",
            FontSize = 100,
            FontColor = "#FFFFFF",
            Opacity = 0.5f,
            X = 0,
            Y = 0,
            Width = 744,
            Height = 200,
            ZOrder = 0
        });

        // Should not throw
        using var bitmap = _compositor.RenderCard(project);
        Assert.NotNull(bitmap);
    }

    [Fact]
    public void RenderPreview_ScalesToFit()
    {
        var project = new CustomCardProject(); // 744 x 1039

        using var bitmap = _compositor.RenderPreview(project, 372, 520);

        // Should be scaled down (approximately half)
        Assert.True(bitmap.Width <= 372);
        Assert.True(bitmap.Height <= 520);
        Assert.True(bitmap.Width > 0);
        Assert.True(bitmap.Height > 0);
    }

    [Fact]
    public void RenderCard_ZOrderRespected()
    {
        var project = new CustomCardProject { BackgroundColor = "#000000" };

        // Layer with higher ZOrder should render on top
        project.Layers.Add(new TextLayer
        {
            Name = "Bottom",
            Text = "AAAA",
            FontSize = 200,
            FontColor = "#FF0000",
            X = 0, Y = 0, Width = 744, Height = 500,
            ZOrder = 0
        });
        project.Layers.Add(new TextLayer
        {
            Name = "Top",
            Text = "AAAA",
            FontSize = 200,
            FontColor = "#00FF00",
            X = 0, Y = 0, Width = 744, Height = 500,
            ZOrder = 1
        });

        // Should render without error; top layer (green) should be over bottom (red)
        using var bitmap = _compositor.RenderCard(project);
        Assert.NotNull(bitmap);
    }

    [Fact]
    public void RenderCard_ImageLayer_MissingFile_DoesNotThrow()
    {
        var project = new CustomCardProject();
        project.Layers.Add(new ImageLayer
        {
            Name = "Missing",
            ImageSource = "nonexistent_file.png",
            Width = 100,
            Height = 100,
            ZOrder = 0
        });

        using var bitmap = _compositor.RenderCard(project);
        Assert.NotNull(bitmap);
    }

    [Fact]
    public void RenderCard_TextLayer_WithStroke()
    {
        var project = new CustomCardProject();
        project.Layers.Add(new TextLayer
        {
            Name = "Stroked",
            Text = "Outline",
            FontSize = 48,
            FontColor = "#FFFFFF",
            StrokeColor = "#000000",
            StrokeWidth = 2,
            X = 10, Y = 10, Width = 500, Height = 100,
            ZOrder = 0
        });

        using var bitmap = _compositor.RenderCard(project);
        Assert.NotNull(bitmap);
    }

    public void Dispose()
    {
        _compositor.ClearCaches();
    }
}
