using System.Drawing.Drawing2D;

namespace RhythmGame;

public sealed partial class GameForm
{
    private void DrawAnalyze(Graphics g)
    {
        DrawAnalyzeBackground(g);

        using var logoFont = new Font("Segoe UI Light", Math.Max(18f, ScaleTextY(32f)), FontStyle.Regular);
        using var stageFont = new Font("Segoe UI", Math.Max(16f, ScaleTextY(28f)), FontStyle.Regular);
        using var titleBrush = new SolidBrush(AnalyzeTitle);

        DrawSpacedString(g, "MuWorld", logoFont, titleBrush, ScaleX(DesignWidth / 2f), ScaleY(14f), ScaleX(9f), centered: true);
        DrawHeaderRule(g, ScaleX(DesignWidth / 2f), ScaleY(56f), ScaleX(170f));
        DrawAnalyzeDifficultyBadge(g);

        string stageText = _analyzeClearType == ClearType.Failed ? "STAGE FAILED" : "STAGE CLEAR";
        float stageCenterX = ScaleX(DesignWidth / 2f);
        float stageSpacing = ScaleX(9f);
        DrawSpacedString(g, stageText, stageFont, titleBrush, stageCenterX, ScaleY(122f), stageSpacing, centered: true);
        DrawStageSideLights(g, ScaleY(146f), MeasureSpacedString(g, stageText, stageFont, stageSpacing) / 2f);

        Rectangle panel = GetAnalyzeContentBounds();
        DrawAnalyzeContentPanel(g, panel);
        DrawAnalyzeSongAndGrade(g, panel);
        DrawAnalyzeScoreBlock(g, panel);
        DrawAnalyzeJudgmentPanel(g, panel);
        DrawAnalyzeClearBadge(g, panel);
        DrawAnalyzeLearningSummary(g, panel);
        DrawAnalyzeActionButtons(g, panel);

        if (_analyzeIsNewRecord)
            DrawAnalyzeNewRecordBadge(g, panel.Left + ScaleX(64f), panel.Top + ScaleY(365f));
    }

    private void DrawAnalyzeBackground(Graphics g)
    {
        Rectangle layoutRect = new(0, 0, (int)ScaleX(DesignWidth), (int)ScaleY(DesignHeight));
        using (var bgBrush = new LinearGradientBrush(layoutRect, AnalyzeBg1, AnalyzeBg2, LinearGradientMode.Vertical))
            g.FillRectangle(bgBrush, layoutRect);

        Color accent = GetAccentColor();
        Color starColor = UseHighContrast ? AnalyzeTitle : Color.FromArgb(190, 205, 255);
        using var starBrush = new SolidBrush(Color.FromArgb(130, starColor));
        for (int i = 0; i < 120; i++)
        {
            int hash = i * 1103515245 + 12345;
            float x = ScaleX(Math.Abs(hash % 10000) / 10000f * DesignWidth);
            float y = ScaleY(Math.Abs((hash / 97) % 10000) / 10000f * DesignHeight * 0.78f);
            float size = ScaleY(i % 13 == 0 ? 2.3f : 1.1f);
            starBrush.Color = Color.FromArgb(i % 13 == 0 ? 118 : 54, starColor);
            g.FillEllipse(starBrush, x, y, size, size);
        }

        float horizon = ScaleY(560f);
        using (var aura = new LinearGradientBrush(
            new RectangleF(0, horizon - ScaleY(170f), ScaleX(DesignWidth), ScaleY(260f)),
            Color.FromArgb(0, accent),
            Color.FromArgb(58, accent),
            LinearGradientMode.Vertical))
            g.FillRectangle(aura, 0, horizon - ScaleY(170f), ScaleX(DesignWidth), ScaleY(260f));

        using var mountainBrush = new SolidBrush(UseHighContrast ? AnalyzeBg1 : Color.FromArgb(218, 4, 7, 16));
        PointF[] left =
        [
            new(0, ScaleY(DesignHeight)),
            new(0, horizon + ScaleY(12f)),
            new(ScaleX(120f), horizon - ScaleY(46f)),
            new(ScaleX(280f), horizon + ScaleY(44f)),
            new(ScaleX(455f), horizon + ScaleY(2f)),
            new(ScaleX(575f), ScaleY(DesignHeight)),
        ];
        PointF[] right =
        [
            new(ScaleX(575f), ScaleY(DesignHeight)),
            new(ScaleX(720f), horizon + ScaleY(8f)),
            new(ScaleX(900f), horizon - ScaleY(52f)),
            new(ScaleX(1050f), horizon + ScaleY(34f)),
            new(ScaleX(DesignWidth), horizon - ScaleY(4f)),
            new(ScaleX(DesignWidth), ScaleY(DesignHeight)),
        ];
        g.FillPolygon(mountainBrush, left);
        g.FillPolygon(mountainBrush, right);

        using var roadPen = new Pen(Color.FromArgb(34, accent), Math.Max(1f, ScaleY(1f)));
        for (int i = -5; i <= 5; i++)
        {
            float startX = ScaleX(DesignWidth / 2f + i * 8f);
            float endX = ScaleX(DesignWidth / 2f + i * 94f);
            g.DrawLine(roadPen, startX, horizon, endX, ScaleY(DesignHeight));
        }
    }

