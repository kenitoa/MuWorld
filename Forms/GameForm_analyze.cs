using System.Drawing.Drawing2D;

namespace RhythmGame;

public sealed partial class GameForm
{
    private void DrawAnalyze(Graphics g)
    {
        DrawAnalyzeBackground(g);

        using var titleFont = new Font("Segoe UI", Math.Max(14f, ScaleY(34f)), FontStyle.Bold);
        using var titleBrush = new SolidBrush(AnalyzeTitle);
        using var noteBrush = new SolidBrush(Color.FromArgb(130, 150, 210));

        // Title: RESULTS ♪
        DrawCentered(g, "RESULTS \u266A", titleFont, titleBrush, (int)ScaleX(DesignWidth / 2f), (int)ScaleY(26f));

        // Main content area
        Rectangle contentBounds = GetAnalyzeContentBounds();
        DrawAnalyzeContentPanel(g, contentBounds);

        // Left section: Song card
        DrawAnalyzeLeftPanel(g, contentBounds);

        // Right section: Detailed Analysis
        DrawAnalyzeRightPanel(g, contentBounds);

        // OK button
        DrawAnalyzeOkButton(g);
    }

    private void DrawAnalyzeBackground(Graphics g)
    {
        Rectangle layoutRect = new(0, 0, (int)ScaleX(DesignWidth), (int)ScaleY(DesignHeight));

        // Soft gradient background
        using var bgBrush = new LinearGradientBrush(layoutRect, AnalyzeBg1, AnalyzeBg2, LinearGradientMode.Vertical);
        g.FillRectangle(bgBrush, layoutRect);

        // Decorative sparkle dots
        using var sparklePen = new Pen(Color.FromArgb(60, 160, 180, 230), Math.Max(1f, ScaleY(1.2f)));
        Random rnd = new(42);
        for (int i = 0; i < 30; i++)
        {
            float sx = ScaleX(rnd.Next(0, (int)DesignWidth));
            float sy = ScaleY(rnd.Next(0, (int)DesignHeight));
            float ss = ScaleX(2f + rnd.Next(0, 4));
            g.DrawEllipse(sparklePen, sx, sy, ss, ss);
        }
    }

    private Rectangle GetAnalyzeContentBounds()
    {
        float margin = 60f;
        float top = 80f;
        float bottom = 740f;
        return Rectangle.Round(new RectangleF(
            ScaleX(margin), ScaleY(top),
            ScaleX(DesignWidth - margin * 2f), ScaleY(bottom - top)));
    }

    private void DrawAnalyzeContentPanel(Graphics g, Rectangle bounds)
    {
        // Shadow
        Rectangle shadow = bounds;
        shadow.Offset(0, (int)ScaleY(6f));
        using (var shadowPath = CreateRoundedRect(shadow, ScaleY(22f)))
        using (var shadowBrush = new SolidBrush(Color.FromArgb(30, 80, 110, 170)))
            g.FillPath(shadowBrush, shadowPath);

        // Panel
        using var path = CreateRoundedRect(bounds, ScaleY(22f));
        using var fillBrush = new LinearGradientBrush(bounds, AnalyzePanelFill1, AnalyzePanelFill2, LinearGradientMode.Vertical);
        using var borderPen = new Pen(AnalyzePanelBorder, Math.Max(1.2f, ScaleY(1.8f)));
        g.FillPath(fillBrush, path);
        g.DrawPath(borderPen, path);
    }

    private void DrawAnalyzeLeftPanel(Graphics g, Rectangle contentBounds)
    {
        float leftW = 340f;
        Rectangle leftBounds = Rectangle.Round(new RectangleF(
            contentBounds.Left + ScaleX(20f),
            contentBounds.Top + ScaleY(20f),
            ScaleX(leftW),
            contentBounds.Height - ScaleY(40f)));

        // Song artwork
        Rectangle artBounds = Rectangle.Round(new RectangleF(
            leftBounds.Left + ScaleX(30f),
            leftBounds.Top + ScaleY(10f),
            ScaleX(240f),
            ScaleY(200f)));
        DrawSongArtwork(g, artBounds, _analyzeSongArtworkStyle);

        // Song title & artist below artwork
        using var songTitleFont = new Font("Segoe UI", Math.Max(10f, ScaleY(16f)), FontStyle.Bold);
        using var songArtistFont = new Font("Segoe UI", Math.Max(8f, ScaleY(12f)), FontStyle.Regular);
        using var songTitleBrush = new SolidBrush(AnalyzeSongTitle);
        using var songArtistBrush = new SolidBrush(AnalyzeSongArtist);

        float textLeft = leftBounds.Left + ScaleX(30f);
        float textTop = artBounds.Bottom + ScaleY(24f);
        g.DrawString(_analyzeSongTitle, songTitleFont, songTitleBrush, textLeft, textTop);
        g.DrawString(_analyzeSongArtist, songArtistFont, songArtistBrush, textLeft, textTop + ScaleY(28f));

        // Stats below song info
        float statsTop = textTop + ScaleY(64f);
        float statsBottom = leftBounds.Bottom - ScaleY(10f);
        DrawAnalyzeLeftStats(g, leftBounds.Left + ScaleX(30f), statsTop, leftBounds.Right - ScaleX(20f), statsBottom);
    }

