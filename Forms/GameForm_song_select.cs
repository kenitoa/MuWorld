using System.Diagnostics;
using System.Drawing.Drawing2D;

namespace RhythmGame;

public sealed partial class GameForm
{
    private const int SongRowsPerPage = 5;

    private sealed record SongEntry(
        string SongId,
        string Title,
        string Artist,
        int ArtworkStyle,
        string FilePath,
        string Format,
        float DurationSeconds,
        float Bpm,
        float PreviewStart,
        float PreviewEnd,
        string Genre,
        string Source,
        string BgaPath,
        string CoverPath,
        int HighestScore,
        string BestGrade,
        string BestClearType,
        bool IsFavorite,
        int PlayCount,
        string LastPlayedUtc)
    {
        public SongMetadata ToMetadata() => new()
        {
            SongId = SongId,
            Title = Title,
            Artist = Artist,
            Format = Format,
            DurationSeconds = DurationSeconds,
            Bpm = Bpm,
            PreviewStart = PreviewStart,
            PreviewEnd = PreviewEnd,
            Genre = Genre,
            Source = Source,
            BgaPath = BgaPath,
            CoverPath = CoverPath,
        };
    }

    private static SongEntry[]? _cachedSongList;
    private static readonly SongDataStore SongData = new();

    private static SongEntry[] DiscoverSongs()
    {
        if (_cachedSongList is not null)
            return _cachedSongList;

        string bgmDir = Path.Combine(AppContext.BaseDirectory, "Songs", "InGameBGM", "Original");
        if (!Directory.Exists(bgmDir))
        {
            _cachedSongList = [];
            return _cachedSongList;
        }

        string[] audioFiles = AudioFileCatalog.DiscoverSongFiles(bgmDir);

        var metadataItems = new SongMetadata[audioFiles.Length];
        for (int i = 0; i < audioFiles.Length; i++)
            metadataItems[i] = AudioFileCatalog.ReadSongMetadata(audioFiles[i]);

        SongData.UpsertMetadataBatch(metadataItems);

        var songs = new SongEntry[audioFiles.Length];
        for (int i = 0; i < audioFiles.Length; i++)
        {
            SongMetadata metadata = metadataItems[i];
            SongScoreRecord? record = SongData.TryGetScore(metadata.SongId) ?? SongData.TryGetScore(AudioFileCatalog.GetLegacySongId(audioFiles[i]));
            int highestScore = record?.HighestScore ?? 0;
            string name = metadata.Title;
            string artist = $"InGameBGM · {AudioFileCatalog.GetFormatLabel(audioFiles[i])}";
            artist = metadata.Artist;
            songs[i] = new SongEntry(
                metadata.SongId,
                name,
                artist,
                i % 6,
                audioFiles[i],
                metadata.Format,
                metadata.DurationSeconds,
                metadata.Bpm,
                metadata.PreviewStart,
                metadata.PreviewEnd,
                metadata.Genre,
                metadata.Source,
                metadata.BgaPath,
                metadata.CoverPath,
                highestScore,
                record?.BestGrade ?? string.Empty,
                record?.BestClearType ?? string.Empty,
                record?.IsFavorite ?? false,
                record?.PlayCount ?? 0,
                record?.LastPlayedUtc ?? string.Empty);
        }

        _cachedSongList = songs;
        return _cachedSongList;
    }

    /// <summary>곡 목록 캐시를 무효화한다 (새 WAV 추가 시).</summary>
    private static void InvalidateSongCache() => _cachedSongList = null;

    private static string BuildSongMetadata(SongEntry song, bool includeBest = false)
    {
        string duration = FormatSongDuration(song.DurationSeconds);
        string bpm = song.Bpm > 0f ? $"BPM {song.Bpm:F0}" : "BPM --";
        string genre = string.IsNullOrWhiteSpace(song.Genre) ? string.Empty : $" | {song.Genre}";
        string favorite = song.IsFavorite ? " | FAV" : string.Empty;
        string text = $"{song.Artist} | {song.Format} | {duration} | {bpm}{genre}{favorite}";
        if (!AudioAnalysisPipeline.CanAnalyze(song.FilePath))
            text += " | analysis needs ffmpeg";
        if (!includeBest)
            return text;

        string grade = string.IsNullOrWhiteSpace(song.BestGrade) ? "--" : song.BestGrade;
        string clear = string.IsNullOrWhiteSpace(song.BestClearType) ? "No Clear" : song.BestClearType;
        return $"{text} | Best {song.HighestScore:N0} | {grade} | {clear}";
    }

    private static string FormatSongDuration(float seconds)
    {
        if (seconds <= 0f)
            return "--:--";

        int totalSeconds = (int)Math.Round(seconds);
        return $"{totalSeconds / 60:00}:{totalSeconds % 60:00}";
    }

