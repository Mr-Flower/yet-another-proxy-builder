using MTGProxyBuilder.Core.Services;

namespace MTGProxyBuilder.Tests.Services;

public class ScryfallServiceTests
{
    [Fact]
    public void ScryfallCard_GetImageUrl_FromImageUris()
    {
        var card = new ScryfallCard
        {
            ImageUris = new Dictionary<string, string>
            {
                ["small"] = "https://example.com/small.jpg",
                ["normal"] = "https://example.com/normal.jpg",
                ["large"] = "https://example.com/large.jpg"
            }
        };
        Assert.Equal("https://example.com/large.jpg", card.GetImageUrl("large"));
        Assert.Equal("https://example.com/small.jpg", card.GetImageUrl("small"));
    }

    [Fact]
    public void ScryfallCard_GetImageUrl_FallsBackToCardFaces()
    {
        var card = new ScryfallCard
        {
            ImageUris = null,
            CardFaces = new List<CardFace>
            {
                new() { ImageUris = new Dictionary<string, string> { ["large"] = "https://example.com/front.jpg" } },
                new() { ImageUris = new Dictionary<string, string> { ["large"] = "https://example.com/back.jpg" } }
            }
        };
        Assert.Equal("https://example.com/front.jpg", card.GetImageUrl("large"));
    }

    [Fact]
    public void ScryfallCard_GetImageUrl_ReturnsNull_WhenNoImages()
    {
        var card = new ScryfallCard();
        Assert.Null(card.GetImageUrl());
    }

    [Fact]
    public void ScryfallCard_GetBackImageUrl_FromCardFaces()
    {
        var card = new ScryfallCard
        {
            CardFaces = new List<CardFace>
            {
                new() { ImageUris = new Dictionary<string, string> { ["large"] = "https://front.jpg" } },
                new() { ImageUris = new Dictionary<string, string> { ["large"] = "https://back.jpg" } }
            }
        };
        Assert.Equal("https://back.jpg", card.GetBackImageUrl("large"));
    }

    [Fact]
    public void ScryfallCard_GetBackImageUrl_ReturnsNull_SingleFaced()
    {
        var card = new ScryfallCard
        {
            ImageUris = new Dictionary<string, string> { ["large"] = "https://front.jpg" }
        };
        Assert.Null(card.GetBackImageUrl());
    }

    [Fact]
    public void ScryfallCard_ToString_ContainsNameAndSet()
    {
        var card = new ScryfallCard { Name = "Lightning Bolt", SetName = "Alpha", CollectorNumber = "161" };
        var str = card.ToString();
        Assert.Contains("Lightning Bolt", str);
        Assert.Contains("Alpha", str);
        Assert.Contains("161", str);
    }

    [Fact]
    public void ToCardModel_MapsBasicProperties()
    {
        var card = new ScryfallCard
        {
            Id = "abc123",
            Name = "Lightning Bolt",
            ManaCost = "{R}",
            CMC = 1,
            TypeLine = "Instant",
            OracleText = "Deal 3 damage.",
            Rarity = "common",
            Colors = new List<string> { "R" },
            ColorIdentity = new List<string> { "R" },
            Keywords = new List<string> { "Damage" },
            SetCode = "lea",
            SetName = "Limited Edition Alpha",
            CollectorNumber = "161",
            Artist = "Christopher Rush",
            Power = null,
            Toughness = null,
            Loyalty = null
        };

        var model = card.ToCardModel("/path/front.jpg", null);

        Assert.Equal("Lightning Bolt", model.Name);
        Assert.Equal("abc123", model.ScryfallId);
        Assert.Equal("/path/front.jpg", model.ArtworkPath);
        Assert.Null(model.BackArtworkPath);
        Assert.False(model.IncludeBack);
        Assert.Equal("{R}", model.ManaCost);
        Assert.Equal(1f, model.CMC);
        Assert.Equal("Instant", model.TypeLine);
        Assert.Equal("Deal 3 damage.", model.OracleText);
        Assert.Equal("common", model.Rarity);
        Assert.Equal("R", model.Colors);
        Assert.Equal("R", model.ColorIdentity);
        Assert.Equal("Damage", model.Keywords);
        Assert.Equal("lea", model.SetCode);
        Assert.Equal("Limited Edition Alpha", model.SetName);
        Assert.Equal("161", model.CollectorNumber);
        Assert.Equal("Christopher Rush", model.Artist);
    }

    [Fact]
    public void ToCardModel_WithBackArt_SetsIncludeBack()
    {
        var card = new ScryfallCard { Name = "Delver of Secrets" };
        var model = card.ToCardModel("/front.jpg", "/back.jpg");

        Assert.Equal("/back.jpg", model.BackArtworkPath);
        Assert.Equal("/back.jpg", model.OriginalBackArtworkPath);
        Assert.True(model.IncludeBack);
    }

    [Fact]
    public void ToCardModel_NullColors_DefaultsToEmpty()
    {
        var card = new ScryfallCard
        {
            Name = "Sol Ring",
            Colors = null,
            ColorIdentity = null,
            Keywords = null
        };
        var model = card.ToCardModel("/art.jpg", null);

        Assert.Equal(string.Empty, model.Colors);
        Assert.Equal(string.Empty, model.ColorIdentity);
        Assert.Equal(string.Empty, model.Keywords);
    }

    [Fact]
    public void ToCardModel_MultipleColors_JoinedWithComma()
    {
        var card = new ScryfallCard
        {
            Name = "Atraxa",
            Colors = new List<string> { "W", "U", "B", "G" }
        };
        var model = card.ToCardModel("/art.jpg", null);
        Assert.Equal("W,U,B,G", model.Colors);
    }

    [Fact]
    public void ToCardModel_DoubleFacedCard_UsesFirstFaceData()
    {
        var card = new ScryfallCard
        {
            Name = "Delver of Secrets // Insectile Aberration",
            ManaCost = null,
            TypeLine = null,
            OracleText = null,
            Power = null,
            Toughness = null,
            CardFaces = new List<CardFace>
            {
                new()
                {
                    Name = "Delver of Secrets",
                    ManaCost = "{U}",
                    TypeLine = "Creature — Human Wizard",
                    OracleText = "Transform this.",
                    Power = "1",
                    Toughness = "1"
                },
                new()
                {
                    Name = "Insectile Aberration",
                    ManaCost = "",
                    TypeLine = "Creature — Human Insect",
                    OracleText = "Flying",
                    Power = "3",
                    Toughness = "2"
                }
            }
        };

        var model = card.ToCardModel("/front.jpg", "/back.jpg");
        Assert.Equal("{U}", model.ManaCost);
        Assert.Equal("Creature — Human Wizard", model.TypeLine);
        Assert.Equal("Transform this.", model.OracleText);
        Assert.Equal("1", model.Power);
        Assert.Equal("1", model.Toughness);
    }

    [Fact]
    public void ToCardModel_DateAdded_IsSetToNow()
    {
        var before = DateTime.Now.AddSeconds(-1);
        var card = new ScryfallCard { Name = "Test" };
        var model = card.ToCardModel("/art.jpg", null);
        var after = DateTime.Now.AddSeconds(1);

        Assert.InRange(model.DateAdded, before, after);
    }
}
