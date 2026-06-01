using MTGProxyBuilder.Core.Models;
using MTGProxyBuilder.Core.Services;
using Newtonsoft.Json;

namespace MTGProxyBuilder.Tests.Services;

public class YgoProDeckServiceTests
{
    [Fact]
    public void GetImageUrl_ReturnsRequestedPrinting()
    {
        var card = new YgoProDeckCard
        {
            CardImages = new List<YgoProDeckCardImage>
            {
                new() { Id = 1, ImageUrl = "https://img/1.jpg" },
                new() { Id = 2, ImageUrl = "https://img/2.jpg" }
            }
        };
        Assert.Equal("https://img/1.jpg", card.GetImageUrl());
        Assert.Equal("https://img/2.jpg", card.GetImageUrl(1));
    }

    [Fact]
    public void GetImageUrl_OutOfRange_ReturnsNull()
    {
        var card = new YgoProDeckCard
        {
            CardImages = new List<YgoProDeckCardImage> { new() { ImageUrl = "https://img/1.jpg" } }
        };
        Assert.Null(card.GetImageUrl(5));
        Assert.Null(card.GetImageUrl(-1));
    }

    [Fact]
    public void GetImageUrl_NoImages_ReturnsNull()
        => Assert.Null(new YgoProDeckCard().GetImageUrl());

    [Fact]
    public void SmallImageUrl_UsesFirstArtworkThumbnail()
    {
        var card = new YgoProDeckCard
        {
            CardImages = new List<YgoProDeckCardImage>
            {
                new() { ImageUrlSmall = "https://img/small1.jpg" }
            }
        };
        Assert.Equal("https://img/small1.jpg", card.SmallImageUrl);
    }

    [Fact]
    public void ToCardModel_MapsCoreFieldsAndSource()
    {
        var card = new YgoProDeckCard
        {
            Id = 46986414,
            Name = "Dark Magician",
            Type = "Normal Monster",
            Desc = "The ultimate wizard in terms of attack and defense.",
            Race = "Spellcaster",
            Attribute = "DARK",
            Archetype = "Dark Magician",
            Atk = 2500,
            Def = 2100,
            Level = 7,
            CardImages = new List<YgoProDeckCardImage>
            {
                new() { Id = 46986414, ImageUrl = "https://img/dm.jpg", ImageUrlSmall = "https://img/dm_s.jpg" }
            }
        };

        var model = card.ToCardModel("/cache/ygo_46986414.jpg");

        Assert.Equal("Dark Magician", model.Name);
        Assert.Equal("/cache/ygo_46986414.jpg", model.ArtworkPath);
        Assert.Equal(CardSource.YgoProDeck, model.Source);
        Assert.False(model.IncludeBack);
        Assert.Equal("https://img/dm.jpg", model.FullResFrontUrl);
        Assert.Equal("The ultimate wizard in terms of attack and defense.", model.OracleText);
        Assert.Equal("2500", model.Power);
        Assert.Equal("2100", model.Toughness);
        Assert.Equal("Dark Magician", model.Keywords);
    }

    [Fact]
    public void ToCardModel_BuildsReadableTypeLine()
    {
        var monster = new YgoProDeckCard
        { Type = "Effect Monster", Attribute = "LIGHT", Race = "Warrior" };
        Assert.Equal("Effect Monster · LIGHT · Warrior", monster.ToCardModel("/a.jpg").TypeLine);

        // Spells/traps have no attribute -> it is omitted, no dangling separator.
        var spell = new YgoProDeckCard { Type = "Spell Card", Race = "Continuous" };
        Assert.Equal("Spell Card · Continuous", spell.ToCardModel("/a.jpg").TypeLine);
    }

    [Fact]
    public void ToCardModel_NonMonster_LeavesAtkDefEmpty()
    {
        var spell = new YgoProDeckCard { Name = "Pot of Greed", Type = "Spell Card", Atk = null, Def = null };
        var model = spell.ToCardModel("/a.jpg");
        Assert.Equal(string.Empty, model.Power);
        Assert.Equal(string.Empty, model.Toughness);
    }

    [Fact]
    public void Deserialize_RealisticResponse_ParsesDataAndImages()
    {
        // Trimmed shape of an actual db.ygoprodeck.com/api/v7/cardinfo.php response.
        const string json = """
        {
          "data": [
            {
              "id": 46986414,
              "name": "Dark Magician",
              "type": "Normal Monster",
              "desc": "The ultimate wizard.",
              "atk": 2500,
              "def": 2100,
              "level": 7,
              "race": "Spellcaster",
              "attribute": "DARK",
              "card_images": [
                {
                  "id": 46986414,
                  "image_url": "https://images.ygoprodeck.com/images/cards/46986414.jpg",
                  "image_url_small": "https://images.ygoprodeck.com/images/cards_small/46986414.jpg"
                },
                {
                  "id": 36996508,
                  "image_url": "https://images.ygoprodeck.com/images/cards/36996508.jpg",
                  "image_url_small": "https://images.ygoprodeck.com/images/cards_small/36996508.jpg"
                }
              ]
            }
          ]
        }
        """;

        var result = JsonConvert.DeserializeObject<YgoProDeckSearchResult>(json);

        Assert.NotNull(result);
        Assert.Null(result!.Error);
        var card = Assert.Single(result.Data!);
        Assert.Equal("Dark Magician", card.Name);
        Assert.Equal(2500, card.Atk);
        Assert.Equal(2, card.CardImages!.Count);
        Assert.Equal("https://images.ygoprodeck.com/images/cards/46986414.jpg", card.GetImageUrl());
        Assert.Equal("https://images.ygoprodeck.com/images/cards/36996508.jpg", card.GetImageUrl(1));
    }

    [Fact]
    public void Deserialize_ErrorResponse_PopulatesError()
    {
        const string json = """{ "error": "No card matching your query was found in the database." }""";
        var result = JsonConvert.DeserializeObject<YgoProDeckSearchResult>(json);
        Assert.NotNull(result);
        Assert.Null(result!.Data);
        Assert.Contains("No card matching", result.Error);
    }

    [Fact]
    public void ToString_ContainsNameAndType()
    {
        var card = new YgoProDeckCard { Name = "Blue-Eyes White Dragon", Type = "Normal Monster" };
        Assert.Contains("Blue-Eyes White Dragon", card.ToString());
        Assert.Contains("Normal Monster", card.ToString());
    }
}