    // 모든 난이도에서 동일한 곡 목록을 사용 (채보만 다름)
    private void DrawSongSelect(Graphics g)
    {
        DrawSongSelectBackground(g);
        DrawSongSelectCloseButton(g, GetSongSelectCloseButtonBounds(), _hoverSongPlayIndex == 0);

        Rectangle panel = GetSongSelectPanelBounds();
        DrawSongSelectPanel(g, panel);

        using var titleFont = new Font("Segoe UI", Math.Max(12f, ScaleTextY(31f)), FontStyle.Bold);
        using var titleBrush = new SolidBrush(GetAccentColor());
        using var rowTitleFont = new Font("Segoe UI", Math.Max(9.5f, ScaleTextY(17f)), FontStyle.Bold);
        using var artistFont = new Font("Segoe UI", Math.Max(8.5f, ScaleTextY(12.5f)), FontStyle.Regular);
        using var dimBrush = new SolidBrush(SubTextColor);
        using var rowTitleBrush = new SolidBrush(PrimaryTextColor);
        using var artistBrush = new SolidBrush(SecondaryTextColor);
        using var sepPen = new Pen(SeparatorColor, Math.Max(1f, ScaleY(1.1f)));
        using var playFont = new Font("Segoe UI", Math.Max(10f, ScaleTextY(20f)), FontStyle.Bold);

        string title = "SONG SELECT";
        SizeF titleSize = g.MeasureString(title, titleFont);
        float noteGap = ScaleX(8f);
        float noteSize = ScaleY(22f);
        float totalWidth = titleSize.Width + noteGap + noteSize;
        float titleLeft = ScaleX(DesignWidth / 2f) - totalWidth / 2f;
        float titleY = ScaleY(35f);
        g.DrawString(title, titleFont, titleBrush, titleLeft, titleY);
        DrawSongTitleNote(g, titleLeft + titleSize.Width + noteGap, titleY + ScaleY(7f), GetAccentColor());

        Rectangle searchBounds = GetSongSearchBounds(panel);
        DrawSongSearchBox(g, searchBounds, dimBrush);
        DrawSongLibraryControls(g, panel, artistFont, rowTitleBrush, artistBrush);

        Rectangle tabBounds = GetSongDifficultyBounds(panel);
        DrawSongDifficultyTabs(g, tabBounds);

        Rectangle leftListBounds = GetSongListBounds(panel);
        DrawSongRows(g, leftListBounds, rowTitleFont, artistFont, rowTitleBrush, artistBrush, sepPen);

        DrawSongScrollBar(g, leftListBounds);
        DrawSongPager(g, panel);

        Rectangle rightPreviewBounds = GetSongPreviewArtworkBounds(panel);
        SongEntry? selectedSong = GetSelectedSong();
        EnsureSongPreview(selectedSong);
        DrawSongArtwork(g, rightPreviewBounds, selectedSong);

        Rectangle rightInfoTop = GetSongPreviewTopTextBounds(panel);
        g.DrawString(selectedSong?.Title ?? "No Result", rowTitleFont, rowTitleBrush, rightInfoTop.Left, rightInfoTop.Top);
        g.DrawString(selectedSong is null ? "Try another keyword" : BuildSongMetadata(selectedSong, includeBest: true), artistFont, artistBrush, rightInfoTop.Left, rightInfoTop.Top + ScaleY(43f));

        Rectangle bottomInfo = GetSongPreviewBottomTextBounds(panel);
        g.DrawString(selectedSong?.Title ?? "No Result", rowTitleFont, rowTitleBrush, bottomInfo.Left, bottomInfo.Top);
        g.DrawString(selectedSong is null ? "Try another keyword" : BuildSongMetadata(selectedSong, includeBest: true), artistFont, artistBrush, bottomInfo.Left, bottomInfo.Top + ScaleY(42f));
        DrawSongChartPreview(g, GetSongChartPreviewBounds(panel), selectedSong, artistFont);

        DrawSongPlayButton(g, GetSongPlayButtonBounds(panel), _hoverSongPlayIndex == 1, playFont);
        DrawSongSelectHints(g, panel, artistFont);
    }

    private void DrawSongSelectBackground(Graphics g)
    {
        Rectangle layoutRect = new(0, 0, (int)ScaleX(DesignWidth), (int)ScaleY(DesignHeight));
        using var bgBrush = new LinearGradientBrush(layoutRect, BgColor1, BgColor2, LinearGradientMode.Vertical);
        g.FillRectangle(bgBrush, layoutRect);
    }

    private Rectangle GetSongSelectPanelBounds()
    {
        return GetCenteredDesignRect(1018f, 566f, 150f);
    }

    private void DrawSongSelectPanel(Graphics g, Rectangle bounds)
    {
        var shadow = bounds;
        shadow.Offset(0, (int)ScaleY(8f));
        using (var shadowPath = CreateRoundedRect(shadow, ScaleY(36f)))
        using (var shadowBrush = new SolidBrush(Color.FromArgb(22, 62, 92, 136)))
            g.FillPath(shadowBrush, shadowPath);

        using var panelPath = CreateRoundedRect(bounds, ScaleY(36f));
        using var panelBrush = new SolidBrush(PanelFill1);
        using var panelPen = new Pen(PanelBorder, Math.Max(1.2f, ScaleY(1.7f)));
        g.FillPath(panelBrush, panelPath);
        g.DrawPath(panelPen, panelPath);

        int dividerX = bounds.Left + (int)ScaleX(427f);
        using var dividerPen = new Pen(PanelDivider, Math.Max(1f, ScaleY(1.1f)));
        g.DrawLine(dividerPen, dividerX, bounds.Top + (int)ScaleY(26f), dividerX, bounds.Bottom - (int)ScaleY(22f));
    }

    private Rectangle GetSongSearchBounds(Rectangle panel)
    {
        return Rectangle.Round(new RectangleF(panel.Left + ScaleX(28f), panel.Top + ScaleY(26f), ScaleX(386f), ScaleY(48f)));
    }

    private void DrawSongSearchBox(Graphics g, Rectangle bounds, Brush textBrush)
    {
        using var path = CreateRoundedRect(bounds, bounds.Height / 2f);
        using var fill = new LinearGradientBrush(bounds, SearchFill1, SearchFill2, LinearGradientMode.Vertical);
        using var pen = new Pen(SearchBorder, Math.Max(1f, ScaleY(1.2f)));
        g.FillPath(fill, path);
        g.DrawPath(pen, path);

        float iconX = bounds.Left + ScaleX(28f);
        float iconY = bounds.Top + ScaleY(14f);
        using var iconPen = new Pen(SearchIconColor, Math.Max(2f, ScaleY(2.8f))) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawEllipse(iconPen, iconX, iconY, ScaleX(19f), ScaleY(19f));
        g.DrawLine(iconPen, iconX + ScaleX(14f), iconY + ScaleY(14f), iconX + ScaleX(24f), iconY + ScaleY(24f));

        using var font = new Font("Segoe UI", Math.Max(9f, ScaleTextY(13f)), FontStyle.Regular);
        if (string.IsNullOrWhiteSpace(_songSearchQuery))
        {
            g.DrawString("Songlist name...", font, textBrush, bounds.Left + ScaleX(70f), bounds.Top + ScaleY(11f));
        }
        else
        {
            using var activeBrush = new SolidBrush(SearchActiveText);
            g.DrawString(_songSearchQuery, font, activeBrush, bounds.Left + ScaleX(70f), bounds.Top + ScaleY(11f));
        }

        if (_isSongSearchFocused)
        {
            using var focusPen = new Pen(Color.FromArgb(120, GetAccentColor()), Math.Max(1.2f, ScaleY(1.6f)));
            g.DrawPath(focusPen, path);
        }
    }

    private Rectangle GetSongDifficultyBounds(Rectangle panel)
    {
        return Rectangle.Round(new RectangleF(panel.Left + ScaleX(448f), panel.Top + ScaleY(31f), ScaleX(541f), ScaleY(44f)));
    }

    private Rectangle GetSongSortButtonBounds(Rectangle panel)
    {
        return Rectangle.Round(new RectangleF(panel.Left + ScaleX(448f), panel.Top + ScaleY(83f), ScaleX(142f), ScaleY(30f)));
    }

