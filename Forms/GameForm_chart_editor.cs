using System.Drawing.Drawing2D;

namespace RhythmGame;

public sealed partial class GameForm
{
    private void OpenChartEditor(SongEntry song)
    {
        _audio.StopSongPreview();
        EditableChart chart = NoteLane.LoadEditableChart(song.Title, _songSelectDifficultyIndex, LaneCount);
        _chartEditorSongTitle = song.Title;
        _chartEditorSongDuration = Math.Max(30f, song.DurationSeconds);
        _chartEditorBpm = chart.Bpm > 0f ? chart.Bpm : Math.Max(60f, song.Bpm);
        _chartEditorPath = chart.Path;
        _chartEditorNotes = chart.Notes.OrderBy(n => n.Time).ThenBy(n => n.Lane).ToList();
        _chartEditorUndo.Clear();
        _chartEditorSelectedIndex = _chartEditorNotes.Count > 0 ? 0 : -1;
        _chartEditorCursorTime = _chartEditorSelectedIndex >= 0 ? _chartEditorNotes[_chartEditorSelectedIndex].Time : 0f;
        _chartEditorDifficulty = chart.Difficulty;
        _chartEditorStatus = chart.Diagnostics.Count == 0
            ? $"Loaded {Path.GetFileName(chart.Path)}"
            : $"Loaded with {chart.Diagnostics.Count} warnings";
        _screen = UiScreen.ChartEditor;
    }

    private void DrawChartEditor(Graphics g)
    {
        Rectangle layoutRect = new(0, 0, (int)ScaleX(DesignWidth), (int)ScaleY(DesignHeight));
        using (var bg = new LinearGradientBrush(layoutRect, Color.FromArgb(9, 13, 24), Color.FromArgb(19, 28, 46), LinearGradientMode.Vertical))
            g.FillRectangle(bg, layoutRect);

        using var titleFont = new Font("Segoe UI", Math.Max(11f, ScaleY(26f)), FontStyle.Bold);
        using var labelFont = new Font("Segoe UI", Math.Max(7f, ScaleY(11f)), FontStyle.Bold);
        using var smallFont = new Font("Segoe UI", Math.Max(6f, ScaleY(9.5f)), FontStyle.Bold);
        using var titleBrush = new SolidBrush(Color.FromArgb(238, 246, 255));
        using var mutedBrush = new SolidBrush(Color.FromArgb(175, 190, 218));

        DrawCentered(g, "CHART EDITOR", titleFont, titleBrush, (int)ScaleX(DesignWidth / 2f), (int)ScaleY(28f));
        string header = $"{_chartEditorSongTitle}  |  {LaneCount}K  |  {GetDifficultyLabel(_songSelectDifficultyIndex)}  |  BPM {_chartEditorBpm:F0}";
        DrawCentered(g, header, labelFont, mutedBrush, (int)ScaleX(DesignWidth / 2f), (int)ScaleY(66f));

        DrawChartEditorToolbar(g, labelFont);
        DrawChartEditorGrid(g, labelFont, smallFont);
        DrawChartEditorStats(g, smallFont);
    }

    private void DrawChartEditorToolbar(Graphics g, Font font)
    {
        string[] labels = ["BACK", "SAVE", "UNDO", $"TYPE {_chartEditorInsertType}", "BPM -", "BPM +", "TIME -", "TIME +", "PREVIEW"];
        for (int i = 0; i < labels.Length; i++)
            DrawChartEditorButton(g, GetChartEditorActionBounds(i), labels[i], _hoverChartEditorAction == i, font);
    }

    private Rectangle GetChartEditorGridBounds()
    {
        return Rectangle.Round(new RectangleF(ScaleX(70f), ScaleY(142f), ScaleX(1012f), ScaleY(440f)));
    }

    private Rectangle GetChartEditorActionBounds(int index)
    {
        float width = index == 3 ? 128f : 92f;
        float x = 62f + index * 116f;
        if (index > 3)
            x += 36f;
        return Rectangle.Round(new RectangleF(ScaleX(x), ScaleY(94f), ScaleX(width), ScaleY(34f)));
    }

