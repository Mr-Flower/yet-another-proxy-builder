using MTGProxyBuilder.Core.Services;

namespace MTGProxyBuilder.Tests.Services;

public class FrontArtLibraryServiceTests : IDisposable
{
    private readonly string _testDir;
    private readonly string _testImagePath;
    private readonly string _testImagePath2;

    public FrontArtLibraryServiceTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"MTGProxyBuilder_Test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);

        _testImagePath = Path.Combine(_testDir, "test_front.png");
        File.WriteAllBytes(_testImagePath, new byte[] { 0x89, 0x50, 0x4E, 0x47 });

        _testImagePath2 = Path.Combine(_testDir, "test_front2.png");
        File.WriteAllBytes(_testImagePath2, new byte[] { 0x89, 0x50, 0x4E, 0x47 });
    }

    public void Dispose()
    {
        try { Directory.Delete(_testDir, true); } catch { }
    }

    [Fact]
    public void Initially_HasNoEntries()
    {
        var svc = new FrontArtLibraryService();
        Assert.NotNull(svc.Entries);
    }

    [Fact]
    public void AddFromFile_ReturnsEntry()
    {
        var svc = new FrontArtLibraryService();
        var entry = svc.AddFromFile(_testImagePath, $"Test_{Guid.NewGuid():N}");
        Assert.NotNull(entry);
        Assert.False(string.IsNullOrEmpty(entry!.Id));
        Assert.True(File.Exists(entry.FilePath));

        svc.Remove(entry.Id);
    }

    [Fact]
    public void AddFromFile_DuplicateName_ReturnsExisting()
    {
        var svc = new FrontArtLibraryService();
        string uniqueName = $"Dup_{Guid.NewGuid():N}";
        var first = svc.AddFromFile(_testImagePath, uniqueName);
        var second = svc.AddFromFile(_testImagePath, uniqueName);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(first!.Id, second!.Id);

        svc.Remove(first.Id);
    }

    [Fact]
    public void AddFromFile_NonexistentFile_ReturnsNull()
    {
        var svc = new FrontArtLibraryService();
        Assert.Null(svc.AddFromFile("/nonexistent/path.png"));
    }

    [Fact]
    public void Remove_DeletesEntryAndFile()
    {
        var svc = new FrontArtLibraryService();
        string uniqueName = $"Remove_{Guid.NewGuid():N}";
        var entry = svc.AddFromFile(_testImagePath, uniqueName);
        Assert.NotNull(entry);

        string filePath = entry!.FilePath;
        bool removed = svc.Remove(entry.Id);
        Assert.True(removed);
        Assert.False(File.Exists(filePath));
    }

    [Fact]
    public void Remove_NonexistentId_ReturnsFalse()
    {
        var svc = new FrontArtLibraryService();
        Assert.False(svc.Remove("nonexistent_id"));
    }

    [Fact]
    public void GetById_FindsEntry()
    {
        var svc = new FrontArtLibraryService();
        string uniqueName = $"Find_{Guid.NewGuid():N}";
        var entry = svc.AddFromFile(_testImagePath, uniqueName);
        Assert.NotNull(entry);

        var found = svc.GetById(entry!.Id);
        Assert.NotNull(found);
        Assert.Equal(entry.Name, found!.Name);

        svc.Remove(entry.Id);
    }

    [Fact]
    public void GetById_NotFound_ReturnsNull()
    {
        var svc = new FrontArtLibraryService();
        Assert.Null(svc.GetById("nonexistent"));
    }

    [Fact]
    public void AddFromFile_NoContributor_DefaultsToLocal()
    {
        var svc = new FrontArtLibraryService();
        string uniqueName = $"Src1_{Guid.NewGuid():N}";
        var entry = svc.AddFromFile(_testImagePath, uniqueName);
        Assert.NotNull(entry);
        Assert.Equal("Local", entry!.Source);

        svc.Remove(entry.Id);
    }

    [Fact]
    public void AddFromFile_WithContributor_SetsSource()
    {
        var svc = new FrontArtLibraryService();
        string uniqueName = $"Src2_{Guid.NewGuid():N}";
        var entry = svc.AddFromFile(_testImagePath, uniqueName, "Chilli_Axe");
        Assert.NotNull(entry);
        Assert.Equal("Chilli_Axe", entry!.Source);

        svc.Remove(entry.Id);
    }

    // --- SearchByCardName Tests ---

    [Fact]
    public void SearchByCardName_FindsMatchingEntries()
    {
        var svc = new FrontArtLibraryService();
        string guid = Guid.NewGuid().ToString("N")[..8];
        var entry = svc.AddFromFile(_testImagePath, $"Lightning Bolt [{guid}]", "TestSource");
        Assert.NotNull(entry);

        var results = svc.SearchByCardName("Lightning Bolt");
        Assert.Contains(results, r => r.Id == entry!.Id);

        svc.Remove(entry!.Id);
    }

    [Fact]
    public void SearchByCardName_IsCaseInsensitive()
    {
        var svc = new FrontArtLibraryService();
        string guid = Guid.NewGuid().ToString("N")[..8];
        var entry = svc.AddFromFile(_testImagePath, $"Counterspell [{guid}]", "TestSource");
        Assert.NotNull(entry);

        var results = svc.SearchByCardName("counterspell");
        Assert.Contains(results, r => r.Id == entry!.Id);

        svc.Remove(entry!.Id);
    }

    [Fact]
    public void SearchByCardName_ReturnsEmptyForNoMatch()
    {
        var svc = new FrontArtLibraryService();
        var results = svc.SearchByCardName($"NonexistentCard_{Guid.NewGuid():N}");
        Assert.Empty(results);
    }

    [Fact]
    public void SearchByCardName_ReturnsEmptyForNullOrWhitespace()
    {
        var svc = new FrontArtLibraryService();
        Assert.Empty(svc.SearchByCardName(""));
        Assert.Empty(svc.SearchByCardName("   "));
        Assert.Empty(svc.SearchByCardName(null!));
    }

    [Fact]
    public void SearchByCardName_FindsMultipleVersions()
    {
        var svc = new FrontArtLibraryService();
        string guid = Guid.NewGuid().ToString("N")[..8];
        var e1 = svc.AddFromFile(_testImagePath, $"Dark Ritual [{guid}_A]", "Source1");
        var e2 = svc.AddFromFile(_testImagePath2, $"Dark Ritual [{guid}_B]", "Source2");
        Assert.NotNull(e1);
        Assert.NotNull(e2);

        var results = svc.SearchByCardName("Dark Ritual");
        Assert.True(results.Count >= 2);

        svc.Remove(e1!.Id);
        svc.Remove(e2!.Id);
    }

    // --- Batch Mode Tests ---

    [Fact]
    public void BatchMode_AddsMultipleEntries()
    {
        var svc = new FrontArtLibraryService();
        string guid = Guid.NewGuid().ToString("N")[..8];

        svc.BeginBatch();
        var e1 = svc.AddFromFile(_testImagePath, $"Batch1_{guid}");
        var e2 = svc.AddFromFile(_testImagePath2, $"Batch2_{guid}");
        svc.EndBatch();

        Assert.NotNull(e1);
        Assert.NotNull(e2);
        Assert.NotEqual(e1!.Id, e2!.Id);

        svc.Remove(e1.Id);
        svc.Remove(e2.Id);
    }

    [Fact]
    public void BatchMode_SkipsDuplicateNames()
    {
        var svc = new FrontArtLibraryService();
        string uniqueName = $"BatchDup_{Guid.NewGuid():N}";

        svc.BeginBatch();
        var first = svc.AddFromFile(_testImagePath, uniqueName);
        var second = svc.AddFromFile(_testImagePath2, uniqueName);
        svc.EndBatch();

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(first!.Id, second!.Id);

        svc.Remove(first.Id);
    }
}
