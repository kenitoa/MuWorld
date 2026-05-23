using System.Drawing.Drawing2D;

namespace RhythmGame;

public sealed partial class GameForm
{
    private void DrawAnalyze(Graphics g)
    {
        DrawAnalyzeBackground(g);

        using var titleFont = new Font("Segoe UI", Math.Max(14f, ScaleTextY(34f)), FontStyle.Bold);
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
        using var songTitleFont = new Font("Segoe UI", Math.Max(10f, ScaleTextY(16f)), FontStyle.Bold);
        using var songArtistFont = new Font("Segoe UI", Math.Max(8f, ScaleTextY(12f)), FontStyle.Regular);
        using var songTitleBrush = new SolidBrush(AnalyzeSongTitle);
        using var songArtistBrush = new SolidBrush(AnalyzeSongArtist);

        float textLeft = leftBounds.Left + ScaleX(30f);
        float textTop = artBounds.Bottom + ScaleY(24f);
        g.DrawString(_analyzeSongTitle, songTitleFont, songTitleBrush, textLeft, textTop);
        g.DrawString(_analyzeSongArtist, songArtistFont, songArtistBrush, textLeft, textTop + ScaleY(28f));
        if (_analyzeIsNewRecord)
            DrawAnalyzeNewRecordBadge(g, textLeft, textTop + ScaleY(52f));

        // Stats below song info
        float statsTop = textTop + ScaleY(_analyzeIsNewRecord ? 82f : 64f);
        float statsBottom = leftBounds.Bottom - ScaleY(10f);
        DrawAnalyzeLeftStats(g, leftBounds.Left + ScaleX(30f), statsTop, leftBounds.Right - ScaleX(20f), statsBottom);
    }

    private void DrawAnalyzeNewRecordBadge(Graphics g, float x, float y)
    {
        Rectangle bounds = Rectangle.Round(new RectangleF(x, y, ScaleX(132f), ScaleY(24f)));
        Color accent = GetAccentColor();
        using var path = CreateRoundedRect(bounds, ScaleY(8f));
        using var fill = new LinearGradientBrush(bounds, Color.FromArgb(190, accent), Color.FromArgb(105, accent), LinearGradientMode.Vertical);
        using var border = new Pen(Color.FromArgb(180, 255, 255, 255), Math.Max(1f, ScaleY(1.1f)));
        using var font = new Font("Segoe UI", Math.Max(6.5f, ScaleTextY(9f)), FontStyle.Bold);
        using var brush = new SolidBrush(Color.White);
        g.FillPath(fill, path);
        g.DrawPath(border, path);
        DrawCentered(g, "NEW RECORD", font, brush, bounds.Left + bounds.Width / 2, bounds.Top + (int)ScaleY(5f));
    }

