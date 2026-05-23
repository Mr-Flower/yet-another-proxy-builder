using MTGProxyBuilder.Core.Models;

namespace MTGProxyBuilder.Tests.Models;

public class LayerBaseTests
{
    // Use ImageLayer as a concrete subclass for testing LayerBase
    private static ImageLayer CreateLayer() => new();

    [Fact]
    public void NewLayer_HasUniqueId()
    {
        var layer1 = CreateLayer();
        var layer2 = CreateLayer();
        Assert.NotEqual(layer1.Id, layer2.Id);
    }

    [Fact]
    public void NewLayer_HasDefaultValues()
    {
        var layer = CreateLayer();
        Assert.Equal(string.Empty, layer.Name);
        Assert.True(layer.IsVisible);
        Assert.Equal(1.0f, layer.Opacity);
        Assert.Equal(0, layer.ZOrder);
        Assert.Equal(0f, layer.X);
        Assert.Equal(0f, layer.Y);
        Assert.Equal(0f, layer.Width);
        Assert.Equal(0f, layer.Height);
        Assert.Equal(0f, layer.Rotation);
        Assert.False(layer.IsLocked);
    }

    [Fact]
    public void Opacity_ClampedToValidRange()
    {
        var layer = CreateLayer();

        layer.Opacity = 1.5f;
        Assert.Equal(1.0f, layer.Opacity);

        layer.Opacity = -0.5f;
        Assert.Equal(0f, layer.Opacity);

        layer.Opacity = 0.5f;
        Assert.Equal(0.5f, layer.Opacity);
    }

    [Fact]
    public void PropertyChanged_RaisedOnSetters()
    {
        var layer = CreateLayer();
        var changedProps = new List<string>();
        layer.PropertyChanged += (_, e) => changedProps.Add(e.PropertyName!);

        layer.Name = "Test";
        layer.IsVisible = false;
        layer.Opacity = 0.5f;
        layer.ZOrder = 3;
        layer.X = 10;
        layer.Y = 20;
        layer.Width = 100;
        layer.Height = 200;
        layer.Rotation = 45;
        layer.IsLocked = true;

        Assert.Contains("Name", changedProps);
        Assert.Contains("IsVisible", changedProps);
        Assert.Contains("Opacity", changedProps);
        Assert.Contains("ZOrder", changedProps);
        Assert.Contains("X", changedProps);
        Assert.Contains("Y", changedProps);
        Assert.Contains("Width", changedProps);
        Assert.Contains("Height", changedProps);
        Assert.Contains("Rotation", changedProps);
        Assert.Contains("IsLocked", changedProps);
    }

    [Fact]
    public void ImageLayer_HasDefaultValues()
    {
        var layer = new ImageLayer();
        Assert.Equal(string.Empty, layer.ImageSource);
        Assert.Null(layer.ImageBytes);
        Assert.False(layer.MaskEnabled);
        Assert.Null(layer.MaskPath);
    }

    [Fact]
    public void TextLayer_HasDefaultValues()
    {
        var layer = new TextLayer();
        Assert.Equal(string.Empty, layer.Text);
        Assert.Equal("Arial", layer.FontFamily);
        Assert.Equal(24f, layer.FontSize);
        Assert.Equal("#FFFFFF", layer.FontColor);
        Assert.False(layer.IsBold);
        Assert.False(layer.IsItalic);
        Assert.Equal(CardTextAlignment.Left, layer.TextAlignment);
        Assert.Equal(1.2f, layer.LineSpacing);
        Assert.Null(layer.StrokeColor);
        Assert.Equal(0f, layer.StrokeWidth);
    }
}
