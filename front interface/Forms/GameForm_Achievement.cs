using System.Drawing.Drawing2D;

namespace RhythmGame;

public sealed partial class GameForm
{
    private static readonly string[] StatisticsRankLabels = ["S", "A", "B", "C", "D", "F"];

    private void DrawAchievement(Graphics g)
    {
        DrawSettingsBackground(g);

        Color accent = GetAccentColor();
        using var brandFont = new Font("Segoe UI", Math.Max(8f, MenuS(16f)), FontStyle.Regular);
        using var titleFont = new Font("Segoe UI", Math.Max(22f, MenuS(36f)), FontStyle.Regular);
        using var subtitleFont = new Font("Segoe UI", Math.Max(8f, MenuS(13f)), FontStyle.Regular);
        using var labelFont = new Font("Segoe UI", Math.Max(8f, MenuS(12.5f)), FontStyle.Regular);
        using var metricLabelFont = new Font("Segoe UI", Math.Max(8f, MenuS(11.5f)), FontStyle.Regular);
        using var valueFont = new Font("Segoe UI", Math.Max(20f, MenuS(34f)), FontStyle.Regular);
        using var smallFont = new Font("Segoe UI", Math.Max(8f, MenuS(11.5f)), FontStyle.Regular);
        using var textBrush = new SolidBrush(Color.FromArgb(235, 242, 255));
        using var mutedBrush = new SolidBrush(Color.FromArgb(198, 207, 226));
        using var accentBrush = new SolidBrush(accent);

        DrawSettingsBrand(g, MenuX(32f), MenuY(32f), brandFont, mutedBrush);
        DrawStatisticsTopButtons(g, accent);
        DrawGlowingSpacedText(g, "STATISTICS", titleFont, textBrush, MenuX(840f), MenuY(102f), MenuS(16f));
        DrawStatisticsSubtitle(g, subtitleFont, mutedBrush, accent);

        PlayerProgress progress = _playerProgress ?? new PlayerProgress();
        StatisticsSnapshot stats = BuildStatisticsSnapshot(progress, SongData.Load());

        DrawStatisticsPlayerCard(g, GetStatisticsPlayerCardBounds(), stats, labelFont, valueFont, smallFont, textBrush, mutedBrush, accentBrush, accent);

        string[] metricLabels = ["TOTAL PLAYS", "AVG ACCURACY", "MAX COMBO", "BEST RANK"];
        string[] metricValues = [stats.TotalPlaysText, stats.AverageAccuracyText, stats.BestComboText, stats.BestRankText];
        Action<Graphics, Rectangle, Color>[] metricIcons = [DrawPlayGlyph, DrawAimGlyph, DrawLightningGlyph, DrawOutlineStarGlyph];
        for (int i = 0; i < 4; i++)
            DrawStatisticsMetricCard(g, GetStatisticsMetricCardBounds(i), metricLabels[i], metricValues[i], metricIcons[i], i == 3, metricLabelFont, valueFont, mutedBrush, textBrush, accent);

        DrawRecentPerformancePanel(g, GetStatisticsRecentPanelBounds(), stats, labelFont, smallFont, mutedBrush, textBrush, accent);
        DrawRankDistributionPanel(g, GetStatisticsRankPanelBounds(), stats, labelFont, smallFont, mutedBrush, textBrush, accent);
        DrawMenuBottomGlow(g);
        DrawStatisticsBottomBack(g, GetAchievementBackButtonBounds(), labelFont, mutedBrush);
    }

    private void DrawAchievementBackground(Graphics g)
    {
        DrawSettingsBackground(g);
    }

    private void DrawAchievementBackButton(Graphics g, Rectangle bounds, bool hovered)
    {
        using var font = new Font("Segoe UI", Math.Max(8f, MenuS(14f)), FontStyle.Regular);
        using var brush = new SolidBrush(hovered ? Color.White : Color.FromArgb(198, 207, 226));
        DrawStatisticsBottomBack(g, bounds, font, brush);
    }

    private static StatisticsSnapshot BuildStatisticsSnapshot(PlayerProgress progress)
    {
        return BuildStatisticsSnapshot(progress, new SongDataFile());
    }

