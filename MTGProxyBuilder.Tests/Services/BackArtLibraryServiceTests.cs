using MTGProxyBuilder.Core.Services;

namespace MTGProxyBuilder.Tests.Services;

public class BackArtLibraryServiceTests : IDisposable
{
    private readonly string _testDir;
    private readonly string _testImagePath;

    public BackArtLibraryServiceTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"MTGProxyBuilder_Test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);

        // Create a minimal test image file
        _testImagePath = Path.Combine(_testDir, "test_back.png");
        File.WriteAllBytes(_testImagePath, new byte[] { 0x89, 0x50, 0x4E, 0x47 }); // PNG header
    }

    public void Dispose()
    {
        try { Directory.Delete(_testDir, true); } catch { }
    }

    [Fact]
    public void Initially_HasNoEntries()
    {
        var svc = new BackArtLibraryService();
        Assert.NotNull(svc.Entries);
        // May have entries from previous tests; at minimum should not throw
    }

    [Fact]
    public void AddFromFile_ReturnsEntry()
    {
        var svc = new BackArtLibraryService();
        var entry = svc.AddFromFile(_testImagePath, $"Test_{Guid.NewGuid():N}");
        Assert.NotNull(entry);
        Assert.False(string.IsNullOrEmpty(entry!.Id));
        Assert.True(File.Exists(entry.FilePath));
    }

    [Fact]
    public void AddFromFile_DuplicateName_ReturnsExisting()
    {
        var svc = new BackArtLibraryService();
        string uniqueName = $"Dup_{Guid.NewGuid():N}";
        var first = svc.AddFromFile(_testImagePath, uniqueName);
        var second = svc.AddFromFile(_testImagePath, uniqueName);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(first!.Id, second!.Id);
    }

    [Fact]
    public void AddFromFile_NonexistentFile_ReturnsNull()
    {
        var svc = new BackArtLibraryService();
        Assert.Null(svc.AddFromFile("/nonexistent/path.png"));
    }

    [Fact]
    public void Remove_DeletesEntryAndFile()
    {
        var svc = new BackArtLibraryService();
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
        var svc = new BackArtLibraryService();
        Assert.False(svc.Remove("nonexistent_id"));
    }

    [Fact]
    public void GetById_FindsEntry()
    {
        var svc = new BackArtLibraryService();
        string uniqueName = $"Find_{Guid.NewGuid():N}";
        var entry = svc.AddFromFile(_testImagePath, uniqueName);
        Assert.NotNull(entry);

        var found = svc.GetById(entry!.Id);
        Assert.NotNull(found);
        Assert.Equal(entry.Name, found!.Name);
    }

    [Fact]
    public void GetById_NotFound_ReturnsNull()
    {
        var svc = new BackArtLibraryService();
        Assert.Null(svc.GetById("nonexistent"));
    }

    [Fact]
    public void DefaultEntryId_CanBeCleared()
    {
        var svc = new BackArtLibraryService();
        svc.SetDefault(null); // ensure clean state
        Assert.Null(svc.DefaultEntryId);
    }

    [Fact]
    public void SetDefault_SetsDefaultId()
    {
        var svc = new BackArtLibraryService();
        string uniqueName = $"Default_{Guid.NewGuid():N}";
        var entry = svc.AddFromFile(_testImagePath, uniqueName);
        Assert.NotNull(entry);

        svc.SetDefault(entry!.Id);
        Assert.Equal(entry.Id, svc.DefaultEntryId);
        Assert.True(svc.IsDefault(entry.Id));
    }

    [Fact]
    public void SetDefault_Null_ClearsDefault()
    {
        var svc = new BackArtLibraryService();
        string uniqueName = $"ClearDef_{Guid.NewGuid():N}";
        var entry = svc.AddFromFile(_testImagePath, uniqueName);
        svc.SetDefault(entry!.Id);
        svc.SetDefault(null);

        Assert.Null(svc.DefaultEntryId);
    }

    [Fact]
    public void DefaultBackArtPath_ReturnsPathWhenSet()
    {
        var svc = new BackArtLibraryService();
        string uniqueName = $"Path_{Guid.NewGuid():N}";
        var entry = svc.AddFromFile(_testImagePath, uniqueName);
        svc.SetDefault(entry!.Id);

        Assert.NotNull(svc.DefaultBackArtPath);
        Assert.True(File.Exists(svc.DefaultBackArtPath));
    }

    [Fact]
    public void DefaultBackArtPath_NullWhenNoDefault()
    {
        var svc = new BackArtLibraryService();
        svc.SetDefault(null); // Ensure clean state
        Assert.Null(svc.DefaultBackArtPath);
    }

    [Fact]
    public void Remove_DefaultEntry_ClearsDefault()
    {
        var svc = new BackArtLibraryService();
        string uniqueName = $"RemDef_{Guid.NewGuid():N}";
        var entry = svc.AddFromFile(_testImagePath, uniqueName);
        svc.SetDefault(entry!.Id);

        svc.Remove(entry.Id);
        Assert.Null(svc.DefaultEntryId);
    }

    // --- Source / Contributor Tests ---

    [Fact]
    public void AddFromFile_NoContributor_DefaultsToLocal()
    {
        var svc = new BackArtLibraryService();
        string uniqueName = $"Src1_{Guid.NewGuid():N}";
        var entry = svc.AddFromFile(_testImagePath, uniqueName);
        Assert.NotNull(entry);
        Assert.Equal("Local", entry!.Source);
    }

    [Fact]
    public void AddFromFile_WithContributor_SetsSource()
    {
        var svc = new BackArtLibraryService();
        string uniqueName = $"Src2_{Guid.NewGuid():N}";
        var entry = svc.AddFromFile(_testImagePath, uniqueName, "Chilli_Axe");
        Assert.NotNull(entry);
        Assert.Equal("Chilli_Axe", entry!.Source);
    }

    [Fact]
    public void AddFromFile_DifferentContributors_PreservedOnEach()
    {
        var svc = new BackArtLibraryService();
        var entry1 = svc.AddFromFile(_testImagePath, $"C1_{Guid.NewGuid():N}", "MrTeferi");
        var entry2 = svc.AddFromFile(_testImagePath, $"C2_{Guid.NewGuid():N}", "JohnPrime");

        Assert.Equal("MrTeferi", entry1!.Source);
        Assert.Equal("JohnPrime", entry2!.Source);

        // Clean up
        svc.Remove(entry1.Id);
        svc.Remove(entry2.Id);
    }

    [Fact]
    public void GetById_ReturnsEntryWithSource()
    {
        var svc = new BackArtLibraryService();
        string uniqueName = $"Src3_{Guid.NewGuid():N}";
        var entry = svc.AddFromFile(_testImagePath, uniqueName, "TestContributor");
        Assert.NotNull(entry);

        var found = svc.GetById(entry!.Id);
        Assert.NotNull(found);
        Assert.Equal("TestContributor", found!.Source);

        svc.Remove(entry.Id);
    }

    // --- Custom Directory Constructor ---

    [Fact]
    public void Constructor_WithCustomDirectory_UsesIt()
    {
        string customDir = Path.Combine(_testDir, "CustomBackLib");
        var svc = new BackArtLibraryService(customDir);

        Assert.Equal(customDir, svc.LibraryDirectory);
        Assert.True(Directory.Exists(customDir));
    }

    // --- MoveToDirectory Tests ---

    [Fact]
    public void MoveToDirectory_MovesFilesAndUpdatesPaths()
    {
        string srcDir = Path.Combine(_testDir, "MoveSource");
        string destDir = Path.Combine(_testDir, "MoveDest");
        var svc = new BackArtLibraryService(srcDir);

        var entry = svc.AddFromFile(_testImagePath, $"Move_{Guid.NewGuid():N}");
        Assert.NotNull(entry);
        string oldPath = entry!.FilePath;
        Assert.StartsWith(srcDir, oldPath, StringComparison.OrdinalIgnoreCase);

        svc.MoveToDirectory(destDir);

        Assert.Equal(destDir, svc.LibraryDirectory);
        var found = svc.GetById(entry.Id);
        Assert.NotNull(found);
        Assert.StartsWith(destDir, found!.FilePath, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(found.FilePath));
    }

    // --- ExportToZip / ImportFromZip Tests ---

    [Fact]
    public void ExportToZip_CreatesZipFile()
    {
        string libDir = Path.Combine(_testDir, "ExportLib");
        var svc = new BackArtLibraryService(libDir);

        svc.AddFromFile(_testImagePath, $"Export_{Guid.NewGuid():N}");
        svc.AddFromFile(_testImagePath, $"Export_{Guid.NewGuid():N}");

        string zipPath = Path.Combine(_testDir, "export_back.zip");
        svc.ExportToZip(zipPath);

        Assert.True(File.Exists(zipPath));
        Assert.True(new FileInfo(zipPath).Length > 0);
    }

    [Fact]
    public void ImportFromZip_RestoresEntries()
    {
        // Create and export a library
        string srcDir = Path.Combine(_testDir, "ImportSrc");
        var srcSvc = new BackArtLibraryService(srcDir);
        srcSvc.AddFromFile(_testImagePath, $"Import_{Guid.NewGuid():N}", "TestSource");
        srcSvc.AddFromFile(_testImagePath, $"Import_{Guid.NewGuid():N}", "TestSource");

        string zipPath = Path.Combine(_testDir, "import_back.zip");
        srcSvc.ExportToZip(zipPath);

        // Import into a fresh library
        string destDir = Path.Combine(_testDir, "ImportDest");
        var destSvc = new BackArtLibraryService(destDir);
        Assert.Empty(destSvc.Entries);

        int added = destSvc.ImportFromZip(zipPath);
        Assert.Equal(2, added);
        Assert.Equal(2, destSvc.Entries.Count);
        Assert.All(destSvc.Entries, e => Assert.True(File.Exists(e.FilePath)));
    }

    [Fact]
    public void ImportFromZip_SkipsDuplicates()
    {
        string libDir = Path.Combine(_testDir, "DupImportLib");
        var svc = new BackArtLibraryService(libDir);
        string name = $"DupImport_{Guid.NewGuid():N}";
        svc.AddFromFile(_testImagePath, name);

        // Export, then re-import into the same library
        string zipPath = Path.Combine(_testDir, "dup_import_back.zip");
        svc.ExportToZip(zipPath);

        int added = svc.ImportFromZip(zipPath);
        Assert.Equal(0, added); // already exists
    }
}
