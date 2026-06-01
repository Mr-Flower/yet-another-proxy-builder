using MTGProxyBuilder.Core.Models;

namespace MTGProxyBuilder.Tests.Models;

public class CardSizePresetTests
{
    [Fact]
    public void BuiltInPresets_IsNotEmpty()
    {
        Assert.NotEmpty(CardSizePreset.BuiltInPresets);
    }

    [Fact]
    public void BuiltInPresets_ContainsMagicPokerSize()
    {
        var mtg = CardSizePreset.BuiltInPresets.FirstOrDefault(p => p.WidthMm == 63f && p.HeightMm == 88f);
        Assert.NotNull(mtg);
    }

    [Fact]
    public void BuiltInPresets_ContainsYuGiOhSize()
    {
        // The Add-Cards game selector relies on a 59 x 86 (Yu-Gi-Oh!/Japanese) preset existing.
        var ygo = CardSizePreset.BuiltInPresets.FirstOrDefault(p => p.WidthMm == 59f && p.HeightMm == 86f);
        Assert.NotNull(ygo);
    }

    [Fact]
    public void BuiltInPresets_DimensionsAreUnique()
    {
        // FindCardSizePreset matches by dimensions, so no two presets may share a size.
        var sizes = CardSizePreset.BuiltInPresets.Select(p => (p.WidthMm, p.HeightMm)).ToList();
        Assert.Equal(sizes.Count, sizes.Distinct().Count());
    }

    [Fact]
    public void BuiltInPresets_AllHavePositiveDimensions()
    {
        foreach (var preset in CardSizePreset.BuiltInPresets)
        {
            Assert.True(preset.WidthMm > 0, $"{preset.Name} has zero/negative width");
            Assert.True(preset.HeightMm > 0, $"{preset.Name} has zero/negative height");
        }
    }

    [Fact]
    public void BuiltInPresets_AllHaveNames()
    {
        foreach (var preset in CardSizePreset.BuiltInPresets)
        {
            Assert.False(string.IsNullOrWhiteSpace(preset.Name), "Preset has empty name");
        }
    }

    [Fact]
    public void ToString_ContainsNameAndDimensions()
    {
        var preset = new CardSizePreset("Test Game", 50f, 70f);
        var str = preset.ToString();
        Assert.Contains("Test Game", str);
        Assert.Contains("50", str);
        Assert.Contains("70", str);
    }
}