    private void DrawChartEditorButton(Graphics g, Rectangle bounds, string label, bool hover, Font font)
    {
        Color accent = GetAccentColor();
        using var path = CreateRoundedRect(bounds, ScaleY(8f));
        using var fill = new LinearGradientBrush(bounds,
            hover ? Color.FromArgb(170, accent) : Color.FromArgb(105, accent),
            Color.FromArgb(52, accent),
            LinearGradientMode.Vertical);
        using var border = new Pen(hover ? Color.White : Color.FromArgb(120, accent), Math.Max(1f, ScaleY(1.3f)));
        using var brush = new SolidBrush(Color.White);
        g.FillPath(fill, path);
        g.DrawPath(border, path);
        DrawCentered(g, label, font, brush, bounds.Left + bounds.Width / 2, bounds.Top + (int)ScaleY(8f));
    }

    private void DrawChartEditorGrid(Graphics g, Font labelFont, Font smallFont)
    {
        Rectangle grid = GetChartEditorGridBounds();
        using (var path = CreateRoundedRect(grid, ScaleY(12f)))
        using (var fill = new SolidBrush(Color.FromArgb(180, 8, 13, 25)))
        using (var border = new Pen(Color.FromArgb(82, 112, 150, 205), Math.Max(1f, ScaleY(1.4f))))
        {
            g.FillPath(fill, path);
            g.DrawPath(border, path);
        }

        float visibleSeconds = 16f;
        float start = Math.Clamp(_chartEditorCursorTime - visibleSeconds * 0.35f, 0f, Math.Max(0f, _chartEditorSongDuration - visibleSeconds));
        float end = start + visibleSeconds;
        float laneHeight = grid.Height / (float)LaneCount;
        using var lanePen = new Pen(Color.FromArgb(45, 105, 135, 190), Math.Max(1f, ScaleY(1f)));
        using var beatPen = new Pen(Color.FromArgb(42, 180, 200, 245), Math.Max(1f, ScaleY(1f)));
        using var textBrush = new SolidBrush(Color.FromArgb(175, 196, 228));

        for (int lane = 0; lane <= LaneCount; lane++)
        {
            float y = grid.Top + lane * laneHeight;
            g.DrawLine(lanePen, grid.Left, y, grid.Right, y);
            if (lane < LaneCount)
                g.DrawString($"L{lane + 1}", smallFont, textBrush, grid.Left + ScaleX(8f), y + ScaleY(8f));
        }

        float beatStep = Math.Max(0.1f, 60f / Math.Max(1f, _chartEditorBpm));
        for (float t = MathF.Floor(start / beatStep) * beatStep; t <= end; t += beatStep)
        {
            float x = grid.Left + (t - start) / visibleSeconds * grid.Width;
            g.DrawLine(beatPen, x, grid.Top, x, grid.Bottom);
        }

        for (int i = 0; i < _chartEditorNotes.Count; i++)
        {
            LaneNote note = _chartEditorNotes[i];
            if (note.Time < start - 1f || note.Time > end + 1f)
                continue;

            float x = grid.Left + (note.Time - start) / visibleSeconds * grid.Width;
            float y = grid.Top + note.Lane * laneHeight + ScaleY(7f);
            float w = Math.Max(ScaleX(12f), note.Type == NoteType.Tap ? ScaleX(18f) : Math.Max(ScaleX(24f), note.Duration / visibleSeconds * grid.Width));
            RectangleF rect = new(x - w / 2f, y, w, laneHeight - ScaleY(14f));
            Color color = note.Type switch
            {
                NoteType.Long => Color.FromArgb(90, 230, 160),
                NoteType.Slide => Color.FromArgb(255, 185, 90),
                _ => Color.FromArgb(90, 165, 245),
            };
            using var brush = new SolidBrush(i == _chartEditorSelectedIndex ? Color.FromArgb(240, color) : Color.FromArgb(170, color));
            using var pen = new Pen(i == _chartEditorSelectedIndex ? Color.White : Color.FromArgb(140, color), Math.Max(1f, ScaleY(1.4f)));
            g.FillRectangle(brush, rect);
            g.DrawRectangle(pen, Rectangle.Round(rect));
            if (note.Type == NoteType.Slide && note.EndLane != note.Lane)
            {
                float endY = grid.Top + note.EndLane * laneHeight + laneHeight / 2f;
                using var slidePen = new Pen(Color.FromArgb(210, color), Math.Max(2f, ScaleY(2.5f))) { EndCap = LineCap.ArrowAnchor };
                g.DrawLine(slidePen, x, rect.Top + rect.Height / 2f, x + w / 2f, endY);
            }
        }

        float playheadX = grid.Left + (_chartEditorCursorTime - start) / visibleSeconds * grid.Width;
        using var headPen = new Pen(Color.FromArgb(245, 255, 235, 120), Math.Max(2f, ScaleY(2.2f)));
        g.DrawLine(headPen, playheadX, grid.Top, playheadX, grid.Bottom);
        using var timeBrush = new SolidBrush(Color.FromArgb(230, 255, 245, 160));
        g.DrawString($"{_chartEditorCursorTime:F2}s", labelFont, timeBrush, playheadX + ScaleX(6f), grid.Top + ScaleY(6f));
    }