    private void DrawHeaderRule(Graphics g, float centerX, float y, float width)
    {
        Color accent = GetAccentColor();
        using var pen = new Pen(Color.FromArgb(90, accent), Math.Max(1f, ScaleY(1f)));
        g.DrawLine(pen, centerX - width / 2f, y, centerX - ScaleX(64f), y);
        g.DrawLine(pen, centerX + ScaleX(64f), y, centerX + width / 2f, y);
    }

    private void DrawStageSideLights(Graphics g, float y, float textHalfWidth)
    {
        Color accent = GetAccentColor();
        float centerX = ScaleX(DesignWidth / 2f);
        float innerGap = textHalfWidth + ScaleX(22f);
        float outerGap = Math.Max(innerGap + ScaleX(24f), ScaleX(275f));
        using var pen = new Pen(Color.FromArgb(80, accent), Math.Max(1f, ScaleY(1.2f)))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        using var glow = new SolidBrush(Color.FromArgb(190, accent));
        g.DrawLine(pen, centerX - outerGap, y, centerX - innerGap, y);
        g.DrawLine(pen, centerX + innerGap, y, centerX + outerGap, y);
        g.FillEllipse(glow, centerX - innerGap - ScaleX(4f), y - ScaleY(2f), ScaleX(8f), ScaleY(4f));
        g.FillEllipse(glow, centerX + innerGap - ScaleX(4f), y - ScaleY(2f), ScaleX(8f), ScaleY(4f));
    }

    private void DrawAnalyzeDifficultyBadge(Graphics g)
    {
        Rectangle bounds = Rectangle.Round(new RectangleF(ScaleX(1048f), ScaleY(32f), ScaleX(74f), ScaleY(30f)));
        Color accent = GetAccentColor();
        using var path = CreateRoundedRect(bounds, ScaleY(5f));
        using var fill = new SolidBrush(AnalyzePanelFill2);
        using var border = new Pen(Color.FromArgb(190, accent), Math.Max(1f, ScaleY(1f)));
        using var font = new Font("Segoe UI", Math.Max(8f, ScaleTextY(11f)), FontStyle.Regular);
        using var brush = new SolidBrush(AnalyzeValueColor);
        g.FillPath(fill, path);
        g.DrawPath(border, path);
        DrawSpacedString(g, GetDifficultyLabel(_songSelectDifficultyIndex), font, brush, bounds.Left + bounds.Width / 2f, bounds.Top + ScaleY(7f), ScaleX(4f), centered: true);
    }

    private Rectangle GetAnalyzeContentBounds()
    {
        return Rectangle.Round(new RectangleF(ScaleX(82f), ScaleY(196f), ScaleX(988f), ScaleY(540f)));
    }

