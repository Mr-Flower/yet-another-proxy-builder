using System;
using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using MTGProxyBuilder.Core.Models;

namespace MTGProxyBuilder.UI.Controls
{
    /// <summary>
    /// Paints a single card cell directly onto a <see cref="DrawingContext"/> (retained-mode rendering).
    /// Previously this materialised Avalonia controls per cell; drawing immediately is far cheaper and
    /// avoids the control churn that drove the canvas's redraw cost.
    /// </summary>
    public static class CardVisualRenderer
    {
        private const float MmToPx = 96f / 25.4f;
        private static readonly Typeface DefaultTypeface = new("Segoe UI");

        private static FormattedText Text(string text, double size, IBrush brush, double maxWidth,
            TextAlignment align = TextAlignment.Center, FontWeight weight = FontWeight.Normal)
        {
            var ft = new FormattedText(text ?? string.Empty, CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, new Typeface(DefaultTypeface.FontFamily, FontStyle.Normal, weight),
                size, brush)
            {
                MaxTextWidth = maxWidth,
                TextAlignment = align,
            };
            return ft;
        }

        public static void PaintCard(DrawingContext ctx, CardModel card, Bitmap? bmp,
            float x, float y, float cellW, float cellH, float bleed, float cardW, float cardH,
            bool flipped, bool selected, PrintSettings? printSettings, bool imageIncludesBleed)
        {
            bool hasBackArt = !string.IsNullOrEmpty(card.BackArtworkPath);
            bool showNoBackPlaceholder = flipped && !hasBackArt;

            if (showNoBackPlaceholder)
            {
                PaintNoBackPlaceholder(ctx, x + bleed, y + bleed, cardW, cardH, card.Name);
            }
            else if (bmp != null && imageIncludesBleed)
            {
                // Bled image already contains the bleed margin: fill the whole cell, like the PDF.
                ctx.DrawImage(bmp, new Rect(x, y, cellW, cellH));
            }
            else if (bmp != null)
            {
                ctx.DrawImage(bmp, new Rect(x + bleed, y + bleed, cardW, cardH));

                bool regMarksActive = printSettings?.ShowRegistrationMarks == true;
                if (bleed > 0 && !regMarksActive)
                {
                    // Darken the bleed margin (the ring between the cell and the card) so the preview
                    // shows where the trim sits — exclude the card rect from the cell rect.
                    var ring = new CombinedGeometry(
                        GeometryCombineMode.Exclude,
                        new RectangleGeometry(new Rect(x, y, cellW, cellH)),
                        new RectangleGeometry(new Rect(x + bleed, y + bleed, cardW, cardH)));
                    ctx.DrawGeometry(new SolidColorBrush(Color.FromArgb(40, 0, 0, 0)), null, ring);
                }
            }
            else
            {
                PaintPlaceholder(ctx, card, x + bleed, y + bleed, cardW, cardH, flipped);
            }

            if (!flipped && !string.IsNullOrEmpty(card.OverlayText))
                PaintOverlayText(ctx, card.OverlayText, x + bleed, y + bleed, cardW, cardH);

            if (printSettings is { ShowCardOutline: true, ShowRegistrationMarks: false })
                PaintOutline(ctx, x, y, bleed, cardW, cardH, printSettings);

            if (selected)
            {
                var pen = new Pen(Brushes.DodgerBlue, 3);
                ctx.DrawRectangle(null, pen, new Rect(x, y, cellW, cellH), 2, 2);
            }
        }

