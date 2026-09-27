using System.Diagnostics;

namespace RhythmGame;

// Practice uses the production judgment engine and never writes scores or achievements.
internal sealed class TutorialForm : Form
{
    private readonly GameEngine _engine = new();
    private readonly AudioManager _audio = new();
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 8 };
    private readonly Stopwatch _clock = new();
    private readonly Keys[] _keys;
    private readonly HashSet<Keys> _held = [];
    private readonly Label _instructions = new() { Dock = DockStyle.Top, Height = 100, AutoEllipsis = true };
    private readonly Label _feedback = new() { Dock = DockStyle.Bottom, Height = 48 };
    private readonly Button _retry = new() { Text = "연습 시작 / 다시 연습", AutoSize = true };
    private readonly Button _next = new() { Text = "다음 단계", AutoSize = true };
    private readonly string _audioPath;
    private int _stage;
    private double _lastTime;
    internal bool Completed { get; private set; }
    internal bool CalibrateRequested { get; private set; }

    internal TutorialForm(Keys[] keys, int offsetMs)
    {
        _keys = keys.ToArray();
        _engine.AudioOffsetSeconds = offsetMs / 1000f;
        Text = "MuWorld 처음 플레이 연습";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        ClientSize = new Size(900, 640);
        MinimumSize = new Size(720, 580);
        StartPosition = FormStartPosition.CenterParent;
        KeyPreview = true;
        DoubleBuffered = true;
        BackColor = InterfaceTheme.Background;
        ForeColor = InterfaceTheme.Text;
        Font = new Font("Segoe UI", 12);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 50, AutoSize = true };
        var calibrate = new Button { Text = "타이밍 보정", AutoSize = true };
        var skip = new Button { Text = "곡 선택으로", AutoSize = true };
        buttons.Controls.AddRange([_retry, _next, calibrate, skip]);
        Controls.AddRange([_instructions, _feedback, buttons]);
        InterfaceTheme.StyleControls(this);
        _instructions.Padding = new Padding(24, 16, 24, 0);
        _feedback.Padding = new Padding(24, 8, 24, 0);
        _retry.Click += (_, _) => StartStage();
        _next.Click += (_, _) => { StopStage(); if (++_stage >= 5) { Completed = true; Close(); } else ShowStage(); };
        calibrate.Click += (_, _) => { CalibrateRequested = true; Close(); };
        skip.Click += (_, _) => Close();
        _timer.Tick += (_, _) => TickStage();
        Deactivate += (_, _) => { if (_engine.IsRunning) { StopStage(); _feedback.Text = "창을 벗어나 연습을 멈췄습니다. 다시 연습을 누르세요."; } };
        _audioPath = Path.Combine(Path.GetTempPath(), "muworld-tutorial-" + Guid.NewGuid().ToString("N") + ".wav");
        WriteBeatTrack(_audioPath);
        ShowStage();
    }

    internal static IReadOnlyList<LaneNote> BuildStage(int stage, int lanes)
    {
        var notes = new List<LaneNote>();
        if (stage is 0 or 1)
            for (int i = 0; i < 8; i++) notes.Add(new LaneNote(2f + i * 0.5f, i % lanes));
        else if (stage == 2)
            for (int i = 0; i < 4; i++)
            {
                notes.Add(new LaneNote(2f + i, 0));
                notes.Add(new LaneNote(2f + i, lanes - 1));
            }
        else if (stage == 3)
        {
            notes.Add(new LaneNote(2f, 0, NoteType.Long, 1f, 0));
            notes.Add(new LaneNote(4f, lanes - 1, NoteType.Long, 1f, lanes - 1));
        }
        else
        {
            notes.Add(new LaneNote(2f, 0, NoteType.Slide, 1f, 1));
            notes.Add(new LaneNote(4f, lanes - 1, NoteType.Slide, 1f, lanes - 2));
        }
        return notes.OrderBy(n => n.Time).ThenBy(n => n.Lane).ToArray();
    }

    private void ShowStage()
    {
        string[] explanations = [
            "키와 판정선: 노트가 아래 선에 도착할 때 해당 키를 누르세요. 4K~7K는 사용하는 레인 수입니다.",
            "Tap과 Early/Late: 한 번 누릅니다. EARLY는 빠른 입력, LATE는 늦은 입력입니다. 소리와 맞지 않으면 타이밍 보정을 사용하세요.",
            "동시치기: 같은 높이의 노트는 두 키를 함께 누릅니다.",
            "Long: 시작에 누르고 꼬리가 선에 닿을 때까지 유지한 뒤 놓으세요.",
            "Slide: 시작 키를 누르고 유지하다가 중간에 끝 레인 키로 옮겨 유지하세요. 화살표의 도착 키를 확인하세요."
        ];
        _instructions.Text = $"{_stage + 1}/5  {explanations[_stage]}\n입력 키: {string.Join("  ·  ", _keys)}";
        _feedback.Text = "연습 시작을 누르세요. 실패해도 끝까지 연습할 수 있고, 다음 단계로 건너뛸 수 있습니다.";
        _next.Text = _stage == 4 ? "연습 완료 · 곡 선택" : "다음 단계";
        Invalidate();
    }

    private void StartStage()
    {
        StopStage();
        _engine.Start(ClientSize.Height, BuildStage(_stage, _keys.Length), _keys.Length);
        _audio.PlayInGameBgm(_audioPath);
        if (!_audio.IsInGameBgmPlaying)
        {
            _engine.Stop();
            _feedback.Text = "연습 오디오를 재생할 수 없습니다. 재생 장치를 확인한 뒤 다시 시도하세요.";
            return;
        }
        _held.Clear();
        _lastTime = 0;
        _clock.Restart();
        _timer.Start();
        _feedback.Text = "두 초 뒤 시작합니다. 박자 소리에 맞춰 입력하세요.";
        Focus();
    }

    private void TickStage()
    {
        double now = _clock.Elapsed.TotalSeconds;
        _engine.Update((float)(now - _lastTime), _audio.GetInGameBgmPositionSeconds());
        _lastTime = now;
        if (_engine.IsChartComplete || now >= 9)
        {
            _feedback.Text = $"연습 결과: 정확도 {_engine.Score.Accuracy:F1}% · MISS {_engine.Score.MissCount}. 다시 연습하거나 다음 단계로 가세요.";
            StopStage();
        }
        Invalidate();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Escape) { StopStage(); Close(); e.SuppressKeyPress = true; return; }
        int lane = Array.IndexOf(_keys, e.KeyCode);
        if (!_engine.IsRunning || lane < 0) return;
        e.SuppressKeyPress = true;
        if (!_held.Add(e.KeyCode)) return;
        _engine.SetLaneHeld(lane, true);
        GameEngine.HitResult? hit = _engine.TryHit(lane);
        if (hit is { } result) _feedback.Text = $"{result.Label} · {result.TimingLabel} ({result.OffsetSeconds * 1000:F0} ms)";
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        int lane = Array.IndexOf(_keys, e.KeyCode);
        if (!_engine.IsRunning || lane < 0) return;
        _held.Remove(e.KeyCode);
        _engine.SetLaneHeld(lane, false);
        _engine.TryRelease(lane);
        e.SuppressKeyPress = true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        float width = (ClientSize.Width - 80f) / _keys.Length;
        float hitY = ClientSize.Height - 155f;
        using var pen = new Pen(SystemInformation.HighContrast ? ForeColor : InterfaceTheme.Accent, 2);
        using var brush = new SolidBrush(SystemInformation.HighContrast ? ForeColor : InterfaceTheme.Accent);
        e.Graphics.DrawLine(pen, 40, hitY, ClientSize.Width - 40, hitY);
        for (int lane = 0; lane < _keys.Length; lane++)
        {
            float x = 40 + lane * width;
            using var laneBrush = new SolidBrush(_held.Contains(_keys[lane]) ? InterfaceTheme.Raised : InterfaceTheme.Surface);
            e.Graphics.FillRectangle(laneBrush, x + 1, 115, width - 2, hitY - 115);
            using var divider = new Pen(InterfaceTheme.Border);
            e.Graphics.DrawLine(divider, x, 115, x, hitY);
            e.Graphics.DrawString(_keys[lane].ToString(), Font, brush, x + 8, hitY + 8);
        }
        foreach (Note note in _engine.Notes.Where(n => n.State is NoteState.Active or NoteState.Holding))
        {
            float y = hitY - (note.TargetTime - _engine.CurrentChartTime) * 125f;
            float tail = hitY - (note.EndTargetTime - _engine.CurrentChartTime) * 125f;
            if (y < 115 || tail > hitY + 30) continue;
            float x = 40 + note.Lane * width + 12;
            if (note.Type != NoteType.Tap)
                e.Graphics.DrawLine(pen, x + 10, Math.Clamp(y, 115, hitY), 40 + note.EndLane * width + 22, Math.Max(115, tail));
            e.Graphics.FillRectangle(brush, x, Math.Min(y, hitY), width - 24, 12);
        }
    }

    private void StopStage() { _timer.Stop(); _clock.Stop(); _engine.Stop(); _audio.StopInGameBgm(); _held.Clear(); }

    internal static void WriteBeatTrack(string path)
    {
        const int rate = 22050;
        const int samples = rate * 9;
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write("RIFF"u8); writer.Write(36 + samples * 2); writer.Write("WAVEfmt "u8);
        writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(rate);
        writer.Write(rate * 2); writer.Write((short)2); writer.Write((short)16);
        writer.Write("data"u8); writer.Write(samples * 2);
        for (int i = 0; i < samples; i++)
        {
            double phase = i / (double)rate % 0.5;
            double value = phase < 0.035 ? Math.Sin(2 * Math.PI * 880 * phase) * (1 - phase / 0.035) * 5000 : 0;
            writer.Write((short)value);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            StopStage(); _timer.Dispose(); _audio.Dispose();
            try { File.Delete(_audioPath); }
            catch (IOException ex) { AppLogger.Error("Tutorial temporary audio cleanup failed.", ex); }
        }
        base.Dispose(disposing);
    }
}
