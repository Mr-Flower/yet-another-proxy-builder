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
    public void BuiltInPresets_ContainsMtg()
    {
        var mtg = CardSizePreset.BuiltInPresets.FirstOrDefault(p => p.Name == "Magic: The Gathering");
        Assert.NotNull(mtg);
        Assert.Equal(63f, mtg.WidthMm);
        Assert.Equal(88f, mtg.HeightMm);
    }

    [Fact]
    public void BuiltInPresets_OnlyMtg()
    {
        // This is an MTG-only proxy builder; other-game presets were removed.
        Assert.Single(CardSizePreset.BuiltInPresets);
        Assert.Equal("Magic: The Gathering", CardSizePreset.BuiltInPresets[0].Name);
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
