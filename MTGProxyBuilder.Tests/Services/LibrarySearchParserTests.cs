using MTGProxyBuilder.Core.Models;
using MTGProxyBuilder.Core.Services;

namespace MTGProxyBuilder.Tests.Services;

public class LibrarySearchParserTests
{
    private static BackArtEntry Entry(string name, string source = "Local", DateTime? added = null) => new()
    {
        Id = Guid.NewGuid().ToString("N")[..12],
        Name = name,
        Source = source,
        FilePath = "C:\\fake.png",
        AddedDate = added ?? new DateTime(2025, 6, 15)
    };

    private static readonly BackArtEntry Bolt = Entry("Lightning Bolt [Chilli_Axe]", "Chilli_Axe");
    private static readonly BackArtEntry Counter = Entry("Counterspell [MrTeferi]", "MrTeferi");
    private static readonly BackArtEntry Lotus = Entry("Black Lotus [Chilli_Axe]", "Chilli_Axe");
    private static readonly BackArtEntry Old = Entry("Old Card", "Local", new DateTime(2024, 1, 1));
    private static readonly BackArtEntry New = Entry("New Card", "Local", new DateTime(2026, 3, 15));

    private static readonly BackArtEntry[] All = { Bolt, Counter, Lotus, Old, New };

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
        Assert.Equal(5, Search("").Count);
        Assert.Equal(5, Search("   ").Count);
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
        Assert.Equal(2, results.Count);
        Assert.Contains(Bolt, results);
        Assert.Contains(Lotus, results);
    }

    [Fact]
    public void SrcPrefix_AliasForSource()
    {
        Assert.Single(Search("src:MrTeferi"));
    }

    [Fact]
    public void SPrefix_AliasForSource()
    {
        Assert.Single(Search("s:MrTeferi"));
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
        Assert.Equal(4, results.Count);
        Assert.DoesNotContain(Old, results);
    }

    [Fact]
    public void InvalidDate_DoesNotFilter()
    {
        Assert.Equal(5, Search("date>notadate").Count);
    }

    // ================================================================
    //  NEGATION (-)
    // ================================================================

    [Fact]
    public void Negation_ExcludesMatches()
    {
        var results = Search("-Lotus");
        Assert.Equal(4, results.Count);
        Assert.DoesNotContain(Lotus, results);
    }

    [Fact]
    public void Negation_WithPrefix()
    {
        var results = Search("-source:Chilli_Axe");
        Assert.Equal(3, results.Count);
        Assert.DoesNotContain(Bolt, results);
        Assert.DoesNotContain(Lotus, results);
    }

    [Fact]
    public void Negation_WithQuotedPhrase()
    {
        var results = Search("-\"Black Lotus\"");
        Assert.Equal(4, results.Count);
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
        Assert.Equal(3, results.Count);
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
        Assert.Equal(5, Search("name:/[invalid/").Count);
    }

    // ================================================================
    //  COMBINED QUERIES
    // ================================================================

    [Fact]
    public void Combined_SourceAndNegation()
    {
        var results = Search("source:Chilli_Axe -Bolt");
        Assert.Single(results);
        Assert.Equal(Lotus, results[0]);
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
        var results = Search("source:Local date>2025-01-01");
        Assert.Single(results);
        Assert.Equal(New, results[0]);
    }

    [Fact]
    public void Combined_ComplexQuery()
    {
        // Chilli_Axe entries that don't have "Bolt", OR any Local entry
        var results = Search("(source:Chilli_Axe -Bolt) or source:Local");
        Assert.Equal(3, results.Count);
        Assert.Contains(Lotus, results);
        Assert.Contains(Old, results);
        Assert.Contains(New, results);
    }
}
