using System.Drawing.Drawing2D;

namespace RhythmGame;

public sealed partial class GameForm
{
    private IReadOnlyList<ChartTempoPoint> _chartEditorTempoMap = [new(0f, 120f)];
    private IReadOnlyList<ChartTempoPoint> EffectiveEditorTempoMap => _chartEditorTempoMap.Select((p, i) => i == 0 ? p with { Bpm = _chartEditorBpm } : p).ToArray();
    private readonly Stack<ChartEditorSnapshot> _chartEditorRedo = new();
    private readonly HashSet<int> _chartEditorSelection = [];
    private List<LaneNote> _chartEditorClipboard = [];
    private int _chartEditorSubdivision = 4;
    private ChartEditorSnapshot? _chartEditorSaved;
    private float? _chartEditorRangeStart;
    private int _chartEditorDiagnosticIndex;

    private string[] GetChartEditorActions() => ["BACK", "SAVE", "UNDO", $"TYPE {_chartEditorInsertType}",
        "BPM -", "BPM +", "TIME -", "TIME +", "PREVIEW", "REDO", _chartEditorSubdivision == 0 ? "SNAP OFF" : $"SNAP 1/{_chartEditorSubdivision} BEAT",
        "SELECT ALL", "COPY", "PASTE", "MIRROR", "QUANTIZE", "RANGE IN", "RANGE OUT", "WARNING",
        "INSERT", "LENGTH -", "LENGTH +", "EDIT VALUES"];

    private ChartEditorSnapshot CaptureChartEditor() => new(_chartEditorNotes.ToList(), _chartEditorBpm,
        _chartEditorSelectedIndex, _chartEditorCursorTime);

    private bool ChartEditorDirty => _chartEditorSaved is not null &&
        (_chartEditorBpm != _chartEditorSaved.Bpm || !_chartEditorNotes.SequenceEqual(_chartEditorSaved.Notes));

