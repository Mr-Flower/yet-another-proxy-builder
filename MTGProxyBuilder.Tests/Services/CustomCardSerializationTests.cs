using MTGProxyBuilder.Core.Models;
using MTGProxyBuilder.Core.Services;

namespace MTGProxyBuilder.Tests.Services;

public class CustomCardSerializationTests : IDisposable
{
    private readonly CustomCardSerializationService _service = new();
    private readonly string _tempDir;

    public CustomCardSerializationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ccproj_tests_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public async Task SaveAndLoad_RoundTrips()
    {
        var project = new CustomCardProject
        {
            ProjectName = "Test Card",
            BackgroundColor = "#112233",
            CardWidthPx = 800,
            CardHeightPx = 1100
        };
        project.Layers.Add(new ImageLayer
        {
            Name = "Background",
            ImageSource = "test.png",
            X = 0, Y = 0, Width = 800, Height = 1100,
            ZOrder = 0
        });
        project.Layers.Add(new TextLayer
        {
            Name = "Title",
            Text = "Dragon's Fire",
            FontFamily = "Arial",
            FontSize = 36,
            FontColor = "#FFFFFF",
            IsBold = true,
            TextAlignment = CardTextAlignment.Center,
            X = 100, Y = 50, Width = 600, Height = 80,
            ZOrder = 1
        });

        var filePath = Path.Combine(_tempDir, "test.ccproj");
        await _service.SaveProjectAsync(project, filePath);

        var loaded = await _service.LoadProjectAsync(filePath);

        Assert.NotNull(loaded);
        Assert.Equal("Test Card", loaded.ProjectName);
        Assert.Equal("#112233", loaded.BackgroundColor);
        Assert.Equal(800, loaded.CardWidthPx);
        Assert.Equal(1100, loaded.CardHeightPx);
        Assert.Equal(2, loaded.Layers.Count);
    }

    [Fact]
    public async Task SaveAndLoad_PreservesLayerTypes()
    {
        var project = new CustomCardProject();
        project.Layers.Add(new ImageLayer { Name = "Img", ImageSource = "x.png", ZOrder = 0 });
        project.Layers.Add(new TextLayer { Name = "Txt", Text = "Hello", ZOrder = 1 });

        var filePath = Path.Combine(_tempDir, "types.ccproj");
        await _service.SaveProjectAsync(project, filePath);
        var loaded = await _service.LoadProjectAsync(filePath);

        Assert.NotNull(loaded);
        Assert.IsType<ImageLayer>(loaded.Layers[0]);
        Assert.IsType<TextLayer>(loaded.Layers[1]);

        var imgLayer = (ImageLayer)loaded.Layers[0];
        Assert.Equal("x.png", imgLayer.ImageSource);

        var txtLayer = (TextLayer)loaded.Layers[1];
        Assert.Equal("Hello", txtLayer.Text);
    }

    [Fact]
    public async Task SaveAndLoad_PreservesTextLayerProperties()
    {
        var project = new CustomCardProject();
        project.Layers.Add(new TextLayer
        {
            Name = "Styled",
            Text = "Test",
            FontFamily = "Beleren",
            FontSize = 48,
            FontColor = "#FF0000",
            IsBold = true,
            IsItalic = true,
            TextAlignment = CardTextAlignment.Right,
            LineSpacing = 1.5f,
            StrokeColor = "#000000",
            StrokeWidth = 3,
            Opacity = 0.8f,
            Rotation = 15,
            ZOrder = 0
        });

        var filePath = Path.Combine(_tempDir, "styled.ccproj");
        await _service.SaveProjectAsync(project, filePath);
        var loaded = await _service.LoadProjectAsync(filePath);

        Assert.NotNull(loaded);
        var layer = Assert.IsType<TextLayer>(loaded.Layers[0]);
        Assert.Equal("Styled", layer.Name);
        Assert.Equal("Test", layer.Text);
        Assert.Equal("Beleren", layer.FontFamily);
        Assert.Equal(48, layer.FontSize);
        Assert.Equal("#FF0000", layer.FontColor);
        Assert.True(layer.IsBold);
        Assert.True(layer.IsItalic);
        Assert.Equal(CardTextAlignment.Right, layer.TextAlignment);
        Assert.Equal(1.5f, layer.LineSpacing);
        Assert.Equal("#000000", layer.StrokeColor);
        Assert.Equal(3, layer.StrokeWidth);
        Assert.Equal(0.8f, layer.Opacity);
        Assert.Equal(15, layer.Rotation);
    }

    [Fact]
    public async Task LoadProject_NonExistentFile_ReturnsNull()
    {
        var result = await _service.LoadProjectAsync(Path.Combine(_tempDir, "missing.ccproj"));
        Assert.Null(result);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }
}
