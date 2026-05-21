using MTGProxyBuilder.Core.Models;
using MTGProxyBuilder.Core.Services;

namespace MTGProxyBuilder.Tests.Services;

public class LibrarySearchParserTests
{
    private static BackArtEntry Entry(string name, string source = "Local", DateTime? added = null,
        string type = "", string oracle = "", string rarity = "", string colors = "",
        string setCode = "", string setName = "", string artist = "",
        string power = "", string toughness = "", float cmc = 0, string keywords = "",
        string manaCost = "") => new()
    {
        Id = Guid.NewGuid().ToString("N")[..12],
        Name = name,
        Source = source,
        FilePath = "C:\\fake.png",
        AddedDate = added ?? new DateTime(2025, 6, 15),
        TypeLine = type,
        OracleText = oracle,
        Rarity = rarity,
        Colors = colors,
        SetCode = setCode,
        SetName = setName,
        Artist = artist,
        Power = power,
        Toughness = toughness,
        CMC = cmc,
        Keywords = keywords,
        ManaCost = manaCost
    };

    private static readonly BackArtEntry Bolt = Entry("Lightning Bolt [Chilli_Axe]", "Chilli_Axe",
        type: "Instant", oracle: "Lightning Bolt deals 3 damage to any target.", rarity: "common",
        colors: "R", setCode: "2ED", setName: "Unlimited Edition", artist: "Christopher Rush",
        cmc: 1, manaCost: "{R}", keywords: "");
    private static readonly BackArtEntry Counter = Entry("Counterspell [MrTeferi]", "MrTeferi",
        type: "Instant", oracle: "Counter target spell.", rarity: "uncommon",
        colors: "U", setCode: "ICE", setName: "Ice Age", artist: "Mark Poole",
        cmc: 2, manaCost: "{U}{U}", keywords: "");
    private static readonly BackArtEntry Lotus = Entry("Black Lotus [Chilli_Axe]", "Chilli_Axe",
        type: "Artifact", oracle: "{T}, Sacrifice Black Lotus: Add three mana of any one color.",
        rarity: "rare", colors: "", setCode: "LEA", setName: "Limited Edition Alpha",
        artist: "Christopher Rush", cmc: 0, manaCost: "{0}", keywords: "");
    private static readonly BackArtEntry Goyf = Entry("Tarmogoyf [Chilli_Axe]", "Chilli_Axe",
        type: "Creature — Lhurgoyf", oracle: "Tarmogoyf's power is equal to the number of card types among cards in all graveyards.",
        rarity: "mythic", colors: "G", setCode: "FUT", setName: "Future Sight",
        artist: "Ryan Barger", cmc: 2, manaCost: "{1}{G}", power: "*", toughness: "1+*",
        keywords: "");
    private static readonly BackArtEntry Bird = Entry("Birds of Paradise [Local]", "Local",
        type: "Creature — Bird", oracle: "Flying\n{T}: Add one mana of any color.", rarity: "rare",
        colors: "G", setCode: "10E", setName: "Tenth Edition", artist: "Marcelo Vignali",
        cmc: 1, manaCost: "{G}", power: "0", toughness: "1", keywords: "Flying");
    private static readonly BackArtEntry Old = Entry("Old Card", "Local", new DateTime(2024, 1, 1));
    private static readonly BackArtEntry New = Entry("New Card", "Local", new DateTime(2026, 3, 15));

    private static readonly BackArtEntry[] All = { Bolt, Counter, Lotus, Goyf, Bird, Old, New };

    private List<BackArtEntry> Search(string query) =>
        All.Where(LibrarySearchParser.Parse(query)).ToList();

    // ================================================================
    //  BARE TEXT (name search)
    // ================================================================

    [Fact]
    public void BareText_MatchesName()
    {
        var results = Search("Lightning");
        Assert.Single(results);
        Assert.Equal(Bolt, results[0]);
    }

    [Fact]
    public void BareText_CaseInsensitive()
    {
        Assert.Single(Search("lightning bolt"));
    }

    [Fact]
    public void EmptyQuery_ReturnsAll()
    {
        Assert.Equal(All.Length, Search("").Count);
        Assert.Equal(All.Length, Search("   ").Count);
    }

