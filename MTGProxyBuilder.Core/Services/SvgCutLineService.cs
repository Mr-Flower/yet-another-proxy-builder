using System.Globalization;
using System.Text;
using MTGProxyBuilder.Core.Models;

namespace MTGProxyBuilder.Core.Services
{
    public class SvgCutLineService
    {
        private const float MmToPt = 72f / 25.4f;

        /// <summary>
        /// Generates SVG cut line files for unique page layouts.
        /// Returns the list of generated file paths.
        /// </summary>
        public Task<List<string>> GenerateSvgAsync(ProjectModel project, string outputDirectory, string baseName)
        {
            return Task.Run(() =>
            {
                var settings = project.PageSettings;
                var printSettings = project.PrintSettings;
                var generatedFiles = new List<string>();

                int perPage = settings.CardsPerPage;
                if (perPage <= 0) return generatedFiles;

                // Determine total cards for front pages
                int totalCards = project.Cards.Sum(c => c.Quantity);
                if (totalCards == 0) return generatedFiles;

                int fullPages = totalCards / perPage;
                int remainder = totalCards % perPage;

                // Generate SVG for a full page (all slots filled)
                if (fullPages > 0 || remainder == 0)
                {
                    int cardCount = remainder == 0 && fullPages > 0 ? perPage : (fullPages > 0 ? perPage : remainder);
                    if (fullPages > 0)
                    {
                        string fullPath = Path.Combine(outputDirectory, $"{baseName}_full.svg");
                        string svg = BuildSvg(settings, printSettings, perPage);
                        File.WriteAllText(fullPath, svg);
                        generatedFiles.Add(fullPath);
                    }
                }

                // Generate SVG for the partial last page (if different from full)
                if (remainder > 0)
                {
                    string partialPath = fullPages > 0
                        ? Path.Combine(outputDirectory, $"{baseName}_partial_{remainder}.svg")
                        : Path.Combine(outputDirectory, $"{baseName}_full.svg");

                    // Only generate if we haven't already generated a file with this card count
                    if (fullPages > 0 || !generatedFiles.Any())
                    {
                        string svg = BuildSvg(settings, printSettings, remainder);
                        File.WriteAllText(partialPath, svg);
                        generatedFiles.Add(partialPath);
                    }
                }

                return generatedFiles;
            });
        }

        private string BuildSvg(PageLayout settings, PrintSettings printSettings, int cardCount)
        {
            float pageW = settings.PageWidthMm * MmToPt;
            float pageH = settings.PageHeightMm * MmToPt;
            float startX = settings.MarginLeftMm * MmToPt;
            float startY = settings.MarginTopMm * MmToPt;
            float bleedPt = settings.BleedWidthMm * MmToPt;
            float cardWPt = settings.CardWidthMm * MmToPt;
            float cardHPt = settings.CardHeightMm * MmToPt;
            float cellW = cardWPt + 2 * bleedPt;
            float cellH = cardHPt + 2 * bleedPt;
            int cols = settings.CardsPerRow;
            float radiusPt = printSettings.CornerRadiusMm * MmToPt;

            var sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            sb.AppendLine(FormattableString.Invariant(
                $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{pageW:F2}\" height=\"{pageH:F2}\" viewBox=\"0 0 {pageW:F2} {pageH:F2}\">"));

            // Cut lines for each card position
            for (int i = 0; i < cardCount; i++)
            {
                int row = i / cols;
                int col = i % cols;

                float cellX = startX + col * cellW;
                float cellY = startY + row * cellH;

                // Card boundary (inside the bleed area)
                float cardX = cellX + bleedPt;
                float cardY = cellY + bleedPt;

                if (radiusPt > 0)
                {
                    sb.AppendLine(FormattableString.Invariant(
                        $"  <rect x=\"{cardX:F2}\" y=\"{cardY:F2}\" width=\"{cardWPt:F2}\" height=\"{cardHPt:F2}\" rx=\"{radiusPt:F2}\" ry=\"{radiusPt:F2}\" stroke=\"black\" stroke-width=\"1\" fill=\"none\"/>"));
                }
                else
                {
                    sb.AppendLine(FormattableString.Invariant(
                        $"  <rect x=\"{cardX:F2}\" y=\"{cardY:F2}\" width=\"{cardWPt:F2}\" height=\"{cardHPt:F2}\" stroke=\"black\" stroke-width=\"1\" fill=\"none\"/>"));
                }
            }

            sb.AppendLine("</svg>");
            return sb.ToString();
        }
    }
}
