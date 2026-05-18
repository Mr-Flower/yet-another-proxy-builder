using MTGProxyBuilder.Core.Services;

namespace MTGProxyBuilder.Tests.Services;

public class MpcFillSearchOptionsTests
{
    [Fact]
    public void Defaults_MatchHardcodedValues()
    {
        var opts = new MpcFillSearchOptions();
        Assert.Equal(new[] { "CARD" }, opts.CardTypes);
        Assert.Equal("nameAscending", opts.SortBy);
        Assert.Equal(0, opts.MinimumDpi);
        Assert.Equal(1500, opts.MaximumDpi);
        Assert.Equal(30, opts.MaximumSize);
        Assert.True(opts.FuzzySearch);
        Assert.False(opts.FilterCardbacks);
        Assert.Empty(opts.Languages);
        Assert.Empty(opts.IncludesTags);
        Assert.Empty(opts.ExcludesTags);
    }

    [Fact]
    public void FromSettings_WithDefaults_MatchesDefaultOptions()
    {
        var settings = new AppSettings();
        var opts = MpcFillSearchOptions.FromSettings(settings);

        Assert.Equal(new[] { "CARD" }, opts.CardTypes);
        Assert.Equal("nameAscending", opts.SortBy);
        Assert.Equal(0, opts.MinimumDpi);
        Assert.Equal(1500, opts.MaximumDpi);
        Assert.Equal(30, opts.MaximumSize);
        Assert.True(opts.FuzzySearch);
        Assert.False(opts.FilterCardbacks);
        Assert.Empty(opts.Languages);
        Assert.Empty(opts.IncludesTags);
        Assert.Empty(opts.ExcludesTags);
    }

    [Fact]
    public void FromSettings_RespectsCustomValues()
    {
        var settings = new AppSettings
        {
            MpcFillDefaultMinDpi = 300,
            MpcFillDefaultMaxDpi = 800,
            MpcFillDefaultFuzzySearch = false,
            MpcFillDefaultSortBy = "dateCreatedDescending",
            MpcFillCardTypes = new() { "CARD", "TOKEN" },
            MpcFillFilterCardbacks = true,
            MpcFillMaximumSize = 15,
            MpcFillLanguages = new() { "EN", "JA" }
        };

        var opts = MpcFillSearchOptions.FromSettings(settings);

        Assert.Equal(new[] { "CARD", "TOKEN" }, opts.CardTypes);
        Assert.Equal("dateCreatedDescending", opts.SortBy);
        Assert.Equal(300, opts.MinimumDpi);
        Assert.Equal(800, opts.MaximumDpi);
        Assert.Equal(15, opts.MaximumSize);
        Assert.False(opts.FuzzySearch);
        Assert.True(opts.FilterCardbacks);
        Assert.Equal(new[] { "EN", "JA" }, opts.Languages);
    }

    [Fact]
    public void FromSettings_ExcludeNsfw_AddsTag()
    {
        var settings = new AppSettings { MpcFillExcludeNsfw = true };
        var opts = MpcFillSearchOptions.FromSettings(settings);
        Assert.Contains("NSFW", opts.ExcludesTags);
    }

    [Fact]
    public void FromSettings_ExcludeAiArt_AddsTag()
    {
        var settings = new AppSettings { MpcFillExcludeAiArt = true };
        var opts = MpcFillSearchOptions.FromSettings(settings);
        Assert.Contains("AI Art", opts.ExcludesTags);
    }

    [Fact]
    public void FromSettings_BothExclusions_AddsBothTags()
    {
        var settings = new AppSettings
        {
            MpcFillExcludeNsfw = true,
            MpcFillExcludeAiArt = true
        };
        var opts = MpcFillSearchOptions.FromSettings(settings);
        Assert.Contains("NSFW", opts.ExcludesTags);
        Assert.Contains("AI Art", opts.ExcludesTags);
    }

    [Fact]
    public void FromSettings_ExcludeTagsFromSettings_CombinedWithBooleans()
    {
        var settings = new AppSettings
        {
            MpcFillExcludeTags = new() { "Full-Art" },
            MpcFillExcludeNsfw = true
        };
        var opts = MpcFillSearchOptions.FromSettings(settings);
        Assert.Contains("Full-Art", opts.ExcludesTags);
        Assert.Contains("NSFW", opts.ExcludesTags);
    }

    [Fact]
    public void FromSettings_NoDuplicateExcludeTags()
    {
        var settings = new AppSettings
        {
            MpcFillExcludeTags = new() { "NSFW" },
            MpcFillExcludeNsfw = true
        };
        var opts = MpcFillSearchOptions.FromSettings(settings);
        Assert.Single(opts.ExcludesTags.Where(t => t == "NSFW"));
    }

    [Fact]
    public void FromSettings_EmptyCardTypes_DefaultsToCard()
    {
        var settings = new AppSettings { MpcFillCardTypes = new() };
        var opts = MpcFillSearchOptions.FromSettings(settings);
        Assert.Equal(new[] { "CARD" }, opts.CardTypes);
    }

    [Fact]
    public void FromSettings_NullLists_HandledGracefully()
    {
        var settings = new AppSettings
        {
            MpcFillCardTypes = null!,
            MpcFillLanguages = null!,
            MpcFillExcludeTags = null!,
            MpcFillIncludeTags = null!
        };

        var opts = MpcFillSearchOptions.FromSettings(settings);
        Assert.Equal(new[] { "CARD" }, opts.CardTypes);
        Assert.Empty(opts.Languages);
        Assert.Empty(opts.IncludesTags);
        Assert.Empty(opts.ExcludesTags);
    }

    [Fact]
    public void FromSettings_ZeroMaxDpi_DefaultsTo1500()
    {
        var settings = new AppSettings { MpcFillDefaultMaxDpi = 0 };
        var opts = MpcFillSearchOptions.FromSettings(settings);
        Assert.Equal(1500, opts.MaximumDpi);
    }

    [Fact]
    public void FromSettings_ZeroMaxSize_DefaultsTo30()
    {
        var settings = new AppSettings { MpcFillMaximumSize = 0 };
        var opts = MpcFillSearchOptions.FromSettings(settings);
        Assert.Equal(30, opts.MaximumSize);
    }

    [Fact]
    public void FromSettings_NullSortBy_DefaultsToNameAscending()
    {
        var settings = new AppSettings { MpcFillDefaultSortBy = null! };
        var opts = MpcFillSearchOptions.FromSettings(settings);
        Assert.Equal("nameAscending", opts.SortBy);
    }
}