    private Rectangle GetSongFavoriteFilterBounds(Rectangle panel)
    {
        return Rectangle.Round(new RectangleF(panel.Left + ScaleX(602f), panel.Top + ScaleY(83f), ScaleX(104f), ScaleY(30f)));
    }

    private Rectangle GetSongRescanButtonBounds(Rectangle panel)
    {
        return Rectangle.Round(new RectangleF(panel.Left + ScaleX(718f), panel.Top + ScaleY(83f), ScaleX(104f), ScaleY(30f)));
    }

    private Rectangle GetSongDetailButtonBounds(Rectangle panel)
    {
        return Rectangle.Round(new RectangleF(panel.Left + ScaleX(834f), panel.Top + ScaleY(83f), ScaleX(128f), ScaleY(30f)));
    }

    private void DrawSongLibraryControls(Graphics g, Rectangle panel, Font font, Brush titleBrush, Brush dimBrush)
    {
        DrawSongControlButton(g, GetSongSortButtonBounds(panel), $"SORT {SongSortLabels[_songSortModeIndex]}", font, titleBrush, _hoverSongPlayIndex == 40);
        DrawSongControlButton(g, GetSongFavoriteFilterBounds(panel), _songFavoritesOnly ? "FAV ON" : "FAV ALL", font, dimBrush, _hoverSongPlayIndex == 41);
        DrawSongControlButton(g, GetSongRescanButtonBounds(panel), "RESCAN", font, dimBrush, _hoverSongPlayIndex == 42);
        DrawSongControlButton(g, GetSongDetailButtonBounds(panel), "DETAIL", font, dimBrush, _hoverSongPlayIndex == 43);
    }

    private void DrawSongControlButton(Graphics g, Rectangle bounds, string text, Font font, Brush textBrush, bool hovered)
    {
        using var path = CreateRoundedRect(bounds, ScaleY(8f));
        using var fill = new SolidBrush(hovered ? Color.FromArgb(64, 42, 58, 88) : Color.FromArgb(42, 26, 38, 60));
        using var border = new Pen(Color.FromArgb(88, 100, 132, 190), Math.Max(1f, ScaleY(1f)));
        g.FillPath(fill, path);
        g.DrawPath(border, path);
        DrawCentered(g, text, font, textBrush, bounds.Left + bounds.Width / 2, bounds.Top + (int)ScaleY(6f));
    }

    private void DrawSongSelectHints(Graphics g, Rectangle panel, Font font)
    {
        Rectangle bounds = Rectangle.Round(new RectangleF(
            panel.Left + ScaleX(448f),
            panel.Bottom - ScaleY(34f),
            ScaleX(540f),
            ScaleY(22f)));
        using var brush = new SolidBrush(Color.FromArgb(160, 172, 190, 220));
        DrawCentered(g, "Enter Play   L Replay   D Detail   E Chart   F Favorite   R Rescan   5/6 Lane   Esc Back", font, brush, bounds.Left + bounds.Width / 2, bounds.Top + (int)ScaleY(3f));
    }

    private void DrawSongDifficultyTabs(Graphics g, Rectangle bounds)
    {
        string[] labels = ["EASY", "NORMAL", "HARD"];

        using var path = CreateRoundedRect(bounds, bounds.Height / 2f);
        using var fill = new LinearGradientBrush(bounds, TabFill1, TabFill2, LinearGradientMode.Vertical);
        using var pen = new Pen(TabBorder, Math.Max(1.2f, ScaleY(1.8f)));
        g.FillPath(fill, path);
        g.DrawPath(pen, path);

        int tabWidth = bounds.Width / 3;
        for (int i = 0; i < 3; i++)
        {
            Rectangle tab = new(bounds.Left + tabWidth * i, bounds.Top, tabWidth, bounds.Height);
            bool selected = _songSelectDifficultyIndex == i;
            bool hovered = _hoverSongPlayIndex == 10 + i;

            if (selected)
            {
                Rectangle selectedRect = Rectangle.Inflate(tab, -2, 0);
                using var sp = CreateRoundedRect(selectedRect, selectedRect.Height / 2f);
                using var sb = new LinearGradientBrush(selectedRect, Color.FromArgb(88, 145, 231), Color.FromArgb(45, 102, 196), LinearGradientMode.Vertical);
                using var sh = new Pen(Color.FromArgb(61, 114, 206), Math.Max(1.4f, ScaleY(2f)));
                g.FillPath(sb, sp);
                g.DrawPath(sh, sp);
            }

            using var tabFont = new Font("Segoe UI", Math.Max(9f, ScaleTextY(17f)), FontStyle.Bold);
            using var tb = new SolidBrush(selected ? Color.White : hovered ? Color.FromArgb(83, 108, 150) : TabText);
            DrawCentered(g, labels[i], tabFont, tb, tab.Left + tab.Width / 2, tab.Top + (int)ScaleY(9f));
        }

    }

    private Rectangle GetSongListBounds(Rectangle panel)
    {
        return Rectangle.Round(new RectangleF(panel.Left + ScaleX(24f), panel.Top + ScaleY(94f), ScaleX(388f), ScaleY(400f)));
    }

    private Rectangle GetSongRowBounds(Rectangle listBounds, int visibleRow)
    {
        float rowHeight = ScaleY(79f);
        return Rectangle.Round(new RectangleF(listBounds.Left, listBounds.Top + visibleRow * rowHeight, listBounds.Width, rowHeight));
    }

    private void DrawSongRows(Graphics g, Rectangle listBounds, Font titleFont, Font artistFont, Brush titleBrush, Brush artistBrush, Pen separatorPen)
    {
        SongEntry[] songs = GetFilteredSongs();
        for (int i = 0; i < SongRowsPerPage; i++)
        {
            Rectangle rowBounds = GetSongRowBounds(listBounds, i);
            int songIndex = _songSelectPageIndex * SongRowsPerPage + i;
            SongEntry? song = songIndex >= 0 && songIndex < songs.Length ? songs[songIndex] : null;
            bool selected = songIndex == _songSelectSelectedIndex && song is not null;
            bool hovered = _hoverSongPlayIndex == 100 + i;

            DrawSongRow(g, rowBounds, song, selected, hovered, titleFont, artistFont, titleBrush, artistBrush, separatorPen);
        }
    }