    private void DrawAnalyzeLeftStats(Graphics g, float left, float top, float right, float bottom)
    {
        using var labelFont = new Font("Segoe UI", Math.Max(6.5f, ScaleY(9.5f)), FontStyle.Regular);
        using var valueFont = new Font("Segoe UI", Math.Max(7.5f, ScaleY(11f)), FontStyle.Bold);
        using var labelBrush = new SolidBrush(AnalyzeStatLabel);
        using var valueBrush = new SolidBrush(AnalyzeStatValue);

        // Stat icon + label + value rows
        (string icon, string label, string value)[] stats =
        [
            ("\u25C6", "Highest Score:", $"{_analyzeHighestScore:N0}"),
            ("\u25CB", "Max Combo:", $"{_analyzeMaxCombo}"),
            ("\u25CE", "Perfect:", $"{_analyzePerfectCount}"),
            ("\u2605", "Great:", $"{_analyzeGreatCount}"),
            ("\u25B2", "Better:", $"{_analyzeBetterCount}"),
            ("\u2714", "Good:", $"{_analyzeGoodCount}"),
            ("\u25AC", "Bad:", $"{_analyzeBadCount}"),
            ("\u2716", "Misses:", $"{_analyzeMissCount}"),
        ];

        float availH = bottom - top;
        float rowHeight = Math.Min(ScaleY(28f), availH / stats.Length);
        float y = top;

        using var iconFont = new Font("Segoe UI", Math.Max(6f, ScaleY(8.5f)), FontStyle.Regular);
        using var iconBrush = new SolidBrush(Color.FromArgb(140, 165, 210));

        foreach (var (icon, label, value) in stats)
        {
            g.DrawString(icon, iconFont, iconBrush, left, y + ScaleY(1f));
            g.DrawString(label, labelFont, labelBrush, left + ScaleX(20f), y);
            SizeF valueSize = g.MeasureString(value, valueFont);
            g.DrawString(value, valueFont, valueBrush, right - valueSize.Width, y);
            y += rowHeight;
        }

        // Miss Streak sub-label
        if (_analyzeMissCount > 0)
        {
            using var subFont = new Font("Segoe UI", Math.Max(6f, ScaleY(8f)), FontStyle.Regular);
            using var subBrush = new SolidBrush(Color.FromArgb(150, 165, 195));
            g.DrawString($"Miss Streak: {_analyzeMissStreak}", subFont, subBrush, left + ScaleX(20f), y - ScaleY(10f));
        }
    }

    private void DrawAnalyzeRightPanel(Graphics g, Rectangle contentBounds)
    {
        float leftW = 370f;
        Rectangle rightBounds = Rectangle.Round(new RectangleF(
            contentBounds.Left + ScaleX(leftW),
            contentBounds.Top + ScaleY(20f),
            contentBounds.Width - ScaleX(leftW + 20f),
            contentBounds.Height - ScaleY(40f)));

        // Song title & artist at top of right panel
        using var songFont = new Font("Segoe UI", Math.Max(10f, ScaleY(17f)), FontStyle.Bold);
        using var artistFont = new Font("Segoe UI", Math.Max(8f, ScaleY(12f)), FontStyle.Regular);
        using var songBrush = new SolidBrush(AnalyzeSongTitle);
        using var artistBrush = new SolidBrush(AnalyzeSongArtist);

        // Stats rows (Score ~ Accuracy: 6 rows)
        float rowTop = rightBounds.Top + ScaleY(10f);
        float availableHeight = rightBounds.Bottom - rowTop;
        DrawAnalyzeDetailRows(g, rightBounds.Left, rowTop, rightBounds.Right, availableHeight);
    }