    private void DrawAnalyzeLeftStats(Graphics g, float left, float top, float right, float bottom)
    {
        using var labelFont = new Font("Segoe UI", Math.Max(6.5f, ScaleTextY(9.5f)), FontStyle.Regular);
        using var valueFont = new Font("Segoe UI", Math.Max(7.5f, ScaleTextY(11f)), FontStyle.Bold);
        using var labelBrush = new SolidBrush(AnalyzeStatLabel);
        using var valueBrush = new SolidBrush(AnalyzeStatValue);

        // Stat icon + label + value rows
        (string icon, string label, string value)[] stats =
        [
            ("\u25C6", "Highest Score:", $"{_analyzeHighestScore:N0}"),
            ("\u25D4", "Grade:", ScoreManager.FormatGrade(_analyzeGrade)),
            ("\u25C7", "Clear:", ScoreManager.FormatClearType(_analyzeClearType)),
            ("\u25AC", "Groove:", $"{_analyzeGrooveGauge:F0}%"),
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

        using var iconFont = new Font("Segoe UI", Math.Max(6f, ScaleTextY(8.5f)), FontStyle.Regular);
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
            using var subFont = new Font("Segoe UI", Math.Max(6f, ScaleTextY(8f)), FontStyle.Regular);
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
        using var songFont = new Font("Segoe UI", Math.Max(10f, ScaleTextY(17f)), FontStyle.Bold);
        using var artistFont = new Font("Segoe UI", Math.Max(8f, ScaleTextY(12f)), FontStyle.Regular);
        using var songBrush = new SolidBrush(AnalyzeSongTitle);
        using var artistBrush = new SolidBrush(AnalyzeSongArtist);

        DrawAnalyzeDistributionBar(g, rightBounds.Left, rightBounds.Top + ScaleY(8f), rightBounds.Right, ScaleY(42f));

        // Stats rows
        float rowTop = rightBounds.Top + ScaleY(58f);
        float availableHeight = rightBounds.Bottom - rowTop;
        DrawAnalyzeDetailRows(g, rightBounds.Left, rowTop, rightBounds.Right, availableHeight);
    }

    private void DrawAnalyzeDistributionBar(Graphics g, float left, float top, float right, float height)
    {
        int total = _analyzePerfectCount + _analyzeGreatCount + _analyzeBetterCount + _analyzeGoodCount + _analyzeBadCount + _analyzeMissCount;
        Rectangle labelBounds = Rectangle.Round(new RectangleF(left, top, right - left, ScaleY(14f)));
        using var labelFont = new Font("Segoe UI", Math.Max(6f, ScaleTextY(8.5f)), FontStyle.Bold);
        using var labelBrush = new SolidBrush(Color.FromArgb(158, 178, 210));
        g.DrawString("JUDGMENT DISTRIBUTION", labelFont, labelBrush, labelBounds.Left, labelBounds.Top);

        Rectangle bar = Rectangle.Round(new RectangleF(left, top + ScaleY(18f), right - left, Math.Max(ScaleY(16f), height - ScaleY(20f))));
        using var bgPath = CreateRoundedRect(bar, ScaleY(7f));
        using var bg = new SolidBrush(Color.FromArgb(65, 20, 28, 48));
        using var border = new Pen(Color.FromArgb(95, 120, 150, 205), Math.Max(1f, ScaleY(1f)));
        g.FillPath(bg, bgPath);
        g.DrawPath(border, bgPath);

        if (total <= 0)
            return;

        (int count, Color color)[] parts =
        [
            (_analyzePerfectCount, GetJudgmentAccessibleColor(Judgment.Perfect)),
            (_analyzeGreatCount, GetJudgmentAccessibleColor(Judgment.Great)),
            (_analyzeBetterCount, GetJudgmentAccessibleColor(Judgment.Better)),
            (_analyzeGoodCount, GetJudgmentAccessibleColor(Judgment.Good)),
            (_analyzeBadCount, GetJudgmentAccessibleColor(Judgment.Bad)),
            (_analyzeMissCount, UseHighContrast ? Color.FromArgb(255, 230, 0) : Color.FromArgb(255, 72, 80)),
        ];

        int x = bar.Left + 1;
        int remainingWidth = bar.Width - 2;
        for (int i = 0; i < parts.Length; i++)
        {
            int width = i == parts.Length - 1
                ? remainingWidth
                : Math.Max(0, (int)Math.Round((bar.Width - 2) * (parts[i].count / (float)total)));
            width = Math.Min(width, remainingWidth);
            if (width > 0)
            {
                using var brush = new SolidBrush(Color.FromArgb(210, parts[i].color));
                g.FillRectangle(brush, x, bar.Top + 1, width, bar.Height - 2);
            }
            x += width;
            remainingWidth -= width;
        }
    }

    private void DrawAnalyzeDetailRows(Graphics g, float left, float top, float right, float availableHeight)
    {
        using var labelFont = new Font("Segoe UI", Math.Max(6.5f, ScaleTextY(10f)), FontStyle.Bold);
        using var subFont = new Font("Segoe UI", Math.Max(5.5f, ScaleTextY(7.5f)), FontStyle.Regular);
        using var valueFont = new Font("Segoe UI", Math.Max(8f, ScaleTextY(12f)), FontStyle.Bold);

        int totalNotes = _analyzePerfectCount + _analyzeGreatCount + _analyzeBetterCount + _analyzeGoodCount + _analyzeBadCount + _analyzeMissCount;
        float perfectPct = totalNotes > 0 ? _analyzePerfectCount * 100f / totalNotes : 0;
        float greatPct = totalNotes > 0 ? _analyzeGreatCount * 100f / totalNotes : 0;
        float betterPct = totalNotes > 0 ? _analyzeBetterCount * 100f / totalNotes : 0;
        float goodPct = totalNotes > 0 ? _analyzeGoodCount * 100f / totalNotes : 0;
        float badPct = totalNotes > 0 ? _analyzeBadCount * 100f / totalNotes : 0;
        float missPct = totalNotes > 0 ? _analyzeMissCount * 100f / totalNotes : 0;
        float accuracy = _analyzeAccuracy;
        string avgTiming = _analyzeAverageTimingMs < -3
            ? $"EARLY {Math.Abs(_analyzeAverageTimingMs)}ms"
            : _analyzeAverageTimingMs > 3
                ? $"LATE {_analyzeAverageTimingMs}ms"
                : "SYNC";

        (string icon, string label, string sub, string value)[] rows =
        [
            ("\u2605", $"Score  {_analyzeScore:N0}", $"Highest Score: {_analyzeHighestScore:N0}", $"{_analyzeScore:N0}"),
            ("\u25D4", $"Grade:  {ScoreManager.FormatGrade(_analyzeGrade)}", ScoreManager.FormatClearType(_analyzeClearType), ScoreManager.FormatGrade(_analyzeGrade)),
            ("\u25C7", $"Clear Type:  {ScoreManager.FormatClearType(_analyzeClearType)}", $"Max Miss Streak: {_analyzeMissStreak}", ScoreManager.FormatClearType(_analyzeClearType)),
            ("\u25AC", $"Groove:  {_analyzeGrooveGauge:F0}%", $"{_analyzePlayMode} / Clear {_analyzeGaugeClearThreshold:F0}%", $"{_analyzeGrooveGauge:F0}%"),
            ("\u25CB", "Max Combo", "", $"{_analyzeMaxCombo}"),
            ("\u25CE", $"Perfect:  {_analyzePerfectCount} ({perfectPct:F0}%)", "", $"{_analyzePerfectCount} ({perfectPct:F0}%)"),
            ("\u2605", $"Great:  {_analyzeGreatCount} ({greatPct:F0}%)", "", $"{_analyzeGreatCount} ({greatPct:F0}%)"),
            ("\u25B2", $"Better:  {_analyzeBetterCount} ({betterPct:F0}%)", "", $"{_analyzeBetterCount} ({betterPct:F0}%)"),
            ("\u2714", $"Good:  {_analyzeGoodCount} ({goodPct:F0}%)", "", $"{_analyzeGoodCount}"),
            ("\u25AC", $"Bad:  {_analyzeBadCount} ({badPct:F0}%)", "", $"{_analyzeBadCount}"),
            ("\u2716", $"Misses:  {_analyzeMissCount}", _analyzeMissCount > 0 ? $"(Miss Streak: {_analyzeMissStreak})" : "", $"{_analyzeMissCount}"),
            ("\u25C1", $"Early:  {_analyzeEarlyCount}", "", $"{_analyzeEarlyCount}"),
            ("\u25B7", $"Late:  {_analyzeLateCount}", "", $"{_analyzeLateCount}"),
            ("\u25C7", $"Avg Timing:  {avgTiming}", "Use Audio Offset when the bias is consistent.", avgTiming),
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
            using var smallIconFont = new Font("Segoe UI", Math.Max(5f, ScaleTextY(6.5f)), FontStyle.Bold);
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

        using var font = new Font("Segoe UI", Math.Max(12f, ScaleTextY(24f)), FontStyle.Bold);
        DrawCentered(g, "OK", font, textBrush, drawBounds.Left + drawBounds.Width / 2, drawBounds.Top + (int)ScaleY(14f));
    }

    private Rectangle GetAnalyzeOkButtonBounds()
    {
        float width = 220f;
        float height = 68f;
        return Rectangle.Round(new RectangleF(
            ScaleX(DesignWidth / 2f + 160f),
            ScaleY(674f),
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