    private static StatisticsSnapshot BuildStatisticsSnapshot(PlayerProgress progress, SongDataFile songData)
    {
        List<StatisticsSession> sessions = BuildStatisticsSessions(songData);
        int storedPlayCount = songData.Scores.Values.Sum(score => Math.Max(score.PlayCount, score.History.Count));
        int totalPlays = Math.Max(progress.TotalGamesPlayed, Math.Max(storedPlayCount, sessions.Count));
        int bestCombo = Math.Max(
            progress.BestCombo,
            Math.Max(
                sessions.Count == 0 ? 0 : sessions.Max(session => session.MaxCombo),
                songData.Scores.Values.Select(score => score.BestCombo).DefaultIfEmpty(0).Max()));

        float averageAccuracy = CalculateActualAverageAccuracy(progress, sessions);
        ResultGrade? bestGrade = FindBestGrade(progress, songData, sessions, averageAccuracy, bestCombo);
        int playSeconds = Math.Max(progress.TotalPlaySeconds, sessions.Sum(session => session.PlaySeconds));
        int activeDays = Math.Max(
            progress.ActivePlayDateKeys.Count,
            sessions.Where(session => session.PlayedLocalDate.HasValue)
                .Select(session => session.PlayedLocalDate!.Value)
                .Distinct()
                .Count());

        int[] rankCounts = BuildRankCounts(progress, sessions);
        return new StatisticsSnapshot(
            totalPlays.ToString("N0"),
            averageAccuracy > 0f ? $"{averageAccuracy:0.0}%" : "0.0%",
            bestCombo.ToString("N0"),
            bestGrade.HasValue ? ScoreManager.FormatGrade(bestGrade.Value) : "-",
            Math.Max(0, (int)MathF.Round(playSeconds / 60f)),
            Math.Max(0, activeDays),
            BuildRecentAccuracySeries(sessions),
            CalculateRankDistribution(rankCounts));
    }

    private static List<StatisticsSession> BuildStatisticsSessions(SongDataFile songData)
    {
        List<StatisticsSession> sessions = [];
        foreach (SongScoreRecord score in songData.Scores.Values)
        {
            float durationSeconds = songData.Metadata.TryGetValue(score.SongId, out SongMetadata? metadata)
                ? metadata.DurationSeconds
                : 0f;

            foreach (SongPlayHistoryEntry entry in score.History)
            {
                DateTime? localDate = null;
                if (DateTime.TryParse(entry.PlayedUtc, out DateTime parsed))
                    localDate = parsed.ToLocalTime().Date;

                int judged = entry.PerfectCount + entry.GreatCount + entry.BetterCount + entry.GoodCount + entry.BadCount + entry.MissCount;
                sessions.Add(new StatisticsSession(
                    Math.Clamp(entry.Accuracy, 0f, 100f),
                    entry.Grade,
                    Math.Max(0, entry.MaxCombo),
                    Math.Max(0, entry.PerfectCount),
                    Math.Max(0, entry.GreatCount),
                    Math.Max(0, entry.BetterCount),
                    Math.Max(0, entry.GoodCount),
                    Math.Max(0, entry.BadCount),
                    Math.Max(0, entry.MissCount),
                    Math.Max(0, judged),
                    localDate,
                    durationSeconds > 0f ? Math.Max(0, (int)MathF.Round(durationSeconds)) : 0));
            }
        }

        return sessions;
    }

    private static float CalculateActualAverageAccuracy(PlayerProgress progress, IReadOnlyList<StatisticsSession> sessions)
    {
        int perfect = sessions.Sum(session => session.PerfectCount);
        int great = sessions.Sum(session => session.GreatCount);
        int better = sessions.Sum(session => session.BetterCount);
        int good = sessions.Sum(session => session.GoodCount);
        int bad = sessions.Sum(session => session.BadCount);
        int miss = sessions.Sum(session => session.MissCount);
        int judged = perfect + great + better + good + bad + miss;
        if (judged > 0)
            return Math.Clamp(ScoreManager.CalculateWeightedAccuracy(perfect, great, better, good, bad, miss), 0f, 100f);

        if (sessions.Count > 0)
            return Math.Clamp(sessions.Average(session => session.Accuracy), 0f, 100f);

        int exactTotal = progress.TotalPerfectCount +
            progress.TotalGreatCount +
            progress.TotalBetterCount +
            progress.TotalExactGoodCount +
            progress.TotalBadCount +
            progress.TotalMissCount;
        if (exactTotal <= 0)
            return 0f;

        return Math.Clamp(ScoreManager.CalculateWeightedAccuracy(
            progress.TotalPerfectCount,
            progress.TotalGreatCount,
            progress.TotalBetterCount,
            progress.TotalExactGoodCount,
            progress.TotalBadCount,
            progress.TotalMissCount), 0f, 100f);
    }

    private static ResultGrade? FindBestGrade(PlayerProgress progress, SongDataFile songData, IReadOnlyList<StatisticsSession> sessions, float averageAccuracy, int bestCombo)
    {
        ResultGrade? best = null;
        foreach (StatisticsSession session in sessions)
            best = MinGrade(best, TryParseResultGrade(session.Grade));

        foreach (SongScoreRecord score in songData.Scores.Values)
            best = MinGrade(best, TryParseResultGrade(score.BestGrade));

        foreach (string grade in progress.GradeCounts.Keys)
            best = MinGrade(best, TryParseResultGrade(grade));

        if (best is null && averageAccuracy > 0f && bestCombo > 0)
            best = ScoreManager.CalculateGrade(averageAccuracy, progress.TotalMissCount, bestCombo, ScoreManager.CalculateClearType(
                progress.TotalPerfectCount,
                progress.TotalGreatCount,
                progress.TotalBetterCount,
                progress.TotalExactGoodCount,
                progress.TotalBadCount,
                progress.TotalMissCount));

        return best;
    }

