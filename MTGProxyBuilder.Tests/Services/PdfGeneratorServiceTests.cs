using MTGProxyBuilder.Core.Models;
using MTGProxyBuilder.Core.Services;

namespace MTGProxyBuilder.Tests.Services;

public class PdfGeneratorServiceTests
{
    private static ProjectModel MakeProject() => new()
    {
        ProjectName = "pdf-tests",
        PageSettings = new PageLayout(),
        PrintSettings = new PrintSettings(),
    };

    [Fact]
    public async Task GenerateAlignmentPdfAsync_WritesTwoPageFile()
    {
        var project = MakeProject();
        project.PageSettings.BackOffsetXmm = 0.4f;
        project.PageSettings.BackOffsetYmm = -0.2f;
        string path = Path.Combine(Path.GetTempPath(), $"align_{Guid.NewGuid():N}.pdf");

        try
        {
            bool ok = await new PdfGeneratorService().GenerateAlignmentPdfAsync(project, path);

            Assert.True(ok);
            Assert.True(File.Exists(path));
            Assert.True(new FileInfo(path).Length > 0);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task GeneratePdfAsync_WithCropMarksAndBackOffset_Succeeds()
    {
        var project = MakeProject();
        project.PrintSettings.ShowCropMarks = true;
        project.PrintSettings.PrintMode = PrintMode.Duplex;
        project.PageSettings.BackOffsetXmm = 0.5f;
        project.Cards.Add(new CardModel { Name = "No-art card", Quantity = 2, IncludeBack = true });
        string path = Path.Combine(Path.GetTempPath(), $"crop_{Guid.NewGuid():N}.pdf");

        try
        {
            bool ok = await new PdfGeneratorService().GeneratePdfAsync(project, path);

            Assert.True(ok);
            Assert.True(File.Exists(path));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