    private void DrawChartEditorStats(Graphics g, Font font)
    {
        ChartValidationResult result = ChartValidator.ValidateAndFilter(_chartEditorNotes, LaneCount);
        _chartEditorDifficulty = result.Difficulty;
        Rectangle panel = Rectangle.Round(new RectangleF(ScaleX(70f), ScaleY(602f), ScaleX(1012f), ScaleY(88f)));
        using var path = CreateRoundedRect(panel, ScaleY(10f));
        using var fill = new SolidBrush(Color.FromArgb(95, 12, 18, 32));
        using var border = new Pen(Color.FromArgb(70, 115, 150, 205), Math.Max(1f, ScaleY(1.2f)));
        using var brush = new SolidBrush(Color.FromArgb(210, 226, 248));
        g.FillPath(fill, path);
        g.DrawPath(border, path);

        string stats = $"Lv.{result.Difficulty.Level}  {_chartEditorNotes.Count} notes  {result.Difficulty.NotesPerSecond:F1} n/s  Chord {result.Difficulty.ChordRatio:P0}  Jack {result.Difficulty.JackRatio:P0}  Long {result.Difficulty.LongRatio:P0}  Slide {result.Difficulty.SlideRatio:P0}";
        g.DrawString(stats, font, brush, panel.Left + ScaleX(18f), panel.Top + ScaleY(12f));
        g.DrawString(_chartEditorStatus, font, brush, panel.Left + ScaleX(18f), panel.Top + ScaleY(42f));
    }

    private bool UpdateChartEditorHover(Point location)
    {
        int old = _hoverChartEditorAction;
        _hoverChartEditorAction = -1;
        for (int i = 0; i < 9; i++)
        {
            if (GetChartEditorActionBounds(i).Contains(location))
            {
                _hoverChartEditorAction = i;
                break;
            }
        }

        return old != _hoverChartEditorAction;
    }

    private bool IsChartEditorInteractive(Point location)
    {
        if (GetChartEditorGridBounds().Contains(location))
            return true;

        for (int i = 0; i < 9; i++)
            if (GetChartEditorActionBounds(i).Contains(location))
                return true;

        return false;
    }

    private void HandleChartEditorMouseDown(Point location, MouseButtons button)
    {
        for (int i = 0; i < 9; i++)
        {
            if (GetChartEditorActionBounds(i).Contains(location))
            {
                HandleChartEditorAction(i);
                return;
            }
        }

        if (!GetChartEditorGridBounds().Contains(location))
            return;

        int hit = FindChartEditorNoteAt(location);
        if (button == MouseButtons.Right)
        {
            if (hit >= 0)
                RemoveChartEditorNote(hit);
            return;
        }

        if (hit >= 0)
        {
            _chartEditorSelectedIndex = hit;
            _chartEditorCursorTime = _chartEditorNotes[hit].Time;
            return;
        }

        AddChartEditorNote(location);
    }