    private static int[] BuildRankCounts(PlayerProgress progress, IReadOnlyList<StatisticsSession> sessions)
    {
        int[] counts = new int[StatisticsRankLabels.Length];
        if (sessions.Count > 0)
        {
            foreach (StatisticsSession session in sessions)
                AddGradeCount(counts, session.Grade, 1);
            return counts;
        }

        foreach (var (grade, count) in progress.GradeCounts)
            AddGradeCount(counts, grade, count);
        return counts;
    }

    private static void AddGradeCount(int[] counts, string grade, int count)
    {
        if (count <= 0)
            return;

        ResultGrade? parsed = TryParseResultGrade(grade);
        if (parsed is null)
            return;

        counts[GetRankBucketIndex(parsed.Value)] += count;
    }

    private static int[] CalculateRankDistribution(int[] counts)
    {
        int total = counts.Sum();
        if (total <= 0)
            return new int[StatisticsRankLabels.Length];

        int[] percentages = new int[counts.Length];
        for (int i = 0; i < counts.Length; i++)
            percentages[i] = (int)MathF.Round(counts[i] * 100f / total);
        return percentages;
    }

    private static float[] BuildRecentAccuracySeries(IReadOnlyList<StatisticsSession> sessions)
    {
        float[] values = new float[7];
        if (sessions.Count == 0)
            return values;

        DateTime today = DateTime.Now.Date;
        for (int i = 0; i < values.Length; i++)
        {
            DateTime date = today.AddDays(i - 6);
            var daySessions = sessions
                .Where(session => session.PlayedLocalDate == date)
                .ToArray();
            values[i] = daySessions.Length == 0
                ? 0f
                : Math.Clamp(daySessions.Average(session => session.Accuracy), 0f, 100f);
        }

        return values;
    }

    private static ResultGrade? TryParseResultGrade(string value)
    {
        return value switch
        {
            "S+" => ResultGrade.SPlus,
            "S" => ResultGrade.S,
            "A" => ResultGrade.A,
            "B" => ResultGrade.B,
            "C" => ResultGrade.C,
            "D" => ResultGrade.D,
            "F" => ResultGrade.F,
            _ => null,
        };
    }

    private static ResultGrade? MinGrade(ResultGrade? current, ResultGrade? candidate)
    {
        if (candidate is null)
            return current;
        if (current is null || candidate.Value < current.Value)
            return candidate.Value;
        return current.Value;
    }

    private static int GetRankBucketIndex(ResultGrade grade)
    {
        return grade switch
        {
            ResultGrade.SPlus or ResultGrade.S => 0,
            ResultGrade.A => 1,
            ResultGrade.B => 2,
            ResultGrade.C => 3,
            ResultGrade.D => 4,
            _ => 5,
        };
    }

    private void DrawStatisticsSubtitle(Graphics g, Font font, Brush brush, Color accent)
    {
        float centerX = MenuX(840f);
        float y = MenuY(164f);
        using var pen = new Pen(Color.FromArgb(145, accent), Math.Max(1f, MenuS(1.1f)));
        g.DrawLine(pen, centerX - MenuS(202f), y + MenuS(9f), centerX - MenuS(158f), y + MenuS(9f));
        g.DrawLine(pen, centerX + MenuS(158f), y + MenuS(9f), centerX + MenuS(202f), y + MenuS(9f));
        DrawSpacedString(g, "PLAYER RECORDS", font, brush, centerX, y, MenuS(10f), centered: true);
    }

    private void DrawStatisticsTopButtons(Graphics g, Color accent)
    {
        Rectangle home = GetStatisticsHomeButtonBounds();
        Rectangle settings = GetStatisticsSettingsButtonBounds();
        DrawHomeGlyph(g, home, _hoverAchievementCardIndex == 0 ? Color.White : Color.FromArgb(225, 232, 238, 248));
        DrawMenuGearButton(g, settings, _hoverAchievementCardIndex == 1, accent);
    }

