using MTGProxyBuilder.Core.Models;

namespace MTGProxyBuilder.Tests.Models;

public class BackArtEntryTests
{
    [Fact]
    public void NewEntry_HasDefaults()
    {
        var entry = new BackArtEntry();
        Assert.False(string.IsNullOrEmpty(entry.Id));
        Assert.Equal(string.Empty, entry.Name);
        Assert.Equal(string.Empty, entry.FilePath);
        Assert.Equal(string.Empty, entry.Source);
    }

    [Fact]
    public void Source_CanBeSet()
    {
        var entry = new BackArtEntry { Source = "Chilli_Axe" };
        Assert.Equal("Chilli_Axe", entry.Source);
    }

    [Fact]
    public void PropertyChanged_FiresOnSourceChange()
    {
        var entry = new BackArtEntry();
        string? changed = null;
        entry.PropertyChanged += (_, e) => changed = e.PropertyName;
        entry.Source = "MrTeferi";
        Assert.Equal("Source", changed);
    }

    [Fact]
    public void PropertyChanged_FiresOnNameChange()
    {
        var entry = new BackArtEntry();
        string? changed = null;
        entry.PropertyChanged += (_, e) => changed = e.PropertyName;
        entry.Name = "Black Lotus";
        Assert.Equal("Name", changed);
    }

    [Fact]
    public void AddedDate_DefaultsToNow()
    {
        var before = DateTime.Now.AddSeconds(-1);
        var entry = new BackArtEntry();
        var after = DateTime.Now.AddSeconds(1);
        Assert.InRange(entry.AddedDate, before, after);
    }
}