        public static void PaintNoBackPlaceholder(DrawingContext ctx, float x, float y, float w, float h, string cardName)
        {
            ctx.DrawRectangle(new SolidColorBrush(Color.FromArgb(255, 120, 120, 120)),
                new Pen(new SolidColorBrush(Color.FromArgb(255, 90, 90, 90)), 1),
                new Rect(x, y, w, h), 4, 4);

            var title = Text("No Back Art\nAssigned", Math.Max(11, w / 12), Brushes.White, w - 10,
                weight: FontWeight.SemiBold);
            ctx.DrawText(title, new Point(x + 5, y + h / 3 - 10));

            var name = Text(cardName, Math.Max(8, w / 18), new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)), w - 10);
            ctx.DrawText(name, new Point(x + 5, y + h / 3 + 30));
        }

        public static void PaintPlaceholder(DrawingContext ctx, CardModel card, float x, float y, float w, float h, bool flipped)
        {
            ctx.DrawRectangle(
                new SolidColorBrush(flipped ? Color.FromArgb(255, 80, 50, 50) : Color.FromArgb(255, 60, 60, 80)),
                new Pen(Brushes.Gray, 1),
                new Rect(x, y, w, h), 4, 4);

            string label = flipped ? "(Back)\n" + card.Name : (string.IsNullOrEmpty(card.Name) ? "No Image" : card.Name);
            var ft = Text(label, Math.Max(10, w / 15), Brushes.White, w - 10);
            ctx.DrawText(ft, new Point(x + 5, y + h / 3));
        }

        public static void PaintCutGuides(DrawingContext ctx, float x, float y, float bleed,
            float cardW, float cardH, float pageTop, float pageW, float pageH)
        {
            float cardLeft = x + bleed, cardTopY = y + bleed;
            float cardRight = cardLeft + cardW, cardBottomY = cardTopY + cardH;
            float pgBottom = pageTop + pageH;

            var pen = new Pen(new SolidColorBrush(Color.FromArgb(120, 0, 0, 0)), 0.5);
            void Line(float x1, float y1, float x2, float y2)
                => ctx.DrawLine(pen, new Point(x1, y1), new Point(x2, y2));

            Line(cardLeft,  pageTop,      cardLeft,  cardTopY);
            Line(cardRight, pageTop,      cardRight, cardTopY);
            Line(cardLeft,  cardBottomY,  cardLeft,  pgBottom);
            Line(cardRight, cardBottomY,  cardRight, pgBottom);
            Line(0,         cardTopY,     cardLeft,  cardTopY);
            Line(cardRight, cardTopY,     pageW,     cardTopY);
            Line(0,         cardBottomY,  cardLeft,  cardBottomY);
            Line(cardRight, cardBottomY,  pageW,     cardBottomY);
        }

        private static void PaintOverlayText(DrawingContext ctx, string text, float x, float y, float cardW, float cardH)
        {
            float bannerH = cardH * 0.15f;
            float bannerY = y + cardH - bannerH - cardH * 0.08f;

            ctx.DrawRectangle(new SolidColorBrush(Color.FromArgb(160, 0, 0, 0)), null,
                new Rect(x, bannerY, cardW, bannerH));

            double fontSize = Math.Max(8, bannerH * 0.55);
            var ft = Text(text, fontSize, Brushes.White, cardW, weight: FontWeight.Bold);
            ctx.DrawText(ft, new Point(x, bannerY + (bannerH - ft.Height) / 2));
        }

        public static void PaintOutline(DrawingContext ctx, float cellX, float cellY,
            float bleed, float cardW, float cardH, PrintSettings ps)
        {
            SolidColorBrush brush;
            try
            {
                string hex = ps.OutlineColor.TrimStart('#');
                byte r = Convert.ToByte(hex[..2], 16);
                byte g = Convert.ToByte(hex[2..4], 16);
                byte b = Convert.ToByte(hex[4..6], 16);
                brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            }
            catch { brush = new SolidColorBrush(Color.FromRgb(0x66, 0xFF, 0x00)); }

            float weight      = ps.LineWeight * 0.5f;
            float radiusPx    = ps.CornerRadiusMm * MmToPx;
            float cornerLenPx = ps.CornerLengthMm * MmToPx;

            float cardLeft = cellX + bleed, cardTop = cellY + bleed;
            float offset = weight / 2;

            float ox, oy, ow, oh;
            switch (ps.OutlineAlignment)
            {
                case OutlineAlignment.Inside:
                    ox = cardLeft + offset; oy = cardTop + offset; ow = cardW - 2 * offset; oh = cardH - 2 * offset; break;
                case OutlineAlignment.Outside:
                    ox = cardLeft - offset; oy = cardTop - offset; ow = cardW + 2 * offset; oh = cardH + 2 * offset; break;
                default:
                    ox = cardLeft; oy = cardTop; ow = cardW; oh = cardH; break;
            }

            var dash = ps.OutlineLineType == LineType.Dashed ? new DashStyle(new double[] { 4, 2 }, 0) : null;
            var pen = new Pen(brush, weight, dash);

            if (ps.OutlineType == OutlineType.Full)
            {
                ctx.DrawRectangle(null, pen, new Rect(ox, oy, ow, oh), radiusPx, radiusPx);
                return;
            }

            float rr = Math.Min(radiusPx, Math.Min(ow / 2, oh / 2));
            float len = Math.Min(cornerLenPx, Math.Min(ow / 2 - rr, oh / 2 - rr));
            if (len <= 0) len = 5;

            if (rr > 0)
            {
                void Corner(Point lineStart, Point arcStart, Point arcEnd, Point lineEnd)
                {
                    var geom = new StreamGeometry();
                    using (var gc = geom.Open())
                    {
                        gc.BeginFigure(lineStart, false);
                        gc.LineTo(arcStart);
                        gc.ArcTo(arcEnd, new Size(rr, rr), 0, false, SweepDirection.Clockwise);
                        gc.LineTo(lineEnd);
                        gc.EndFigure(false);
                    }
                    ctx.DrawGeometry(null, pen, geom);
                }

                Corner(new Point(ox,                 oy + rr + len), new Point(ox,             oy + rr),      new Point(ox + rr,      oy),           new Point(ox + rr + len,      oy));
                Corner(new Point(ox + ow - rr - len, oy),            new Point(ox + ow - rr,    oy),           new Point(ox + ow,      oy + rr),       new Point(ox + ow,            oy + rr + len));
                Corner(new Point(ox + ow,            oy + oh - rr - len), new Point(ox + ow,    oy + oh - rr), new Point(ox + ow - rr, oy + oh),       new Point(ox + ow - rr - len, oy + oh));
                Corner(new Point(ox + rr + len,      oy + oh),       new Point(ox + rr,         oy + oh),      new Point(ox,           oy + oh - rr),  new Point(ox,                 oy + oh - rr - len));
            }
            else
            {
                void Line(float x1, float y1, float x2, float y2)
                    => ctx.DrawLine(pen, new Point(x1, y1), new Point(x2, y2));

                Line(ox,            oy + len,      ox,            oy);      Line(ox,            oy,       ox + len,       oy);
                Line(ox + ow - len, oy,            ox + ow,       oy);      Line(ox + ow,       oy,       ox + ow,        oy + len);
                Line(ox + ow,       oy + oh - len, ox + ow,       oy + oh); Line(ox + ow,       oy + oh,  ox + ow - len,  oy + oh);
                Line(ox + len,      oy + oh,       ox,            oy + oh); Line(ox,            oy + oh,  ox,             oy + oh - len);
            }
        }
    }
}
