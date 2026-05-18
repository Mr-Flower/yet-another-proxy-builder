using MTGProxyBuilder.Core.Models;
using MTGProxyBuilder.Core.Services;

namespace MTGProxyBuilder.Tests.Services;

public class ProjectSerializationServiceTests : IDisposable
{
    private readonly string _testDir;
    private readonly string _testImagePath;

    public ProjectSerializationServiceTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"MTGProxyBuilder_Test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);

        _testImagePath = Path.Combine(_testDir, "test_card.png");
        File.WriteAllBytes(_testImagePath, new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
    }

    public void Dispose()
    {
        try { Directory.Delete(_testDir, true); } catch { }
    }

    [Fact]
    public async Task SaveAndLoad_RoundTrip_PreservesProjectName()
    {
        var svc = new ProjectSerializationService();
        var project = new ProjectModel { ProjectName = "Test Project" };
        string filePath = Path.Combine(_testDir, "test.mtgproj");

        bool saved = await svc.SaveProjectAsync(project, filePath);
        Assert.True(saved);
        Assert.True(File.Exists(filePath));

        var loaded = await svc.LoadProjectAsync(filePath);
        Assert.NotNull(loaded);
        Assert.Equal("Test Project", loaded!.ProjectName);
    }

    [Fact]
    public async Task SaveAndLoad_PreservesCards()
    {
        var svc = new ProjectSerializationService();
        var project = new ProjectModel { ProjectName = "Cards Test" };
        project.Cards.Add(new CardModel
        {
            Name = "Lightning Bolt",
            ArtworkPath = _testImagePath,
            Quantity = 4,
            OverlayText = "PROXY"
        });
        string filePath = Path.Combine(_testDir, "cards.mtgproj");

        await svc.SaveProjectAsync(project, filePath);
        var loaded = await svc.LoadProjectAsync(filePath);

        Assert.NotNull(loaded);
        Assert.Single(loaded!.Cards);
        Assert.Equal("Lightning Bolt", loaded.Cards[0].Name);
        Assert.Equal(4, loaded.Cards[0].Quantity);
        Assert.Equal("PROXY", loaded.Cards[0].OverlayText);
    }

    [Fact]
    public async Task SaveAndLoad_PreservesArtworkFiles()
    {
        var svc = new ProjectSerializationService();
        var project = new ProjectModel { ProjectName = "Art Test" };
        project.Cards.Add(new CardModel
        {
            Name = "Test Card",
            ArtworkPath = _testImagePath
        });
        string filePath = Path.Combine(_testDir, "art.mtgproj");

        await svc.SaveProjectAsync(project, filePath);
        var loaded = await svc.LoadProjectAsync(filePath);

        Assert.NotNull(loaded);
        Assert.NotEmpty(loaded!.Cards[0].ArtworkPath);
        Assert.True(File.Exists(loaded.Cards[0].ArtworkPath));
    }

    [Fact]
    public async Task Save_CreatesValidZipFile()
    {
        var svc = new ProjectSerializationService();
        var project = new ProjectModel { ProjectName = "Zip Test" };
        string filePath = Path.Combine(_testDir, "valid.mtgproj");

        await svc.SaveProjectAsync(project, filePath);

        // Should be a valid ZIP archive
        using var stream = File.OpenRead(filePath);
        using var zip = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Read);
        Assert.NotNull(zip.GetEntry("project.json"));
    }

    [Fact]
    public async Task Load_NonexistentFile_ReturnsNull()
    {
        var svc = new ProjectSerializationService();
        var result = await svc.LoadProjectAsync(Path.Combine(_testDir, "nonexistent.mtgproj"));
        Assert.Null(result);
    }

    [Fact]
    public async Task Load_InvalidFile_ReturnsNull()
    {
        var svc = new ProjectSerializationService();
        string badFile = Path.Combine(_testDir, "bad.mtgproj");
        File.WriteAllText(badFile, "not a zip file");

        var result = await svc.LoadProjectAsync(badFile);
        Assert.Null(result);
    }

    [Fact]
    public async Task SaveAndLoad_EmptyProject_Works()
    {
        var svc = new ProjectSerializationService();
        var project = new ProjectModel();
        string filePath = Path.Combine(_testDir, "empty.mtgproj");

        bool saved = await svc.SaveProjectAsync(project, filePath);
        Assert.True(saved);

        var loaded = await svc.LoadProjectAsync(filePath);
        Assert.NotNull(loaded);
        Assert.Empty(loaded!.Cards);
    }

    [Fact]
    public async Task SaveAndLoad_MultipleCards_PreservesAll()
    {
        var svc = new ProjectSerializationService();
        var project = new ProjectModel { ProjectName = "Multi" };
        for (int i = 0; i < 5; i++)
        {
            project.Cards.Add(new CardModel
            {
                Name = $"Card {i}",
                ArtworkPath = _testImagePath,
                Quantity = i + 1
            });
        }
        string filePath = Path.Combine(_testDir, "multi.mtgproj");

        await svc.SaveProjectAsync(project, filePath);
        var loaded = await svc.LoadProjectAsync(filePath);

        Assert.NotNull(loaded);
        Assert.Equal(5, loaded!.Cards.Count);
        for (int i = 0; i < 5; i++)
        {
            Assert.Equal($"Card {i}", loaded.Cards[i].Name);
            Assert.Equal(i + 1, loaded.Cards[i].Quantity);
        }
    }

    [Fact]
    public async Task SaveAndLoad_PreservesBackArtwork()
    {
        var svc = new ProjectSerializationService();
        var project = new ProjectModel();
        project.Cards.Add(new CardModel
        {
            Name = "DFC",
            ArtworkPath = _testImagePath,
            BackArtworkPath = _testImagePath,
            IncludeBack = true
        });
        string filePath = Path.Combine(_testDir, "back.mtgproj");

        await svc.SaveProjectAsync(project, filePath);
        var loaded = await svc.LoadProjectAsync(filePath);

        Assert.NotNull(loaded);
        Assert.NotEmpty(loaded!.Cards[0].BackArtworkPath!);
        Assert.True(File.Exists(loaded.Cards[0].BackArtworkPath));
        Assert.True(loaded.Cards[0].IncludeBack);
    }
}