    private void DrawStatisticsPlayerCard(
        Graphics g,
        Rectangle bounds,
        StatisticsSnapshot stats,
        Font labelFont,
        Font valueFont,
        Font smallFont,
        Brush textBrush,
        Brush mutedBrush,
        Brush accentBrush,
        Color accent)
    {
        DrawStatisticsPanel(g, bounds, MenuS(8f));
        Rectangle avatar = new(bounds.Left + (int)MenuS(77f), bounds.Top + (int)MenuS(31f), (int)MenuS(96f), (int)MenuS(96f));
        DrawStatisticsAvatar(g, avatar, accent);
        DrawSpacedStringFitted(g, "PLAYER", labelFont, textBrush, bounds.Left + bounds.Width / 2f, bounds.Top + MenuS(154f), MenuS(9f), bounds.Width - MenuS(48f), centered: true);
        g.DrawString("Lv. 1", smallFont, accentBrush, bounds.Left + MenuS(102f), bounds.Top + MenuS(184f));

        using var divider = new Pen(Color.FromArgb(55, 205, 222, 255), Math.Max(1f, MenuS(1f)));
        g.DrawLine(divider, bounds.Left + MenuS(30f), bounds.Top + MenuS(216f), bounds.Right - MenuS(30f), bounds.Top + MenuS(216f));

        DrawClockGlyph(g, new Rectangle(bounds.Left + (int)MenuS(31f), bounds.Top + (int)MenuS(244f), (int)MenuS(28f), (int)MenuS(28f)), Color.FromArgb(215, 225, 237, 248));
        DrawSpacedStringFitted(g, "TOTAL PLAY TIME", smallFont, mutedBrush, bounds.Left + MenuS(82f), bounds.Top + MenuS(244f), MenuS(3f), bounds.Width - MenuS(104f), centered: false);
        g.DrawString(FormatPlayTime(stats.PlayTimeMinutes), smallFont, accentBrush, bounds.Left + MenuS(82f), bounds.Top + MenuS(273f));

        DrawCalendarGlyph(g, new Rectangle(bounds.Left + (int)MenuS(31f), bounds.Top + (int)MenuS(318f), (int)MenuS(28f), (int)MenuS(28f)), Color.FromArgb(215, 225, 237, 248));
        DrawSpacedStringFitted(g, "ACTIVE DAYS", smallFont, mutedBrush, bounds.Left + MenuS(82f), bounds.Top + MenuS(318f), MenuS(3f), bounds.Width - MenuS(104f), centered: false);
        g.DrawString(stats.ActiveDays.ToString("N0"), smallFont, accentBrush, bounds.Left + MenuS(82f), bounds.Top + MenuS(347f));
    }

    private void DrawStatisticsMetricCard(
        Graphics g,
        Rectangle bounds,
        string label,
        string value,
        Action<Graphics, Rectangle, Color> icon,
        bool rank,
        Font labelFont,
        Font valueFont,
        Brush mutedBrush,
        Brush textBrush,
        Color accent)
    {
        DrawStatisticsPanel(g, bounds, MenuS(8f));
        DrawSpacedStringFitted(g, label, labelFont, mutedBrush, bounds.Left + bounds.Width / 2f, bounds.Top + MenuS(30f), MenuS(5f), bounds.Width - MenuS(34f), centered: true);
        icon(g, new Rectangle(bounds.Left + bounds.Width / 2 - (int)MenuS(14f), bounds.Top + (int)MenuS(77f), (int)MenuS(28f), (int)MenuS(28f)), Color.FromArgb(220, 226, 234, 248));

        using var rankBrush = new SolidBrush(Color.FromArgb(235, 217, 162, 255));
        DrawGlowingMetricValue(g, value, valueFont, rank ? rankBrush : textBrush, bounds.Left + bounds.Width / 2f, bounds.Top + MenuS(130f), rank ? Color.FromArgb(175, 188, 104, 255) : Color.FromArgb(128, 132, 179, 255));

        using var linePen = new Pen(Color.FromArgb(110, accent), Math.Max(1f, MenuS(1f)));
        float y = bounds.Bottom - MenuS(34f);
        g.DrawLine(linePen, bounds.Left + bounds.Width / 2f - MenuS(42f), y, bounds.Left + bounds.Width / 2f + MenuS(42f), y);
    }