    private void DrawSongRow(Graphics g, Rectangle rowBounds, SongEntry? song, bool selected, bool hovered, Font titleFont, Font artistFont, Brush titleBrush, Brush artistBrush, Pen separatorPen)
    {
        if (selected)
        {
            Rectangle selectedRect = Rectangle.Inflate(rowBounds, -1, -3);
            using var sp = CreateRoundedRect(selectedRect, ScaleY(16f));
            using var sb = new LinearGradientBrush(selectedRect, SelectedRowFill1, SelectedRowFill2, LinearGradientMode.Vertical);
            using var sh = new Pen(SelectedRowBorder, Math.Max(1.1f, ScaleY(1.5f)));
            g.FillPath(sb, sp);
            g.DrawPath(sh, sp);
        }

        if (song is not null)
        {
            Rectangle iconCircle = Rectangle.Round(new RectangleF(rowBounds.Left + ScaleX(6f), rowBounds.Top + ScaleY(8f), ScaleX(58f), ScaleY(58f)));
            using (var circleBrush = new SolidBrush(selected ? SelectedCircleFill : RowCircleFill))
            using (var circlePen = new Pen(selected ? SelectedCircleBorder : RowCircleBorder, Math.Max(1f, ScaleY(1.2f))))
            {
                g.FillEllipse(circleBrush, iconCircle);
                g.DrawEllipse(circlePen, iconCircle);
            }

            DrawSmallSongNote(g, iconCircle, selected ? Color.FromArgb(144, 118, 205) : Color.FromArgb(152, 143, 202));
            g.DrawString(song.Title, titleFont, titleBrush, rowBounds.Left + ScaleX(85f), rowBounds.Top + ScaleY(15f));
            g.DrawString(BuildSongMetadata(song), artistFont, artistBrush, rowBounds.Left + ScaleX(85f), rowBounds.Top + ScaleY(47f));

            Rectangle chevron = Rectangle.Round(new RectangleF(rowBounds.Right - ScaleX(34f), rowBounds.Top + ScaleY(26f), ScaleX(12f), ScaleY(20f)));
            using var cp = new Pen(hovered || selected ? Color.FromArgb(127, 155, 208) : Color.FromArgb(176, 187, 207), Math.Max(2f, ScaleY(2.8f))) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLine(cp, chevron.Left, chevron.Top, chevron.Right, chevron.Top + chevron.Height / 2);
            g.DrawLine(cp, chevron.Right, chevron.Top + chevron.Height / 2, chevron.Left, chevron.Bottom);
        }

        g.DrawLine(separatorPen, rowBounds.Left, rowBounds.Bottom, rowBounds.Right, rowBounds.Bottom);
    }

    private void DrawSmallSongNote(Graphics g, Rectangle bounds, Color color)
    {
        using var pen = new Pen(color, Math.Max(2.2f, ScaleY(2.8f))) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var brush = new SolidBrush(color);
        float x = bounds.Left + ScaleX(20f);
        float y = bounds.Top + ScaleY(13f);
        g.DrawLine(pen, x + ScaleX(7f), y, x + ScaleX(7f), y + ScaleY(24f));
        g.DrawLine(pen, x + ScaleX(7f), y, x + ScaleX(20f), y + ScaleY(4f));
        g.FillEllipse(brush, x, y + ScaleY(19f), ScaleX(14f), ScaleY(11f));
    }

    private void DrawSongScrollBar(Graphics g, Rectangle listBounds)
    {
        Rectangle track = Rectangle.Round(new RectangleF(listBounds.Right + ScaleX(10f), listBounds.Top + ScaleY(2f), ScaleX(10f), listBounds.Height - ScaleY(4f)));
        using var trackBrush = new SolidBrush(ScrollTrackColor);
        using var trackPath = CreateRoundedRect(track, track.Width / 2f);
        g.FillPath(trackBrush, trackPath);

        int totalSongs = Math.Max(1, GetFilteredSongs().Length);
        float handleHeight = track.Height * (SongRowsPerPage / (float)totalSongs);
        handleHeight = Math.Clamp(handleHeight, ScaleY(48f), track.Height - ScaleY(16f));
        float pageMax = Math.Max(1, GetSongPageCount() - 1);
        float ratio = _songSelectPageIndex / pageMax;
        float handleY = track.Top + ratio * (track.Height - handleHeight);
        Rectangle handle = Rectangle.Round(new RectangleF(track.Left, handleY, track.Width, handleHeight));
        using var handleBrush = new SolidBrush(ScrollHandleColor);
        using var handlePath = CreateRoundedRect(handle, handle.Width / 2f);
        g.FillPath(handleBrush, handlePath);
    }

    private void DrawSongPager(Graphics g, Rectangle panel)
    {
        Rectangle prev = GetSongPrevButtonBounds(panel);
        Rectangle next = GetSongNextButtonBounds(panel);
        DrawSongArrowButton(g, prev, true, _hoverSongPlayIndex == 20);
        DrawSongArrowButton(g, next, false, _hoverSongPlayIndex == 21);

        Rectangle dots = GetSongDotsBounds(panel);
        int pageCount = GetSongPageCount();
        for (int i = 0; i < pageCount; i++)
        {
            Rectangle dot = GetSongDotBounds(dots, i, pageCount);
            bool selected = i == _songSelectPageIndex;
            bool hovered = _hoverSongPlayIndex == 30 + i;
            using var brush = new SolidBrush(selected
                ? DotColor
                : hovered
                    ? Color.FromArgb(175, 186, 206)
                    : DotColor);
            using var pen = new Pen(DotBorder, Math.Max(1f, ScaleY(1.1f)));
            g.FillEllipse(brush, dot);
            g.DrawEllipse(pen, dot);
        }
    }

    private Rectangle GetSongPrevButtonBounds(Rectangle panel)
    {
        return Rectangle.Round(new RectangleF(panel.Left + ScaleX(54f), panel.Bottom - ScaleY(62f), ScaleX(50f), ScaleY(50f)));
    }

    private Rectangle GetSongNextButtonBounds(Rectangle panel)
    {
        return Rectangle.Round(new RectangleF(panel.Left + ScaleX(344f), panel.Bottom - ScaleY(62f), ScaleX(50f), ScaleY(50f)));
    }

    private Rectangle GetSongDotsBounds(Rectangle panel)
    {
        return Rectangle.Round(new RectangleF(panel.Left + ScaleX(177f), panel.Bottom - ScaleY(45f), ScaleX(96f), ScaleY(20f)));
    }

    private Rectangle GetSongDotBounds(Rectangle dotsBounds, int index, int count)
    {
        float diameter = ScaleX(17f);
        float gap = ScaleX(17f);
        count = Math.Max(1, count);
        float totalWidth = diameter * count + gap * Math.Max(0, count - 1);
        float startX = dotsBounds.Left + (dotsBounds.Width - totalWidth) / 2f;
        float y = dotsBounds.Top + (dotsBounds.Height - diameter) / 2f;
        return Rectangle.Round(new RectangleF(startX + index * (diameter + gap), y, diameter, diameter));
    }

