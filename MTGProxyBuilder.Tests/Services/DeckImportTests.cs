using MTGProxyBuilder.Core.Services;

namespace MTGProxyBuilder.Tests.Services;

public class DeckImportTests
{
    // --- DeckImportService.DetectSource ---

    [Theory]
    [InlineData("https://www.moxfield.com/decks/abc123", DeckSource.Moxfield)]
    [InlineData("https://moxfield.com/decks/abc123", DeckSource.Moxfield)]
    [InlineData("http://moxfield.com/decks/xyz", DeckSource.Moxfield)]
    public void DetectSource_Moxfield(string url, DeckSource expected)
    {
        Assert.Equal(expected, DeckImportService.DetectSource(url));
    }

    [Theory]
    [InlineData("https://archidekt.com/decks/12345/my-deck", DeckSource.Archidekt)]
    [InlineData("https://www.archidekt.com/decks/99999", DeckSource.Archidekt)]
    [InlineData("http://archidekt.com/decks/1", DeckSource.Archidekt)]
    public void DetectSource_Archidekt(string url, DeckSource expected)
    {
        Assert.Equal(expected, DeckImportService.DetectSource(url));
    }

    [Theory]
    [InlineData("https://piltoverarchive.com/decks/view/0741d662-e31b-4999-b1f8-96d89d085423", DeckSource.PiltoverArchive)]
    [InlineData("https://www.piltoverarchive.com/decks/view/abc-123", DeckSource.PiltoverArchive)]
    public void DetectSource_PiltoverArchive(string url, DeckSource expected)
    {
        Assert.Equal(expected, DeckImportService.DetectSource(url));
    }

    [Theory]
    [InlineData("https://google.com")]
    [InlineData("https://scryfall.com/card/m21/1/foo")]
    [InlineData("just some text")]
    [InlineData("")]
    public void DetectSource_Unknown(string url)
    {
        Assert.Equal(DeckSource.Unknown, DeckImportService.DetectSource(url));
    }

    [Fact]
    public void DetectSource_NullOrWhitespace_ReturnsUnknown()
    {
        Assert.Equal(DeckSource.Unknown, DeckImportService.DetectSource(""));
        Assert.Equal(DeckSource.Unknown, DeckImportService.DetectSource("   "));
    }

    // --- DeckImportService.ParseTextList ---

    [Fact]
    public void ParseTextList_BasicQuantitiesAndNames()
    {
        var deck = DeckImportService.ParseTextList("2 Sol Ring\n1 Counterspell\nLightning Bolt");
        Assert.Equal(3, deck.Entries.Count);
        Assert.Equal("Sol Ring", deck.Entries[0].CardName);
        Assert.Equal(2, deck.Entries[0].Quantity);
        Assert.Equal("Counterspell", deck.Entries[1].CardName);
        Assert.Equal(1, deck.Entries[1].Quantity);
        Assert.Equal("Lightning Bolt", deck.Entries[2].CardName);
        Assert.Equal(1, deck.Entries[2].Quantity);
    }

    [Theory]
    [InlineData("3x Forest")]
    [InlineData("3X Forest")]
    [InlineData("3 Forest")]
    public void ParseTextList_AcceptsXQuantityForms(string line)
    {
        var deck = DeckImportService.ParseTextList(line);
        Assert.Single(deck.Entries);
        Assert.Equal("Forest", deck.Entries[0].CardName);
        Assert.Equal(3, deck.Entries[0].Quantity);
    }

    [Fact]
    public void ParseTextList_StripsSetAndCollectorHint()
    {
        var deck = DeckImportService.ParseTextList("1 Sol Ring (C21) 263");
        Assert.Single(deck.Entries);
        Assert.Equal("Sol Ring", deck.Entries[0].CardName);
    }

    [Fact]
    public void ParseTextList_IgnoresHeadersBlanksAndComments()
    {
        var deck = DeckImportService.ParseTextList("Deck\n\n# comment\n// note\n2 Island\nSideboard:\n1 Negate");
        Assert.Equal(2, deck.Entries.Count);
        Assert.Equal("Island", deck.Entries[0].CardName);
        Assert.Equal("Negate", deck.Entries[1].CardName);
    }