    private void DrawRecentPerformancePanel(Graphics g, Rectangle bounds, StatisticsSnapshot stats, Font labelFont, Font smallFont, Brush mutedBrush, Brush textBrush, Color accent)
    {
        DrawStatisticsPanel(g, bounds, MenuS(8f));
        DrawTrendGlyph(g, new Rectangle(bounds.Left + (int)MenuS(28f), bounds.Top + (int)MenuS(26f), (int)MenuS(24f), (int)MenuS(24f)), Color.FromArgb(218, 228, 240, 255));
        DrawSpacedStringFitted(g, "RECENT PERFORMANCE", labelFont, mutedBrush, bounds.Left + MenuS(64f), bounds.Top + MenuS(25f), MenuS(5f), bounds.Width - MenuS(238f), centered: false);
        string axisTitle = "ACCURACY (%)";
        SizeF axisTitleSize = g.MeasureString(axisTitle, smallFont);
        g.DrawString(axisTitle, smallFont, mutedBrush, bounds.Right - MenuS(24f) - axisTitleSize.Width, bounds.Top + MenuS(25f));

        Rectangle chart = new(bounds.Left + (int)MenuS(62f), bounds.Top + (int)MenuS(75f), bounds.Width - (int)MenuS(100f), bounds.Height - (int)MenuS(112f));
        using var gridPen = new Pen(Color.FromArgb(34, 190, 210, 255), Math.Max(1f, MenuS(1f))) { DashStyle = DashStyle.Dot };
        using var axisPen = new Pen(Color.FromArgb(45, 190, 210, 255), Math.Max(1f, MenuS(1f)));
        for (int i = 0; i < 5; i++)
        {
            float y = chart.Top + chart.Height * i / 4f;
            g.DrawLine(gridPen, chart.Left, y, chart.Right, y);
        }
        g.DrawLine(axisPen, chart.Left, chart.Bottom, chart.Right, chart.Bottom);
        g.DrawLine(axisPen, chart.Left, chart.Top, chart.Left, chart.Bottom);

        string[] yLabels = ["100", "75", "50", "25", "0"];
        for (int i = 0; i < yLabels.Length; i++)
            g.DrawString(yLabels[i], smallFont, mutedBrush, bounds.Left + MenuS(24f), chart.Top + chart.Height * i / 4f - MenuS(9f));

        if (stats.RecentAccuracy.Any(v => v > 0f))
            DrawRecentPerformanceLine(g, chart, stats.RecentAccuracy, accent);

        for (int i = 0; i < 7; i++)
        {
            float x = chart.Left + chart.Width * i / 6f;
            g.DrawString((6 - i).ToString(), smallFont, mutedBrush, x - MenuS(5f), chart.Bottom + MenuS(16f));
        }
        g.DrawString("(DAYS AGO)", smallFont, mutedBrush, chart.Right - MenuS(58f), chart.Bottom + MenuS(44f));
    }

    private void DrawRecentPerformanceLine(Graphics g, Rectangle chart, float[] values, Color accent)
    {
        PointF[] points = new PointF[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            float x = chart.Left + chart.Width * i / (values.Length - 1f);
            float y = chart.Bottom - Math.Clamp(values[i] / 100f, 0f, 1f) * chart.Height;
            points[i] = new PointF(x, y);
        }

        using var fillPath = new GraphicsPath();
        fillPath.AddLines(points);
        fillPath.AddLine(points[^1].X, chart.Bottom, points[0].X, chart.Bottom);
        fillPath.CloseFigure();
        using var fill = new LinearGradientBrush(chart, Color.FromArgb(56, accent), Color.FromArgb(0, accent), LinearGradientMode.Vertical);
        g.FillPath(fill, fillPath);

        using var glowPen = new Pen(Color.FromArgb(92, accent), Math.Max(5f, MenuS(5f))) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        using var linePen = new Pen(Color.FromArgb(235, 167, 135, 255), Math.Max(1.4f, MenuS(1.8f))) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        g.DrawLines(glowPen, points);
        g.DrawLines(linePen, points);

        using var dotBrush = new SolidBrush(Color.FromArgb(248, 240, 245, 255));
        foreach (PointF point in points)
            g.FillEllipse(dotBrush, point.X - MenuS(4f), point.Y - MenuS(4f), MenuS(8f), MenuS(8f));
    }

    private void DrawRankDistributionPanel(Graphics g, Rectangle bounds, StatisticsSnapshot stats, Font labelFont, Font smallFont, Brush mutedBrush, Brush textBrush, Color accent)
    {
        DrawStatisticsPanel(g, bounds, MenuS(8f));
        DrawStatsGlyph(g, new Rectangle(bounds.Left + (int)MenuS(28f), bounds.Top + (int)MenuS(25f), (int)MenuS(24f), (int)MenuS(24f)), Color.FromArgb(218, 228, 240, 255));
        DrawSpacedStringFitted(g, "RANK DISTRIBUTION", labelFont, mutedBrush, bounds.Left + MenuS(64f), bounds.Top + MenuS(25f), MenuS(5f), bounds.Width - MenuS(106f), centered: false);

        for (int i = 0; i < StatisticsRankLabels.Length; i++)
        {
            float y = bounds.Top + MenuS(74f + i * 34f);
            using var rankBrush = new SolidBrush(i == 0 ? Color.FromArgb(224, 198, 160, 255) : Color.FromArgb(226, 174, 200, 255));
            g.DrawString(StatisticsRankLabels[i], labelFont, rankBrush, bounds.Left + MenuS(28f), y - MenuS(5f));

            RectangleF track = new(bounds.Left + MenuS(76f), y, MenuS(350f), MenuS(12f));
            using var trackPath = CreateRoundedRect(Rectangle.Round(track), MenuS(3f));
            using var trackBrush = new SolidBrush(Color.FromArgb(34, 80, 100, 150));
            g.FillPath(trackBrush, trackPath);

            RectangleF bar = new(track.Left, track.Top, track.Width * stats.RankDistribution[i] / 100f, track.Height);
            if (bar.Width >= 1f)
            {
                using var barPath = CreateRoundedRect(Rectangle.Round(bar), MenuS(3f));
                using var barBrush = new LinearGradientBrush(bar, i == 0 ? Color.FromArgb(190, 162, 120, 255) : Color.FromArgb(190, 84, 127, 226), Color.FromArgb(150, accent), LinearGradientMode.Horizontal);
                g.FillPath(barBrush, barPath);
            }
            g.DrawString($"{stats.RankDistribution[i]}%", smallFont, textBrush, bounds.Right - MenuS(72f), y - MenuS(7f));
        }
    }