    private void DrawSongArrowButton(Graphics g, Rectangle bounds, bool left, bool hovered)
    {
        using var path = CreateRoundedRect(bounds, bounds.Height / 2f);
        using var fill = new LinearGradientBrush(bounds,
            hovered ? Color.FromArgb(Math.Min(255, ArrowBtnFill1.A + 5), Math.Min(255, ArrowBtnFill1.R + 5), Math.Min(255, ArrowBtnFill1.G + 5), Math.Min(255, ArrowBtnFill1.B + 5)) : ArrowBtnFill1,
            hovered ? Color.FromArgb(ArrowBtnFill2.A, Math.Min(255, ArrowBtnFill2.R + 3), Math.Min(255, ArrowBtnFill2.G + 3), Math.Min(255, ArrowBtnFill2.B + 3)) : ArrowBtnFill2,
            LinearGradientMode.Vertical);
        using var pen = new Pen(ArrowBtnBorder, Math.Max(1.1f, ScaleY(1.5f)));
        g.FillPath(fill, path);
        g.DrawPath(pen, path);

        using var arrowPen = new Pen(ArrowColor, Math.Max(2f, ScaleY(2.8f))) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        int cx = bounds.Left + bounds.Width / 2;
        int cy = bounds.Top + bounds.Height / 2;
        int w = (int)ScaleX(9f);
        int h = (int)ScaleY(14f);
        if (left)
        {
            g.DrawLine(arrowPen, cx + w / 2, cy - h / 2, cx - w / 2, cy);
            g.DrawLine(arrowPen, cx - w / 2, cy, cx + w / 2, cy + h / 2);
        }
        else
        {
            g.DrawLine(arrowPen, cx - w / 2, cy - h / 2, cx + w / 2, cy);
            g.DrawLine(arrowPen, cx + w / 2, cy, cx - w / 2, cy + h / 2);
        }
    }

    private Rectangle GetSongPreviewArtworkBounds(Rectangle panel)
    {
        return Rectangle.Round(new RectangleF(panel.Left + ScaleX(451f), panel.Top + ScaleY(121f), ScaleX(252f), ScaleY(260f)));
    }

    private Rectangle GetSongPreviewTopTextBounds(Rectangle panel)
    {
        return Rectangle.Round(new RectangleF(panel.Left + ScaleX(731f), panel.Top + ScaleY(143f), ScaleX(220f), ScaleY(100f)));
    }

    private Rectangle GetSongPreviewBottomTextBounds(Rectangle panel)
    {
        return Rectangle.Round(new RectangleF(panel.Left + ScaleX(446f), panel.Top + ScaleY(408f), ScaleX(320f), ScaleY(95f)));
    }

    private Rectangle GetSongPlayButtonBounds(Rectangle panel)
    {
        return Rectangle.Round(new RectangleF(panel.Left + ScaleX(786f), panel.Top + ScaleY(455f), ScaleX(202f), ScaleY(62f)));
    }

    private Rectangle GetSongChartPreviewBounds(Rectangle panel)
    {
        return Rectangle.Round(new RectangleF(panel.Left + ScaleX(730f), panel.Top + ScaleY(258f), ScaleX(235f), ScaleY(128f)));
    }

    private Rectangle GetSongSelectCloseButtonBounds()
    {
        return Rectangle.Round(new RectangleF(ScaleX(1056f), ScaleY(34f), ScaleX(52f), ScaleY(52f)));
    }

    private void DrawSongSelectCloseButton(Graphics g, Rectangle bounds, bool hovered)
    {
        using var path = CreateRoundedRect(bounds, bounds.Height / 2f);
        using var fill = new LinearGradientBrush(bounds,
            hovered ? Color.FromArgb(CloseBtnFill1.A, Math.Min(255, CloseBtnFill1.R + 5), Math.Min(255, CloseBtnFill1.G + 5), Math.Min(255, CloseBtnFill1.B + 5)) : CloseBtnFill1,
            hovered ? Color.FromArgb(CloseBtnFill2.A, Math.Min(255, CloseBtnFill2.R + 3), Math.Min(255, CloseBtnFill2.G + 3), Math.Min(255, CloseBtnFill2.B + 3)) : CloseBtnFill2,
            LinearGradientMode.Vertical);
        using var pen = new Pen(CloseBtnBorder, Math.Max(1.2f, ScaleY(1.6f)));
        g.FillPath(fill, path);
        g.DrawPath(pen, path);

        using var xPen = new Pen(CloseBtnX, Math.Max(2.5f, ScaleY(3.3f))) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(xPen, bounds.Left + ScaleX(16f), bounds.Top + ScaleY(16f), bounds.Right - ScaleX(16f), bounds.Bottom - ScaleY(16f));
        g.DrawLine(xPen, bounds.Right - ScaleX(16f), bounds.Top + ScaleY(16f), bounds.Left + ScaleX(16f), bounds.Bottom - ScaleY(16f));
    }

    private int GetSongSelectHoverCode(Point location)
    {
        if (GetSongSelectCloseButtonBounds().Contains(location)) return 0;

        Rectangle panel = GetSongSelectPanelBounds();
        if (GetSongPlayButtonBounds(panel).Contains(location)) return 1;

        Rectangle tabs = GetSongDifficultyBounds(panel);
        int tabWidth = tabs.Width / 3;
        for (int i = 0; i < 3; i++)
        {
            Rectangle tab = new(tabs.Left + tabWidth * i, tabs.Top, tabWidth, tabs.Height);
            if (tab.Contains(location)) return 10 + i;
        }

        if (GetSongPrevButtonBounds(panel).Contains(location)) return 20;
        if (GetSongNextButtonBounds(panel).Contains(location)) return 21;
        if (GetSongSortButtonBounds(panel).Contains(location)) return 40;
        if (GetSongFavoriteFilterBounds(panel).Contains(location)) return 41;
        if (GetSongRescanButtonBounds(panel).Contains(location)) return 42;
        if (GetSongDetailButtonBounds(panel).Contains(location)) return 43;

        Rectangle dots = GetSongDotsBounds(panel);
        int pageCount = GetSongPageCount();
        for (int i = 0; i < pageCount; i++)
            if (GetSongDotBounds(dots, i, pageCount).Contains(location)) return 30 + i;

        Rectangle list = GetSongListBounds(panel);
        for (int i = 0; i < SongRowsPerPage; i++)
        {
            Rectangle row = GetSongRowBounds(list, i);
            if (row.Contains(location)) return 100 + i;
        }

        return -1;
    }

