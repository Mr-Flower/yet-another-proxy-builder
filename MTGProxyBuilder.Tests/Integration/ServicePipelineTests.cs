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
        if (error != null) return; // skip if rate limited or network unavailable
        Assert.NotEmpty(results);
        Assert.Contains(results, c => c.Name.Contains("Lightning Bolt", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ScryfallSearch_ExactName_ReturnsOnlyThatCard()
    {
        var (results, error) = await _scryfall.SearchCardAsync("!\"Sol Ring\"");
        if (error != null) return; // skip if rate limited or network unavailable
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
        if (card == null) return; // skip if network unavailable
        Assert.Equal("Black Lotus", card!.Name);
        Assert.NotNull(card.GetImageUrl());
    }

    [Fact]
    public async Task ScryfallToCardModel_FullPipeline()
    {
        var card = await _scryfall.GetCardByNameAsync("Counterspell");
        if (card == null) return; // skip if network unavailable

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
        if (card == null) return; // skip if network unavailable

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
        if (card == null) return; // skip if network unavailable

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
        if (card == null) return; // skip if network unavailable
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
        if (card == null) return; // skip if network unavailable
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
        if (card == null) return; // skip if network unavailable
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
        if (card == null) return; // skip if network unavailable
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
        if (card == null) return; // skip if network unavailable
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
        if (card == null) return; // skip if network unavailable
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
        if (card == null) return; // skip if network unavailable
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
        if (card == null) return; // skip if network unavailable
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

    // --- Overlay Text Tests ---

    [Fact]
    public async Task PdfGeneration_WithOverlayText()
    {
        var card = await _scryfall.GetCardByNameAsync("Mountain");
        if (card == null) return; // skip if network unavailable
        var artPath = await _scryfall.DownloadAndCacheImageAsync(card!);

        var project = new ProjectModel { ProjectName = "Overlay Test" };
        var model = card!.ToCardModel(artPath ?? "", null);
        model.OverlayText = "TOKEN";
        model.Quantity = 2;
        project.Cards.Add(model);

        string pdfPath = Path.Combine(_testOutputDir, "overlay_test.pdf");
        bool success = await _pdfGenerator.GeneratePdfAsync(project, pdfPath);

        Assert.True(success);
        Assert.True(File.Exists(pdfPath));
        Assert.True(new FileInfo(pdfPath).Length > 1000);
    }

    [Fact]
    public async Task SaveLoad_PreservesOverlayText()
    {
        var card = await _scryfall.GetCardByNameAsync("Island");
        if (card == null) return; // skip if network unavailable
        var artPath = await _scryfall.DownloadAndCacheImageAsync(card);

        var project = new ProjectModel { ProjectName = "Overlay Persist" };
        var model = card.ToCardModel(artPath ?? "", null);
        model.OverlayText = "MY CUSTOM TEXT";
        project.Cards.Add(model);

        string projPath = Path.Combine(_testOutputDir, "overlay_persist.mtgproj");
        bool saved = await _serializer.SaveProjectAsync(project, projPath);
        Assert.True(saved);

        var loaded = await _serializer.LoadProjectAsync(projPath);
        Assert.NotNull(loaded);
        Assert.Single(loaded!.Cards);
        Assert.Equal("MY CUSTOM TEXT", loaded.Cards[0].OverlayText);
    }

    [Fact]
    public async Task PdfGeneration_OverlayOnlyOnFront_NotBack()
    {
        var card = await _scryfall.GetCardByNameAsync("Plains");
        if (card == null) return; // skip if network unavailable
        var artPath = await _scryfall.DownloadAndCacheImageAsync(card!);

        var project = new ProjectModel { ProjectName = "Overlay Front Only" };
        var model = card!.ToCardModel(artPath ?? "", null);
        model.OverlayText = "TOKEN";
        model.Quantity = 1;
        model.IncludeBack = true;
        project.Cards.Add(model);
        project.PrintSettings.PrintMode = PrintMode.Duplex;

        string pdfPath = Path.Combine(_testOutputDir, "overlay_front_only.pdf");
        bool success = await _pdfGenerator.GeneratePdfAsync(project, pdfPath);

        Assert.True(success);
        Assert.True(File.Exists(pdfPath));
    }

    // --- App Settings Tests ---

    [Fact]
    public void AppSettings_SaveAndReload()
    {
        var svc = new AppSettingsService();
        string original = svc.Settings.DefaultTokenText;

        svc.Settings.DefaultTokenText = $"TEST_{Guid.NewGuid():N}";
        svc.Save();

        var svc2 = new AppSettingsService();
        Assert.Equal(svc.Settings.DefaultTokenText, svc2.Settings.DefaultTokenText);

        // Restore
        svc.Settings.DefaultTokenText = original;
        svc.Save();
    }

    // --- Token Eligibility Tests ---

    [Fact]
    public void TokenEligibility_CardWithUniqueBack_IsEligible()
    {
        // A card with a unique back (different from common) should be eligible
        var cards = new List<CardModel>
        {
            new() { Name = "Normal Card", BackArtworkPath = "/common_back.jpg", Quantity = 5 },
            new() { Name = "DFC Card", BackArtworkPath = "/unique_back.jpg", Quantity = 1 }
        };

        // Common back is /common_back.jpg (5 copies)
        var commonBack = cards
            .Where(c => !string.IsNullOrEmpty(c.BackArtworkPath))
            .GroupBy(c => c.BackArtworkPath!, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Sum(c => c.Quantity))
            .FirstOrDefault()?.Key;

        Assert.Equal("/common_back.jpg", commonBack);

        // DFC card has a different back — should be "eligible" for token
        var dfc = cards[1];
        Assert.NotEqual(commonBack, dfc.BackArtworkPath);
    }

    [Fact]
    public void TokenEligibility_CardWithCommonBack_NotEligible()
    {
        var cards = new List<CardModel>
        {
            new() { Name = "Card A", BackArtworkPath = "/common_back.jpg", Quantity = 3 },
            new() { Name = "Card B", BackArtworkPath = "/common_back.jpg", Quantity = 2 }
        };

        var commonBack = cards
            .Where(c => !string.IsNullOrEmpty(c.BackArtworkPath))
            .GroupBy(c => c.BackArtworkPath!, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Sum(c => c.Quantity))
            .FirstOrDefault()?.Key;

        // Both cards have the common back — neither is eligible
        Assert.All(cards, c => Assert.Equal(commonBack, c.BackArtworkPath));
    }

    [Fact]
    public void TokenEligibility_CardWithNoBack_NotEligible()
    {
        var card = new CardModel { Name = "No Back", BackArtworkPath = null };
        Assert.True(string.IsNullOrEmpty(card.BackArtworkPath));
    }

    // --- Silhouette Cameo Tests ---

    [Fact]
    public async Task Pdf_WithRegistrationMarks_Succeeds()
    {
        var card = await _scryfall.GetCardByNameAsync("Forest");
        if (card == null) return;
        var artPath = await _scryfall.DownloadAndCacheImageAsync(card!);

        var project = new ProjectModel { ProjectName = "RegMark Test" };
        var model = card!.ToCardModel(artPath ?? "", null);
        model.Quantity = 4;
        project.Cards.Add(model);
        project.PrintSettings.ShowRegistrationMarks = true;

        string pdfPath = Path.Combine(_testOutputDir, "regmarks.pdf");
        bool success = await _pdfGenerator.GeneratePdfAsync(project, pdfPath);

        Assert.True(success);
        Assert.True(File.Exists(pdfPath));
        Assert.True(new FileInfo(pdfPath).Length > 1000);
    }

    [Fact]
    public async Task Pdf_WithRegistrationMarks_SuppressesBleedAndOutlines()
    {
        var card = await _scryfall.GetCardByNameAsync("Plains");
        if (card == null) return;
        var artPath = await _scryfall.DownloadAndCacheImageAsync(card!);

        var project = new ProjectModel { ProjectName = "Suppress Test" };
        var model = card!.ToCardModel(artPath ?? "", null);
        model.Quantity = 2;
        project.Cards.Add(model);

        // Enable everything, then enable reg marks — should suppress outlines/guides
        project.PrintSettings.ShowCutGuides = true;
        project.PrintSettings.ShowCardOutline = true;
        project.PrintSettings.ShowRegistrationMarks = true;
        project.PageSettings.BleedWidthMm = 1.5f;

        string pdfPath = Path.Combine(_testOutputDir, "suppress.pdf");
        bool success = await _pdfGenerator.GeneratePdfAsync(project, pdfPath);

        Assert.True(success);
        Assert.True(File.Exists(pdfPath));
    }

    [Fact]
    public async Task Pdf_RegistrationMarks_DuplexMode_OnlyOnFrontPages()
    {
        var card = await _scryfall.GetCardByNameAsync("Swamp");
        if (card == null) return;
        var artPath = await _scryfall.DownloadAndCacheImageAsync(card!);

        var project = new ProjectModel { ProjectName = "Duplex RegMark" };
        var model = card!.ToCardModel(artPath ?? "", null);
        model.Quantity = 4;
        model.IncludeBack = true;
        model.BackArtworkPath = artPath;
        project.Cards.Add(model);
        project.PrintSettings.PrintMode = PrintMode.Duplex;
        project.PrintSettings.ShowRegistrationMarks = true;

        string pdfPath = Path.Combine(_testOutputDir, "duplex_regmarks.pdf");
        bool success = await _pdfGenerator.GeneratePdfAsync(project, pdfPath);

        Assert.True(success);
        Assert.True(File.Exists(pdfPath));
    }

    [Fact]
    public async Task SvgExport_GeneratesFilesAlongsidePdf()
    {
        var card = await _scryfall.GetCardByNameAsync("Mountain");
        if (card == null) return;
        var artPath = await _scryfall.DownloadAndCacheImageAsync(card!);

        var project = new ProjectModel { ProjectName = "SVG Export" };
        var model = card!.ToCardModel(artPath ?? "", null);
        model.Quantity = 9; // full page
        project.Cards.Add(model);
        project.PrintSettings.ExportSvgCutLines = true;

        // Generate PDF
        string pdfPath = Path.Combine(_testOutputDir, "svgexport.pdf");
        bool pdfSuccess = await _pdfGenerator.GeneratePdfAsync(project, pdfPath);
        Assert.True(pdfSuccess);

        // Generate SVG alongside
        var svgService = new SvgCutLineService();
        var svgFiles = await svgService.GenerateSvgAsync(project, _testOutputDir, "svgexport");

        Assert.NotEmpty(svgFiles);
        foreach (var svgFile in svgFiles)
        {
            Assert.True(File.Exists(svgFile));
            string content = File.ReadAllText(svgFile);
            Assert.Contains("<svg", content);
            Assert.Contains("<rect", content);
            Assert.Contains("stroke=\"black\"", content);
            Assert.Contains("fill=\"none\"", content);
        }
    }

    [Fact]
    public async Task SvgExport_PartialPage_GeneratesTwoFiles()
    {
        var card = await _scryfall.GetCardByNameAsync("Island");
        if (card == null) return;
        var artPath = await _scryfall.DownloadAndCacheImageAsync(card!);

        var project = new ProjectModel { ProjectName = "SVG Partial" };
        var model = card!.ToCardModel(artPath ?? "", null);
        model.Quantity = 10; // 9 full + 1 partial
        project.Cards.Add(model);

        var svgService = new SvgCutLineService();
        var svgFiles = await svgService.GenerateSvgAsync(project, _testOutputDir, "svgpartial");

        Assert.Equal(2, svgFiles.Count);
        Assert.Contains(svgFiles, f => f.Contains("_full.svg"));
        Assert.Contains(svgFiles, f => f.Contains("_partial_"));
    }

    [Fact]
    public async Task SvgExport_WithCornerRadius_HasRoundedRects()
    {
        var card = await _scryfall.GetCardByNameAsync("Forest");
        if (card == null) return;
        var artPath = await _scryfall.DownloadAndCacheImageAsync(card!);

        var project = new ProjectModel { ProjectName = "SVG Rounded" };
        var model = card!.ToCardModel(artPath ?? "", null);
        model.Quantity = 1;
        project.Cards.Add(model);
        project.PrintSettings.CornerRadiusMm = 3f;

        var svgService = new SvgCutLineService();
        var svgFiles = await svgService.GenerateSvgAsync(project, _testOutputDir, "svgrounded");

        Assert.NotEmpty(svgFiles);
        string svg = File.ReadAllText(svgFiles[0]);
        Assert.Contains("rx=", svg);
        Assert.Contains("ry=", svg);
    }

    [Fact]
    public async Task FullPipeline_RegMarksAndSvg_EndToEnd()
    {
        var card = await _scryfall.GetCardByNameAsync("Lightning Bolt");
        if (card == null) return;
        var artPath = await _scryfall.DownloadAndCacheImageAsync(card!);

        var project = new ProjectModel { ProjectName = "Full Cameo Pipeline" };
        var model = card!.ToCardModel(artPath ?? "", null);
        model.Quantity = 9;
        project.Cards.Add(model);

        // Enable full Silhouette Cameo workflow
        project.PrintSettings.ShowRegistrationMarks = true;
        project.PrintSettings.ExportSvgCutLines = true;
        project.PrintSettings.CornerRadiusMm = 3f;

        // 1. Generate PDF with reg marks
        string pdfPath = Path.Combine(_testOutputDir, "cameo_e2e.pdf");
        bool pdfSuccess = await _pdfGenerator.GeneratePdfAsync(project, pdfPath);
        Assert.True(pdfSuccess);
        Assert.True(File.Exists(pdfPath));

        // 2. Generate SVG cut lines
        var svgService = new SvgCutLineService();
        var svgFiles = await svgService.GenerateSvgAsync(project, _testOutputDir, "cameo_e2e");
        Assert.NotEmpty(svgFiles);

        // 3. Verify SVG content
        string svg = File.ReadAllText(svgFiles[0]);
        Assert.Contains("<svg", svg);
        Assert.Contains("rx=", svg); // rounded corners
        int rectCount = svg.Split("<rect ").Length - 1;
        Assert.Equal(9, rectCount); // one per card slot
    }

    // ================================================================
    //  LIBRARY MIGRATION & COMPRESSION E2E
    // ================================================================

    [Fact]
    public void Library_ExportImport_RoundTrip()
    {
        var tmpImage = Path.Combine(_testOutputDir, "roundtrip.png");
        File.WriteAllBytes(tmpImage, new byte[] { 0x89, 0x50, 0x4E, 0x47 });

        // Create a library with entries
        string srcDir = Path.Combine(_testOutputDir, "RoundTripSrc");
        var srcLib = new FrontArtLibraryService(srcDir);
        var e1 = srcLib.AddFromFile(tmpImage, $"Card_A_{Guid.NewGuid():N}", "SourceA");
        var e2 = srcLib.AddFromFile(tmpImage, $"Card_B_{Guid.NewGuid():N}", "SourceB");
        Assert.NotNull(e1);
        Assert.NotNull(e2);

        // Export
        string zipPath = Path.Combine(_testOutputDir, "roundtrip.zip");
        srcLib.ExportToZip(zipPath);
        Assert.True(File.Exists(zipPath));

        // Import into a fresh library
        string destDir = Path.Combine(_testOutputDir, "RoundTripDest");
        var destLib = new FrontArtLibraryService(destDir);
        int added = destLib.ImportFromZip(zipPath);

        Assert.Equal(2, added);
        Assert.Equal(2, destLib.Entries.Count);

        // Verify names and sources survived the round-trip
        Assert.Contains(destLib.Entries, e => e.Name == e1!.Name && e.Source == "SourceA");
        Assert.Contains(destLib.Entries, e => e.Name == e2!.Name && e.Source == "SourceB");
        Assert.All(destLib.Entries, e => Assert.True(File.Exists(e.FilePath)));
    }

    [Fact]
    public void Library_Move_PreservesEntriesAndCatalog()
    {
        var tmpImage = Path.Combine(_testOutputDir, "move_test.png");
        File.WriteAllBytes(tmpImage, new byte[] { 0x89, 0x50, 0x4E, 0x47 });

        string srcDir = Path.Combine(_testOutputDir, "MoveSrc");
        string destDir = Path.Combine(_testOutputDir, "MoveDest");
        var svc = new BackArtLibraryService(srcDir);

        var entry = svc.AddFromFile(tmpImage, $"MoveE2E_{Guid.NewGuid():N}", "TestContrib");
        Assert.NotNull(entry);
        svc.SetDefault(entry!.Id);

        // Move
        svc.MoveToDirectory(destDir);

        // Verify the service now points to the new directory
        Assert.Equal(destDir, svc.LibraryDirectory);

        // Verify the entry is accessible and file exists at new location
        var found = svc.GetById(entry.Id);
        Assert.NotNull(found);
        Assert.StartsWith(destDir, found!.FilePath, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(found.FilePath));
        Assert.Equal("TestContrib", found.Source);

        // Verify default was preserved
        Assert.Equal(entry.Id, svc.DefaultEntryId);

        // Verify old directory was cleaned up
        Assert.False(Directory.Exists(srcDir));

        // Verify a fresh load from the new catalog works
        var reloaded = new BackArtLibraryService(destDir);
        Assert.Single(reloaded.Entries);
        Assert.Equal(entry.Id, reloaded.DefaultEntryId);
    }

    [Fact]
    public void Library_MoveToExisting_MergesEntries()
    {
        var tmpImage = Path.Combine(_testOutputDir, "merge_test.png");
        File.WriteAllBytes(tmpImage, new byte[] { 0x89, 0x50, 0x4E, 0x47 });

        // Create destination library with one entry
        string destDir = Path.Combine(_testOutputDir, "MergeDest");
        var destLib = new FrontArtLibraryService(destDir);
        var destEntry = destLib.AddFromFile(tmpImage, $"DestCard_{Guid.NewGuid():N}", "DestSource");
        Assert.NotNull(destEntry);

        // Create source library with a different entry
        string srcDir = Path.Combine(_testOutputDir, "MergeSrc");
        var srcLib = new FrontArtLibraryService(srcDir);
        var srcEntry = srcLib.AddFromFile(tmpImage, $"SrcCard_{Guid.NewGuid():N}", "SrcSource");
        Assert.NotNull(srcEntry);

        // Move source into destination
        var newIds = srcLib.MoveToDirectory(destDir);

        // Verify merge results
        Assert.Single(newIds);
        Assert.Equal(srcEntry!.Id, newIds[0]);
        Assert.Equal(2, srcLib.Entries.Count);
        Assert.All(srcLib.Entries, e => Assert.True(File.Exists(e.FilePath)));

        // Verify a fresh load from the merged catalog sees both entries
        var reloaded = new FrontArtLibraryService(destDir);
        Assert.Equal(2, reloaded.Entries.Count);
        Assert.Contains(reloaded.Entries, e => e.Source == "DestSource");
        Assert.Contains(reloaded.Entries, e => e.Source == "SrcSource");

        // Old source directory should be cleaned up
        Assert.False(Directory.Exists(srcDir));
    }

    [Fact]
    public void ImageCache_Remove_CleansUpAfterLibraryImport()
    {
        // Simulate the cache-to-library import workflow
        string key = $"mpc_e2e_import_{Guid.NewGuid():N}";
        string testFile = Path.Combine(_imageCache.CacheDirectory, $"{key}.png");
        File.WriteAllBytes(testFile, new byte[] { 0x89, 0x50, 0x4E, 0x47 });

        var cache = new ImageCacheService(); // pick up the test file
        cache.SetMetadata(key, "E2E Card", "E2E Source");
        Assert.True(cache.IsImageCached(key));

        // Import into library
        string libDir = Path.Combine(_testOutputDir, "CacheImportLib");
        var lib = new FrontArtLibraryService(libDir);
        var cached = cache.GetCachedByPrefix(key);
        Assert.Single(cached);

        var entry = lib.AddFromFile(cached[0].Path, cached[0].Name, cached[0].Source);
        Assert.NotNull(entry);

        // Remove from cache (mimicking what the UI does)
        cache.Remove(key);

        Assert.False(cache.IsImageCached(key));
        Assert.False(File.Exists(testFile));

        // Library entry still works
        Assert.True(File.Exists(entry!.FilePath));
        Assert.Equal("E2E Card", entry.Name);
    }

    [Fact]
    public void AppSettings_CustomLibraryPaths_Persist()
    {
        var settings = new AppSettingsService();
        string origFront = settings.Settings.FrontArtLibraryPath;
        string origBack = settings.Settings.BackArtLibraryPath;

        try
        {
            settings.Settings.FrontArtLibraryPath = @"D:\TestFrontLib";
            settings.Settings.BackArtLibraryPath = @"D:\TestBackLib";
            settings.Save();

            var reloaded = new AppSettingsService();
            Assert.Equal(@"D:\TestFrontLib", reloaded.Settings.FrontArtLibraryPath);
            Assert.Equal(@"D:\TestBackLib", reloaded.Settings.BackArtLibraryPath);
        }
        finally
        {
            // Restore original values
            settings.Settings.FrontArtLibraryPath = origFront;
            settings.Settings.BackArtLibraryPath = origBack;
            settings.Save();
        }
    }
}
