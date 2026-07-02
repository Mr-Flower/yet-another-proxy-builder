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

        /// <summary>
        /// Generates a two-page duplex calibration test (ported from upstream, adapted to this fork's
        /// offset model): page 1 (front, solid) and page 2 (back, dashed) draw the same grid boundary,
        /// precision targets at the corners/center and mm rulers; the front adds CMYK/grayscale density
        /// bars. The back page gets the current BackOffsetXmm/Ymm correction, so re-printing after
        /// entering the measured offsets verifies they converge. Print duplex, hold up to light, measure
        /// the solid→dashed displacement and enter it as the back offset.
        /// </summary>
        public Task<bool> GenerateAlignmentPdfAsync(ProjectModel project, string outputPath)
        {
            return Task.Run(() =>
            {
                try
                {
                    var document = new PdfDocument();
                    document.Info.Title = "Printer Calibration Test";

                    var settings = project.PageSettings;
                    var front = ComputeGeometry(settings, front: true);
                    var back = ComputeGeometry(settings, front: false);

                    int cols = settings.CardsPerRow;
                    int rows = settings.CardsPerColumn;
                    float gridW = cols * front.CellW;
                    float gridH = rows * front.CellH;

                    var titleFont = CreatePortableFont(10, XFontStyleEx.Bold);
                    var infoFont = CreatePortableFont(8, XFontStyleEx.Regular);
                    var instructionFont = CreatePortableFont(6, XFontStyleEx.Regular);
                    var leftFormat = new XStringFormat
                    {
                        Alignment = XStringAlignment.Near,
                        LineAlignment = XLineAlignment.Near
                    };

                    (float X, float Y, string Label)[] Targets(PageGeometry g) => new[]
                    {
                        (g.StartX, g.StartY, "TL"),
                        (g.StartX + gridW, g.StartY, "TR"),
                        (g.StartX, g.StartY + gridH, "BL"),
                        (g.StartX + gridW, g.StartY + gridH, "BR"),
                        (g.StartX + gridW / 2, g.StartY + gridH / 2, "C")
                    };

                    // ===== Page 1: Front (reference, solid) =====
                    var frontPage = document.AddPage();
                    SetPageSize(frontPage, settings);
                    using (var gfx = XGraphics.FromPdfPage(frontPage))
                    {
                        float titleY = Math.Min(front.StartY - 24, 10);
                        gfx.DrawString("PRINTER CALIBRATION TEST — FRONT", titleFont, XBrushes.Black,
                            front.StartX, titleY, leftFormat);
                        string settingsInfo = $"{settings.PageWidthMm}x{settings.PageHeightMm}mm page | " +
                            $"{settings.CardWidthMm}x{settings.CardHeightMm}mm card | " +
                            $"{cols}x{rows} grid | {DateTime.Now:yyyy-MM-dd}";
                        gfx.DrawString(settingsInfo, infoFont, XBrushes.Black,
                            front.StartX, titleY + 12, leftFormat);

                        var gridPen = new XPen(XColors.Black, 0.5);
                        gfx.DrawRectangle(gridPen, front.StartX, front.StartY, gridW, gridH);

                        foreach (var (tx, ty, label) in Targets(front))
                            DrawAlignmentTarget(gfx, tx, ty, label, false);

                        float rulerOffset = 3 * MmToPt; // 3mm outside the grid
                        DrawRuler(gfx, front.StartX, front.StartY - rulerOffset, gridW, true, false);
                        DrawRuler(gfx, front.StartX - rulerOffset, front.StartY, gridH, false, false);

                        DrawColorBars(gfx, front.StartX, front.StartY, cols, rows,
                            front.CellW, front.CellH, front.PageWPt, front.PageHPt);

                        gfx.DrawString(
                            $"Applied back offset: X={settings.BackOffsetXmm:F2}mm, Y={settings.BackOffsetYmm:F2}mm",
                            infoFont, XBrushes.Black, front.StartX, front.PageHPt - 28, leftFormat);
                        gfx.DrawString(
                            "Print this page duplex (flip on long edge). Hold up to light. Measure offset between " +
                            "solid (front) and dashed (back) targets and enter it as the Back offset in the Layout panel.",
                            instructionFont, XBrushes.DarkGray, front.StartX, front.PageHPt - 16, leftFormat);
                    }

                    // ===== Page 2: Back (dashed, with duplex correction applied) =====
                    var backPage = document.AddPage();
                    SetPageSize(backPage, settings);
                    using (var gfx = XGraphics.FromPdfPage(backPage))
                    {
                        float titleY = Math.Min(back.StartY - 24, 10);
                        gfx.DrawString("PRINTER CALIBRATION TEST — BACK", titleFont, XBrushes.Black,
                            back.StartX, titleY, leftFormat);
                        gfx.DrawString(
                            $"Applied back offset: X={settings.BackOffsetXmm:F2}mm, Y={settings.BackOffsetYmm:F2}mm",
                            infoFont, XBrushes.Black, back.StartX, titleY + 12, leftFormat);

                        var dashedGridPen = new XPen(XColors.Black, 0.5) { DashStyle = XDashStyle.Dash };
                        gfx.DrawRectangle(dashedGridPen, back.StartX, back.StartY, gridW, gridH);

                        foreach (var (tx, ty, label) in Targets(back))
                            DrawAlignmentTarget(gfx, tx, ty, label, true);

                        float rulerOffset = 3 * MmToPt;
                        DrawRuler(gfx, back.StartX, back.StartY - rulerOffset, gridW, true, true);
                        DrawRuler(gfx, back.StartX - rulerOffset, back.StartY, gridH, false, true);
                        // No color bars on the back (saves ink)
                    }

                    document.Save(outputPath);
                    Serilog.Log.Information("Alignment test PDF exported -> {Path}", outputPath);
                    return true;
                }
                catch (Exception ex)
                {
                    Serilog.Log.Error(ex, "Alignment PDF error");
                    return false;
                }
            });
        }

        /// <summary>
        /// Draws a precision alignment target with concentric circles, graduated crosshair, and label.
        /// </summary>
        private void DrawAlignmentTarget(XGraphics gfx, float cx, float cy, string label, bool dashed)
        {
            var finePen = new XPen(XColors.Black, 0.25);
            if (dashed) finePen.DashStyle = XDashStyle.Dash;

            // Concentric circles at 2mm, 4mm, 6mm radius
            foreach (float rMm in new[] { 2f, 4f, 6f })
            {
                float r = rMm * MmToPt;
                gfx.DrawEllipse(finePen, cx - r, cy - r, 2 * r, 2 * r);
            }

            // Crosshair arms extending 8mm in each direction
            float armLen = 8 * MmToPt;
            gfx.DrawLine(finePen, cx - armLen, cy, cx + armLen, cy);
            gfx.DrawLine(finePen, cx, cy - armLen, cx, cy + armLen);

            // Graduated ruler marks along crosshair arms (1mm ticks, longer + labelled at 5mm)
            var tickPen = new XPen(XColors.Black, 0.25);
            if (dashed) tickPen.DashStyle = XDashStyle.Dash;
            var tickLabelFont = CreatePortableFont(4, XFontStyleEx.Regular);
            float shortTick = 0.5f * MmToPt;
            float longTick = 1.0f * MmToPt;

            for (int mm = 1; mm <= 8; mm++)
            {
                float d = mm * MmToPt;
                bool isMajor = (mm % 5 == 0);
                float tickLen = isMajor ? longTick : shortTick;

                gfx.DrawLine(tickPen, cx + d, cy - tickLen, cx + d, cy + tickLen); // right arm
                gfx.DrawLine(tickPen, cx - d, cy - tickLen, cx - d, cy + tickLen); // left arm
                gfx.DrawLine(tickPen, cx - tickLen, cy + d, cx + tickLen, cy + d); // down arm
                gfx.DrawLine(tickPen, cx - tickLen, cy - d, cx + tickLen, cy - d); // up arm

                if (isMajor)
                {
                    gfx.DrawString(mm.ToString(), tickLabelFont, XBrushes.Black,
                        cx + d, cy + tickLen + 1, new XStringFormat
                        {
                            Alignment = XStringAlignment.Center,
                            LineAlignment = XLineAlignment.Near
                        });
                }
            }

            // Position label (TL, TR, BL, BR, C)
            var labelFont = CreatePortableFont(5, XFontStyleEx.Bold);
            float labelOffset = 7 * MmToPt;
            gfx.DrawString(label, labelFont, XBrushes.Black,
                cx + labelOffset, cy - labelOffset, new XStringFormat
                {
                    Alignment = XStringAlignment.Near,
                    LineAlignment = XLineAlignment.Far
                });
        }

        /// <summary>
        /// Draws a precision measurement ruler with mm graduations.
        /// Short ticks every 1mm, medium ticks every 5mm, tall ticks every 10mm with number labels.
        /// </summary>
        private void DrawRuler(XGraphics gfx, float originX, float originY, float length, bool horizontal, bool dashed)
        {
            var pen = new XPen(XColors.Black, 0.25);
            if (dashed) pen.DashStyle = XDashStyle.Dash;

            var labelFont = CreatePortableFont(5, XFontStyleEx.Regular);
            int totalTicks = (int)Math.Floor(length / MmToPt);

            float shortTick = 1.0f * MmToPt;
            float medTick = 1.5f * MmToPt;
            float tallTick = 2.5f * MmToPt;

            if (horizontal)
                gfx.DrawLine(pen, originX, originY, originX + length, originY);
            else
                gfx.DrawLine(pen, originX, originY, originX, originY + length);

            for (int mm = 0; mm <= totalTicks; mm++)
            {
                float d = mm * MmToPt;
                float tickLen = mm % 10 == 0 ? tallTick : (mm % 5 == 0 ? medTick : shortTick);

                if (horizontal)
                {
                    float x = originX + d;
                    gfx.DrawLine(pen, x, originY, x, originY + tickLen);
                    if (mm % 10 == 0 && mm > 0)
                        gfx.DrawString(mm.ToString(), labelFont, XBrushes.Black,
                            x, originY + tallTick + 1, new XStringFormat
                            {
                                Alignment = XStringAlignment.Center,
                                LineAlignment = XLineAlignment.Near
                            });
                }
                else
                {
                    float y = originY + d;
                    gfx.DrawLine(pen, originX, y, originX + tickLen, y);
                    if (mm % 10 == 0 && mm > 0)
                        gfx.DrawString(mm.ToString(), labelFont, XBrushes.Black,
                            originX + tallTick + 1, y, new XStringFormat
                            {
                                Alignment = XStringAlignment.Near,
                                LineAlignment = XLineAlignment.Center
                            });
                }
            }
        }

        /// <summary>
        /// Draw CMYK density bars in available margin space.
        /// Tries bottom margin first (horizontal), then right margin (vertical).
        /// </summary>
        private void DrawColorBars(XGraphics gfx, float startX, float startY,
            int cols, int rows, float cellW, float cellH, float pageW, float pageH)
        {
            float gridRight = startX + cols * cellW;
            float gridBottom = startY + rows * cellH;
            float gridWidth = cols * cellW;
            float gridHeight = rows * cellH;
            float barThickness = 4 * MmToPt;
            float gap = 2 * MmToPt;
            float minClearance = 3 * MmToPt;

            bool fitsBottom = gridBottom + gap + barThickness <= pageH - minClearance;
            bool fitsRight = gridRight + gap + barThickness <= pageW - minClearance;

            if (!fitsBottom && !fitsRight) return;

            if (fitsBottom)
                DrawColorBarStrip(gfx, startX, gridBottom + gap, gridWidth, barThickness, false);
            else
                DrawColorBarStrip(gfx, gridRight + gap, startY, gridHeight, barThickness, true);
        }

        private void DrawColorBarStrip(XGraphics gfx, float originX, float originY,
            float stripLength, float stripThickness, bool vertical)
        {
            var colors = new (string Label, int R, int G, int B)[]
            {
                ("C", 0, 174, 239), ("M", 236, 0, 140), ("Y", 255, 242, 0),
                ("K", 0, 0, 0), ("R", 237, 28, 36), ("G", 0, 166, 81), ("B", 46, 49, 146),
            };

            int totalPatches = colors.Length * 4 + 8; // 4 densities per colour + 8 grayscale steps
            float patchSize = stripLength / totalPatches;
            float pos = 0;

            var labelFont = CreatePortableFont(5, XFontStyleEx.Regular);
            var labelFormat = new XStringFormat
            {
                Alignment = XStringAlignment.Center,
                LineAlignment = XLineAlignment.Far
            };

            foreach (var (label, cr, cg, cb) in colors)
            {
                foreach (float dnst in new[] { 0.25f, 0.50f, 0.75f, 1.0f })
                {
                    int r = (int)(255 + (cr - 255) * dnst);
                    int g = (int)(255 + (cg - 255) * dnst);
                    int b = (int)(255 + (cb - 255) * dnst);
                    var brush = new XSolidBrush(XColor.FromArgb(r, g, b));

                    if (vertical)
                        gfx.DrawRectangle(brush, originX, originY + pos, stripThickness, patchSize);
                    else
                        gfx.DrawRectangle(brush, originX + pos, originY, patchSize, stripThickness);
                    pos += patchSize;
                }

                float labelPos = pos - patchSize * 2;
                if (vertical)
                    gfx.DrawString(label, labelFont, XBrushes.Black,
                        originX + stripThickness + 2, originY + labelPos + patchSize / 2, labelFormat);
                else
                    gfx.DrawString(label, labelFont, XBrushes.Black,
                        originX + labelPos, originY - 1, labelFormat);
            }

            for (int i = 0; i < 8; i++)
            {
                int v = 255 - (int)(255 * i / 7.0);
                var brush = new XSolidBrush(XColor.FromArgb(v, v, v));
                if (vertical)
                    gfx.DrawRectangle(brush, originX, originY + pos, stripThickness, patchSize);
                else
                    gfx.DrawRectangle(brush, originX + pos, originY, patchSize, stripThickness);
                pos += patchSize;
            }

            if (vertical)
                gfx.DrawRectangle(new XPen(XColors.Black, 0.25),
                    originX, originY, stripThickness, stripLength);
            else
                gfx.DrawRectangle(new XPen(XColors.Black, 0.25),
                    originX, originY, stripLength, stripThickness);
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
            var geo = ComputeGeometry(settings, front);
            // Registration-marks mode suppresses bleed, cut guides and outlines.
            bool useBleed = bleedCache.Count > 0 && !printSettings.ShowRegistrationMarks;

            if (printSettings.ShowCutGuides && !printSettings.ShowRegistrationMarks)
                DrawCutGuidesPass(gfx, geo, cards, startIdx, perPage, front);

            if (printSettings.ShowCropMarks && !printSettings.ShowRegistrationMarks)
                DrawCropMarksPass(gfx, geo, printSettings, cards, startIdx, perPage, front);

            DrawCardsPass(gfx, geo, cards, startIdx, perPage, front, bleedCache, useBleed);

            if (printSettings.ShowCardOutline && !printSettings.ShowRegistrationMarks)
                DrawOutlinesPass(gfx, geo, printSettings, cards, startIdx, perPage, front);

            if (printSettings.ShowRegistrationMarks && front)
                DrawRegistrationMarks(gfx, geo.PageWPt, geo.PageHPt, printSettings);
        }

        /// <summary>
        /// Computes the page grid geometry in points. The grid origin includes the user's card-position
        /// adjustment (printer-offset compensation — moves the whole grid, not card spacing); back pages
        /// additionally get the duplex correction (BackOffsetXmm/Ymm). The bleed
        /// is the MPC 1/8" standard (BleedWidthMm only toggles it), matching the bled images.
        /// </summary>
        private static PageGeometry ComputeGeometry(PageLayout s, bool front)
        {
            float bleedPt = s.EffectiveBleedMm * MmToPt;
            float cardWPt = s.CardWidthMm * MmToPt;
            float cardHPt = s.CardHeightMm * MmToPt;
            float backX = front ? 0 : s.BackOffsetXmm;
            float backY = front ? 0 : s.BackOffsetYmm;
            return new PageGeometry(
                StartX: (s.MarginLeftMm + s.OffsetXmm + backX) * MmToPt,
                StartY: (s.MarginTopMm + s.OffsetYmm + backY) * MmToPt,
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

        /// <summary>Draws the crop marks for every occupied cell (behind the card art).</summary>
        private void DrawCropMarksPass(XGraphics gfx, PageGeometry g, PrintSettings ps,
            List<CardModel> cards, int startIdx, int perPage, bool front)
        {
            float cropLen = ps.CropMarkLengthMm * MmToPt;
            float cropOffset = ps.CropMarkOffsetMm * MmToPt;
            for (int i = 0; i < perPage && startIdx + i < cards.Count; i++)
            {
                var (x, y) = CellOrigin(g, i, front);
                DrawCropMarks(gfx, x, y, g.BleedPt, g.CardWPt, g.CardHPt, cropLen, cropOffset);
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

        /// <summary>
        /// Draw professional crop marks at each corner of a card.
        /// Marks are short lines at the trim boundary (card edge), extending outward
        /// into the bleed area with a small gap from the card edge.
        /// </summary>
        private void DrawCropMarks(XGraphics gfx, float cellX, float cellY,
            float bleed, float cardW, float cardH, float markLen, float offset)
        {
            float cardLeft = cellX + bleed;
            float cardTop = cellY + bleed;
            float cardRight = cellX + bleed + cardW;
            float cardBottom = cellY + bleed + cardH;

            var pen = new XPen(XColors.Black, 0.25);

            // Top-left corner
            gfx.DrawLine(pen, cardLeft, cardTop - offset, cardLeft, cardTop - offset - markLen);         // vertical up
            gfx.DrawLine(pen, cardLeft - offset, cardTop, cardLeft - offset - markLen, cardTop);         // horizontal left

            // Top-right corner
            gfx.DrawLine(pen, cardRight, cardTop - offset, cardRight, cardTop - offset - markLen);       // vertical up
            gfx.DrawLine(pen, cardRight + offset, cardTop, cardRight + offset + markLen, cardTop);       // horizontal right

            // Bottom-left corner
            gfx.DrawLine(pen, cardLeft, cardBottom + offset, cardLeft, cardBottom + offset + markLen);   // vertical down
            gfx.DrawLine(pen, cardLeft - offset, cardBottom, cardLeft - offset - markLen, cardBottom);   // horizontal left

            // Bottom-right corner
            gfx.DrawLine(pen, cardRight, cardBottom + offset, cardRight, cardBottom + offset + markLen); // vertical down
            gfx.DrawLine(pen, cardRight + offset, cardBottom, cardRight + offset + markLen, cardBottom); // horizontal right
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