    private void DrawStatisticsPanel(Graphics g, Rectangle bounds, float radius)
    {
        using var shadowPath = CreateRoundedRect(new Rectangle(bounds.X, bounds.Y + (int)MenuS(8f), bounds.Width, bounds.Height), radius);
        using var shadowBrush = new SolidBrush(Color.FromArgb(38, 0, 0, 0));
        g.FillPath(shadowBrush, shadowPath);

        using var path = CreateRoundedRect(bounds, radius);
        using var fill = new LinearGradientBrush(bounds, Color.FromArgb(35, 17, 23, 40), Color.FromArgb(17, 7, 11, 24), LinearGradientMode.Vertical);
        using var border = new Pen(Color.FromArgb(195, 100, 158, 255), Math.Max(1f, MenuS(1.1f)));
        g.FillPath(fill, path);
        g.DrawPath(border, path);
    }

    private void DrawStatisticsAvatar(Graphics g, Rectangle bounds, Color accent)
    {
        using var outer = new Pen(Color.FromArgb(210, 120, 150, 255), Math.Max(1f, MenuS(1.2f)));
        using var inner = new Pen(Color.FromArgb(230, 240, 245, 255), Math.Max(1f, MenuS(1.5f)));
        g.DrawEllipse(outer, bounds);
        Rectangle head = new(bounds.Left + (int)MenuS(34f), bounds.Top + (int)MenuS(24f), (int)MenuS(28f), (int)MenuS(28f));
        g.DrawEllipse(inner, head);
        g.DrawArc(inner, bounds.Left + MenuS(25f), bounds.Top + MenuS(58f), MenuS(46f), MenuS(34f), 200f, 140f);
    }

    private static void DrawSpacedStringFitted(Graphics g, string text, Font font, Brush brush, float x, float y, float spacing, float maxWidth, bool centered)
    {
        maxWidth = Math.Max(1f, maxWidth);
        spacing = Math.Max(0f, spacing);
        Font drawFont = font;
        Font? fittedFont = null;

        float width = MeasureSpacedString(g, text, drawFont, spacing);
        if (width > maxWidth)
        {
            int gapCount = Math.Max(0, text.Length - 1);
            if (gapCount > 0)
            {
                float letterWidth = MeasureSpacedString(g, text, drawFont, 0f);
                spacing = Math.Max(0f, Math.Min(spacing, (maxWidth - letterWidth) / gapCount));
                width = MeasureSpacedString(g, text, drawFont, spacing);
            }

            if (width > maxWidth)
            {
                float scaledSize = Math.Max(7f, drawFont.Size * maxWidth / width);
                if (scaledSize < drawFont.Size)
                {
                    fittedFont = new Font(drawFont.FontFamily, scaledSize, drawFont.Style);
                    drawFont = fittedFont;
                }
            }
        }

        DrawSpacedString(g, text, drawFont, brush, x, y, spacing, centered);
        fittedFont?.Dispose();
    }

    private void DrawStatisticsBottomBack(Graphics g, Rectangle bounds, Font font, Brush brush)
    {
        using var boxPath = CreateRoundedRect(new Rectangle(bounds.Left, bounds.Top, (int)MenuS(44f), bounds.Height), MenuS(4f));
        using var boxFill = new SolidBrush(Color.FromArgb(40, 210, 222, 255));
        using var boxBorder = new Pen(Color.FromArgb(105, 222, 232, 255), Math.Max(1f, MenuS(1f)));
        g.FillPath(boxFill, boxPath);
        g.DrawPath(boxBorder, boxPath);
        g.DrawString("ESC", font, brush, bounds.Left + MenuS(9f), bounds.Top + MenuS(7f));
        DrawSpacedString(g, "BACK", font, brush, bounds.Left + MenuS(66f), bounds.Top + MenuS(8f), MenuS(8f), centered: false);
    }