    private void DrawAnalyzeContentPanel(Graphics g, Rectangle bounds)
    {
        Rectangle shadow = new(bounds.Left, bounds.Top + (int)ScaleY(12f), bounds.Width, bounds.Height);
        using (var shadowPath = CreateRoundedRect(shadow, ScaleY(14f)))
        using (var shadowBrush = new SolidBrush(Color.FromArgb(130, 0, 0, 0)))
            g.FillPath(shadowBrush, shadowPath);

        using var path = CreateRoundedRect(bounds, ScaleY(15f));
        using var fill = new LinearGradientBrush(bounds, AnalyzePanelFill1, AnalyzePanelFill2, LinearGradientMode.Vertical);
        using var border = new Pen(AnalyzePanelBorder, Math.Max(1f, ScaleY(1.2f)));
        g.FillPath(fill, path);
        g.DrawPath(border, path);

        using var linePen = new Pen(AnalyzeRowBorder, Math.Max(1f, ScaleY(1f)));
        g.DrawLine(linePen, bounds.Left + ScaleX(456f), bounds.Top + ScaleY(30f), bounds.Left + ScaleX(456f), bounds.Top + ScaleY(404f));
        g.DrawLine(linePen, bounds.Left + ScaleX(24f), bounds.Bottom - ScaleY(130f), bounds.Right - ScaleX(24f), bounds.Bottom - ScaleY(130f));
    }

    private void DrawAnalyzeSongAndGrade(Graphics g, Rectangle panel)
    {
        Rectangle art = Rectangle.Round(new RectangleF(panel.Left + ScaleX(36f), panel.Top + ScaleY(36f), ScaleX(154f), ScaleY(154f)));
        DrawSongArtwork(g, art, _analyzeSongArtworkStyle);

        using var titleFont = new Font("Segoe UI", Math.Max(12f, ScaleTextY(20f)), FontStyle.Regular);
        using var artistFont = new Font("Segoe UI", Math.Max(8f, ScaleTextY(13f)), FontStyle.Regular);
        using var titleBrush = new SolidBrush(AnalyzeSongTitle);
        using var artistBrush = new SolidBrush(AnalyzeSongArtist);
        RectangleF titleBounds = new(panel.Left + ScaleX(220f), panel.Top + ScaleY(40f), ScaleX(218f), ScaleY(32f));
        RectangleF artistBounds = new(panel.Left + ScaleX(220f), panel.Top + ScaleY(84f), ScaleX(218f), ScaleY(22f));
        DrawTrimmedString(g, _analyzeSongTitle, titleFont, titleBrush, titleBounds);
        DrawTrimmedString(g, _analyzeSongArtist, artistFont, artistBrush, artistBounds);

        Rectangle diff = Rectangle.Round(new RectangleF(panel.Left + ScaleX(220f), panel.Top + ScaleY(118f), ScaleX(76f), ScaleY(28f)));
        DrawSmallOutlinePill(g, diff, GetDifficultyLabel(_songSelectDifficultyIndex));

        using var separator = new Pen(AnalyzeRowBorder, Math.Max(1f, ScaleY(1f)));
        g.DrawLine(separator, panel.Left + ScaleX(220f), panel.Top + ScaleY(172f), panel.Left + ScaleX(430f), panel.Top + ScaleY(172f));

        DrawAnalyzeGrade(g, panel);
    }

    private void DrawAnalyzeGrade(Graphics g, Rectangle panel)
    {
        string grade = ScoreManager.FormatGrade(_analyzeGrade);
        Color accent = _analyzeGrade == ResultGrade.F ? Color.FromArgb(255, 94, 106) : GetAccentColor();
        float cx = panel.Left + ScaleX(320f);
        float cy = panel.Top + ScaleY(315f);

        using var ringPen = new Pen(Color.FromArgb(56, accent), Math.Max(1f, ScaleY(1f)));
        for (int i = 0; i < 4; i++)
        {
            float r = ScaleY(48f + i * 18f);
            g.DrawEllipse(ringPen, cx - r, cy - r, r * 2f, r * 2f);
        }

        using var gradeFont = new Font("Segoe UI Light", Math.Max(50f, ScaleTextY(96f)), FontStyle.Regular);
        float gradeHalfWidth = g.MeasureString(grade, gradeFont).Width / 2f + ScaleX(18f);
        float flareLeft = panel.Left + ScaleX(166f);
        float flareRight = panel.Left + ScaleX(438f);
        using var flarePen = new Pen(Color.FromArgb(120, accent), Math.Max(1f, ScaleY(1.2f)))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        if (flareLeft < cx - gradeHalfWidth)
            g.DrawLine(flarePen, flareLeft, cy, cx - gradeHalfWidth, cy);
        if (cx + gradeHalfWidth < flareRight)
            g.DrawLine(flarePen, cx + gradeHalfWidth, cy, flareRight, cy);

        using var glowBrush = new SolidBrush(Color.FromArgb(82, accent));
        using var textBrush = new SolidBrush(AnalyzeValueColor);
        DrawCentered(g, grade, gradeFont, glowBrush, (int)(cx + ScaleX(2f)), (int)(cy - ScaleY(62f) + ScaleY(2f)));
        DrawCentered(g, grade, gradeFont, textBrush, (int)cx, (int)(cy - ScaleY(62f)));
    }

