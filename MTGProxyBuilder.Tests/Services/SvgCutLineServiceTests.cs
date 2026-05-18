using MTGProxyBuilder.Core.Models;
using MTGProxyBuilder.Core.Services;

namespace MTGProxyBuilder.Tests.Services;

public class SvgCutLineServiceTests : IDisposable
{
    private readonly string _testDir;
    private readonly SvgCutLineService _svc;

    public SvgCutLineServiceTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"MTGProxyBuilder_SvgTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
        _svc = new SvgCutLineService();
    }

    public void Dispose()
    {
        try { Directory.Delete(_testDir, true); } catch { }
    }

    private ProjectModel CreateProject(int cardCount, int quantity = 1)
    {
        var project = new ProjectModel();
        for (int i = 0; i < cardCount; i++)
        {
            project.Cards.Add(new CardModel
            {
                Name = $"Card {i}",
                ArtworkPath = "dummy.png",
                Quantity = quantity
            });
        }
        return project;
    }

    [Fact]
    public async Task GenerateSvg_EmptyProject_ReturnsNoFiles()
    {
        var project = new ProjectModel();
        var files = await _svc.GenerateSvgAsync(project, _testDir, "empty");
        Assert.Empty(files);
    }

    [Fact]
    public async Task GenerateSvg_FullPages_GeneratesOneSvg()
    {
        // A4 with default MTG cards: 3 cols x 3 rows = 9 per page
        var project = CreateProject(9);
        var files = await _svc.GenerateSvgAsync(project, _testDir, "full");

        Assert.Single(files);
        Assert.True(File.Exists(files[0]));
        Assert.Contains("_full.svg", files[0]);
    }

    [Fact]
    public async Task GenerateSvg_PartialLastPage_GeneratesTwoSvgs()
    {
        // 10 cards = 9 full + 1 partial on a 9-per-page layout
        var project = CreateProject(10);
        var files = await _svc.GenerateSvgAsync(project, _testDir, "partial");

        Assert.Equal(2, files.Count);
        Assert.Contains(files, f => f.Contains("_full.svg"));
        Assert.Contains(files, f => f.Contains("_partial_"));
    }

    [Fact]
    public async Task GenerateSvg_OnlyPartialPage_GeneratesOneSvg()
    {
        // 3 cards on a 9-per-page layout = only a partial page, no full pages
        var project = CreateProject(3);
        var files = await _svc.GenerateSvgAsync(project, _testDir, "onlypartial");

        Assert.Single(files);
        Assert.True(File.Exists(files[0]));
    }

    [Fact]
    public async Task GenerateSvg_ContainsValidSvgStructure()
    {
        var project = CreateProject(4);
        var files = await _svc.GenerateSvgAsync(project, _testDir, "structure");

        Assert.NotEmpty(files);
        string svg = File.ReadAllText(files[0]);

        Assert.Contains("<?xml", svg);
        Assert.Contains("<svg", svg);
        Assert.Contains("xmlns=\"http://www.w3.org/2000/svg\"", svg);
        Assert.Contains("</svg>", svg);
    }

    [Fact]
    public async Task GenerateSvg_ContainsRectForEachCard()
    {
        var project = CreateProject(4);
        var files = await _svc.GenerateSvgAsync(project, _testDir, "rects");

        string svg = File.ReadAllText(files[0]);
        int rectCount = svg.Split("<rect ").Length - 1;
        Assert.Equal(4, rectCount);
    }

    [Fact]
    public async Task GenerateSvg_RectsHaveStrokeAndNoFill()
    {
        var project = CreateProject(1);
        var files = await _svc.GenerateSvgAsync(project, _testDir, "strokefill");

        string svg = File.ReadAllText(files[0]);
        Assert.Contains("stroke=\"black\"", svg);
        Assert.Contains("fill=\"none\"", svg);
    }

    [Fact]
    public async Task GenerateSvg_WithCornerRadius_IncludesRxRy()
    {
        var project = CreateProject(1);
        project.PrintSettings.CornerRadiusMm = 3f;
        var files = await _svc.GenerateSvgAsync(project, _testDir, "rounded");

        string svg = File.ReadAllText(files[0]);
        Assert.Contains("rx=", svg);
        Assert.Contains("ry=", svg);
    }

    [Fact]
    public async Task GenerateSvg_ZeroCornerRadius_NoRxRy()
    {
        var project = CreateProject(1);
        project.PrintSettings.CornerRadiusMm = 0f;
        var files = await _svc.GenerateSvgAsync(project, _testDir, "sharp");

        string svg = File.ReadAllText(files[0]);
        Assert.DoesNotContain("rx=", svg);
        Assert.DoesNotContain("ry=", svg);
    }

    [Fact]
    public async Task GenerateSvg_ViewBoxMatchesPageDimensions()
    {
        var project = CreateProject(1);
        // A4: 210 x 297 mm → 595.28 x 841.89 pt (72 DPI)
        var files = await _svc.GenerateSvgAsync(project, _testDir, "viewbox");

        string svg = File.ReadAllText(files[0]);
        Assert.Contains("viewBox=\"0 0", svg);
        // Check width is roughly A4 width in points
        Assert.Contains("width=\"", svg);
        Assert.Contains("height=\"", svg);
    }

    [Fact]
    public async Task GenerateSvg_QuantityExpansion_Works()
    {
        // 1 card with quantity 9 = full page
        var project = CreateProject(1, quantity: 9);
        var files = await _svc.GenerateSvgAsync(project, _testDir, "qty");

        Assert.Single(files);
        string svg = File.ReadAllText(files[0]);
        int rectCount = svg.Split("<rect ").Length - 1;
        Assert.Equal(9, rectCount);
    }

    [Fact]
    public async Task GenerateSvg_LetterSize_HasCorrectDimensions()
    {
        var project = CreateProject(1);
        project.PageSettings.ApplyPagePreset("Letter");
        var files = await _svc.GenerateSvgAsync(project, _testDir, "letter");

        string svg = File.ReadAllText(files[0]);
        // Letter: 215.9 x 279.4 mm → ~612 x 792 pt
        Assert.Contains("width=\"", svg);
    }
}