    [Fact]
    public void ParseTextList_SumsDuplicateNames()
    {
        var deck = DeckImportService.ParseTextList("2 Forest\n3 Forest");
        Assert.Single(deck.Entries);
        Assert.Equal(5, deck.Entries[0].Quantity);
    }

    [Fact]
    public void ParseTextList_EmptyReturnsNoEntries()
    {
        Assert.Empty(DeckImportService.ParseTextList("").Entries);
        Assert.Empty(DeckImportService.ParseTextList("   \n  \n").Entries);
    }

    // --- MoxfieldService.ParseDeckId ---

    [Theory]
    [InlineData("https://www.moxfield.com/decks/oEWXWHM5eEGMmopExLWRCA", "oEWXWHM5eEGMmopExLWRCA")]
    [InlineData("https://moxfield.com/decks/abc123", "abc123")]
    [InlineData("https://moxfield.com/decks/abc123/primer", "abc123")]
    public void MoxfieldParseDeckId_ValidUrls(string url, string expectedId)
    {
        Assert.Equal(expectedId, MoxfieldService.ParseDeckId(url));
    }

    [Fact]
    public void MoxfieldParseDeckId_DirectId()
    {
        Assert.Equal("oEWXWHM5eEGMmopExLWRCA", MoxfieldService.ParseDeckId("oEWXWHM5eEGMmopExLWRCA"));
    }

    [Theory]
    [InlineData("https://moxfield.com/")]
    [InlineData("https://moxfield.com/decks")]
    [InlineData("https://moxfield.com/decks/")]
    public void MoxfieldParseDeckId_InvalidUrls_ReturnsNull(string url)
    {
        var result = MoxfieldService.ParseDeckId(url);
        // Either null or empty string for edge cases
        Assert.True(result == null || result == "", $"Expected null/empty but got '{result}'");
    }

    // --- ArchidektService.ParseDeckId ---

    [Theory]
    [InlineData("https://archidekt.com/decks/12345/my-deck-name", "12345")]
    [InlineData("https://archidekt.com/decks/99999", "99999")]
    [InlineData("https://www.archidekt.com/decks/1/test", "1")]
    public void ArchidektParseDeckId_ValidUrls(string url, string expectedId)
    {
        Assert.Equal(expectedId, ArchidektService.ParseDeckId(url));
    }

    [Fact]
    public void ArchidektParseDeckId_DirectNumericId()
    {
        Assert.Equal("12345", ArchidektService.ParseDeckId("12345"));
    }

    [Theory]
    [InlineData("https://archidekt.com/")]
    [InlineData("https://archidekt.com/decks")]
    [InlineData("not a url")]
    public void ArchidektParseDeckId_InvalidUrls_ReturnsNull(string url)
    {
        Assert.Null(ArchidektService.ParseDeckId(url));
    }

    // --- Case sensitivity ---

    [Fact]
    public void DetectSource_CaseInsensitive()
    {
        Assert.Equal(DeckSource.Moxfield, DeckImportService.DetectSource("https://MOXFIELD.COM/decks/abc"));
        Assert.Equal(DeckSource.Archidekt, DeckImportService.DetectSource("https://ARCHIDEKT.COM/decks/123"));
    }

    // --- URL with extra path segments ---

    [Fact]
    public void MoxfieldParseDeckId_WithQueryString()
    {
        var id = MoxfieldService.ParseDeckId("https://moxfield.com/decks/abc123?tab=mainboard");
        Assert.Equal("abc123", id);
    }

    [Fact]
    public void ArchidektParseDeckId_WithExtraSegments()
    {
        var id = ArchidektService.ParseDeckId("https://archidekt.com/decks/12345/my-deck/edit");
        Assert.Equal("12345", id);
    }

    // --- Whitespace handling ---

    [Fact]
    public void MoxfieldParseDeckId_TrimsWhitespace()
    {
        var id = MoxfieldService.ParseDeckId("  https://moxfield.com/decks/abc123  ");
        Assert.Equal("abc123", id);
    }

    [Fact]
    public void ArchidektParseDeckId_TrimsWhitespace()
    {
        var id = ArchidektService.ParseDeckId("  12345  ");
        Assert.Equal("12345", id);
    }
}