    private void DrawAnalyzeScoreBlock(Graphics g, Rectangle panel)
    {
        float left = panel.Left + ScaleX(492f);
        float top = panel.Top + ScaleY(70f);
        using var labelFont = new Font("Segoe UI", Math.Max(7f, ScaleTextY(12f)), FontStyle.Regular);
        using var scoreFont = new Font("Segoe UI Light", Math.Max(28f, ScaleTextY(42f)), FontStyle.Regular);
        using var valueFont = new Font("Segoe UI Light", Math.Max(22f, ScaleTextY(32f)), FontStyle.Regular);
        using var comboFont = new Font("Segoe UI Light", Math.Max(20f, ScaleTextY(30f)), FontStyle.Regular);
        using var labelBrush = new SolidBrush(AnalyzeLabelColor);
        using var valueBrush = new SolidBrush(AnalyzeValueColor);
        using var linePen = new Pen(AnalyzeRowBorder, Math.Max(1f, ScaleY(1f)));

        DrawSpacedString(g, "SCORE", labelFont, labelBrush, left, top, ScaleX(5f), centered: false);
        g.DrawString(_analyzeScore.ToString("D7"), scoreFont, valueBrush, left, top + ScaleY(34f));
        g.DrawLine(linePen, left, top + ScaleY(110f), panel.Left + ScaleX(728f), top + ScaleY(110f));

        DrawSpacedString(g, "ACCURACY", labelFont, labelBrush, left, top + ScaleY(145f), ScaleX(5f), centered: false);
        g.DrawString($"{_analyzeAccuracy:F2}%", valueFont, valueBrush, left, top + ScaleY(178f));
        g.DrawLine(linePen, left, top + ScaleY(232f), panel.Left + ScaleX(728f), top + ScaleY(232f));

        g.DrawString("MAX COMBO", labelFont, labelBrush, left, top + ScaleY(262f));
        g.DrawString(_analyzeMaxCombo.ToString(), comboFont, valueBrush, left, top + ScaleY(298f));

        float missLeft = left + ScaleX(132f);
        g.DrawLine(linePen, missLeft - ScaleX(18f), top + ScaleY(258f), missLeft - ScaleX(18f), top + ScaleY(342f));
        g.DrawString("MAX MISS", labelFont, labelBrush, missLeft, top + ScaleY(262f));
        g.DrawString(_analyzeMissStreak.ToString(), comboFont, valueBrush, missLeft, top + ScaleY(298f));
    }