    private void HandleChartEditorAction(int action)
    {
        switch (action)
        {
            case 0:
                _screen = UiScreen.SongSelect;
                _previewSongKey = string.Empty;
                InvalidateSongCache();
                break;
            case 1:
                SaveChartEditor();
                break;
            case 2:
                UndoChartEditor();
                break;
            case 3:
                CycleChartEditorType();
                break;
            case 4:
                PushChartEditorUndo();
                _chartEditorBpm = Math.Max(40f, _chartEditorBpm - 1f);
                break;
            case 5:
                PushChartEditorUndo();
                _chartEditorBpm = Math.Min(300f, _chartEditorBpm + 1f);
                break;
            case 6:
                _chartEditorCursorTime = Math.Max(0f, _chartEditorCursorTime - 1f);
                break;
            case 7:
                _chartEditorCursorTime = Math.Min(_chartEditorSongDuration, _chartEditorCursorTime + 1f);
                break;
            case 8:
                if (GetSelectedSong() is SongEntry song)
                    _audio.PlaySongPreview(song.FilePath, _chartEditorCursorTime, 10f, _previewVolume);
                break;
        }
    }

    private void HandleChartEditorKeyDown(Keys key)
    {
        switch (key)
        {
            case Keys.Escape:
            case Keys.Back:
                _screen = UiScreen.SongSelect;
                _previewSongKey = string.Empty;
                InvalidateSongCache();
                break;
            case Keys.S:
                SaveChartEditor();
                break;
            case Keys.Z:
                UndoChartEditor();
                break;
            case Keys.T:
                CycleChartEditorType();
                break;
            case Keys.Delete:
                if (_chartEditorSelectedIndex >= 0)
                    RemoveChartEditorNote(_chartEditorSelectedIndex);
                break;
            case Keys.Left:
                MoveSelectedChartEditorNote(-1, 0);
                break;
            case Keys.Right:
                MoveSelectedChartEditorNote(1, 0);
                break;
            case Keys.Up:
                MoveSelectedChartEditorNote(0, -1);
                break;
            case Keys.Down:
                MoveSelectedChartEditorNote(0, 1);
                break;
            case Keys.Oemplus:
            case Keys.Add:
                HandleChartEditorAction(5);
                break;
            case Keys.OemMinus:
            case Keys.Subtract:
                HandleChartEditorAction(4);
                break;
            case Keys.Space:
                HandleChartEditorAction(8);
                break;
        }
    }

    private int FindChartEditorNoteAt(Point location)
    {
        Rectangle grid = GetChartEditorGridBounds();
        float visibleSeconds = 16f;
        float start = Math.Clamp(_chartEditorCursorTime - visibleSeconds * 0.35f, 0f, Math.Max(0f, _chartEditorSongDuration - visibleSeconds));
        float laneHeight = grid.Height / (float)LaneCount;
        int best = -1;
        float bestDistance = ScaleX(28f);

        for (int i = 0; i < _chartEditorNotes.Count; i++)
        {
            LaneNote note = _chartEditorNotes[i];
            float x = grid.Left + (note.Time - start) / visibleSeconds * grid.Width;
            float y = grid.Top + note.Lane * laneHeight + laneHeight / 2f;
            float dist = MathF.Abs(location.X - x) + MathF.Abs(location.Y - y);
            if (dist < bestDistance)
            {
                bestDistance = dist;
                best = i;
            }
        }

        return best;
    }

    private void AddChartEditorNote(Point location)
    {
        Rectangle grid = GetChartEditorGridBounds();
        float visibleSeconds = 16f;
        float start = Math.Clamp(_chartEditorCursorTime - visibleSeconds * 0.35f, 0f, Math.Max(0f, _chartEditorSongDuration - visibleSeconds));
        float laneHeight = grid.Height / (float)LaneCount;
        int lane = Math.Clamp((int)((location.Y - grid.Top) / laneHeight), 0, LaneCount - 1);
        float rawTime = start + (location.X - grid.Left) / (float)grid.Width * visibleSeconds;
        float time = SnapChartEditorTime(rawTime);
        float duration = _chartEditorInsertType == NoteType.Tap ? 0f : _chartEditorInsertType == NoteType.Long ? 0.65f : 0.48f;
        int endLane = _chartEditorInsertType == NoteType.Slide ? Math.Clamp(lane + 1, 0, LaneCount - 1) : lane;
        PushChartEditorUndo();
        _chartEditorNotes.Add(new LaneNote(time, lane, _chartEditorInsertType, duration, endLane));
        SortChartEditorNotes();
        _chartEditorSelectedIndex = _chartEditorNotes.FindIndex(n => MathF.Abs(n.Time - time) < 0.001f && n.Lane == lane);
        _chartEditorCursorTime = time;
        _chartEditorStatus = "Note added";
    }