    private Rectangle GetStatisticsPlayerCardBounds() => MenuRect(136f, 211f, 250f, 398f);
    private Rectangle GetStatisticsMetricCardBounds(int index) => MenuRect(450f + index * 285f, 211f, 255f, 236f);
    private Rectangle GetStatisticsRecentPanelBounds() => MenuRect(450f, 476f, 565f, 308f);
    private Rectangle GetStatisticsRankPanelBounds() => MenuRect(1040f, 476f, 510f, 308f);
    private Rectangle GetStatisticsHomeButtonBounds() => MenuRect(1532f, 34f, 42f, 42f);
    private Rectangle GetStatisticsSettingsButtonBounds() => MenuRect(1592f, 32f, 46f, 46f);

    private Rectangle GetAchievementCardBounds(int index)
    {
        return index switch
        {
            0 => GetStatisticsMetricCardBounds(0),
            1 => GetStatisticsMetricCardBounds(1),
            2 => GetStatisticsMetricCardBounds(2),
            _ => Rectangle.Empty,
        };
    }

    private Rectangle GetAchievementBackButtonBounds()
    {
        return MenuRect(1518f, 862f, 132f, 32f);
    }

    private bool IsAchievementBackButtonHit(Point location)
    {
        return GetAchievementBackButtonBounds().Contains(location);
    }

    private int GetHoveredAchievementCardIndex(Point location)
    {
        if (GetStatisticsHomeButtonBounds().Contains(location))
            return 0;
        if (GetStatisticsSettingsButtonBounds().Contains(location))
            return 1;
        return -1;
    }

    private void HandleAchievementMouseDown(Point location)
    {
        if (IsAchievementBackButtonHit(location) || GetStatisticsHomeButtonBounds().Contains(location))
        {
            _screen = UiScreen.MainMenu;
            Invalidate();
            return;
        }

        if (GetStatisticsSettingsButtonBounds().Contains(location))
        {
            _settingsTabIndex = 0;
            _screen = UiScreen.Settings;
            Invalidate();
        }
    }

    private static string FormatPlayTime(int minutes)
    {
        int hours = minutes / 60;
        int mins = minutes % 60;
        return hours > 0 ? $"{hours}h {mins}m" : $"{mins}m";
    }

    private void DrawGlowingMetricValue(Graphics g, string text, Font font, Brush brush, float centerX, float y, Color glowColor)
    {
        SizeF size = g.MeasureString(text, font);
        float x = centerX - size.Width / 2f;
        for (int i = 4; i >= 1; i--)
        {
            using var glow = new SolidBrush(Color.FromArgb(20 * i, glowColor));
            g.DrawString(text, font, glow, x + MenuS(i * 0.45f), y);
        }
        g.DrawString(text, font, brush, x, y);
    }

    private void DrawHomeGlyph(Graphics g, Rectangle bounds, Color color)
    {
        using var pen = new Pen(color, Math.Max(1.4f, MenuS(1.6f))) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        PointF roofTop = new(bounds.Left + bounds.Width / 2f, bounds.Top + MenuS(5f));
        PointF leftRoof = new(bounds.Left + MenuS(8f), bounds.Top + MenuS(20f));
        PointF rightRoof = new(bounds.Right - MenuS(8f), bounds.Top + MenuS(20f));
        g.DrawLines(pen, [leftRoof, roofTop, rightRoof]);
        RectangleF body = new(bounds.Left + MenuS(12f), bounds.Top + MenuS(19f), bounds.Width - MenuS(24f), bounds.Height - MenuS(12f));
        g.DrawLine(pen, body.Left, body.Top, body.Left, body.Bottom);
        g.DrawLine(pen, body.Right, body.Top, body.Right, body.Bottom);
        g.DrawLine(pen, body.Left, body.Bottom, body.Right, body.Bottom);
        g.DrawLine(pen, bounds.Left + bounds.Width / 2f, body.Bottom, bounds.Left + bounds.Width / 2f, body.Bottom - MenuS(12f));
    }

    private void DrawAimGlyph(Graphics g, Rectangle bounds, Color color)
    {
        using var pen = new Pen(color, Math.Max(1f, MenuS(1.2f))) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawEllipse(pen, bounds);
        Rectangle inner = Rectangle.Inflate(bounds, -(int)MenuS(8f), -(int)MenuS(8f));
        g.DrawEllipse(pen, inner);
        g.DrawLine(pen, bounds.Left + bounds.Width / 2f, bounds.Top - MenuS(4f), bounds.Left + bounds.Width / 2f, bounds.Top + MenuS(6f));
        g.DrawLine(pen, bounds.Left + bounds.Width / 2f, bounds.Bottom - MenuS(6f), bounds.Left + bounds.Width / 2f, bounds.Bottom + MenuS(4f));
        g.DrawLine(pen, bounds.Left - MenuS(4f), bounds.Top + bounds.Height / 2f, bounds.Left + MenuS(6f), bounds.Top + bounds.Height / 2f);
        g.DrawLine(pen, bounds.Right - MenuS(6f), bounds.Top + bounds.Height / 2f, bounds.Right + MenuS(4f), bounds.Top + bounds.Height / 2f);
    }