    private void DrawAnalyzeJudgmentPanel(Graphics g, Rectangle panel)
    {
        Rectangle bounds = Rectangle.Round(new RectangleF(panel.Left + ScaleX(744f), panel.Top + ScaleY(36f), ScaleX(224f), ScaleY(276f)));
        DrawInsetPanel(g, bounds);

        using var titleFont = new Font("Segoe UI", Math.Max(7f, ScaleTextY(11f)), FontStyle.Regular);
        using var rowFont = new Font("Segoe UI", Math.Max(8f, ScaleTextY(12f)), FontStyle.Bold);
        using var valueFont = new Font("Segoe UI", Math.Max(9f, ScaleTextY(14f)), FontStyle.Regular);
        using var titleBrush = new SolidBrush(AnalyzeLabelColor);
        using var valueBrush = new SolidBrush(AnalyzeValueColor);
        DrawSpacedString(g, "JUDGMENT", titleFont, titleBrush, bounds.Left + bounds.Width / 2f, bounds.Top + ScaleY(26f), ScaleX(7f), centered: true);

        (string label, int value, Color color)[] rows =
        [
            ("PERFECT", _analyzePerfectCount, GetJudgmentAccessibleColor(Judgment.Perfect)),
            ("GREAT", _analyzeGreatCount, GetJudgmentAccessibleColor(Judgment.Great)),
            ("BETTER", _analyzeBetterCount, GetJudgmentAccessibleColor(Judgment.Better)),
            ("GOOD", _analyzeGoodCount, GetJudgmentAccessibleColor(Judgment.Good)),
            ("BAD", _analyzeBadCount, GetJudgmentAccessibleColor(Judgment.Bad)),
            ("MISS", _analyzeMissCount, Color.FromArgb(255, 96, 110)),
        ];

        using var rowLine = new Pen(AnalyzeRowBorder, Math.Max(1f, ScaleY(1f)));
        float y = bounds.Top + ScaleY(62f);
        float rowHeight = ScaleY(34f);
        foreach ((string label, int value, Color color) in rows)
        {
            using var rowBrush = new SolidBrush(color);
            g.DrawString(label, rowFont, rowBrush, bounds.Left + ScaleX(24f), y);
            DrawRightAlignedString(g, value.ToString("D4"), valueFont, valueBrush, bounds.Right - ScaleX(24f), y);
            g.DrawLine(rowLine, bounds.Left + ScaleX(24f), y + ScaleY(24f), bounds.Right - ScaleX(24f), y + ScaleY(24f));
            y += rowHeight;
        }
    }

    private void DrawAnalyzeClearBadge(Graphics g, Rectangle panel)
    {
        Rectangle bounds = Rectangle.Round(new RectangleF(panel.Left + ScaleX(744f), panel.Top + ScaleY(332f), ScaleX(224f), ScaleY(70f)));
        DrawInsetPanel(g, bounds);

        bool failed = _analyzeClearType == ClearType.Failed;
        Color accent = failed ? Color.FromArgb(255, 96, 110) : GetAccentColor();
        Rectangle circle = Rectangle.Round(new RectangleF(bounds.Left + ScaleX(24f), bounds.Top + ScaleY(18f), ScaleX(34f), ScaleY(34f)));
        using var circlePen = new Pen(Color.FromArgb(230, accent), Math.Max(2f, ScaleY(2f)));
        using var glowBrush = new SolidBrush(Color.FromArgb(42, accent));
        g.FillEllipse(glowBrush, Rectangle.Inflate(circle, (int)ScaleX(7f), (int)ScaleY(7f)));
        g.DrawEllipse(circlePen, circle);

        if (failed)
        {
            g.DrawLine(circlePen, circle.Left + ScaleX(10f), circle.Top + ScaleY(10f), circle.Right - ScaleX(10f), circle.Bottom - ScaleY(10f));
            g.DrawLine(circlePen, circle.Right - ScaleX(10f), circle.Top + ScaleY(10f), circle.Left + ScaleX(10f), circle.Bottom - ScaleY(10f));
        }
        else
        {
            g.DrawLine(circlePen, circle.Left + ScaleX(9f), circle.Top + ScaleY(18f), circle.Left + ScaleX(15f), circle.Bottom - ScaleY(9f));
            g.DrawLine(circlePen, circle.Left + ScaleX(15f), circle.Bottom - ScaleY(9f), circle.Right - ScaleX(7f), circle.Top + ScaleY(8f));
        }

        using var font = new Font("Segoe UI", Math.Max(7f, ScaleTextY(11f)), FontStyle.Bold);
        using var smallFont = new Font("Segoe UI", Math.Max(6.5f, ScaleTextY(9f)), FontStyle.Regular);
        using var brush = new SolidBrush(AnalyzeValueColor);
        string clearType = ScoreManager.FormatClearType(_analyzeClearType).ToUpperInvariant();
        g.DrawString(clearType, font, brush, bounds.Left + ScaleX(72f), bounds.Top + ScaleY(12f));
        DrawTrimmedString(g, _analyzeFeedback.FailureCompactLabel, smallFont, brush, new RectangleF(
            bounds.Left + ScaleX(72f),
            bounds.Top + ScaleY(34f),
            bounds.Width - ScaleX(88f),
            ScaleY(18f)));
        DrawAnalyzeMissTimeline(g, bounds);
    }