    private void HandleSongSelectMouseDown(Point location)
    {
        _isSongSearchFocused = IsSongSearchBoxHit(location);
        if (_isSongSearchFocused)
        {
            Invalidate();
            return;
        }

        int code = GetSongSelectHoverCode(location);

        if (code == 0)
        {
            _hoverSongPlayIndex = -1;
            _screen = UiScreen.MainMenu;
            _audio.StopSongPreview();
            _audio.PlayMainScreenBgm();
            Invalidate();
            return;
        }

        if (code == 1)
        {
            if (GetSelectedSong() is null)
                return;
            BeginGame();
            return;
        }

        if (code is >= 10 and <= 12)
        {
            _songSelectDifficultyIndex = code - 10;
            _songSelectPageIndex = 0;
            _songSelectSelectedIndex = 0;
            _previewSongKey = string.Empty;
            Invalidate();
            return;
        }

        if (code == 20)
        {
            _songSelectPageIndex = Math.Max(0, _songSelectPageIndex - 1);
            _songSelectSelectedIndex = _songSelectPageIndex * SongRowsPerPage;
            Invalidate();
            return;
        }

        if (code == 21)
        {
            _songSelectPageIndex = Math.Min(GetSongPageCount() - 1, _songSelectPageIndex + 1);
            _songSelectSelectedIndex = _songSelectPageIndex * SongRowsPerPage;
            Invalidate();
            return;
        }

        if (code == 40)
        {
            _songSortModeIndex = (_songSortModeIndex + 1) % SongSortLabels.Length;
            _songSelectPageIndex = 0;
            _songSelectSelectedIndex = 0;
            _previewSongKey = string.Empty;
            Invalidate();
            return;
        }

        if (code == 41)
        {
            _songFavoritesOnly = !_songFavoritesOnly;
            _songSelectPageIndex = 0;
            _songSelectSelectedIndex = 0;
            _previewSongKey = string.Empty;
            Invalidate();
            return;
        }

        if (code == 42)
        {
            RescanSongs();
            return;
        }

        if (code == 43)
        {
            OpenSelectedSongDetail();
            return;
        }

        if (code is >= 30 and < 30 + 12)
        {
            _songSelectPageIndex = code - 30;
            _songSelectSelectedIndex = _songSelectPageIndex * SongRowsPerPage;
            Invalidate();
            return;
        }

        if (code >= 100)
        {
            int visibleIndex = code - 100;
            int absoluteIndex = _songSelectPageIndex * SongRowsPerPage + visibleIndex;
            if (GetSongByIndex(absoluteIndex) is not null)
            {
                _songSelectSelectedIndex = absoluteIndex;
                _previewSongKey = string.Empty;
                Invalidate();
            }
        }
    }

    private SongEntry[] GetCurrentSongs()
    {
        // 모든 난이도에서 동일한 곡 목록, 채보만 다름
        return DiscoverSongs();
    }

    private SongEntry? GetSongByIndex(int index)
    {
        SongEntry[] songs = GetFilteredSongs();
        if (index < 0 || index >= songs.Length)
            return null;

        return songs[index];
    }

    private SongEntry[] GetFilteredSongs()
    {
        SongEntry[] songs = GetCurrentSongs();
        string query = _songSearchQuery.Trim();
        IEnumerable<SongEntry> filtered = songs;

        if (_songFavoritesOnly)
            filtered = filtered.Where(song => song.IsFavorite);

        if (!string.IsNullOrEmpty(query))
            filtered = filtered.Where(song => IsSongMatch(song, query));

        return ApplySongSort(filtered).ToArray();
    }

    private IEnumerable<SongEntry> ApplySongSort(IEnumerable<SongEntry> songs)
    {
        return _songSortModeIndex switch
        {
            1 => songs.OrderBy(s => s.Artist, StringComparer.OrdinalIgnoreCase).ThenBy(s => s.Title, StringComparer.OrdinalIgnoreCase),
            2 => songs.OrderBy(s => s.Bpm <= 0f).ThenBy(s => s.Bpm).ThenBy(s => s.Title, StringComparer.OrdinalIgnoreCase),
            3 => songs.OrderBy(s => s.DurationSeconds <= 0f).ThenBy(s => s.DurationSeconds).ThenBy(s => s.Title, StringComparer.OrdinalIgnoreCase),
            4 => songs.OrderByDescending(s => s.HighestScore).ThenBy(s => s.Title, StringComparer.OrdinalIgnoreCase),
            5 => songs.OrderByDescending(s => ParseSortableUtc(s.LastPlayedUtc)).ThenBy(s => s.Title, StringComparer.OrdinalIgnoreCase),
            6 => songs.OrderByDescending(GetSongLevelForSort).ThenBy(s => s.Title, StringComparer.OrdinalIgnoreCase),
            7 => songs.OrderByDescending(s => s.IsFavorite).ThenBy(s => s.Title, StringComparer.OrdinalIgnoreCase),
            _ => songs.OrderBy(s => s.Title, StringComparer.OrdinalIgnoreCase),
        };
    }

    private int GetSongLevelForSort(SongEntry song)
    {
        ChartValidationResult result = NoteLane.LoadValidatedChart(song.Title, song.Artist, _songSelectDifficultyIndex, LaneCount);
        return result.Difficulty.Level;
    }

    private static long ParseSortableUtc(string value)
    {
        return DateTime.TryParse(value, out DateTime parsed) ? parsed.ToUniversalTime().Ticks : 0L;
    }

    private static bool IsSongMatch(SongEntry song, string query)
    {
        string qNorm = NormalizeForSearch(query);

        bool basicMatch = song.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                          song.Artist.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                          song.Format.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                          song.Genre.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                          song.Source.Contains(query, StringComparison.OrdinalIgnoreCase);
        bool normalizedMatch = qNorm.Length > 0 &&
                               (NormalizeForSearch(song.Title).Contains(qNorm) ||
                                NormalizeForSearch(song.Artist).Contains(qNorm) ||
                                NormalizeForSearch(song.Genre).Contains(qNorm) ||
                                NormalizeForSearch(song.Source).Contains(qNorm));

        return basicMatch || normalizedMatch;
    }

    private static string NormalizeForSearch(string text)
    {
        Span<char> buffer = stackalloc char[text.Length];
        int index = 0;
        foreach (char ch in text)
        {
            if (char.IsLetterOrDigit(ch))
                buffer[index++] = char.ToLowerInvariant(ch);
        }
        return new string(buffer[..index]);
    }

