using System.Drawing.Drawing2D;

namespace RhythmGame;

public sealed partial class GameForm
{
    private Rectangle GetSongDetailBackButtonBounds()
    {
        return Rectangle.Round(new RectangleF(ScaleX(40f), ScaleY(38f), ScaleX(58f), ScaleY(58f)));
    }

    private Rectangle GetSongDetailFavoriteButtonBounds()
    {
        return Rectangle.Round(new RectangleF(ScaleX(890f), ScaleY(642f), ScaleX(180f), ScaleY(48f)));
    }

    private void DrawSongDetail(Graphics g)
    {
        DrawSongSelectBackground(g);
        DrawBackButton(g, GetSongDetailBackButtonBounds());

        SongEntry? song = GetSelectedSong();
        if (song is null)
            return;

        Rectangle panel = GetCenteredDesignRect(920f, 570f, 118f);
        DrawCard(g, panel);

        Rectangle art = Rectangle.Round(new RectangleF(panel.Left + ScaleX(36f), panel.Top + ScaleY(40f), ScaleX(280f), ScaleY(280f)));
        DrawSongArtwork(g, art, song);

        using var titleFont = new Font("Segoe UI", Math.Max(12f, ScaleY(28f)), FontStyle.Bold);
        using var labelFont = new Font("Segoe UI", Math.Max(9f, ScaleY(13f)), FontStyle.Bold);
        using var valueFont = new Font("Segoe UI", Math.Max(9f, ScaleY(15f)), FontStyle.Regular);
        using var titleBrush = new SolidBrush(PrimaryTextColor);
        using var labelBrush = new SolidBrush(LabelColor);
        using var valueBrush = new SolidBrush(SecondaryTextColor);

        float x = panel.Left + ScaleX(350f);
        float y = panel.Top + ScaleY(42f);
        g.DrawString(song.Title, titleFont, titleBrush, x, y);
        y += ScaleY(48f);
        g.DrawString(song.Artist, valueFont, valueBrush, x, y);
        y += ScaleY(42f);

        SongScoreRecord? score = SongData.TryGetScore(song.SongId);
        DrawSongDetailRow(g, "ID", song.SongId, labelFont, valueFont, labelBrush, valueBrush, x, ref y);
        DrawSongDetailRow(g, "FORMAT", $"{song.Format}  {FormatSongDuration(song.DurationSeconds)}  BPM {(song.Bpm > 0f ? song.Bpm.ToString("F0") : "--")}", labelFont, valueFont, labelBrush, valueBrush, x, ref y);
        DrawSongDetailRow(g, "GENRE", string.IsNullOrWhiteSpace(song.Genre) ? "--" : song.Genre, labelFont, valueFont, labelBrush, valueBrush, x, ref y);
        DrawSongDetailRow(g, "SOURCE", string.IsNullOrWhiteSpace(song.Source) ? "--" : song.Source, labelFont, valueFont, labelBrush, valueBrush, x, ref y);
        DrawSongDetailRow(g, "BEST", $"{song.HighestScore:N0}  {DisplayOrDash(song.BestGrade)}  {DisplayOrDash(song.BestClearType)}", labelFont, valueFont, labelBrush, valueBrush, x, ref y);
        DrawSongDetailRow(g, "PLAY COUNT", $"{song.PlayCount}", labelFont, valueFont, labelBrush, valueBrush, x, ref y);
        DrawSongDetailRow(g, "LAST PLAYED", FormatLastPlayed(song.LastPlayedUtc), labelFont, valueFont, labelBrush, valueBrush, x, ref y);
        DrawSongDetailRow(g, "LOWEST MISS STREAK", score?.LowestMaxMissStreak?.ToString() ?? "--", labelFont, valueFont, labelBrush, valueBrush, x, ref y);

        y += ScaleY(18f);
        DrawSongDetailModes(g, song, score, labelFont, valueFont, labelBrush, valueBrush, panel);
        DrawSongDetailHistory(g, score, labelFont, valueFont, labelBrush, valueBrush, panel);
        DrawSongControlButton(g, GetSongDetailFavoriteButtonBounds(), song.IsFavorite ? "UNFAVORITE" : "FAVORITE", labelFont, titleBrush, false);
    }

