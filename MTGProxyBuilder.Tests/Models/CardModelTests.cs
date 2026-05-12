using MTGProxyBuilder.Core.Models;

namespace MTGProxyBuilder.Tests.Models;

public class CardModelTests
{
    [Fact]
    public void NewCard_HasUniqueId()
    {
        var card1 = new CardModel();
        var card2 = new CardModel();
        Assert.NotEqual(card1.CardId, card2.CardId);
    }

    [Fact]
    public void NewCard_HasDefaultValues()
    {
        var card = new CardModel();
        Assert.Equal(string.Empty, card.Name);
        Assert.Equal(string.Empty, card.ArtworkPath);
        Assert.Null(card.BackArtworkPath);
        Assert.Null(card.ScryfallId);
        Assert.Equal(1, card.Quantity);
        Assert.False(card.IncludeBack);
        Assert.Equal(string.Empty, card.ManaCost);
        Assert.Equal(0f, card.CMC);
    }

    [Theory]
    [InlineData("Creature — Human Wizard", "Creature")]
    [InlineData("Legendary Creature — Dragon", "Creature")]
    [InlineData("Instant", "Instant")]
    [InlineData("Sorcery", "Sorcery")]
    [InlineData("Artifact — Equipment", "Artifact")]
    [InlineData("Enchantment — Aura", "Enchantment")]
    [InlineData("Land", "Land")]
    [InlineData("Legendary Planeswalker — Jace", "Planeswalker")]
    [InlineData("", "")]
    public void PrimaryType_ExtractsCorrectly(string typeLine, string expected)
    {
        var card = new CardModel { TypeLine = typeLine };
        Assert.Equal(expected, card.PrimaryType);
    }

    [Fact]
    public void PropertyChanged_FiresOnNameChange()
    {
        var card = new CardModel();
        string? changedProp = null;
        card.PropertyChanged += (_, e) => changedProp = e.PropertyName;

        card.Name = "Lightning Bolt";
        Assert.Equal("Name", changedProp);
    }

    [Fact]
    public void PropertyChanged_FiresOnQuantityChange()
    {
        var card = new CardModel();
        string? changedProp = null;
        card.PropertyChanged += (_, e) => changedProp = e.PropertyName;

        card.Quantity = 4;
        Assert.Equal("Quantity", changedProp);
    }

    [Fact]
    public void DateAdded_DefaultsToNow()
    {
        var before = DateTime.Now.AddSeconds(-1);
        var card = new CardModel();
        var after = DateTime.Now.AddSeconds(1);

        Assert.InRange(card.DateAdded, before, after);
    }

    [Fact]
    public void OriginalBackArtworkPath_InitiallyNull()
    {
        var card = new CardModel();
        Assert.Null(card.OriginalBackArtworkPath);
    }

    [Fact]
    public void OriginalBackArtworkPath_CanBeSet()
    {
        var card = new CardModel { OriginalBackArtworkPath = "/original/back.jpg" };
        Assert.Equal("/original/back.jpg", card.OriginalBackArtworkPath);
    }

    [Fact]
    public void OriginalBackArtworkPath_IndependentOfBackArtworkPath()
    {
        var card = new CardModel
        {
            BackArtworkPath = "/current/back.jpg",
            OriginalBackArtworkPath = "/original/back.jpg"
        };
        card.BackArtworkPath = "/new/back.jpg";
        Assert.Equal("/new/back.jpg", card.BackArtworkPath);
        Assert.Equal("/original/back.jpg", card.OriginalBackArtworkPath);
    }

    [Fact]
    public void Source_DefaultsToEmpty()
    {
        var card = new CardModel();
        Assert.Equal(string.Empty, card.SetCode);
        Assert.Equal(string.Empty, card.SetName);
        Assert.Equal(string.Empty, card.Artist);
        Assert.Equal(string.Empty, card.Keywords);
        Assert.Equal(string.Empty, card.Power);
        Assert.Equal(string.Empty, card.Toughness);
    }

    [Fact]
    public void AllMetadata_PropertyChangedFires()
    {
        var card = new CardModel();
        var changed = new List<string>();
        card.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);

        card.ManaCost = "{2}{U}";
        card.CMC = 3;
        card.TypeLine = "Instant";
        card.Rarity = "rare";
        card.Colors = "U";
        card.SetCode = "m21";
        card.Artist = "John Avon";

        Assert.Contains("ManaCost", changed);
        Assert.Contains("CMC", changed);
        Assert.Contains("TypeLine", changed);
        Assert.Contains("Rarity", changed);
        Assert.Contains("Colors", changed);
        Assert.Contains("SetCode", changed);
        Assert.Contains("Artist", changed);
    }

    // --- Overlay Text ---

    [Fact]
    public void OverlayText_DefaultsToEmpty()
    {
        var card = new CardModel();
        Assert.Equal(string.Empty, card.OverlayText);
    }

    [Fact]
    public void OverlayText_CanBeSet()
    {
        var card = new CardModel { OverlayText = "TOKEN" };
        Assert.Equal("TOKEN", card.OverlayText);
    }

    [Fact]
    public void OverlayText_PropertyChangedFires()
    {
        var card = new CardModel();
        string? changed = null;
        card.PropertyChanged += (_, e) => changed = e.PropertyName;
        card.OverlayText = "PROXY";
        Assert.Equal("OverlayText", changed);
    }
}