    private SongEntry? GetSelectedSong()
    {
        SongEntry[] songs = GetFilteredSongs();
        if (songs.Length == 0)
            return null;

        _songSelectSelectedIndex = Math.Clamp(_songSelectSelectedIndex, 0, songs.Length - 1);
        return songs[_songSelectSelectedIndex];
    }

    private int GetSongPageCount()
    {
        int count = GetFilteredSongs().Length;
        return Math.Max(1, (int)Math.Ceiling(count / (double)SongRowsPerPage));
    }

    private bool IsSongSearchBoxHit(Point location)
    {
        return GetSongSearchBounds(GetSongSelectPanelBounds()).Contains(location);
    }

    private void ApplySongSearchInput(char? appendChar = null, bool removeLast = false)
    {
        if (removeLast)
        {
            if (_songSearchQuery.Length > 0)
                _songSearchQuery = _songSearchQuery[..^1];
        }
        else if (appendChar is not null)
        {
            if (_songSearchQuery.Length < 40)
                _songSearchQuery += appendChar.Value;
        }

        _songSelectPageIndex = 0;
        _songSelectSelectedIndex = 0;
        _hoverSongPlayIndex = -1;
    }

    private bool IsSongSelectInteractive(Point location)
    {
        return GetSongSelectHoverCode(location) >= 0;
    }

    private void RescanSongs()
    {
        InvalidateSongCache();
        _songSelectPageIndex = 0;
        _songSelectSelectedIndex = 0;
        _previewSongKey = string.Empty;
        _songPreviewNotes = [];
        _songPreviewDifficulty = null;
        _audio.StopSongPreview();
        _feedback = "Songs rescanned";
        _feedbackTime = DateTime.Now;
        Invalidate();
    }

    private void OpenSelectedSongDetail()
    {
        if (GetSelectedSong() is null)
            return;

        _screen = UiScreen.SongDetail;
        _audio.StopSongPreview();
        Invalidate();
    }

    private void ToggleSelectedSongFavorite()
    {
        SongEntry? song = GetSelectedSong();
        if (song is null)
            return;

        SongData.SetFavorite(song.SongId, !song.IsFavorite);
        InvalidateSongCache();
        _previewSongKey = string.Empty;
    }

    private void OpenSelectedChartForEditing()
    {
        SongEntry? song = GetSelectedSong();
        if (song is null)
            return;

        try
        {
            OpenChartEditor(song);
            _feedback = "Chart editor";
            _feedbackTime = DateTime.Now;
            InvalidateSongCache();
        }
        catch
        {
            _feedback = "Chart editor failed";
            _feedbackTime = DateTime.Now;
        }

        Invalidate();
    }

    private void EnsureSongPreview(SongEntry? song)
    {
        if (song is null)
        {
            _audio.StopSongPreview();
            _previewSongKey = string.Empty;
            return;
        }

        string key = $"{song.SongId}:{_songSelectDifficultyIndex}:{LaneCount}";
        if (_previewSongKey == key && (DateTime.Now - _previewStartedAt).TotalSeconds < 14.5)
            return;

        _previewSongKey = key;
        _previewStartedAt = DateTime.Now;
        ChartValidationResult result = NoteLane.LoadValidatedChart(song.Title, song.Artist, _songSelectDifficultyIndex, LaneCount);
        _songPreviewNotes = result.Notes;
        _songPreviewDifficulty = result.Difficulty;
        _songPreviewStatus = result.Diagnostics.Count == 0
            ? "CHART OK"
            : $"{result.Diagnostics.Count} WARN";

        float start = song.PreviewStart > 0f
            ? song.PreviewStart
            : song.DurationSeconds > 25f ? MathF.Min(20f, song.DurationSeconds * 0.25f) : 0f;
        float duration = song.PreviewEnd > song.PreviewStart
            ? Math.Clamp(song.PreviewEnd - song.PreviewStart, 3f, 30f)
            : 15f;
        _audio.PlaySongPreview(song.FilePath, start, duration, _previewVolume);
    }

    private void DrawSongChartPreview(Graphics g, Rectangle bounds, SongEntry? song, Font font)
    {
        using var path = CreateRoundedRect(bounds, ScaleY(12f));
        using var fill = new SolidBrush(Color.FromArgb(52, 16, 24, 42));
        using var border = new Pen(Color.FromArgb(84, 120, 155, 210), Math.Max(1f, ScaleY(1.2f)));
        using var textBrush = new SolidBrush(Color.FromArgb(214, 226, 248));
        g.FillPath(fill, path);
        g.DrawPath(border, path);

        if (song is null || _songPreviewDifficulty is not ChartDifficultyInfo difficulty)
        {
            g.DrawString("NO CHART", font, textBrush, bounds.Left + ScaleX(14f), bounds.Top + ScaleY(12f));
            return;
        }

        string title = $"Lv.{difficulty.Level}  {_songPreviewStatus}";
        g.DrawString(title, font, textBrush, bounds.Left + ScaleX(12f), bounds.Top + ScaleY(10f));

        Rectangle graph = Rectangle.Round(new RectangleF(bounds.Left + ScaleX(12f), bounds.Top + ScaleY(42f), bounds.Width - ScaleX(24f), ScaleY(54f)));
        using var basePen = new Pen(Color.FromArgb(52, 120, 150, 205), Math.Max(1f, ScaleY(1f)));
        g.DrawRectangle(basePen, graph);

        int buckets = 18;
        int[] counts = new int[buckets];
        float duration = Math.Max(1f, song.DurationSeconds);
        foreach (LaneNote note in _songPreviewNotes)
        {
            int bucket = Math.Clamp((int)(note.Time / duration * buckets), 0, buckets - 1);
            counts[bucket]++;
        }

        int max = Math.Max(1, counts.Max());
        float barGap = ScaleX(2f);
        float barW = (graph.Width - barGap * (buckets - 1)) / buckets;
        using var barBrush = new SolidBrush(GetAccentColor());
        for (int i = 0; i < buckets; i++)
        {
            float h = graph.Height * counts[i] / max;
            RectangleF bar = new(graph.Left + i * (barW + barGap), graph.Bottom - h, barW, h);
            g.FillRectangle(barBrush, bar);
        }

        using var subBrush = new SolidBrush(Color.FromArgb(170, 188, 218));
        string sub = $"{_songPreviewNotes.Count} notes  {difficulty.NotesPerSecond:F1} n/s";
        g.DrawString(sub, font, subBrush, bounds.Left + ScaleX(12f), bounds.Bottom - ScaleY(25f));
    }