    private void DrawSongDetailRow(Graphics g, string label, string value, Font labelFont, Font valueFont, Brush labelBrush, Brush valueBrush, float x, ref float y)
    {
        g.DrawString(label, labelFont, labelBrush, x, y);
        g.DrawString(value, valueFont, valueBrush, x + ScaleX(150f), y - ScaleY(2f));
        y += ScaleY(34f);
    }

    private void DrawSongDetailModes(Graphics g, SongEntry song, SongScoreRecord? score, Font labelFont, Font valueFont, Brush labelBrush, Brush valueBrush, Rectangle panel)
    {
        float x = panel.Left + ScaleX(38f);
        float y = panel.Top + ScaleY(352f);
        g.DrawString("DIFFICULTY RECORDS", labelFont, labelBrush, x, y);
        y += ScaleY(34f);

        string[] difficulties = ["Easy", "Normal", "Hard"];
        for (int difficulty = 0; difficulty < difficulties.Length; difficulty++)
        {
            string key = SongDataStore.GetDifficultyModeKey(difficulty, LaneCount);
            int best = score?.DifficultyHighScores.GetValueOrDefault(key) ?? score?.DifficultyHighScores.GetValueOrDefault(difficulties[difficulty]) ?? 0;
            float acc = score?.DifficultyBestAccuracy.GetValueOrDefault(key) ?? 0f;
            string grade = score?.DifficultyBestGrade.GetValueOrDefault(key) ?? "--";
            string clear = score?.DifficultyBestClearType.GetValueOrDefault(key) ?? "--";
            ChartValidationResult chart = NoteLane.LoadValidatedChart(song.Title, song.Artist, difficulty, LaneCount);
            string text = $"{difficulties[difficulty],-6}  Lv.{chart.Difficulty.Level:00}  {best,8:N0}  {acc,5:F1}%  {grade,-2}  {clear}";
            g.DrawString(text, valueFont, valueBrush, x, y);
            y += ScaleY(28f);
        }
    }

    private void DrawSongDetailHistory(Graphics g, SongScoreRecord? score, Font labelFont, Font valueFont, Brush labelBrush, Brush valueBrush, Rectangle panel)
    {
        float x = panel.Left + ScaleX(350f);
        float y = panel.Top + ScaleY(396f);
        g.DrawString("RECENT HISTORY", labelFont, labelBrush, x, y);
        y += ScaleY(32f);

        IReadOnlyList<SongPlayHistoryEntry> history = score?.History ?? [];
        if (history.Count == 0)
        {
            g.DrawString("--", valueFont, valueBrush, x, y);
            return;
        }

        foreach (SongPlayHistoryEntry entry in history.Take(5))
        {
            string text = $"{FormatHistoryDate(entry.PlayedUtc)}  {entry.ModeKey,-10}  {entry.Score,8:N0}  {entry.Accuracy,5:F1}%  {entry.ClearType}";
            g.DrawString(text, valueFont, valueBrush, x, y);
            y += ScaleY(26f);
        }
    }

    private void HandleSongDetailMouseDown(Point location)
    {
        if (GetSongDetailBackButtonBounds().Contains(location))
        {
            _screen = UiScreen.SongSelect;
            Invalidate();
            return;
        }

        if (GetSongDetailFavoriteButtonBounds().Contains(location))
        {
            ToggleSelectedSongFavorite();
            Invalidate();
        }
    }

    private static string DisplayOrDash(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "--" : value;
    }

    private static string FormatLastPlayed(string value)
    {
        return DateTime.TryParse(value, out DateTime parsed)
            ? parsed.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
            : "--";
    }

    private static string FormatHistoryDate(string value)
    {
        return DateTime.TryParse(value, out DateTime parsed)
            ? parsed.ToLocalTime().ToString("MM-dd HH:mm")
            : "--";
    }
}
