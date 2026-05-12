using MTGProxyBuilder.Core.Models;
using MTGProxyBuilder.Core.Services;

namespace MTGProxyBuilder.Tests.Integration;

/// <summary>
/// Integration tests that verify the full service pipeline works end-to-end.
/// These tests make real network calls to Scryfall and real file I/O.
/// They are slower than unit tests but verify the actual integration.
/// </summary>
[Trait("Category", "Integration")]
public class ServicePipelineTests : IDisposable
{
    private readonly string _testOutputDir;
    private readonly ImageCacheService _imageCache;
    private readonly ScryfallService _scryfall;
    private readonly PdfGeneratorService _pdfGenerator;
    private readonly ProjectSerializationService _serializer;
    private readonly BackArtLibraryService _backArtLibrary;

    public ServicePipelineTests()
    {
        _testOutputDir = Path.Combine(Path.GetTempPath(), $"MTGProxy_E2E_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testOutputDir);

        _imageCache = new ImageCacheService();
        _scryfall = new ScryfallService(_imageCache);
        _pdfGenerator = new PdfGeneratorService();
        _serializer = new ProjectSerializationService();
        _backArtLibrary = new BackArtLibraryService();
    }

    public void Dispose()
    {
        try { Directory.Delete(_testOutputDir, true); } catch { }
    }

    [Fact]
    public async Task ScryfallSearch_ReturnsResults()
    {
        var (results, error) = await _scryfall.SearchCardAsync("lightning bolt");
        Assert.Null(error);
        Assert.NotEmpty(results);
        Assert.Contains(results, c => c.Name.Contains("Lightning Bolt", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ScryfallSearch_ExactName_ReturnsOnlyThatCard()
    {
        var (results, error) = await _scryfall.SearchCardAsync("!\"Sol Ring\"");
        Assert.Null(error);
        Assert.NotEmpty(results);
        Assert.All(results, c => Assert.Equal("Sol Ring", c.Name));
    }

    [Fact]
    public async Task ScryfallSearch_InvalidQuery_ReturnsError()
    {
        var (results, error) = await _scryfall.SearchCardAsync("zzzznotarealcard999");
        // Scryfall returns 404 for no results
        Assert.Empty(results);
    }

    [Fact]
    public async Task ScryfallGetCardByName_FindsCard()
    {
        var card = await _scryfall.GetCardByNameAsync("Black Lotus");
        Assert.NotNull(card);
        Assert.Equal("Black Lotus", card!.Name);
        Assert.NotNull(card.GetImageUrl());
    }

    [Fact]
    public async Task ScryfallToCardModel_FullPipeline()
    {
        var card = await _scryfall.GetCardByNameAsync("Counterspell");
        Assert.NotNull(card);

        var model = card!.ToCardModel("/fake/path.jpg", null);
        Assert.Equal("Counterspell", model.Name);
        Assert.False(string.IsNullOrEmpty(model.TypeLine));
        Assert.False(string.IsNullOrEmpty(model.ManaCost));
        Assert.True(model.CMC > 0);
    }

    [Fact]
    public async Task ScryfallDownloadImage_CachesLocally()
    {
        var card = await _scryfall.GetCardByNameAsync("Forest");
        Assert.NotNull(card);

        var path = await _scryfall.DownloadAndCacheImageAsync(card!);
        Assert.NotNull(path);
        Assert.True(File.Exists(path));
        Assert.True(new FileInfo(path!).Length > 0);
    }

    [Fact]
    public async Task FullPipeline_SearchDownloadGeneratePdf()
    {
        // 1. Search for a card
        var card = await _scryfall.GetCardByNameAsync("Mountain");
        Assert.NotNull(card);

        // 2. Download artwork
        var artPath = await _scryfall.DownloadAndCacheImageAsync(card!);
        Assert.NotNull(artPath);

        // 3. Create a project
        var project = new ProjectModel { ProjectName = "E2E Test" };
        var cardModel = card!.ToCardModel(artPath!, null);
        cardModel.Quantity = 9; // fill a page
        project.Cards.Add(cardModel);

        // 4. Generate PDF
        string pdfPath = Path.Combine(_testOutputDir, "e2e_test.pdf");
        bool success = await _pdfGenerator.GeneratePdfAsync(project, pdfPath);

        Assert.True(success);
        Assert.True(File.Exists(pdfPath));
        Assert.True(new FileInfo(pdfPath).Length > 1000, "PDF should be more than 1KB");
    }

    [Fact]
    public async Task FullPipeline_SaveAndLoadProject()
    {
        // 1. Create a project with a card
        var card = await _scryfall.GetCardByNameAsync("Island");
        Assert.NotNull(card);
        var artPath = await _scryfall.DownloadAndCacheImageAsync(card!);

        var project = new ProjectModel { ProjectName = "Save Load Test" };
        project.Cards.Add(card!.ToCardModel(artPath ?? "", null));

        // 2. Save to portable format
        string projPath = Path.Combine(_testOutputDir, "test_project.mtgproj");
        bool saved = await _serializer.SaveProjectAsync(project, projPath);
        Assert.True(saved);
        Assert.True(File.Exists(projPath));

        // 3. Load back
        var loaded = await _serializer.LoadProjectAsync(projPath);
        Assert.NotNull(loaded);
        Assert.Equal("Save Load Test", loaded!.ProjectName);
        Assert.Single(loaded.Cards);
        Assert.Equal("Island", loaded.Cards[0].Name);

        // 4. Verify artwork was preserved
        Assert.False(string.IsNullOrEmpty(loaded.Cards[0].ArtworkPath));
        Assert.True(File.Exists(loaded.Cards[0].ArtworkPath));
    }

    [Fact]
    public async Task FullPipeline_DuplexPdf()
    {
        var card = await _scryfall.GetCardByNameAsync("Plains");
        Assert.NotNull(card);
        var artPath = await _scryfall.DownloadAndCacheImageAsync(card!);

        var project = new ProjectModel { ProjectName = "Duplex Test" };
        var model = card!.ToCardModel(artPath ?? "", null);
        model.Quantity = 4;
        model.IncludeBack = true;
        // Back will be blank but PDF should still generate
        project.Cards.Add(model);
        project.PrintSettings.PrintMode = PrintMode.Duplex;

        string pdfPath = Path.Combine(_testOutputDir, "duplex_test.pdf");
        bool success = await _pdfGenerator.GeneratePdfAsync(project, pdfPath);

        Assert.True(success);
        Assert.True(File.Exists(pdfPath));
    }

    [Fact]
    public void DeckImport_DetectSource_AllSupported()
    {
        Assert.Equal(DeckSource.Moxfield, DeckImportService.DetectSource("https://moxfield.com/decks/abc"));
        Assert.Equal(DeckSource.Archidekt, DeckImportService.DetectSource("https://archidekt.com/decks/123/name"));
        Assert.Equal(DeckSource.Unknown, DeckImportService.DetectSource("https://example.com"));
    }

    [Fact]
    public void UndoRedo_FullCycle()
    {
        var undo = new UndoService();
        var cards = new List<CardModel> { new() { Name = "Card A" } };

        // Save state, modify, undo
        undo.SaveState(cards);
        cards.Add(new CardModel { Name = "Card B" });

        var restored = undo.Undo(cards);
        Assert.NotNull(restored);
        Assert.Single(restored!);
        Assert.Equal("Card A", restored[0].Name);

        // Redo
        var redone = undo.Redo(restored);
        Assert.NotNull(redone);
        Assert.Equal(2, redone!.Count);
    }

    [Fact]
    public void CacheManager_CleanupAndMeasure()
    {
        var mgr = new CacheManager();
        mgr.CleanupOnStartup();

        long size = mgr.GetTotalCacheSizeBytes();
        Assert.True(size >= 0);

        string formatted = CacheManager.FormatBytes(size);
        Assert.False(string.IsNullOrEmpty(formatted));
    }

    [Fact]
    public void BackArtLibrary_FullLifecycle()
    {
        // Create test image
        var tmpPath = Path.Combine(_testOutputDir, "test_back.png");
        File.WriteAllBytes(tmpPath, new byte[] { 0x89, 0x50, 0x4E, 0x47 });

        string uniqueName = $"E2E_{Guid.NewGuid():N}";

        // Add
        var entry = _backArtLibrary.AddFromFile(tmpPath, uniqueName, "TestContributor");
        Assert.NotNull(entry);
        Assert.Equal("TestContributor", entry!.Source);

        // Set default
        _backArtLibrary.SetDefault(entry.Id);
        Assert.Equal(entry.Id, _backArtLibrary.DefaultEntryId);
        Assert.NotNull(_backArtLibrary.DefaultBackArtPath);

        // Remove
        _backArtLibrary.Remove(entry.Id);
        Assert.Null(_backArtLibrary.DefaultEntryId);
    }

    [Fact]
    public void CardSizePresets_AllHaveValidDimensions()
    {
        foreach (var preset in CardSizePreset.BuiltInPresets)
        {
            Assert.True(preset.WidthMm > 0, $"{preset.Name} width");
            Assert.True(preset.HeightMm > 0, $"{preset.Name} height");
            Assert.True(preset.HeightMm > preset.WidthMm, $"{preset.Name} should be portrait (height > width)");
        }
    }

    [Fact]
    public void PageLayout_AllPresetsProduceValidGrid()
    {
        foreach (var preset in new[] { "A4", "A3", "Letter", "Legal", "Tabloid" })
        {
            var layout = new PageLayout();
            layout.ApplyPagePreset(preset);
            layout.CenterGrid();

            Assert.True(layout.CardsPerRow > 0, $"{preset}: no columns");
            Assert.True(layout.CardsPerColumn > 0, $"{preset}: no rows");
            Assert.True(layout.CardsPerPage > 0, $"{preset}: no cards per page");
            Assert.True(layout.MarginLeftMm >= 0, $"{preset}: negative left margin");
            Assert.True(layout.MarginTopMm >= 0, $"{preset}: negative top margin");
        }
    }

    // --- Card Outline Guide Integration Tests ---

    [Fact]
    public async Task PdfGeneration_WithFullOutline()
    {
        var card = await _scryfall.GetCardByNameAsync("Swamp");
        Assert.NotNull(card);
        var artPath = await _scryfall.DownloadAndCacheImageAsync(card!);

        var project = new ProjectModel { ProjectName = "Outline Full Test" };
        var model = card!.ToCardModel(artPath ?? "", null);
        model.Quantity = 4;
        project.Cards.Add(model);

        project.PrintSettings.ShowCardOutline = true;
        project.PrintSettings.OutlineType = OutlineType.Full;
        project.PrintSettings.OutlineColor = "#66FF00";
        project.PrintSettings.CornerRadiusMm = 3f;
        project.PrintSettings.OutlineAlignment = OutlineAlignment.Center;
        project.PrintSettings.OutlineLineType = LineType.Solid;
        project.PrintSettings.LineWeight = 2f;

        string pdfPath = Path.Combine(_testOutputDir, "outline_full.pdf");
        bool success = await _pdfGenerator.GeneratePdfAsync(project, pdfPath);

        Assert.True(success);
        Assert.True(File.Exists(pdfPath));
        Assert.True(new FileInfo(pdfPath).Length > 1000);
    }

    [Fact]
    public async Task PdfGeneration_WithCornerOutline()
    {
        var card = await _scryfall.GetCardByNameAsync("Forest");
        Assert.NotNull(card);
        var artPath = await _scryfall.DownloadAndCacheImageAsync(card!);

        var project = new ProjectModel { ProjectName = "Outline Corners Test" };
        var model = card!.ToCardModel(artPath ?? "", null);
        model.Quantity = 2;
        project.Cards.Add(model);

        project.PrintSettings.ShowCardOutline = true;
        project.PrintSettings.OutlineType = OutlineType.Corners;
        project.PrintSettings.OutlineColor = "#FF0000";
        project.PrintSettings.CornerRadiusMm = 3f;
        project.PrintSettings.OutlineAlignment = OutlineAlignment.Outside;
        project.PrintSettings.CornerLengthMm = 5f;
        project.PrintSettings.LineWeight = 2f;

        string pdfPath = Path.Combine(_testOutputDir, "outline_corners.pdf");
        bool success = await _pdfGenerator.GeneratePdfAsync(project, pdfPath);

        Assert.True(success);
        Assert.True(File.Exists(pdfPath));
    }

    [Fact]
    public async Task PdfGeneration_WithSharpCorners()
    {
        var card = await _scryfall.GetCardByNameAsync("Mountain");
        Assert.NotNull(card);
        var artPath = await _scryfall.DownloadAndCacheImageAsync(card!);

        var project = new ProjectModel { ProjectName = "Sharp Corners Test" };
        var model = card!.ToCardModel(artPath ?? "", null);
        model.Quantity = 1;
        project.Cards.Add(model);

        project.PrintSettings.ShowCardOutline = true;
        project.PrintSettings.OutlineType = OutlineType.Corners;
        project.PrintSettings.CornerRadiusMm = 0f; // sharp corners
        project.PrintSettings.OutlineAlignment = OutlineAlignment.Inside;
        project.PrintSettings.OutlineLineType = LineType.Dashed;
        project.PrintSettings.CornerLengthMm = 8f;
        project.PrintSettings.LineWeight = 1f;

        string pdfPath = Path.Combine(_testOutputDir, "outline_sharp.pdf");
        bool success = await _pdfGenerator.GeneratePdfAsync(project, pdfPath);

        Assert.True(success);
        Assert.True(File.Exists(pdfPath));
    }

    [Fact]
    public async Task PdfGeneration_OutlineDisabled()
    {
        var card = await _scryfall.GetCardByNameAsync("Plains");
        Assert.NotNull(card);
        var artPath = await _scryfall.DownloadAndCacheImageAsync(card!);

        var project = new ProjectModel { ProjectName = "No Outline Test" };
        var model = card!.ToCardModel(artPath ?? "", null);
        model.Quantity = 1;
        project.Cards.Add(model);

        project.PrintSettings.ShowCardOutline = false;

        string pdfPath = Path.Combine(_testOutputDir, "no_outline.pdf");
        bool success = await _pdfGenerator.GeneratePdfAsync(project, pdfPath);

        Assert.True(success);
        Assert.True(File.Exists(pdfPath));
    }

    [Fact]
    public async Task PdfGeneration_OutlineWithDashedFullAndAllAlignments()
    {
        var card = await _scryfall.GetCardByNameAsync("Island");
        Assert.NotNull(card);
        var artPath = await _scryfall.DownloadAndCacheImageAsync(card!);

        foreach (var alignment in Enum.GetValues<OutlineAlignment>())
        {
            var project = new ProjectModel { ProjectName = $"Outline {alignment}" };
            var model = card!.ToCardModel(artPath ?? "", null);
            model.Quantity = 1;
            project.Cards.Add(model);

            project.PrintSettings.ShowCardOutline = true;
            project.PrintSettings.OutlineType = OutlineType.Full;
            project.PrintSettings.OutlineAlignment = alignment;
            project.PrintSettings.OutlineLineType = LineType.Dashed;
            project.PrintSettings.CornerRadiusMm = 2f;
            project.PrintSettings.LineWeight = 3f;

            string pdfPath = Path.Combine(_testOutputDir, $"outline_{alignment}.pdf");
            bool success = await _pdfGenerator.GeneratePdfAsync(project, pdfPath);

            Assert.True(success, $"Failed for alignment {alignment}");
            Assert.True(File.Exists(pdfPath));
        }
    }

    [Fact]
    public async Task SaveLoad_PreservesOutlineSettings()
    {
        var card = await _scryfall.GetCardByNameAsync("Mountain");
        Assert.NotNull(card);
        var artPath = await _scryfall.DownloadAndCacheImageAsync(card!);

        var project = new ProjectModel { ProjectName = "Outline Persist Test" };
        project.Cards.Add(card!.ToCardModel(artPath ?? "", null));

        project.PrintSettings.ShowCardOutline = true;
        project.PrintSettings.OutlineColor = "#FF00AA";
        project.PrintSettings.OutlineAlignment = OutlineAlignment.Inside;
        project.PrintSettings.CornerRadiusMm = 5f;
        project.PrintSettings.OutlineType = OutlineType.Corners;
        project.PrintSettings.OutlineLineType = LineType.Dashed;
        project.PrintSettings.CornerLengthMm = 7f;
        project.PrintSettings.LineWeight = 4f;

        string projPath = Path.Combine(_testOutputDir, "outline_persist.mtgproj");
        bool saved = await _serializer.SaveProjectAsync(project, projPath);
        Assert.True(saved);

        var loaded = await _serializer.LoadProjectAsync(projPath);
        Assert.NotNull(loaded);

        var ps = loaded!.PrintSettings;
        Assert.True(ps.ShowCardOutline);
        Assert.Equal("#FF00AA", ps.OutlineColor);
        Assert.Equal(OutlineAlignment.Inside, ps.OutlineAlignment);
        Assert.Equal(5f, ps.CornerRadiusMm);
        Assert.Equal(OutlineType.Corners, ps.OutlineType);
        Assert.Equal(LineType.Dashed, ps.OutlineLineType);
        Assert.Equal(7f, ps.CornerLengthMm);
        Assert.Equal(4f, ps.LineWeight);
    }
}