    private void DrawAnalyzeDetailRows(Graphics g, float left, float top, float right, float availableHeight)
    {
        using var labelFont = new Font("Segoe UI", Math.Max(6.5f, ScaleY(10f)), FontStyle.Bold);
        using var subFont = new Font("Segoe UI", Math.Max(5.5f, ScaleY(7.5f)), FontStyle.Regular);
        using var valueFont = new Font("Segoe UI", Math.Max(8f, ScaleY(12f)), FontStyle.Bold);

        int totalNotes = _analyzePerfectCount + _analyzeGreatCount + _analyzeBetterCount + _analyzeGoodCount + _analyzeBadCount + _analyzeMissCount;
        float perfectPct = totalNotes > 0 ? _analyzePerfectCount * 100f / totalNotes : 0;
        float greatPct = totalNotes > 0 ? _analyzeGreatCount * 100f / totalNotes : 0;
        float betterPct = totalNotes > 0 ? _analyzeBetterCount * 100f / totalNotes : 0;
        float goodPct = totalNotes > 0 ? _analyzeGoodCount * 100f / totalNotes : 0;
        float badPct = totalNotes > 0 ? _analyzeBadCount * 100f / totalNotes : 0;
        float missPct = totalNotes > 0 ? _analyzeMissCount * 100f / totalNotes : 0;
        float accuracy = totalNotes > 0 ? (_analyzePerfectCount * 100f + _analyzeGreatCount * 90f + _analyzeBetterCount * 75f + _analyzeGoodCount * 50f + _analyzeBadCount * 25f) / totalNotes : 0;

        (string icon, string label, string sub, string value)[] rows =
        [
            ("\u2605", $"Score  {_analyzeScore:N0}", $"Highest Score: {_analyzeHighestScore:N0}", $"{_analyzeScore:N0}"),
            ("\u25CB", "Max Combo", "", $"{_analyzeMaxCombo}"),
            ("\u25CE", $"Perfect:  {_analyzePerfectCount} ({perfectPct:F0}%)", "", $"{_analyzePerfectCount} ({perfectPct:F0}%)"),
            ("\u2605", $"Great:  {_analyzeGreatCount} ({greatPct:F0}%)", "", $"{_analyzeGreatCount} ({greatPct:F0}%)"),
            ("\u25B2", $"Better:  {_analyzeBetterCount} ({betterPct:F0}%)", "", $"{_analyzeBetterCount} ({betterPct:F0}%)"),
            ("\u2714", $"Good:  {_analyzeGoodCount} ({goodPct:F0}%)", "", $"{_analyzeGoodCount}"),
            ("\u25AC", $"Bad:  {_analyzeBadCount} ({badPct:F0}%)", "", $"{_analyzeBadCount}"),
            ("\u2716", $"Misses:  {_analyzeMissCount}", _analyzeMissCount > 0 ? $"(Miss Streak: {_analyzeMissStreak})" : "", $"{_analyzeMissCount}"),
            ("\u25D4", $"Accuracy:  {accuracy:F0}%", "", $"{accuracy:F0}%"),
        ];

        // Calculate row height to fit available space evenly
        float rowGap = ScaleY(5f);
        float totalGaps = (rows.Length - 1) * rowGap;
        float rowH = Math.Min(ScaleY(48f), (availableHeight - totalGaps) / rows.Length);
        float radius = ScaleY(10f);
        float y = top;

        using var rowBorderPen = new Pen(AnalyzeRowBorder, Math.Max(1f, ScaleY(1.2f)));

        for (int i = 0; i < rows.Length; i++)
        {
            var row = rows[i];
            bool altBg = i % 2 == 0;
            Rectangle rowBounds = Rectangle.Round(new RectangleF(left, y, right - left, rowH));

            using var rowPath = CreateRoundedRect(rowBounds, radius);
            using var rowFill = new SolidBrush(altBg ? AnalyzeRowAlt1 : AnalyzeRowAlt2);
            g.FillPath(rowFill, rowPath);
            g.DrawPath(rowBorderPen, rowPath);

            // Icon circle
            float iconDiam = Math.Min(ScaleX(20f), rowH - ScaleY(8f));
            Rectangle iconCircle = Rectangle.Round(new RectangleF(
                rowBounds.Left + ScaleX(8f),
                rowBounds.Top + (rowH - iconDiam) / 2f,
                iconDiam, iconDiam));
            using var iconCircleBrush = new LinearGradientBrush(iconCircle, Color.FromArgb(190, 210, 240), Color.FromArgb(155, 180, 220), LinearGradientMode.Vertical);
            using var iconCirclePen = new Pen(Color.FromArgb(170, 195, 228), Math.Max(1f, ScaleY(1.2f)));
            g.FillEllipse(iconCircleBrush, iconCircle);
            g.DrawEllipse(iconCirclePen, iconCircle);

            using var iconBrush = new SolidBrush(Color.White);
            using var smallIconFont = new Font("Segoe UI", Math.Max(5f, ScaleY(6.5f)), FontStyle.Bold);
            DrawCentered(g, row.icon, smallIconFont, iconBrush, iconCircle.Left + iconCircle.Width / 2, iconCircle.Top + (int)(iconDiam * 0.15f));

            // Label text
            float textLeft = rowBounds.Left + ScaleX(36f);
            using var labelBrush = new SolidBrush(AnalyzeLabelColor);

            if (string.IsNullOrEmpty(row.sub))
            {
                // Single line - vertically centered
                g.DrawString(row.label, labelFont, labelBrush, textLeft, rowBounds.Top + (rowH - labelFont.GetHeight(g)) / 2f);
            }
            else
            {
                // Two lines
                float lineH = labelFont.GetHeight(g);
                float subH = subFont.GetHeight(g);
                float totalTextH = lineH + subH;
                float textY = rowBounds.Top + (rowH - totalTextH) / 2f;
                g.DrawString(row.label, labelFont, labelBrush, textLeft, textY);
                using var subBrush = new SolidBrush(Color.FromArgb(140, 160, 195));
                g.DrawString(row.sub, subFont, subBrush, textLeft, textY + lineH);
            }

            // Value on right side - vertically centered
            using var valBrush = new SolidBrush(AnalyzeValueColor);
            SizeF valSize = g.MeasureString(row.value, valueFont);
            g.DrawString(row.value, valueFont, valBrush, rowBounds.Right - valSize.Width - ScaleX(10f), rowBounds.Top + (rowH - valSize.Height) / 2f);

            y += rowH + rowGap;
        }
    }