    private void DrawSongPlayButton(Graphics g, Rectangle bounds, bool hovered, Font font)
    {
        Color accent = GetAccentColor();
        Rectangle shadowBounds = bounds;
        shadowBounds.Offset(0, (int)ScaleY(4f));
        using (var shadowPath = CreateRoundedRect(shadowBounds, shadowBounds.Height / 2f))
        using (var shadowBrush = new SolidBrush(Color.FromArgb(28, 71, 96, 142)))
        {
            g.FillPath(shadowBrush, shadowPath);
        }

        Rectangle drawBounds = bounds;
        if (hovered)
            drawBounds.Offset(0, -(int)ScaleY(2f));

        using var path = CreateRoundedRect(drawBounds, drawBounds.Height / 2f);
        using var fillBrush = new LinearGradientBrush(
            drawBounds,
            Color.FromArgb(88, 145, 231),
            Color.FromArgb(45, 102, 196),
            LinearGradientMode.Vertical);
        using var glowPen = new Pen(Color.FromArgb(122, 167, 237), Math.Max(1.2f, ScaleY(1.6f)));
        using var borderPen = new Pen(Color.FromArgb(55, 106, 196), Math.Max(1.6f, ScaleY(2f)));
        using var textBrush = new SolidBrush(Color.FromArgb(240, 246, 255));
        g.FillPath(fillBrush, path);
        g.DrawPath(glowPen, path);
        g.DrawPath(borderPen, path);
        DrawCentered(g, "PLAY ▶", font, textBrush, drawBounds.Left + drawBounds.Width / 2, drawBounds.Top + (int)ScaleY(10f));
    }

    private void DrawSongArtwork(Graphics g, Rectangle bounds, SongEntry? song)
    {
        using var path = CreateRoundedRect(bounds, ScaleY(14f));
        using var borderPen = new Pen(PanelBorder, Math.Max(1.2f, ScaleY(1.6f)));
        using var clipPath = (GraphicsPath)path.Clone();
        GraphicsState state = g.Save();
        g.SetClip(clipPath);

        if (TryDrawCoverImage(g, bounds, song?.CoverPath))
        {
            g.Restore(state);
            g.DrawPath(borderPen, path);
            return;
        }

        switch (song?.ArtworkStyle ?? 0)
        {
            case 0:
                using (var bg = new LinearGradientBrush(bounds, Color.FromArgb(229, 205, 245), Color.FromArgb(172, 224, 248), LinearGradientMode.Vertical))
                    g.FillRectangle(bg, bounds);
                using (var sparkle = new Pen(Color.FromArgb(175, 255, 255, 255), Math.Max(1f, ScaleY(1.4f))))
                {
                    for (int i = 0; i < 8; i++)
                    {
                        float x = bounds.Left + ScaleX(24f + i * 24f);
                        float y = bounds.Top + ScaleY(22f + (i % 3) * 26f);
                        g.DrawEllipse(sparkle, x, y, ScaleX(6f), ScaleY(6f));
                    }
                }
                break;
            case 1:
                using (var bg = new LinearGradientBrush(bounds, Color.FromArgb(54, 73, 129), Color.FromArgb(140, 176, 231), LinearGradientMode.ForwardDiagonal))
                    g.FillRectangle(bg, bounds);
                break;
            case 2:
                using (var bg = new LinearGradientBrush(bounds, Color.FromArgb(49, 96, 176), Color.FromArgb(106, 206, 255), LinearGradientMode.Vertical))
                    g.FillRectangle(bg, bounds);
                break;
            case 3:
                using (var bg = new LinearGradientBrush(bounds, Color.FromArgb(60, 38, 137), Color.FromArgb(147, 94, 209), LinearGradientMode.Horizontal))
                    g.FillRectangle(bg, bounds);
                break;
            case 4:
                using (var bg = new LinearGradientBrush(bounds, Color.FromArgb(18, 46, 101), Color.FromArgb(37, 120, 193), LinearGradientMode.Vertical))
                    g.FillRectangle(bg, bounds);
                break;
            default:
                using (var bg = new LinearGradientBrush(bounds, Color.FromArgb(92, 84, 189), Color.FromArgb(219, 178, 250), LinearGradientMode.Vertical))
                    g.FillRectangle(bg, bounds);
                break;
        }

        using (var notePen = new Pen(Color.FromArgb(145, 114, 214), Math.Max(4f, ScaleY(6f))) { StartCap = LineCap.Round, EndCap = LineCap.Round })
        using (var noteBrush = new SolidBrush(Color.FromArgb(167, 118, 226)))
        {
            float nx = bounds.Left + ScaleX(126f);
            float ny = bounds.Top + ScaleY(66f);
            g.DrawLine(notePen, nx, ny, nx, ny + ScaleY(94f));
            g.DrawLine(notePen, nx, ny, nx + ScaleX(58f), ny + ScaleY(15f));
            g.FillEllipse(noteBrush, nx - ScaleX(42f), ny + ScaleY(77f), ScaleX(83f), ScaleY(62f));
        }

        g.Restore(state);
        g.DrawPath(borderPen, path);
    }

    private void DrawSongArtwork(Graphics g, Rectangle bounds, int style)
    {
        DrawSongArtwork(g, bounds, new SongEntry(
            string.Empty,
            string.Empty,
            string.Empty,
            style,
            string.Empty,
            string.Empty,
            0f,
            0f,
            0f,
            0f,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            0,
            string.Empty,
            string.Empty,
            false,
            0,
            string.Empty));
    }

    private static bool TryDrawCoverImage(Graphics g, Rectangle bounds, string? coverPath)
    {
        if (string.IsNullOrWhiteSpace(coverPath) || !File.Exists(coverPath))
            return false;

        try
        {
            using Image image = Image.FromFile(coverPath);
            float scale = Math.Max(bounds.Width / (float)image.Width, bounds.Height / (float)image.Height);
            float width = image.Width * scale;
            float height = image.Height * scale;
            RectangleF dest = new(bounds.Left + (bounds.Width - width) / 2f, bounds.Top + (bounds.Height - height) / 2f, width, height);
            g.DrawImage(image, dest);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void DrawSongTitleNote(Graphics g, float x, float y, Color color)
    {
        using var pen = new Pen(color, 4f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        using var brush = new SolidBrush(color);
        g.DrawLine(pen, x + 4f, y + 2f, x + 4f, y + 22f);
        g.DrawLine(pen, x + 4f, y + 2f, x + 16f, y + 6f);
        g.FillEllipse(brush, x - 1f, y + 18f, 11f, 9f);
        g.FillEllipse(brush, x + 11f, y + 14f, 11f, 9f);
    }
}