    private void CloseChartEditor()
    {
        if (ChartEditorDirty)
        {
            DialogResult choice = MessageBox.Show(this, "Save chart changes before leaving?", "Chart editor",
                MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (choice == DialogResult.Cancel) return;
            if (choice == DialogResult.Yes && !SaveChartEditor()) return;
        }
        _audio.StopSongPreview();
        _screen = UiScreen.SongSelect;
        _previewSongKey = string.Empty;
        InvalidateSongCache();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_selfTestMode && _screen == UiScreen.ChartEditor && ChartEditorDirty)
        {
            DialogResult choice = MessageBox.Show(this, "Save chart changes before closing?", "Chart editor",
                MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (choice == DialogResult.Cancel || (choice == DialogResult.Yes && !SaveChartEditor())) e.Cancel = true;
        }
        base.OnFormClosing(e);
    }

    private void OpenChartEditor(SongEntry song)
    {
        _audio.StopSongPreview();
        EditableChart chart;
        try { chart = NoteLane.LoadEditableChart(song.Title, _songSelectDifficultyIndex, LaneCount); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            AppLogger.Error("Could not open chart editor.", ex);
            MessageBox.Show(this, "차트를 열 수 없습니다. 파일 권한과 백업을 확인하세요.", "Chart editor");
            return;
        }
        _chartEditorSongTitle = song.Title;
        _chartEditorSongDuration = Math.Max(30f, song.DurationSeconds);
        _chartEditorBpm = chart.Bpm > 0f ? chart.Bpm : Math.Max(60f, song.Bpm);
        _chartEditorPath = chart.Path;
        _chartEditorTempoMap = chart.TempoMap;
        _chartEditorNotes = chart.Notes.OrderBy(n => n.Time).ThenBy(n => n.Lane).ToList();
        _chartEditorUndo.Clear();
        _chartEditorSelectedIndex = _chartEditorNotes.Count > 0 ? 0 : -1;
        _chartEditorCursorTime = _chartEditorSelectedIndex >= 0 ? _chartEditorNotes[_chartEditorSelectedIndex].Time : 0f;
        _chartEditorDifficulty = chart.Difficulty;
        _chartEditorStatus = chart.Diagnostics.Count == 0
            ? $"Loaded {Path.GetFileName(chart.Path)}"
            : $"Loaded with {chart.Diagnostics.Count} warnings";
        _chartEditorRedo.Clear();
        _chartEditorSelection.Clear();
        _chartEditorRangeStart = null;
        _chartEditorSaved = CaptureChartEditor();
        _screen = UiScreen.ChartEditor;
    }

    private void DrawChartEditor(Graphics g)
    {
        Rectangle layoutRect = new(0, 0, (int)ScaleX(DesignWidth), (int)ScaleY(DesignHeight));
        using (var bg = new LinearGradientBrush(layoutRect, UseHighContrast ? Color.Black : Color.FromArgb(9, 13, 24), UseHighContrast ? Color.Black : Color.FromArgb(19, 28, 46), LinearGradientMode.Vertical))
            g.FillRectangle(bg, layoutRect);

        using var titleFont = new Font("Segoe UI", Math.Max(11f, ScaleY(26f) * _textScalePercent / 100f), FontStyle.Regular);
        using var labelFont = new Font("Segoe UI", Math.Max(7f, ScaleY(11f)), FontStyle.Regular);
        using var smallFont = new Font("Segoe UI", Math.Max(6f, ScaleTextY(9.5f)), FontStyle.Regular);
        using var titleBrush = new SolidBrush(Color.FromArgb(238, 246, 255));
        using var mutedBrush = new SolidBrush(Color.FromArgb(175, 190, 218));

        DrawCentered(g, ChartEditorDirty ? "CHART EDITOR *" : "CHART EDITOR", titleFont, titleBrush, (int)ScaleX(DesignWidth / 2f), (int)ScaleY(28f));
        string header = $"{_chartEditorSongTitle}  |  {LaneCount}K  |  {GetDifficultyLabel(_songSelectDifficultyIndex)}  |  BPM {_chartEditorBpm:F0}";
        DrawCentered(g, header, labelFont, mutedBrush, (int)ScaleX(DesignWidth / 2f), (int)ScaleY(66f));

        using var toolbarFont = new Font("Segoe UI", Math.Max(7f, ScaleTextY(9.5f)), FontStyle.Regular);
        g.DrawString("TRANSFORM / PROPERTIES", smallFont, mutedBrush, ScaleX(894), ScaleY(194));
        DrawChartEditorToolbar(g, toolbarFont);
        DrawChartEditorGrid(g, labelFont, smallFont);
        DrawChartEditorStats(g, smallFont);
    }

    private void DrawChartEditorToolbar(Graphics g, Font font)
    {
        string[] labels = GetChartEditorActions();
        for (int i = 0; i < labels.Length; i++)
            DrawChartEditorButton(g, GetChartEditorActionBounds(i), labels[i], _hoverChartEditorAction == i, font);
    }

    private Rectangle GetChartEditorGridBounds()
    {
        return Rectangle.Round(new RectangleF(ScaleX(70f), ScaleY(184f), ScaleX(800f), ScaleY(350f)));
    }

    private Rectangle GetChartEditorActionBounds(int index)
    {
        if (index == 22) return Rectangle.Round(new RectangleF(ScaleX(894), ScaleY(140), ScaleX(193), ScaleY(38)));
        int[] document = [0, 1, 2, 9, 8];
        int position = Array.IndexOf(document, index);
        if (position >= 0)
            return Rectangle.Round(new RectangleF(ScaleX(70 + position * 142), ScaleY(94), ScaleX(132), ScaleY(38)));
        int[] tools = [3, 10, 19];
        position = Array.IndexOf(tools, index);
        if (position >= 0)
            return Rectangle.Round(new RectangleF(ScaleX(70 + position * 190), ScaleY(140), ScaleX(180), ScaleY(34)));
        int[] properties = [4, 5, 6, 7, 20, 21, 11, 18, 12, 13, 14, 15, 16, 17];
        position = Array.IndexOf(properties, index);
        return Rectangle.Round(new RectangleF(ScaleX(894 + position % 2 * 99),
            ScaleY(222 + position / 2 * 44), ScaleX(94), ScaleY(36)));
    }

    private void DrawChartEditorButton(Graphics g, Rectangle bounds, string label, bool hover, Font font)
    {
        DrawConsoleButton(g, bounds, label, font, hover, label == "SAVE");
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

        GraphicsState gridState = g.Save();
        g.SetClip(grid, CombineMode.Intersect);
        IReadOnlyList<ChartTempoPoint> tempo = EffectiveEditorTempoMap;
        for (int segment = 0; segment < tempo.Count; segment++)
        {
            ChartTempoPoint point = tempo[segment];
            float segmentEnd = segment + 1 < tempo.Count ? Math.Min(end, tempo[segment + 1].Time) : end;
            if (segmentEnd < start || point.Time > end) continue;
            float step = Math.Max(0.01f, 60f / point.Bpm / Math.Max(1, _chartEditorSubdivision));
            float first = point.Time + Math.Max(0, MathF.Ceiling((start - point.Time) / step)) * step;
            for (float t = first; t <= segmentEnd; t += step)
            {
                float x = grid.Left + (t - start) / visibleSeconds * grid.Width;
                g.DrawLine(beatPen, x, grid.Top, x, grid.Bottom);
            }
        }

        for (int i = 0; i < _chartEditorNotes.Count; i++)
        {
            LaneNote note = _chartEditorNotes[i];
            if (note.Time < start - 1f || note.Time > end + 1f)
                continue;

            float x = grid.Left + (note.Time - start) / visibleSeconds * grid.Width;
            float y = grid.Top + note.Lane * laneHeight + ScaleY(7f);
            float w = Math.Max(ScaleX(12f), note.Type == NoteType.Tap ? ScaleX(18f) : Math.Max(ScaleX(24f), note.Duration / visibleSeconds * grid.Width));
            RectangleF rect = new(note.Type == NoteType.Tap ? x - w / 2f : x, y, w, laneHeight - ScaleY(14f));
            Color color = note.Type switch
            {
                NoteType.Long => Color.FromArgb(90, 230, 160),
                NoteType.Slide => Color.FromArgb(255, 185, 90),
                _ => Color.FromArgb(90, 165, 245),
            };
            using var brush = new SolidBrush((i == _chartEditorSelectedIndex || _chartEditorSelection.Contains(i)) ? Color.FromArgb(240, color) : Color.FromArgb(170, color));
            using var pen = new Pen((i == _chartEditorSelectedIndex || _chartEditorSelection.Contains(i)) ? Color.White : Color.FromArgb(140, color), Math.Max(1f, ScaleY(1.4f)));
            g.FillRectangle(brush, rect);
            g.DrawRectangle(pen, Rectangle.Round(rect));
            if (UseHighContrast || _colorVisionMode != 0)
                g.DrawString(note.Type == NoteType.Tap ? "T" : note.Type == NoteType.Long ? "L" : "S", smallFont, Brushes.Black, rect.Left + 2, rect.Top + 2);
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
        g.DrawString($"{_chartEditorCursorTime:F2}s", labelFont, timeBrush, grid.Right - ScaleX(90f), grid.Top + ScaleY(6f));
        g.Restore(gridState);
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

        float[] density = ChartEditing.Density(_chartEditorNotes, _chartEditorSongDuration);
        float maximum = Math.Max(1f, density.Max());
        for (int i = 0; i < density.Length; i++)
        {
            float height = density[i] / maximum * ScaleY(38f);
            g.FillRectangle(brush, panel.Left + i * panel.Width / (float)density.Length,
                ScaleY(590f) - height, Math.Max(1f, panel.Width / (float)density.Length - 2f), height);
        }
        Rectangle overview = GetEditorOverviewBounds();
        float start = Math.Clamp(_chartEditorCursorTime - 16f * 0.35f, 0, Math.Max(0, _chartEditorSongDuration - 16));
        using var viewportPen = new Pen(InterfaceTheme.Accent, Math.Max(1, ScaleY(2)));
        g.DrawRectangle(viewportPen, overview.Left + start / Math.Max(1, _chartEditorSongDuration) * overview.Width,
            overview.Top, Math.Min(1, 16f / Math.Max(1, _chartEditorSongDuration)) * overview.Width, overview.Height);
        string stats = $"Lv.{result.Difficulty.Level}  {_chartEditorNotes.Count} notes  {result.Difficulty.NotesPerSecond:F1} n/s  Chord {result.Difficulty.ChordRatio:P0}  Jack {result.Difficulty.JackRatio:P0}  Long {result.Difficulty.LongRatio:P0}  Slide {result.Difficulty.SlideRatio:P0}";
        g.DrawString(stats, font, brush, panel.Left + ScaleX(18f), panel.Top + ScaleY(12f));
        using var statusFormat = new StringFormat { Trimming = StringTrimming.EllipsisCharacter };
        g.DrawString($"{_chartEditorStatus} | {result.Diagnostics.Count} warnings", font, brush,
            new RectangleF(panel.Left + ScaleX(18f), panel.Top + ScaleY(40f), panel.Width - ScaleX(36f), ScaleY(42f)), statusFormat);
    }

    private bool UpdateChartEditorHover(Point location)
    {
        int old = _hoverChartEditorAction;
        _hoverChartEditorAction = -1;
        for (int i = 0; i < GetChartEditorActions().Length; i++)
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
        if (GetChartEditorGridBounds().Contains(location) || GetEditorOverviewBounds().Contains(location))
            return true;

        for (int i = 0; i < GetChartEditorActions().Length; i++)
            if (GetChartEditorActionBounds(i).Contains(location))
                return true;

        return false;
    }

    private Rectangle GetEditorOverviewBounds() =>
        Rectangle.Round(new RectangleF(ScaleX(70), ScaleY(550), ScaleX(1012), ScaleY(42)));

    private void HandleChartEditorMouseDown(Point location, MouseButtons button)
    {
        Rectangle overview = GetEditorOverviewBounds();
        if (overview.Contains(location))
        {
            _chartEditorCursorTime = Math.Clamp((location.X - overview.Left) / (float)overview.Width, 0, 1) * _chartEditorSongDuration;
            Invalidate();
            return;
        }
        for (int i = 0; i < GetChartEditorActions().Length; i++)
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
            if ((ModifierKeys & Keys.Control) == Keys.Control)
            {
                if (!_chartEditorSelection.Add(hit)) _chartEditorSelection.Remove(hit);
            }
            else _chartEditorSelection.Clear();
            _chartEditorSelectedIndex = hit;
            _chartEditorCursorTime = _chartEditorNotes[hit].Time;
            return;
        }

        AddChartEditorNote(location);
    }

    private void HandleChartEditorAction(int action)
    {
        if (action == 22) { OpenChartEditorProperties(); return; }
        switch (action)
        {
            case 0:
                CloseChartEditor();
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
            case 9:
                RedoChartEditor();
                break;
            case 10:
                int index = Array.IndexOf(ChartEditing.Subdivisions, _chartEditorSubdivision);
                _chartEditorSubdivision = ChartEditing.Subdivisions[(index + 1) % ChartEditing.Subdivisions.Length];
                _chartEditorStatus = _chartEditorSubdivision == 0 ? "Snap off" : $"{_chartEditorSubdivision} divisions per beat";
                break;
            case 11:
                _chartEditorSelection.Clear();
                _chartEditorSelection.UnionWith(Enumerable.Range(0, _chartEditorNotes.Count));
                break;
            case 12:
                _chartEditorClipboard = SelectedChartEditorIndices().Select(i => _chartEditorNotes[i]).ToList();
                _chartEditorStatus = $"Copied {_chartEditorClipboard.Count} notes";
                break;
            case 13:
                PasteChartEditor();
                break;
            case 14:
                TransformChartEditor(note => ChartEditing.Mirror(note, LaneCount));
                break;
            case 15:
                TransformChartEditor(note => note with { Time = SnapChartEditorTime(note.Time) });
                break;
            case 16:
                _chartEditorRangeStart = _chartEditorCursorTime;
                _chartEditorStatus = "Range start set; move time, then RANGE OUT";
                break;
            case 17:
                if (_chartEditorRangeStart is float rangeStart)
                {
                    _chartEditorSelection.Clear();
                    _chartEditorSelection.UnionWith(Enumerable.Range(0, _chartEditorNotes.Count).Where(i =>
                        _chartEditorNotes[i].Time >= Math.Min(rangeStart, _chartEditorCursorTime) &&
                        _chartEditorNotes[i].Time <= Math.Max(rangeStart, _chartEditorCursorTime)));
                    _chartEditorStatus = $"Selected {_chartEditorSelection.Count} notes";
                }
                break;
            case 18:
                JumpChartEditorWarning();
                break;
            case 19:
                PushChartEditorUndo();
                int insertLane = _chartEditorSelectedIndex >= 0 ? _chartEditorNotes[_chartEditorSelectedIndex].Lane : 0;
                int insertEndLane = _chartEditorInsertType == NoteType.Slide ? (insertLane == LaneCount - 1 ? insertLane - 1 : insertLane + 1) : insertLane;
                _chartEditorNotes.Add(new LaneNote(SnapChartEditorTime(_chartEditorCursorTime), insertLane,
                    _chartEditorInsertType, _chartEditorInsertType == NoteType.Tap ? 0f : 0.65f, insertEndLane));
                _chartEditorSelectedIndex = _chartEditorNotes.Count - 1;
                SortChartEditorNotes();
                break;
            case 20:
            case 21:
                float lengthChange = (action == 20 ? -1f : 1f) * 60f / _chartEditorBpm / Math.Max(1, _chartEditorSubdivision);
                TransformChartEditor(note => note.Type == NoteType.Tap ? note : note with { Duration = Math.Max(0.1f, note.Duration + lengthChange) });
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
                CloseChartEditor();
                break;
            case Keys.S:
                SaveChartEditor();
                break;
            case Keys.Z:
                UndoChartEditor();
                break;
            case Keys.Y:
                RedoChartEditor();
                break;
            case Keys.G:
                HandleChartEditorAction(10);
                break;
            case Keys.A:
                HandleChartEditorAction(11);
                break;
            case Keys.C:
                HandleChartEditorAction(12);
                break;
            case Keys.V:
                HandleChartEditorAction(13);
                break;
            case Keys.M:
                HandleChartEditorAction(14);
                break;
            case Keys.Q:
                HandleChartEditorAction(15);
                break;
            case Keys.Insert:
                HandleChartEditorAction(19);
                break;
            case Keys.T:
                CycleChartEditorType();
                break;
            case Keys.Delete:
                int[] selectedForDelete = SelectedChartEditorIndices();
                if (selectedForDelete.Length > 0)
                {
                    PushChartEditorUndo();
                    foreach (int i in selectedForDelete.OrderDescending()) _chartEditorNotes.RemoveAt(i);
                    _chartEditorSelection.Clear();
                    _chartEditorSelectedIndex = _chartEditorNotes.Count > 0 ? 0 : -1;
                }
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
        _chartEditorSelection.Clear();
        _chartEditorSelectedIndex = Math.Clamp(index - 1, -1, _chartEditorNotes.Count - 1);
        _chartEditorStatus = "Note removed";
    }

    private void MoveSelectedChartEditorNote(int timeSteps, int laneDelta)
    {
        if (_chartEditorSelectedIndex < 0 || _chartEditorSelectedIndex >= _chartEditorNotes.Count)
            return;

        float cursorBpm = EffectiveEditorTempoMap.Last(p => p.Time <= _chartEditorCursorTime).Bpm;
        float step = 60f / cursorBpm / Math.Max(1, _chartEditorSubdivision);
        TransformChartEditor(note => note with
        {
            Time = note.Time + timeSteps * step,
            Lane = note.Lane + laneDelta,
            EndLane = note.EndLane < 0 ? -1 : note.EndLane + laneDelta,
        });
    }

    private bool SaveChartEditor()
    {
        try
        {
            ChartGenerator.SaveUserChart(_chartEditorSongTitle, _songSelectDifficultyIndex, LaneCount, _chartEditorBpm, _chartEditorNotes, EffectiveEditorTempoMap);
            _chartEditorPath = ChartGenerator.GetUserChartPath(_chartEditorSongTitle, _songSelectDifficultyIndex, LaneCount);
            _chartEditorSaved = CaptureChartEditor();
            _chartEditorStatus = $"Saved {Path.GetFileName(_chartEditorPath)}";
            _previewSongKey = string.Empty;
            InvalidateSongCache();
            return true;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            AppLogger.Error("Chart save failed; original preserved.", ex);
            _chartEditorStatus = "Save failed. Check warnings and write permission; original preserved.";
            return false;
        }
    }

    private void RestoreChartEditor(ChartEditorSnapshot state)
    {
        _chartEditorNotes = state.Notes.ToList();
        _chartEditorBpm = state.Bpm;
        _chartEditorSelectedIndex = state.SelectedIndex;
        _chartEditorCursorTime = state.Cursor;
        _chartEditorSelection.Clear();
    }

    private void UndoChartEditor()
    {
        if (_chartEditorUndo.Count == 0) { _chartEditorStatus = "Nothing to undo"; return; }
        _chartEditorRedo.Push(CaptureChartEditor());
        RestoreChartEditor(_chartEditorUndo.Pop());
        _chartEditorStatus = "Undo";
    }

    private void RedoChartEditor()
    {
        if (_chartEditorRedo.Count == 0) { _chartEditorStatus = "Nothing to redo"; return; }
        _chartEditorUndo.Push(CaptureChartEditor());
        RestoreChartEditor(_chartEditorRedo.Pop());
        _chartEditorStatus = "Redo";
    }

    private int[] SelectedChartEditorIndices() => _chartEditorSelection.Count > 0
        ? _chartEditorSelection.Order().ToArray()
        : _chartEditorSelectedIndex >= 0 ? [_chartEditorSelectedIndex] : [];

    private void TransformChartEditor(Func<LaneNote, LaneNote> transform)
    {
        int[] selected = SelectedChartEditorIndices();
        if (selected.Length == 0) return;
        var candidate = _chartEditorNotes.ToList();
        foreach (int i in selected) candidate[i] = transform(candidate[i]);
        if (!AcceptChartEditorCandidate(candidate)) return;
        PushChartEditorUndo();
        _chartEditorNotes = candidate;
        SortChartEditorNotes();
        _chartEditorStatus = $"Changed {selected.Length} notes";
    }

    private bool AcceptChartEditorCandidate(List<LaneNote> notes)
    {
        if (notes.Any(n => n.Time < 0f || n.Time + n.Duration > _chartEditorSongDuration) ||
            ChartValidator.ValidateAndFilter(notes, LaneCount).Notes.Count != notes.Count)
        {
            _chartEditorStatus = "Edit rejected: overlap, invalid lane or outside song. Nothing changed.";
            return false;
        }
        return true;
    }

    private void PasteChartEditor()
    {
        if (_chartEditorClipboard.Count == 0) return;
        float start = _chartEditorClipboard.Min(n => n.Time);
        var additions = _chartEditorClipboard.Select(n => n with { Time = n.Time - start + _chartEditorCursorTime }).ToList();
        var candidate = _chartEditorNotes.Concat(additions).ToList();
        if (!AcceptChartEditorCandidate(candidate)) return;
        PushChartEditorUndo();
        _chartEditorNotes = candidate;
        _chartEditorSelection.Clear();
        _chartEditorSelection.UnionWith(Enumerable.Range(candidate.Count - additions.Count, additions.Count));
        SortChartEditorNotes();
        _chartEditorStatus = $"Pasted {additions.Count} notes";
    }

    private void JumpChartEditorWarning()
    {
        ChartValidationResult validation = ChartValidator.ValidateAndFilter(_chartEditorNotes, LaneCount);
        var accepted = validation.Notes.GroupBy(n => n).ToDictionary(g => g.Key, g => g.Count());
        var invalidList = new List<int>();
        foreach (int i in Enumerable.Range(0, _chartEditorNotes.Count))
        {
            LaneNote note = _chartEditorNotes[i];
            if (note.EndLane < 0) note = note with { EndLane = note.Lane };
            if (accepted.TryGetValue(note, out int count) && count > 0) accepted[note] = count - 1;
            else invalidList.Add(i);
        }
        int[] invalid = invalidList.ToArray();
        if (invalid.Length == 0) { _chartEditorStatus = "No validation warnings"; return; }
        _chartEditorSelectedIndex = invalid[_chartEditorDiagnosticIndex++ % invalid.Length];
        _chartEditorCursorTime = _chartEditorNotes[_chartEditorSelectedIndex].Time;
        _chartEditorStatus = $"Warning: inspect selected note ({invalid.Length} invalid notes)";
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
        return Math.Clamp(ChartEditing.Snap(time, EffectiveEditorTempoMap, _chartEditorSubdivision), 0f, _chartEditorSongDuration);
    }

    private void PushChartEditorUndo()
    {
        _chartEditorUndo.Push(CaptureChartEditor());
        _chartEditorRedo.Clear();
    }

    private void SortChartEditorNotes()
    {
        var selectedNotes = _chartEditorSelection.Where(i => i < _chartEditorNotes.Count).Select(i => _chartEditorNotes[i]).ToHashSet();
        LaneNote? selected = _chartEditorSelectedIndex >= 0 && _chartEditorSelectedIndex < _chartEditorNotes.Count
            ? _chartEditorNotes[_chartEditorSelectedIndex]
            : null;
        _chartEditorNotes = _chartEditorNotes.OrderBy(n => n.Time).ThenBy(n => n.Lane).ToList();
        _chartEditorSelection.Clear();
        _chartEditorSelection.UnionWith(Enumerable.Range(0, _chartEditorNotes.Count).Where(i => selectedNotes.Contains(_chartEditorNotes[i])));
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