    private void DrawAnalyzeOkButton(Graphics g)
    {
        Rectangle bounds = GetAnalyzeOkButtonBounds();
        bool hovered = _isAnalyzeOkHovered;
        Color accent = GetAccentColor();

        // Shadow
        Rectangle shadow = bounds;
        shadow.Offset(0, (int)ScaleY(5f));
        using (var shadowPath = CreateRoundedRect(shadow, shadow.Height / 2f))
        using (var shadowBrush = new SolidBrush(Color.FromArgb(40, 60, 96, 144)))
            g.FillPath(shadowBrush, shadowPath);

        Rectangle drawBounds = bounds;
        if (hovered)
            drawBounds.Offset(0, -(int)ScaleY(2f));

        using var path = CreateRoundedRect(drawBounds, drawBounds.Height / 2f);
        using var fillBrush = new LinearGradientBrush(drawBounds, Color.FromArgb(140, accent), Color.FromArgb(98, accent), LinearGradientMode.Vertical);
        using var borderPen = new Pen(Color.FromArgb(128, accent), Math.Max(2f, ScaleY(2.5f)));
        using var innerPen = new Pen(Color.FromArgb(170, 196, 224, 255), Math.Max(1.5f, ScaleY(1.8f)));
        using var textBrush = new SolidBrush(Color.White);

        g.FillPath(fillBrush, path);
        g.DrawPath(borderPen, path);
        using (var inner = CreateRoundedRect(new Rectangle(drawBounds.X + 4, drawBounds.Y + 4, drawBounds.Width - 8, drawBounds.Height - 8), (drawBounds.Height - 8) / 2f))
            g.DrawPath(innerPen, inner);

        using var font = new Font("Segoe UI", Math.Max(12f, ScaleY(24f)), FontStyle.Bold);
        DrawCentered(g, "OK", font, textBrush, drawBounds.Left + drawBounds.Width / 2, drawBounds.Top + (int)ScaleY(14f));
    }

    private Rectangle GetAnalyzeOkButtonBounds()
    {
        float width = 220f;
        float height = 68f;
        return Rectangle.Round(new RectangleF(
            ScaleX(DesignWidth / 2f + 160f),
            ScaleY(755f),
            ScaleX(width), ScaleY(height)));
    }

    private void HandleAnalyzeMouseDown(Point location)
    {
        if (GetAnalyzeOkButtonBounds().Contains(location))
        {
            _audio.PlayMainScreenBgm();
            _screen = UiScreen.MainMenu;
            Invalidate();
        }
    }
}
