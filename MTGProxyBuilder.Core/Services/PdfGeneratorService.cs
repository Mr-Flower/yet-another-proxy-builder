using MTGProxyBuilder.Core.Models;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace MTGProxyBuilder.Core.Services
{
    public class PdfGeneratorService
    {
        private const float MmToPt = 72f / 25.4f;
        private readonly BleedProcessor _bleedProcessor = new();

        public Task<bool> GeneratePdfAsync(ProjectModel project, string outputPath)
        {
            return Task.Run(() =>
            {
                try
                {
                    Serilog.Log.Information("PDF export started: {Project} ({Cards} cards) -> {Path}",
                        project.ProjectName, project.Cards.Count, outputPath);
                    var document = new PdfDocument();
                    document.Info.Title = project.ProjectName;

                    var settings = project.PageSettings;
                    var printSettings = project.PrintSettings;

                    // Pre-process all unique images so each becomes exactly "card + 2*bleed": Scryfall
                    // scans are edge-extended up to the chosen bleed; MPCFill art has its baked 1/8"
                    // bleed cropped down to it. The card region is unchanged either way, so both sources
                    // print at the same size. Skipped only in registration-marks mode (cards drawn bare).
                    // Always run (even at bleed 0) so MPCFill's native bleed is still trimmed off.
                    float effectiveBleedMm = settings.EffectiveBleedMm;
                    var bleedCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    if (!printSettings.ShowRegistrationMarks)
                    {
                        var uniquePaths = project.Cards
                            .SelectMany(c => new[] { c.ArtworkPath, c.BackArtworkPath })
                            .Where(p => !string.IsNullOrEmpty(p) && File.Exists(p))
                            .Distinct(StringComparer.OrdinalIgnoreCase);

                        foreach (var path in uniquePaths)
                        {
                            var result = _bleedProcessor.GetDisplayImage(
                                path!, effectiveBleedMm, settings.CardWidthMm);
                            if (result != null)
                                bleedCache[path!] = result;
                        }
                    }

                    var expandedFronts = ExpandCards(project.Cards);
                    var expandedBacks = ExpandCards(project.Cards.Where(c => c.IncludeBack).ToList());

                    if (printSettings.PrintMode == PrintMode.Duplex)
                    {
                        int frontPageCount = CalcPageCount(expandedFronts.Count, settings);
                        int backPageCount = CalcPageCount(expandedBacks.Count, settings);
                        int totalPages = Math.Max(frontPageCount, backPageCount);

                        for (int i = 0; i < totalPages; i++)
                        {
                            AddPage(document, settings, printSettings, expandedFronts, i, true, bleedCache);
                            AddPage(document, settings, printSettings, expandedBacks, i, false, bleedCache);
                        }
                    }
                    else if (printSettings.PrintMode == PrintMode.FrontsOnly)
                    {
                        int pageCount = CalcPageCount(expandedFronts.Count, settings);
                        for (int i = 0; i < pageCount; i++)
                            AddPage(document, settings, printSettings, expandedFronts, i, true, bleedCache);
                    }
                    else
                    {
                        int pageCount = CalcPageCount(expandedBacks.Count, settings);
                        for (int i = 0; i < pageCount; i++)
                            AddPage(document, settings, printSettings, expandedBacks, i, false, bleedCache);
                    }

                    if (document.PageCount == 0)
                    {
                        var page = document.AddPage();
                        SetPageSize(page, settings);
                    }

                    int savedPages = document.PageCount; // read before Save — PdfSharp locks the doc afterwards
                    document.Save(outputPath);
                    Serilog.Log.Information("PDF export finished: {Pages} pages -> {Path}", savedPages, outputPath);
                    return true;
                }
                catch (Exception ex)
                {
                    Serilog.Log.Error(ex, "PDF generation error");
                    return false;
                }
            });
        }

        /// <summary>Pre-computed point-space geometry for one page's card grid.</summary>
        private readonly record struct PageGeometry(
            float StartX, float StartY, float BleedPt, float CardWPt, float CardHPt,
            float CellW, float CellH, int Cols, float PageWPt, float PageHPt);

        /// <summary>Renders one page: cut guides (behind), card art + overlay text, outlines, then
        /// registration marks. Returns early when the page would be empty.</summary>
        private void AddPage(PdfDocument doc, PageLayout settings, PrintSettings printSettings,
            List<CardModel> cards, int pageIndex, bool front,
            Dictionary<string, string> bleedCache)
        {
            var page = doc.AddPage();
            SetPageSize(page, settings);

            int perPage = settings.CardsPerPage;
            if (perPage <= 0) return;
            int startIdx = pageIndex * perPage;
            if (startIdx >= cards.Count) return;

            using var gfx = XGraphics.FromPdfPage(page);
            var geo = ComputeGeometry(settings);
            // Registration-marks mode suppresses bleed, cut guides and outlines.
            bool useBleed = bleedCache.Count > 0 && !printSettings.ShowRegistrationMarks;

            if (printSettings.ShowCutGuides && !printSettings.ShowRegistrationMarks)
                DrawCutGuidesPass(gfx, geo, cards, startIdx, perPage, front);

            DrawCardsPass(gfx, geo, cards, startIdx, perPage, front, bleedCache, useBleed);

            if (printSettings.ShowCardOutline && !printSettings.ShowRegistrationMarks)
                DrawOutlinesPass(gfx, geo, printSettings, cards, startIdx, perPage, front);

            if (printSettings.ShowRegistrationMarks && front)
                DrawRegistrationMarks(gfx, geo.PageWPt, geo.PageHPt, printSettings);
        }

        /// <summary>
        /// Computes the page grid geometry in points. The grid origin includes the user's card-position
        /// adjustment (printer-offset compensation — moves the whole grid, not card spacing). The bleed
        /// is the MPC 1/8" standard (BleedWidthMm only toggles it), matching the bled images.
        /// </summary>
        private static PageGeometry ComputeGeometry(PageLayout s)
        {
            float bleedPt = s.EffectiveBleedMm * MmToPt;
            float cardWPt = s.CardWidthMm * MmToPt;
            float cardHPt = s.CardHeightMm * MmToPt;
            return new PageGeometry(
                StartX: (s.MarginLeftMm + s.OffsetXmm) * MmToPt,
                StartY: (s.MarginTopMm + s.OffsetYmm) * MmToPt,
                BleedPt: bleedPt, CardWPt: cardWPt, CardHPt: cardHPt,
                CellW: cardWPt + 2 * bleedPt, CellH: cardHPt + 2 * bleedPt,
                Cols: s.CardsPerRow,
                PageWPt: s.PageWidthMm * MmToPt, PageHPt: s.PageHeightMm * MmToPt);
        }

        /// <summary>Top-left point of cell <paramref name="i"/>. Back pages mirror columns so they line
        /// up with the fronts when the sheet is flipped.</summary>
        private static (float X, float Y) CellOrigin(PageGeometry g, int i, bool front)
        {
            int row = i / g.Cols;
            int col = front ? (i % g.Cols) : (g.Cols - 1 - (i % g.Cols));
            return (g.StartX + col * g.CellW, g.StartY + row * g.CellH);
        }

        /// <summary>Draws the cut guides for every occupied cell (behind the card art).</summary>
        private void DrawCutGuidesPass(XGraphics gfx, PageGeometry g, List<CardModel> cards,
            int startIdx, int perPage, bool front)
        {
            for (int i = 0; i < perPage && startIdx + i < cards.Count; i++)
            {
                var (x, y) = CellOrigin(g, i, front);
                DrawCutGuides(gfx, x, y, g.CellW, g.CellH, g.BleedPt, g.CardWPt, g.CardHPt, g.PageWPt, g.PageHPt);
            }
        }

        /// <summary>Draws each card's image (and its overlay text) on top of the cut guides.</summary>
        private void DrawCardsPass(XGraphics gfx, PageGeometry g, List<CardModel> cards,
            int startIdx, int perPage, bool front, Dictionary<string, string> bleedCache, bool useBleed)
        {
            for (int i = 0; i < perPage && startIdx + i < cards.Count; i++)
            {
                var card = cards[startIdx + i];
                var (x, y) = CellOrigin(g, i, front);
                DrawCardImage(gfx, g, card, x, y, front, bleedCache, useBleed);

                if (front && !string.IsNullOrEmpty(card.OverlayText))
                    DrawOverlayText(gfx, card.OverlayText, x + g.BleedPt, y + g.BleedPt, g.CardWPt, g.CardHPt);
            }
        }

        /// <summary>Draws one card: the bled image filling the cell when bleed is on, otherwise the bare
        /// card inset by the bleed margin (or a placeholder when there's no art).</summary>
        private void DrawCardImage(XGraphics gfx, PageGeometry g, CardModel card, float cellX, float cellY,
            bool front, Dictionary<string, string> bleedCache, bool useBleed)
        {
            string imagePath = front ? card.ArtworkPath : (card.BackArtworkPath ?? card.ArtworkPath);

            if (useBleed && !string.IsNullOrEmpty(imagePath) && bleedCache.TryGetValue(imagePath, out var bleedImage))
                DrawCard(gfx, bleedImage, cellX, cellY, g.CellW, g.CellH);
            else if (!string.IsNullOrEmpty(imagePath))
                DrawCard(gfx, imagePath, cellX + g.BleedPt, cellY + g.BleedPt, g.CardWPt, g.CardHPt);
            else
                DrawCard(gfx, null, cellX + g.BleedPt, cellY + g.BleedPt, g.CardWPt, g.CardHPt);
        }

        /// <summary>Draws the card outline guides for every occupied cell (on top of the card art).</summary>
        private void DrawOutlinesPass(XGraphics gfx, PageGeometry g, PrintSettings printSettings,
            List<CardModel> cards, int startIdx, int perPage, bool front)
        {
            for (int i = 0; i < perPage && startIdx + i < cards.Count; i++)
            {
                var (x, y) = CellOrigin(g, i, front);
                DrawCardOutline(gfx, x, y, g.CellW, g.CellH, g.BleedPt, g.CardWPt, g.CardHPt, printSettings);
            }
        }

        private void DrawCardOutline(XGraphics gfx, float cellX, float cellY,
            float cellW, float cellH, float bleed, float cardW, float cardH,
            PrintSettings ps)
        {
            // Parse outline color
            XColor color;
            try
            {
                string hex = ps.OutlineColor.TrimStart('#');
                int r = Convert.ToInt32(hex[..2], 16);
                int g = Convert.ToInt32(hex[2..4], 16);
                int b = Convert.ToInt32(hex[4..6], 16);
                color = XColor.FromArgb(r, g, b);
            }
            catch { color = XColor.FromArgb(0x66, 0xFF, 0x00); }

            var pen = new XPen(color, ps.LineWeight);
            if (ps.OutlineLineType == LineType.Dashed)
                pen.DashStyle = XDashStyle.Dash;

            float radiusPt = ps.CornerRadiusMm * MmToPt;
            float cornerLenPt = ps.CornerLengthMm * MmToPt;

            // Calculate card rect position based on alignment
            float cardLeft = cellX + bleed;
            float cardTop = cellY + bleed;
            float offset = ps.LineWeight / 2; // half the line weight for alignment

            float x, y, w, h;
            switch (ps.OutlineAlignment)
            {
                case OutlineAlignment.Inside:
                    x = cardLeft + offset;
                    y = cardTop + offset;
                    w = cardW - 2 * offset;
                    h = cardH - 2 * offset;
                    break;
                case OutlineAlignment.Outside:
                    x = cardLeft - offset;
                    y = cardTop - offset;
                    w = cardW + 2 * offset;
                    h = cardH + 2 * offset;
                    break;
                default: // Center
                    x = cardLeft;
                    y = cardTop;
                    w = cardW;
                    h = cardH;
                    break;
            }

            if (ps.OutlineType == OutlineType.Full)
            {
                // Full rounded rectangle
                if (radiusPt > 0)
                    DrawRoundedRect(gfx, pen, x, y, w, h, radiusPt);
                else
                    gfx.DrawRectangle(pen, x, y, w, h);
            }
            else // Corners only
            {
                DrawCornerMarks(gfx, pen, x, y, w, h, radiusPt, cornerLenPt);
            }
        }

        private void DrawRoundedRect(XGraphics gfx, XPen pen, float x, float y, float w, float h, float r)
        {
            r = Math.Min(r, Math.Min(w / 2, h / 2));

            var path = new XGraphicsPath();
            // Top-left arc
            path.AddArc(x, y, 2 * r, 2 * r, 180, 90);
            // Top edge
            path.AddLine(x + r, y, x + w - r, y);
            // Top-right arc
            path.AddArc(x + w - 2 * r, y, 2 * r, 2 * r, 270, 90);
            // Right edge
            path.AddLine(x + w, y + r, x + w, y + h - r);
            // Bottom-right arc
            path.AddArc(x + w - 2 * r, y + h - 2 * r, 2 * r, 2 * r, 0, 90);
            // Bottom edge
            path.AddLine(x + w - r, y + h, x + r, y + h);
            // Bottom-left arc
            path.AddArc(x, y + h - 2 * r, 2 * r, 2 * r, 90, 90);
            // Left edge
            path.AddLine(x, y + h - r, x, y + r);
            path.CloseFigure();

            gfx.DrawPath(pen, path);
        }

        private void DrawCornerMarks(XGraphics gfx, XPen pen, float x, float y, float w, float h, float r, float len)
        {
            r = Math.Min(r, Math.Min(w / 2, h / 2));
            len = Math.Min(len, Math.Min(w / 2 - r, h / 2 - r));
            if (len <= 0) len = 5;

            if (r > 0)
            {
                // Top-left corner: arc + straight stubs
                var path = new XGraphicsPath();
                path.AddLine(x, y + r + len, x, y + r);
                path.AddArc(x, y, 2 * r, 2 * r, 180, 90);
                path.AddLine(x + r, y, x + r + len, y);
                gfx.DrawPath(pen, path);

                // Top-right corner
                path = new XGraphicsPath();
                path.AddLine(x + w - r - len, y, x + w - r, y);
                path.AddArc(x + w - 2 * r, y, 2 * r, 2 * r, 270, 90);
                path.AddLine(x + w, y + r, x + w, y + r + len);
                gfx.DrawPath(pen, path);

                // Bottom-right corner
                path = new XGraphicsPath();
                path.AddLine(x + w, y + h - r - len, x + w, y + h - r);
                path.AddArc(x + w - 2 * r, y + h - 2 * r, 2 * r, 2 * r, 0, 90);
                path.AddLine(x + w - r, y + h, x + w - r - len, y + h);
                gfx.DrawPath(pen, path);

                // Bottom-left corner
                path = new XGraphicsPath();
                path.AddLine(x + r + len, y + h, x + r, y + h);
                path.AddArc(x, y + h - 2 * r, 2 * r, 2 * r, 90, 90);
                path.AddLine(x, y + h - r, x, y + h - r - len);
                gfx.DrawPath(pen, path);
            }
            else
            {
                // Sharp corners — just L-shaped marks
                // Top-left
                gfx.DrawLine(pen, x, y + len, x, y);
                gfx.DrawLine(pen, x, y, x + len, y);
                // Top-right
                gfx.DrawLine(pen, x + w - len, y, x + w, y);
                gfx.DrawLine(pen, x + w, y, x + w, y + len);
                // Bottom-right
                gfx.DrawLine(pen, x + w, y + h - len, x + w, y + h);
                gfx.DrawLine(pen, x + w, y + h, x + w - len, y + h);
                // Bottom-left
                gfx.DrawLine(pen, x + len, y + h, x, y + h);
                gfx.DrawLine(pen, x, y + h, x, y + h - len);
            }
        }

        private void DrawCutGuides(XGraphics gfx, float cellX, float cellY,
            float cellW, float cellH, float bleed, float cardW, float cardH,
            float pageW, float pageH)
        {
            float cardLeft = cellX + bleed;
            float cardTop = cellY + bleed;
            float cardRight = cellX + bleed + cardW;
            float cardBottom = cellY + bleed + cardH;

            var pen = new XPen(XColors.Black, 0.25);

            // Vertical lines extend from card edge to top/bottom page edges
            gfx.DrawLine(pen, cardLeft, 0, cardLeft, cardTop);           // top-left vertical up
            gfx.DrawLine(pen, cardRight, 0, cardRight, cardTop);         // top-right vertical up
            gfx.DrawLine(pen, cardLeft, cardBottom, cardLeft, pageH);    // bottom-left vertical down
            gfx.DrawLine(pen, cardRight, cardBottom, cardRight, pageH);  // bottom-right vertical down

            // Horizontal lines extend from card edge to left/right page edges
            gfx.DrawLine(pen, 0, cardTop, cardLeft, cardTop);           // top-left horizontal left
            gfx.DrawLine(pen, cardRight, cardTop, pageW, cardTop);      // top-right horizontal right
            gfx.DrawLine(pen, 0, cardBottom, cardLeft, cardBottom);     // bottom-left horizontal left
            gfx.DrawLine(pen, cardRight, cardBottom, pageW, cardBottom); // bottom-right horizontal right
        }

        private const float InToPt = 72f;

        private void DrawRegistrationMarks(XGraphics gfx, float pageW, float pageH, PrintSettings ps)
        {
            float inset = ps.RegMarkInsetIn * InToPt;
            float squareSize = ps.RegMarkSquareSizeIn * InToPt;  // 5mm filled square
            float armLength = ps.RegMarkLengthIn * InToPt;       // 20mm L-shape arms
            float thickness = ps.RegMarkThicknessIn * InToPt;    // 0.3mm arm thickness

            var brush = XBrushes.Black;

            // Top-left mark: filled square (5mm x 5mm)
            gfx.DrawRectangle(brush, inset, inset, squareSize, squareSize);

            // Top-right mark: L-shape with corner at (pageW - inset, inset)
            // Horizontal bar going left
            gfx.DrawRectangle(brush, pageW - inset - armLength, inset, armLength, thickness);
            // Vertical bar going down
            gfx.DrawRectangle(brush, pageW - inset - thickness, inset + thickness, thickness, armLength - thickness);

            // Bottom-left mark: L-shape with corner at (inset, pageH - inset)
            // Vertical bar going up
            gfx.DrawRectangle(brush, inset, pageH - inset - armLength, thickness, armLength - thickness);
            // Horizontal bar going right
            gfx.DrawRectangle(brush, inset, pageH - inset - thickness, armLength, thickness);
        }

        private void DrawOverlayText(XGraphics gfx, string text, float x, float y, float w, float h)
        {
            // Semi-transparent dark banner across the bottom third of the card
            float bannerH = h * 0.15f;
            float bannerY = y + h - bannerH - h * 0.08f;

            var bannerBrush = new XSolidBrush(XColor.FromArgb(160, 0, 0, 0));
            gfx.DrawRectangle(bannerBrush, x, bannerY, w, bannerH);

            // White text centered in the banner
            var font = CreatePortableFont(Math.Max(8, bannerH * 0.6), XFontStyleEx.Bold);
            var textBrush = XBrushes.White;
            var format = new XStringFormat
            {
                Alignment = XStringAlignment.Center,
                LineAlignment = XLineAlignment.Center
            };
            gfx.DrawString(text, font, textBrush,
                new XRect(x, bannerY, w, bannerH), format);
        }

        private void DrawCard(XGraphics gfx, string? imagePath, float x, float y, float w, float h)
        {
            if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath))
            {
                gfx.DrawRectangle(XPens.LightGray, x, y, w, h);
                return;
            }

            try
            {
                using var image = XImage.FromFile(imagePath);
                gfx.DrawImage(image, x, y, w, h);
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "Error drawing card image");
                gfx.DrawRectangle(XPens.Red, x, y, w, h);
            }
        }

        private void SetPageSize(PdfPage page, PageLayout settings)
        {
            page.Width = new XUnit(settings.PageWidthMm * MmToPt, XGraphicsUnit.Point);
            page.Height = new XUnit(settings.PageHeightMm * MmToPt, XGraphicsUnit.Point);
        }

        private List<CardModel> ExpandCards(List<CardModel> cards)
        {
            var result = new List<CardModel>();
            foreach (var card in cards)
                for (int i = 0; i < card.Quantity; i++)
                    result.Add(card);
            return result;
        }

        private int CalcPageCount(int cardCount, PageLayout settings)
        {
            int perPage = settings.CardsPerPage;
            if (perPage <= 0) return 0;
            return (int)Math.Ceiling((double)cardCount / perPage);
        }

        private static XFont CreatePortableFont(double size, XFontStyleEx style)
        {
            foreach (var name in new[] { "Arial", "Liberation Sans", "DejaVu Sans", "Courier New" })
            {
                try { return new XFont(name, size, style); }
                catch { }
            }
            return new XFont("Courier New", size, XFontStyleEx.Regular);
        }
    }
}