    private void DrawLightningGlyph(Graphics g, Rectangle bounds, Color color)
    {
        using var pen = new Pen(color, Math.Max(1.2f, MenuS(1.4f))) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        PointF[] points =
        [
            new(bounds.Left + MenuS(17f), bounds.Top + MenuS(1f)),
            new(bounds.Left + MenuS(7f), bounds.Top + MenuS(15f)),
            new(bounds.Left + MenuS(17f), bounds.Top + MenuS(15f)),
            new(bounds.Left + MenuS(11f), bounds.Bottom - MenuS(1f)),
            new(bounds.Right - MenuS(5f), bounds.Top + MenuS(10f)),
            new(bounds.Left + MenuS(18f), bounds.Top + MenuS(10f)),
        ];
        g.DrawLines(pen, points);
    }

    private void DrawOutlineStarGlyph(Graphics g, Rectangle bounds, Color color)
    {
        PointF[] points = BuildStarPoints(bounds);
        using var pen = new Pen(color, Math.Max(1.1f, MenuS(1.3f))) { LineJoin = LineJoin.Round };
        g.DrawPolygon(pen, points);
    }

    private static PointF[] BuildStarPoints(Rectangle bounds)
    {
        PointF center = new(bounds.Left + bounds.Width / 2f, bounds.Top + bounds.Height / 2f);
        float outer = bounds.Width * 0.46f;
        float inner = bounds.Width * 0.20f;
        PointF[] points = new PointF[10];
        for (int i = 0; i < points.Length; i++)
        {
            float radius = i % 2 == 0 ? outer : inner;
            double angle = (-90 + i * 36) * Math.PI / 180.0;
            points[i] = new PointF(center.X + MathF.Cos((float)angle) * radius, center.Y + MathF.Sin((float)angle) * radius);
        }
        return points;
    }

    private void DrawClockGlyph(Graphics g, Rectangle bounds, Color color)
    {
        using var pen = new Pen(color, Math.Max(1f, MenuS(1.2f))) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawEllipse(pen, bounds);
        PointF c = new(bounds.Left + bounds.Width / 2f, bounds.Top + bounds.Height / 2f);
        g.DrawLine(pen, c.X, c.Y, c.X, bounds.Top + MenuS(7f));
        g.DrawLine(pen, c.X, c.Y, c.X + MenuS(7f), c.Y + MenuS(5f));
    }

    private void DrawCalendarGlyph(Graphics g, Rectangle bounds, Color color)
    {
        using var pen = new Pen(color, Math.Max(1f, MenuS(1.2f))) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawRectangle(pen, bounds);
        g.DrawLine(pen, bounds.Left, bounds.Top + MenuS(8f), bounds.Right, bounds.Top + MenuS(8f));
        using var dotBrush = new SolidBrush(color);
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 2; j++)
                g.FillRectangle(dotBrush, bounds.Left + MenuS(7f + i * 8f), bounds.Top + MenuS(14f + j * 7f), MenuS(2f), MenuS(2f));
    }

    private void DrawTrendGlyph(Graphics g, Rectangle bounds, Color color)
    {
        using var pen = new Pen(color, Math.Max(1f, MenuS(1.2f))) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(pen, bounds.Left, bounds.Bottom, bounds.Left, bounds.Top);
        g.DrawLine(pen, bounds.Left, bounds.Bottom, bounds.Right, bounds.Bottom);
        PointF[] line =
        [
            new(bounds.Left + MenuS(4f), bounds.Bottom - MenuS(6f)),
            new(bounds.Left + MenuS(10f), bounds.Bottom - MenuS(14f)),
            new(bounds.Left + MenuS(16f), bounds.Bottom - MenuS(10f)),
            new(bounds.Right - MenuS(4f), bounds.Top + MenuS(5f)),
        ];
        g.DrawLines(pen, line);
    }

    private readonly record struct StatisticsSnapshot(
        string TotalPlaysText,
        string AverageAccuracyText,
        string BestComboText,
        string BestRankText,
        int PlayTimeMinutes,
        int ActiveDays,
        float[] RecentAccuracy,
        int[] RankDistribution);

    private readonly record struct StatisticsSession(
        float Accuracy,
        string Grade,
        int MaxCombo,
        int PerfectCount,
        int GreatCount,
        int BetterCount,
        int GoodCount,
        int BadCount,
        int MissCount,
        int TotalJudgedNotes,
        DateTime? PlayedLocalDate,
        int PlaySeconds);
}