    private void DrawAnalyzeMissTimeline(Graphics g, Rectangle bounds)
    {
        RectangleF rail = new(
            bounds.Left + ScaleX(72f),
            bounds.Bottom - ScaleY(11f),
            Math.Max(1f, bounds.Width - ScaleX(88f)),
            Math.Max(2f, ScaleY(3f)));
        using var railBrush = new SolidBrush(AnalyzeRowBorder);
        using var missBrush = new SolidBrush(Color.FromArgb(240, 255, 92, 108));
        g.FillRectangle(railBrush, rail);
        foreach (float position in _analyzeFeedback.MissPositions)
        {
            float x = rail.Left + Math.Clamp(position, 0f, 1f) * rail.Width;
            g.FillRectangle(missBrush, x - ScaleX(1f), rail.Top - ScaleY(2f), Math.Max(2f, ScaleX(2f)), rail.Height + ScaleY(4f));
        }
    }

    private void DrawAnalyzeLearningSummary(Graphics g, Rectangle panel)
    {
        using var font = new Font("Segoe UI", Math.Max(7f, ScaleTextY(10.5f)), FontStyle.Bold);
        using var brush = new SolidBrush(GetAnalyzeLearningSummaryColor());
        string resultMessage = $"{_analyzeFeedback.TimingLabel}  |  {_analyzeFeedback.NextGoal}";
        string message = string.IsNullOrWhiteSpace(_analyzeReplayStatus)
            ? resultMessage
            : $"{_analyzeReplayStatus}  |  {resultMessage}";
        RectangleF bounds = new(
            panel.Left + ScaleX(38f),
            panel.Bottom - ScaleY(125f),
            panel.Width - ScaleX(76f),
            ScaleY(24f));
        using var path = CreateRoundedRect(bounds, ScaleY(5f));
        using var fill = new LinearGradientBrush(bounds, AnalyzeRowAlt1, AnalyzeRowAlt2, LinearGradientMode.Vertical);
        using var border = new Pen(AnalyzeRowBorder, Math.Max(1f, ScaleY(1f)));
        using var format = new StringFormat(StringFormatFlags.NoWrap)
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter,
        };
        g.FillPath(fill, path);
        g.DrawPath(border, path);
        g.DrawString(message, font, brush, bounds, format);
    }

    private Color GetAnalyzeLearningSummaryColor()
    {
        if (string.IsNullOrWhiteSpace(_analyzeReplayStatus))
            return GetTimingSummaryColor();

        if (_analyzeReplayStatus.Contains("MISMATCH", StringComparison.OrdinalIgnoreCase) ||
            _analyzeReplayStatus.Contains("FAILED", StringComparison.OrdinalIgnoreCase) ||
            _analyzeReplayStatus.Contains("NOT SAVED", StringComparison.OrdinalIgnoreCase))
        {
            return UseHighContrast ? Color.Red : Color.FromArgb(255, 96, 110);
        }

        if (_analyzeReplayStatus.Contains("VERIFIED", StringComparison.OrdinalIgnoreCase))
            return UseHighContrast ? Color.Lime : Color.FromArgb(126, 230, 178);

        return GetTimingSummaryColor();
    }

    private Color GetTimingSummaryColor()
    {
        if (UseHighContrast)
            return Color.White;

        if (_analyzeFeedback.TimingLabel.StartsWith("EARLY", StringComparison.Ordinal))
            return Color.FromArgb(118, 198, 255);
        if (_analyzeFeedback.TimingLabel.StartsWith("LATE", StringComparison.Ordinal))
            return Color.FromArgb(255, 178, 112);
        return Color.FromArgb(126, 230, 178);
    }

    private void DrawAnalyzeActionButtons(Graphics g, Rectangle panel)
    {
        string[] labels = ["RETRY", "SONG SELECT", "NEXT"];
        for (int i = 0; i < labels.Length; i++)
        {
            bool enabled = i != 2 || CanPlayNextSong();
            DrawAnalyzeActionButton(g, GetAnalyzeActionButtonBounds(i), labels[i], enabled && _hoverAnalyzeAction == i, i == 2, enabled);
        }
    }

    private void DrawAnalyzeActionButton(Graphics g, Rectangle bounds, string label, bool hovered, bool primary, bool enabled)
    {
        Color accent = primary ? GetAccentColor() : Color.FromArgb(112, 154, 234);
        Rectangle drawBounds = bounds;
        if (hovered && !_reducedMotionEnabled)
            drawBounds.Offset(0, -(int)ScaleY(2f));

        using var path = CreateRoundedRect(drawBounds, ScaleY(6f));
        using var fill = new LinearGradientBrush(
            drawBounds,
            Color.FromArgb(enabled ? primary ? 72 : 36 : 12, accent),
            Color.FromArgb(enabled ? primary ? 24 : 16 : 8, accent),
            LinearGradientMode.Vertical);
        using var border = new Pen(Color.FromArgb(enabled ? hovered || primary ? 220 : 140 : 54, accent), Math.Max(1f, primary ? ScaleY(1.7f) : ScaleY(1.1f)));
        using var textBrush = new SolidBrush(enabled ? AnalyzeValueColor : Color.FromArgb(104, AnalyzeValueColor));
        g.FillPath(fill, path);
        g.DrawPath(border, path);

        using var iconFont = new Font("Segoe UI", Math.Max(12f, ScaleTextY(18f)), FontStyle.Regular);
        using var labelFont = new Font("Segoe UI", Math.Max(8f, ScaleTextY(12f)), FontStyle.Regular);
        string icon = label switch
        {
            "RETRY" => "R",
            "SONG SELECT" => "=",
            _ => ">"
        };
        DrawCentered(g, icon, iconFont, textBrush, bounds.Left + (int)ScaleX(46f), bounds.Top + (int)ScaleY(22f));
        DrawCentered(g, label, labelFont, textBrush, bounds.Left + bounds.Width / 2 + (int)ScaleX(18f), bounds.Top + (int)ScaleY(27f));
    }

    private Rectangle GetAnalyzeActionButtonBounds(int index)
    {
        Rectangle panel = GetAnalyzeContentBounds();
        float buttonWidth = 252f;
        float buttonHeight = 68f;
        float gap = 30f;
        float left = panel.Left / _layoutScale + 68f + index * (buttonWidth + gap);
        float top = panel.Bottom / _layoutScale - 100f;
        return Rectangle.Round(new RectangleF(ScaleX(left), ScaleY(top), ScaleX(buttonWidth), ScaleY(buttonHeight)));
    }

    private Rectangle GetAnalyzeOkButtonBounds() => GetAnalyzeActionButtonBounds(1);

    private int GetAnalyzeActionAt(Point location)
    {
        for (int i = 0; i < 3; i++)
        {
            if (i == 2 && !CanPlayNextSong())
                continue;
            if (GetAnalyzeActionButtonBounds(i).Contains(location))
                return i;
        }

        return -1;
    }

    private void HandleAnalyzeMouseDown(Point location)
    {
        int action = GetAnalyzeActionAt(location);
        if (action < 0)
            return;

        _hoverAnalyzeAction = action;
        ActivateAnalyzeAction(action);
    }

    private void ActivateAnalyzeAction(int action)
    {
        switch (action)
        {
            case 0:
                if (!TryRestoreAnalyzeSongSelection())
                {
                    _screen = UiScreen.SongSelect;
                    Invalidate();
                    return;
                }
                _screen = UiScreen.SongSelect;
                BeginGame();
                break;
            case 1:
                _audio.PlayMainScreenBgm();
                _screen = UiScreen.SongSelect;
                _previewSongKey = string.Empty;
                Invalidate();
                break;
            case 2:
                int playedIndex = FindAnalyzeSongIndex();
                SongEntry[] songs = GetFilteredSongs();
                if (playedIndex < 0 || playedIndex >= songs.Length - 1)
                    return;
                _songSelectSelectedIndex = playedIndex + 1;
                _songSelectPageIndex = _songSelectSelectedIndex / SongRowsPerPage;
                _previewSongKey = string.Empty;
                _screen = UiScreen.SongSelect;
                BeginGame();
                break;
        }
    }

    private bool CanPlayNextSong()
    {
        SongEntry[] songs = GetFilteredSongs();
        int playedIndex = FindAnalyzeSongIndex(songs);
        return playedIndex >= 0 && playedIndex < songs.Length - 1;
    }

    private bool TryRestoreAnalyzeSongSelection()
    {
        SongEntry[] songs = GetFilteredSongs();
        int playedIndex = FindAnalyzeSongIndex(songs);
        if (playedIndex < 0)
            return false;

        _songSelectSelectedIndex = playedIndex;
        _songSelectPageIndex = playedIndex / SongRowsPerPage;
        _previewSongKey = string.Empty;
        return true;
    }

    private int FindAnalyzeSongIndex()
    {
        return FindAnalyzeSongIndex(GetFilteredSongs());
    }

    private int FindAnalyzeSongIndex(IReadOnlyList<SongEntry> songs)
    {
        if (string.IsNullOrWhiteSpace(_analyzeSongId))
            return -1;

        for (int i = 0; i < songs.Count; i++)
        {
            if (string.Equals(songs[i].SongId, _analyzeSongId, StringComparison.Ordinal))
                return i;
        }

        return -1;
    }

    private void DrawInsetPanel(Graphics g, Rectangle bounds)
    {
        using var path = CreateRoundedRect(bounds, ScaleY(9f));
        using var fill = new LinearGradientBrush(bounds, AnalyzeRowAlt1, AnalyzeRowAlt2, LinearGradientMode.Vertical);
        using var border = new Pen(AnalyzeRowBorder, Math.Max(1f, ScaleY(1f)));
        g.FillPath(fill, path);
        g.DrawPath(border, path);
    }

    private void DrawSmallOutlinePill(Graphics g, Rectangle bounds, string text)
    {
        Color accent = GetAccentColor();
        using var path = CreateRoundedRect(bounds, ScaleY(4f));
        using var fill = new SolidBrush(Color.FromArgb(34, accent));
        using var border = new Pen(Color.FromArgb(190, accent), Math.Max(1f, ScaleY(1f)));
        using var font = new Font("Segoe UI", Math.Max(7f, ScaleTextY(10f)), FontStyle.Regular);
        using var brush = new SolidBrush(AnalyzeValueColor);
        g.FillPath(fill, path);
        g.DrawPath(border, path);
        DrawSpacedString(g, text, font, brush, bounds.Left + bounds.Width / 2f, bounds.Top + ScaleY(7f), ScaleX(4f), centered: true);
    }

    private void DrawAnalyzeNewRecordBadge(Graphics g, float x, float y)
    {
        Rectangle bounds = Rectangle.Round(new RectangleF(x, y, ScaleX(122f), ScaleY(26f)));
        Color accent = GetAccentColor();
        using var path = CreateRoundedRect(bounds, ScaleY(5f));
        using var fill = new SolidBrush(Color.FromArgb(76, accent));
        using var border = new Pen(Color.FromArgb(200, accent), Math.Max(1f, ScaleY(1f)));
        using var font = new Font("Segoe UI", Math.Max(7f, ScaleTextY(10f)), FontStyle.Bold);
        using var brush = new SolidBrush(Color.White);
        g.FillPath(fill, path);
        g.DrawPath(border, path);
        DrawCentered(g, "NEW RECORD", font, brush, bounds.Left + bounds.Width / 2, bounds.Top + (int)ScaleY(6f));
    }

    private static void DrawTrimmedString(Graphics g, string text, Font font, Brush brush, RectangleF bounds)
    {
        using var format = new StringFormat(StringFormatFlags.NoWrap)
        {
            Trimming = StringTrimming.EllipsisCharacter,
            Alignment = StringAlignment.Near,
            LineAlignment = StringAlignment.Near,
        };
        g.DrawString(text, font, brush, bounds, format);
    }

    private static void DrawRightAlignedString(Graphics g, string text, Font font, Brush brush, float right, float y)
    {
        SizeF size = g.MeasureString(text, font);
        g.DrawString(text, font, brush, right - size.Width, y);
    }

    private static string TrimForDisplay(string value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length <= maxLength)
            return value;

        return value[..Math.Max(0, maxLength - 1)] + ".";
    }
}