    [Fact]
    public void MultipleWords_AndLogic()
    {
        // Both words must match in name
        var results = Search("Black Lotus");
        Assert.Single(results);
        Assert.Equal(Lotus, results[0]);
    }

    // ================================================================
    //  QUOTED PHRASE
    // ================================================================

    [Fact]
    public void QuotedPhrase_MatchesExactPhrase()
    {
        var results = Search("\"Black Lotus\"");
        Assert.Single(results);
        Assert.Equal(Lotus, results[0]);
    }

    // ================================================================
    //  EXACT NAME (!)
    // ================================================================

    [Fact]
    public void ExactName_MatchesExactly()
    {
        // "!Old Card" should match only the entry named exactly "Old Card"
        var results = Search("!\"Old Card\"");
        Assert.Single(results);
        Assert.Equal(Old, results[0]);
    }

    [Fact]
    public void ExactName_DoesNotPartialMatch()
    {
        // "!Old" should not match "Old Card" because it's not an exact match
        var results = Search("!Old");
        Assert.Empty(results);
    }

    [Fact]
    public void ExactName_CaseInsensitive()
    {
        var results = Search("!\"old card\"");
        Assert.Single(results);
    }

    // ================================================================
    //  FIELD PREFIXES
    // ================================================================

    [Fact]
    public void NamePrefix_FiltersName()
    {
        Assert.Single(Search("name:Counterspell"));
    }

    [Fact]
    public void NPrefix_AliasForName()
    {
        Assert.Single(Search("n:Bolt"));
    }

    [Fact]
    public void SourcePrefix_FiltersSource()
    {
        var results = Search("source:Chilli_Axe");
        Assert.Contains(Bolt, results);
        Assert.Contains(Lotus, results);
        Assert.Contains(Goyf, results);
    }

    [Fact]
    public void SrcPrefix_AliasForSource()
    {
        Assert.Single(Search("src:MrTeferi"));
    }

    [Fact]
    public void SPrefix_MatchesSet()
    {
        // s: now maps to set, not source
        Assert.Single(Search("s:ICE"));
        Assert.Equal(Counter, Search("s:ICE")[0]);
    }

    // ================================================================
    //  DATE COMPARISONS
    // ================================================================

    [Fact]
    public void DateGreaterThan_FiltersAfter()
    {
        var results = Search("date>2026-01-01");
        Assert.Single(results);
        Assert.Equal(New, results[0]);
    }

    [Fact]
    public void DateLessThan_FiltersBefore()
    {
        var results = Search("date<2025-01-01");
        Assert.Single(results);
        Assert.Equal(Old, results[0]);
    }

    [Fact]
    public void DateEquals_FiltersExactDate()
    {
        Assert.Single(Search("date:2024-01-01"));
        Assert.Single(Search("date=2024-01-01"));
    }

    [Fact]
    public void DateNotEquals_ExcludesDate()
    {
        var results = Search("date!=2024-01-01");
        Assert.Equal(All.Length - 1, results.Count);
        Assert.DoesNotContain(Old, results);
    }

    [Fact]
    public void InvalidDate_DoesNotFilter()
    {
        Assert.Equal(All.Length, Search("date>notadate").Count);
    }

    // ================================================================
    //  NEGATION (-)
    // ================================================================

    [Fact]
    public void Negation_ExcludesMatches()
    {
        var results = Search("-Lotus");
        Assert.Equal(All.Length - 1, results.Count);
        Assert.DoesNotContain(Lotus, results);
    }

    [Fact]
    public void Negation_WithPrefix()
    {
        var results = Search("-source:Chilli_Axe");
        Assert.DoesNotContain(Bolt, results);
        Assert.DoesNotContain(Lotus, results);
        Assert.DoesNotContain(Goyf, results);
        Assert.Contains(Counter, results);
        Assert.Contains(Old, results);
    }

    [Fact]
    public void Negation_WithQuotedPhrase()
    {
        var results = Search("-\"Black Lotus\"");
        Assert.Equal(All.Length - 1, results.Count);
        Assert.DoesNotContain(Lotus, results);
    }

    // ================================================================
    //  OR
    // ================================================================

    [Fact]
    public void OrOperator_EitherMatches()
    {
        var results = Search("Lightning OR Counterspell");
        Assert.Equal(2, results.Count);
    }