    private void RemoveChartEditorNote(int index)
    {
        if (index < 0 || index >= _chartEditorNotes.Count)
            return;

        PushChartEditorUndo();
        _chartEditorNotes.RemoveAt(index);
        _chartEditorSelectedIndex = Math.Clamp(index - 1, -1, _chartEditorNotes.Count - 1);
        _chartEditorStatus = "Note removed";
    }

    private void MoveSelectedChartEditorNote(int timeSteps, int laneDelta)
    {
        if (_chartEditorSelectedIndex < 0 || _chartEditorSelectedIndex >= _chartEditorNotes.Count)
            return;

        PushChartEditorUndo();
        LaneNote note = _chartEditorNotes[_chartEditorSelectedIndex];
        float step = 60f / Math.Max(1f, _chartEditorBpm) / 4f;
        float time = Math.Clamp(note.Time + timeSteps * step, 0f, _chartEditorSongDuration);
        int lane = Math.Clamp(note.Lane + laneDelta, 0, LaneCount - 1);
        int endLane = note.Type == NoteType.Slide
            ? Math.Clamp(note.EndLane + laneDelta, 0, LaneCount - 1)
            : lane;
        _chartEditorNotes[_chartEditorSelectedIndex] = new LaneNote(time, lane, note.Type, note.Duration, endLane);
        _chartEditorCursorTime = time;
        SortChartEditorNotes();
        _chartEditorStatus = "Note moved";
    }

    private void SaveChartEditor()
    {
        ChartGenerator.SaveUserChart(_chartEditorSongTitle, _songSelectDifficultyIndex, LaneCount, _chartEditorBpm, _chartEditorNotes);
        EditableChart chart = NoteLane.LoadEditableChart(_chartEditorSongTitle, _songSelectDifficultyIndex, LaneCount);
        _chartEditorPath = chart.Path;
        _chartEditorNotes = chart.Notes.ToList();
        _chartEditorDifficulty = chart.Difficulty;
        _chartEditorUndo.Clear();
        _chartEditorStatus = $"Saved {Path.GetFileName(_chartEditorPath)}";
        _previewSongKey = string.Empty;
    }

    private void UndoChartEditor()
    {
        if (_chartEditorUndo.Count == 0)
        {
            _chartEditorStatus = "Nothing to undo";
            return;
        }

        _chartEditorNotes = _chartEditorUndo.Pop();
        _chartEditorSelectedIndex = Math.Clamp(_chartEditorSelectedIndex, -1, _chartEditorNotes.Count - 1);
        _chartEditorStatus = "Undo";
    }

    private void CycleChartEditorType()
    {
        _chartEditorInsertType = _chartEditorInsertType switch
        {
            NoteType.Tap => NoteType.Long,
            NoteType.Long => NoteType.Slide,
            _ => NoteType.Tap,
        };
        _chartEditorStatus = $"Insert {_chartEditorInsertType}";
    }

    private float SnapChartEditorTime(float time)
    {
        float step = 60f / Math.Max(1f, _chartEditorBpm) / 4f;
        return Math.Clamp(MathF.Round(time / step) * step, 0f, _chartEditorSongDuration);
    }

    private void PushChartEditorUndo()
    {
        _chartEditorUndo.Push(_chartEditorNotes.ToList());
    }

    private void SortChartEditorNotes()
    {
        LaneNote? selected = _chartEditorSelectedIndex >= 0 && _chartEditorSelectedIndex < _chartEditorNotes.Count
            ? _chartEditorNotes[_chartEditorSelectedIndex]
            : null;
        _chartEditorNotes = _chartEditorNotes.OrderBy(n => n.Time).ThenBy(n => n.Lane).ToList();
        if (selected is LaneNote note)
            _chartEditorSelectedIndex = _chartEditorNotes.FindIndex(n => MathF.Abs(n.Time - note.Time) < 0.001f && n.Lane == note.Lane && n.Type == note.Type);
    }

    private static string GetDifficultyLabel(int difficultyIndex)
    {
        return difficultyIndex switch
        {
            0 => "EASY",
            1 => "NORMAL",
            _ => "HARD",
        };
    }
}
