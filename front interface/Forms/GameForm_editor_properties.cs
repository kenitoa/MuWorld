namespace RhythmGame;

public sealed partial class GameForm
{
    private void OpenChartEditorProperties()
    {
        LaneNote? selected = _chartEditorSelectedIndex >= 0 && _chartEditorSelectedIndex < _chartEditorNotes.Count
            ? _chartEditorNotes[_chartEditorSelectedIndex] : null;
        using var dialog = new Form { Text = "Chart properties", ClientSize = new Size(480, 380),
            FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false, MaximizeBox = false, ShowInTaskbar = false };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 2, RowCount = 7 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52));
        NumericUpDown Number(string name, decimal value, decimal min, decimal max, int digits, int row)
        {
            var input = new NumericUpDown { Minimum = min, Maximum = max, DecimalPlaces = digits,
                Increment = digits == 0 ? 1 : 0.01m, Value = Math.Clamp(value, min, max),
                Dock = DockStyle.Fill, AccessibleName = name };
            layout.Controls.Add(new Label { Text = name, AutoSize = true }, 0, row);
            layout.Controls.Add(input, 1, row);
            return input;
        }
        var bpm = Number("Base BPM", (decimal)_chartEditorBpm, 40, 300, 2, 0);
        var time = Number(selected is null ? "Cursor (seconds)" : "Note time (seconds)",
            (decimal)(selected?.Time ?? _chartEditorCursorTime), 0, (decimal)Math.Max(1, _chartEditorSongDuration), 3, 1);
        var lane = Number("Lane", (selected?.Lane ?? 0) + 1, 1, LaneCount, 0, 2);
        var duration = Number("Hold length (seconds)", (decimal)(selected?.Duration ?? 0), 0, (decimal)Math.Max(1, _chartEditorSongDuration), 3, 3);
        var endLane = Number("End lane", (selected?.EndLane ?? 0) + 1, 1, LaneCount, 0, 4);
        lane.Enabled = selected is not null;
        duration.Enabled = selected is not null && selected.Value.Type != NoteType.Tap;
        endLane.Enabled = selected is not null && selected.Value.Type == NoteType.Slide;
        var error = new Label { AutoSize = true };
        layout.Controls.Add(error, 0, 5); layout.SetColumnSpan(error, 2);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        var apply = new Button { Text = "Apply", AutoSize = true };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        actions.Controls.AddRange([apply, cancel]);
        layout.Controls.Add(actions, 0, 6); layout.SetColumnSpan(actions, 2);
        dialog.Controls.Add(layout); dialog.AcceptButton = apply; dialog.CancelButton = cancel;
        InterfaceTheme.StyleControls(dialog);
        apply.Click += (_, _) =>
        {
            if (!TryApplyEditorProperties((float)bpm.Value, (float)time.Value, (int)lane.Value - 1,
                (float)duration.Value, (int)endLane.Value - 1))
            {
                error.Text = "Invalid note or overlap. Check time, lane and length.";
                return;
            }
            dialog.DialogResult = DialogResult.OK;
        };
        dialog.ShowDialog(this);
        Invalidate();
    }

    private bool TryApplyEditorProperties(float bpm, float time, int lane, float duration, int endLane)
    {
        if (!float.IsFinite(bpm) || bpm is < 40 or > 300 || !float.IsFinite(time) ||
            time < 0 || time > _chartEditorSongDuration || lane < 0 || lane >= LaneCount ||
            endLane < 0 || endLane >= LaneCount || !float.IsFinite(duration) || duration < 0)
            return false;
        var candidate = _chartEditorNotes.ToList();
        if (_chartEditorSelectedIndex >= 0 && _chartEditorSelectedIndex < candidate.Count)
        {
            LaneNote note = candidate[_chartEditorSelectedIndex];
            float length = note.Type == NoteType.Tap ? 0 : duration;
            if (note.Type != NoteType.Tap && (length < 0.1f || time + length > _chartEditorSongDuration))
                return false;
            candidate[_chartEditorSelectedIndex] = note with { Time = time, Lane = lane, Duration = length,
                EndLane = note.Type == NoteType.Slide ? endLane : lane };
            if (ChartValidator.ValidateAndFilter(candidate, LaneCount).Notes.Count != candidate.Count)
                return false;
        }
        PushChartEditorUndo();
        _chartEditorNotes = candidate;
        _chartEditorBpm = bpm;
        _chartEditorCursorTime = time;
        SortChartEditorNotes();
        _chartEditorStatus = "Properties updated";
        return true;
    }
}