    [Fact]
    public void OrOperator_CaseInsensitive()
    {
        var results = Search("Lightning or Counterspell");
        Assert.Equal(2, results.Count);
    }

    // ================================================================
    //  PARENTHESES
    // ================================================================

    [Fact]
    public void Parentheses_GroupOrWithAnd()
    {
        // Source is Chilli_Axe AND (name has Bolt OR name has Lotus)
        var results = Search("source:Chilli_Axe (Bolt or Lotus)");
        Assert.Equal(2, results.Count);
    }

    [Fact]
    public void Parentheses_NegatedGroup()
    {
        var results = Search("-(Bolt or Counterspell)");
        Assert.Equal(All.Length - 2, results.Count);
        Assert.DoesNotContain(Bolt, results);
        Assert.DoesNotContain(Counter, results);
    }

    // ================================================================
    //  REGEX
    // ================================================================

    [Fact]
    public void Regex_NamePattern()
    {
        var results = Search("name:/^Lightning/");
        Assert.Single(results);
        Assert.Equal(Bolt, results[0]);
    }

    [Fact]
    public void Regex_SourcePattern()
    {
        var results = Search("source:/^Mr/");
        Assert.Single(results);
        Assert.Equal(Counter, results[0]);
    }

    [Fact]
    public void Regex_Invalid_DoesNotFilter()
    {
        Assert.Equal(All.Length, Search("name:/[invalid/").Count);
    }

    // ================================================================
    //  COMBINED QUERIES
    // ================================================================

    [Fact]
    public void Combined_SourceAndNegation()
    {
        var results = Search("source:Chilli_Axe -Bolt");
        Assert.Contains(Lotus, results);
        Assert.Contains(Goyf, results);
        Assert.DoesNotContain(Bolt, results);
    }

    [Fact]
    public void Combined_OrWithSource()
    {
        var results = Search("source:MrTeferi or name:Lotus");
        Assert.Equal(2, results.Count);
    }

    [Fact]
    public void Combined_DateRangeWithSource()
    {
        // Old=2024, New=2026, Bird=2025-06-15; all are "Local" source
        var results = Search("source:Local date>2026-01-01");
        Assert.Single(results);
        Assert.Equal(New, results[0]);
    }

    [Fact]
    public void Combined_ComplexQuery()
    {
        // Chilli_Axe entries that don't have "Bolt", OR any Local entry
        var results = Search("(source:Chilli_Axe -Bolt) or source:Local");
        Assert.Contains(Lotus, results);
        Assert.Contains(Goyf, results);
        Assert.Contains(Old, results);
        Assert.Contains(New, results);
        Assert.Contains(Bird, results);
        Assert.DoesNotContain(Bolt, results);
    }

    // ================================================================
    //  TYPE (t:)
    // ================================================================

    [Fact]
    public void TypePrefix_FiltersType()
    {
        var results = Search("t:creature");
        Assert.Equal(2, results.Count);
        Assert.Contains(Goyf, results);
        Assert.Contains(Bird, results);
    }

    [Fact]
    public void TypePrefix_PartialMatch()
    {
        var results = Search("t:instant");
        Assert.Equal(2, results.Count);
        Assert.Contains(Bolt, results);
        Assert.Contains(Counter, results);
    }

    [Fact]
    public void TypePrefix_Artifact()
    {
        var results = Search("type:artifact");
        Assert.Single(results);
        Assert.Equal(Lotus, results[0]);
    }

    // ================================================================
    //  ORACLE TEXT (o:)
    // ================================================================

    [Fact]
    public void OraclePrefix_MatchesText()
    {
        var results = Search("o:damage");
        Assert.Single(results);
        Assert.Equal(Bolt, results[0]);
    }

    [Fact]
    public void OraclePrefix_MatchesPhrase()
    {
        var results = Search("o:Counter o:target o:spell");
        Assert.Single(results);
        Assert.Equal(Counter, results[0]);
    }

    // ================================================================
    //  RARITY (r:)
    // ================================================================

    [Fact]
    public void RarityPrefix_ExactMatch()
    {
        var results = Search("r:common");
        Assert.Single(results);
        Assert.Equal(Bolt, results[0]);
    }

    [Fact]
    public void RarityPrefix_Comparison()
    {
        var results = Search("r>uncommon");
        Assert.Contains(Lotus, results);
        Assert.Contains(Bird, results);
        Assert.Contains(Goyf, results);
        Assert.DoesNotContain(Bolt, results);
        Assert.DoesNotContain(Counter, results);
    }

    // ================================================================
    //  COLORS (c:)
    // ================================================================

    [Fact]
    public void ColorPrefix_SingleColor()
    {
        var results = Search("c:r");
        Assert.Single(results);
        Assert.Equal(Bolt, results[0]);
    }

    [Fact]
    public void ColorPrefix_Green()
    {
        var results = Search("c:g");
        Assert.Contains(Goyf, results);
        Assert.Contains(Bird, results);
    }

    [Fact]
    public void ColorPrefix_Colorless()
    {
        var results = Search("c:colorless");
        // Empty colors = colorless: Lotus, Old, New (no metadata)
        Assert.Contains(Lotus, results);
        Assert.Contains(Old, results);
        Assert.DoesNotContain(Bolt, results);
    }

    // ================================================================
    //  SET (s: / e:)
    // ================================================================

    [Fact]
    public void SetPrefix_ByCode()
    {
        var results = Search("s:LEA");
        Assert.Single(results);
        Assert.Equal(Lotus, results[0]);
    }

    [Fact]
    public void SetPrefix_ByName()
    {
        // "set:" prefix searches both SetCode and SetName
        var results = Search("set:\"Ice Age\"");
        Assert.Single(results);
        Assert.Equal(Counter, results[0]);
    }

    [Fact]
    public void EditionPrefix_Alias()
    {
        var results = Search("e:FUT");
        Assert.Single(results);
        Assert.Equal(Goyf, results[0]);
    }

    // ================================================================
    //  ARTIST (a:)
    // ================================================================

    [Fact]
    public void ArtistPrefix_MatchesArtist()
    {
        var results = Search("a:Rush");
        Assert.Contains(Bolt, results);
        Assert.Contains(Lotus, results);
        Assert.DoesNotContain(Counter, results);
    }

    // ================================================================
    //  KEYWORDS (kw:)
    // ================================================================

    [Fact]
    public void KeywordPrefix_MatchesKeyword()
    {
        var results = Search("kw:flying");
        Assert.Single(results);
        Assert.Equal(Bird, results[0]);
    }

    // ================================================================
    //  CMC / MANA VALUE (cmc, mv)
    // ================================================================

    [Fact]
    public void CmcEquals()
    {
        var results = Search("cmc=0");
        Assert.Contains(Lotus, results);
    }

    [Fact]
    public void CmcGreaterThan()
    {
        var results = Search("cmc>1");
        Assert.Contains(Counter, results);
        Assert.Contains(Goyf, results);
        Assert.DoesNotContain(Bolt, results);
    }

    // ================================================================
    //  POWER / TOUGHNESS (pow, tou)
    // ================================================================

    [Fact]
    public void PowerEquals()
    {
        var results = Search("pow=0");
        Assert.Contains(Bird, results);
    }

    [Fact]
    public void ToughnessGreaterThan()
    {
        // Bird has tou=1, Goyf has tou="1+*" which parses as 0
        var results = Search("tou>0");
        Assert.Contains(Bird, results);
    }

    // ================================================================
    //  COMBINED CARD-METADATA QUERIES
    // ================================================================

    [Fact]
    public void Combined_TypeAndColor()
    {
        var results = Search("t:creature c:g");
        Assert.Equal(2, results.Count);
        Assert.Contains(Goyf, results);
        Assert.Contains(Bird, results);
    }

    [Fact]
    public void Combined_TypeColorRarity()
    {
        var results = Search("t:instant r:common");
        Assert.Single(results);
        Assert.Equal(Bolt, results[0]);
    }

    [Fact]
    public void Combined_OracleAndNegation()
    {
        var results = Search("o:mana -t:creature");
        Assert.Contains(Lotus, results);
        Assert.DoesNotContain(Bird, results);
    }

    [Fact]
    public void Combined_SetAndArtist()
    {
        var results = Search("s:2ED a:Rush");
        Assert.Single(results);
        Assert.Equal(Bolt, results[0]);
    }
}
