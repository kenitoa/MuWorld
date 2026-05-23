using System.Drawing.Drawing2D;

namespace RhythmGame;

public sealed partial class GameForm
{
    private static readonly HashSet<Keys> ReservedBindingKeys =
    [
        Keys.Escape,
        Keys.Enter,
        Keys.Back,
        Keys.Tab,
        Keys.Delete,
        Keys.ShiftKey,
        Keys.ControlKey,
        Keys.Menu,
        Keys.LWin,
        Keys.RWin,
        Keys.Pause,
        Keys.CapsLock,
    ];

    private int GetLaneForKey(Keys key)
    {
        Keys[] keys = LaneKeys;
        for (int i = 0; i < keys.Length; i++)
        {
            if (keys[i] == key)
                return i;
        }

        return -1;
    }

    private void BeginLaneInput(int lane, string input, string source)
    {
        if (lane < 0 || lane >= LaneCount || _lanePressed[lane])
            return;

        _lanePressed[lane] = true;
        _engine.SetLaneHeld(lane, true);

        GameEngine.HitResult? hit = _engine.TryHit(lane);
        string judgment = string.Empty;
        if (hit is not null)
        {
            judgment = hit.Value.Judgment.ToString();
            _feedback = FormatFeedback(hit.Value);
            _feedbackTiming = hit.Value.TimingLabel;
            _feedbackJudgment = hit.Value.Judgment;
            _feedbackTime = DateTime.Now;
            _audio.PlayHit(_sfxVolume, hit.Value.Judgment);
            ApplyGaugeForHitResult(hit.Value);
        }
        ConsumeEngineGaugeEvents();

        LogInputEvent(lane, input, keyDown: true, judgment, source);
        if (!IsPracticeMode && _gameFailedByGauge)
            EndGame();
    }

    private void EndLaneInput(int lane, string input, string source)
    {
        if (lane < 0 || lane >= LaneCount)
            return;

        _lanePressed[lane] = false;
        _engine.SetLaneHeld(lane, false);
        GameEngine.HitResult? release = _engine.TryRelease(lane);
        string judgment = string.Empty;
        if (release is not null)
        {
            judgment = release.Value.Judgment.ToString();
            _feedback = FormatFeedback(release.Value);
            _feedbackTiming = release.Value.TimingLabel;
            _feedbackJudgment = release.Value.Judgment;
            _feedbackTime = DateTime.Now;
            _audio.PlayHit(_sfxVolume, release.Value.Judgment);
            ApplyGaugeForHitResult(release.Value);
        }
        ConsumeEngineGaugeEvents();

        LogInputEvent(lane, input, keyDown: false, judgment, source);
        if (!IsPracticeMode && _gameFailedByGauge)
            EndGame();
    }

    private static string FormatFeedback(GameEngine.HitResult hit)
    {
        if (hit.ChordSize > 1 && !string.IsNullOrWhiteSpace(hit.Detail))
            return $"{hit.Label}  {hit.Detail}";

        return string.IsNullOrWhiteSpace(hit.Detail)
            ? hit.Label
            : $"{hit.Label}  {hit.Detail}";
    }

    private void LogInputEvent(int lane, string input, bool keyDown, string judgment, string source)
    {
        if (!_engine.IsRunning || _isReplayPlayback)
            return;

        _inputLogEvents.Add(new InputLogEvent(
            _engine.CurrentChartTime,
            lane,
            input,
            keyDown,
            judgment,
            source));
    }

    private void HandleGameplayMouseDown(Point location)
    {
        if (_isGamePaused || _mouseHeldLane >= 0)
            return;

        Rectangle playArea = GetPlayAreaBounds();
        if (!playArea.Contains(location))
            return;

        int laneWidth = Math.Max(1, playArea.Width / LaneCount);
        int lane = Math.Clamp((location.X - playArea.Left) / laneWidth, 0, LaneCount - 1);
        _mouseHeldLane = lane;
        BeginLaneInput(lane, "MouseLeft", "mouse");
    }

    private void HandleGameplayMouseUp()
    {
        if (_mouseHeldLane < 0)
            return;

        EndLaneInput(_mouseHeldLane, "MouseLeft", "mouse");
        _mouseHeldLane = -1;
    }

    private static string FormatKeyLabel(Keys key)
    {
        return key switch
        {
            Keys.Space => "Space",
            Keys.Oemcomma => ",",
            Keys.OemPeriod => ".",
            Keys.OemMinus => "-",
            Keys.Oemplus => "+",
            Keys.OemQuestion => "/",
            Keys.OemSemicolon => ";",
            Keys.OemQuotes => "'",
            Keys.OemOpenBrackets => "[",
            Keys.OemCloseBrackets => "]",
            _ => key.ToString(),
        };
    }

    private void LoadKeyBindingsFromSettings(UserSettings settings)
    {
        TryLoadKeyBindings(0, settings.KeyBindings4K);
        TryLoadKeyBindings(1, settings.KeyBindings5K);
        TryLoadKeyBindings(2, settings.KeyBindings7K);
    }

    private void TryLoadKeyBindings(int modeIndex, string[] serialized)
    {
        if (serialized.Length != LaneModes[modeIndex].Count)
            return;

        var parsed = new Keys[serialized.Length];
        for (int i = 0; i < serialized.Length; i++)
        {
            if (!Enum.TryParse(serialized[i], ignoreCase: true, out Keys key))
                return;

            parsed[i] = key;
        }

        if (!AreKeyBindingsValid(modeIndex, parsed))
            return;

        _laneKeyBindings[modeIndex] = parsed;
    }

    private string[] SerializeKeyBindings(int modeIndex)
    {
        return _laneKeyBindings[modeIndex].Select(key => key.ToString()).ToArray();
    }

    private void ResetKeyBindingsToDefault()
    {
        for (int i = 0; i < LaneModes.Length; i++)
            _laneKeyBindings[i] = LaneModes[i].Keys.ToArray();

        Array.Clear(_keyTestPressed);
        _keyBindingCaptureLane = -1;
        _keyBindingStatus = "DEFAULT KEYS RESTORED";
    }

    private bool AreKeyBindingsValid(int modeIndex, Keys[] keys)
    {
        if (keys.Length != LaneModes[modeIndex].Count)
            return false;

        for (int i = 0; i < keys.Length; i++)
        {
            if (!IsKeyAllowedForLane(modeIndex, i, keys[i], out _))
                return false;
        }

        return keys.Distinct().Count() == keys.Length;
    }

    private bool TryAssignKeyBinding(int modeIndex, int lane, Keys key)
    {
        if (!IsKeyAllowedForLane(modeIndex, lane, key, out string message))
        {
            _keyBindingStatus = message;
            return false;
        }

        Keys[] keys = _laneKeyBindings[modeIndex];
        for (int i = 0; i < keys.Length; i++)
        {
            if (i != lane && keys[i] == key)
            {
                _keyBindingStatus = $"{FormatKeyLabel(key)} IS ALREADY USED";
                return false;
            }
        }

        keys[lane] = key;
        _keyBindingCaptureLane = -1;
        _keyBindingStatus = $"LANE {lane + 1} = {FormatKeyLabel(key)}";
        SaveUserSettings();
        return true;
    }

    private static bool IsKeyAllowedForLane(int modeIndex, int lane, Keys key, out string message)
    {
        int laneCount = LaneModes[modeIndex].Count;
        bool isCenterLane = lane == laneCount / 2;
        if (key == Keys.Space)
        {
            if (laneCount >= 5 && isCenterLane)
            {
                message = string.Empty;
                return true;
            }

            message = "SPACE IS ONLY FOR 5K/7K CENTER";
            return false;
        }

        if (ReservedBindingKeys.Contains(key))
        {
            message = $"{FormatKeyLabel(key)} IS RESERVED";
            return false;
        }

        message = string.Empty;
        return true;
    }

    private void HandleKeyBindingsKeyDown(Keys key)
    {
        if (key == Keys.Escape || key == Keys.Back)
        {
            _keyBindingCaptureLane = -1;
            _screen = UiScreen.Settings;
            return;
        }

        if (_keyBindingCaptureLane >= 0)
        {
            TryAssignKeyBinding(_keyBindingModeIndex, _keyBindingCaptureLane, key);
            return;
        }

        int lane = Array.IndexOf(_laneKeyBindings[_keyBindingModeIndex], key);
        if (lane >= 0)
            _keyTestPressed[lane] = true;
    }

    private void HandleKeyBindingsKeyUp(Keys key)
    {
        int lane = Array.IndexOf(_laneKeyBindings[_keyBindingModeIndex], key);
        if (lane >= 0)
            _keyTestPressed[lane] = false;
    }

    private void DrawKeyBindings(Graphics g)
    {
        Rectangle layoutRect = new(0, 0, (int)ScaleX(DesignWidth), (int)ScaleY(DesignHeight));
        using (var bg = new LinearGradientBrush(layoutRect, Color.FromArgb(7, 11, 21), Color.FromArgb(17, 27, 46), LinearGradientMode.Vertical))
            g.FillRectangle(bg, layoutRect);

        DrawBackButton(g, GetKeyBindingBackButtonBounds());

        using var titleFont = new Font("Segoe UI", Math.Max(13f, ScaleY(32f)), FontStyle.Bold);
        using var labelFont = new Font("Segoe UI", Math.Max(8f, ScaleY(13f)), FontStyle.Bold);
        using var keyFont = new Font("Segoe UI", Math.Max(10f, ScaleY(18f)), FontStyle.Bold);
        using var smallFont = new Font("Segoe UI", Math.Max(7f, ScaleY(10.5f)), FontStyle.Bold);
        using var titleBrush = new SolidBrush(Color.FromArgb(238, 246, 255));
        using var labelBrush = new SolidBrush(Color.FromArgb(172, 190, 224));
        using var valueBrush = new SolidBrush(Color.White);
        using var accentBrush = new SolidBrush(GetAccentColor());

        DrawCentered(g, "KEY BINDINGS", titleFont, titleBrush, (int)ScaleX(DesignWidth / 2f), (int)ScaleY(48f));
        DrawModeTabs(g, GetKeyBindingModeTabBounds(), labelFont);

        Rectangle panel = GetCenteredDesignRect(820f, 450f, 150f);
        using (var panelPath = CreateRoundedRect(panel, ScaleY(16f)))
        using (var panelFill = new SolidBrush(Color.FromArgb(188, 11, 18, 33)))
        using (var panelBorder = new Pen(Color.FromArgb(76, 115, 168, 226), Math.Max(1.2f, ScaleY(1.6f))))
        {
            g.FillPath(panelFill, panelPath);
            g.DrawPath(panelBorder, panelPath);
        }

        Keys[] keys = _laneKeyBindings[_keyBindingModeIndex];
        for (int i = 0; i < keys.Length; i++)
            DrawKeyBindingLaneButton(g, i, keys[i], labelFont, keyFont);

        string status = _keyBindingCaptureLane >= 0
            ? $"PRESS A KEY FOR LANE {_keyBindingCaptureLane + 1}"
            : _keyBindingStatus;
        DrawCentered(g, status, labelFont, accentBrush, panel.Left + panel.Width / 2, panel.Top + (int)ScaleY(220f));

        using var testBrush = new SolidBrush(Color.FromArgb(150, 180, 205, 238));
        g.DrawString("GHOSTING TEST", labelFont, labelBrush, panel.Left + ScaleX(54f), panel.Top + ScaleY(270f));
        g.DrawString("Hold several lane keys together. Missing highlights mean your keyboard may be ghosting.", smallFont, testBrush, panel.Left + ScaleX(54f), panel.Top + ScaleY(296f));
        for (int i = 0; i < keys.Length; i++)
            DrawGhostingTestCell(g, i, keys[i], smallFont);

        DrawKeyBindingActionButton(g, GetKeyBindingResetButtonBounds(), "RESET", _hoverKeyBindingAction == 10, smallFont);
        DrawKeyBindingActionButton(g, GetKeyBindingDoneButtonBounds(), "DONE", _hoverKeyBindingAction == 11, smallFont);
    }

    private void DrawModeTabs(Graphics g, Rectangle bounds, Font font)
    {
        string[] labels = ["4K", "5K", "7K"];
        DrawSegmentedControl(g, bounds, labels, _keyBindingModeIndex);
    }

    private Rectangle GetKeyBindingBackButtonBounds()
    {
        return Rectangle.Round(new RectangleF(ScaleX(37f), ScaleY(27f), ScaleX(58f), ScaleY(58f)));
    }

    private Rectangle GetKeyBindingModeTabBounds()
    {
        return Rectangle.Round(new RectangleF(ScaleX(DesignWidth / 2f - 145f), ScaleY(104f), ScaleX(290f), ScaleY(38f)));
    }

    private Rectangle GetKeyBindingLaneBounds(int lane)
    {
        Rectangle panel = GetCenteredDesignRect(820f, 450f, 150f);
        int count = LaneModes[_keyBindingModeIndex].Count;
        float gap = ScaleX(10f);
        float width = (panel.Width - ScaleX(96f) - gap * (count - 1)) / count;
        return Rectangle.Round(new RectangleF(panel.Left + ScaleX(48f) + lane * (width + gap), panel.Top + ScaleY(56f), width, ScaleY(118f)));
    }

    private Rectangle GetGhostingCellBounds(int lane)
    {
        Rectangle panel = GetCenteredDesignRect(820f, 450f, 150f);
        int count = LaneModes[_keyBindingModeIndex].Count;
        float gap = ScaleX(8f);
        float width = (panel.Width - ScaleX(108f) - gap * (count - 1)) / count;
        return Rectangle.Round(new RectangleF(panel.Left + ScaleX(54f) + lane * (width + gap), panel.Top + ScaleY(340f), width, ScaleY(54f)));
    }

    private Rectangle GetKeyBindingResetButtonBounds()
    {
        return Rectangle.Round(new RectangleF(ScaleX(390f), ScaleY(632f), ScaleX(170f), ScaleY(52f)));
    }

    private Rectangle GetKeyBindingDoneButtonBounds()
    {
        return Rectangle.Round(new RectangleF(ScaleX(592f), ScaleY(632f), ScaleX(170f), ScaleY(52f)));
    }

    private void DrawKeyBindingLaneButton(Graphics g, int lane, Keys key, Font labelFont, Font keyFont)
    {
        Rectangle bounds = GetKeyBindingLaneBounds(lane);
        bool capture = _keyBindingCaptureLane == lane;
        bool hover = _hoverKeyBindingLane == lane;
        Color accent = GetAccentColor();

        using var path = CreateRoundedRect(bounds, ScaleY(12f));
        using var fill = new LinearGradientBrush(bounds,
            capture ? Color.FromArgb(98, accent) : hover ? Color.FromArgb(54, 45, 63, 92) : Color.FromArgb(42, 18, 27, 45),
            Color.FromArgb(22, 10, 16, 30),
            LinearGradientMode.Vertical);
        using var border = new Pen(capture || hover ? accent : Color.FromArgb(90, 88, 112, 152), Math.Max(1.5f, ScaleY(2f)));
        using var labelBrush = new SolidBrush(Color.FromArgb(176, 194, 226));
        using var keyBrush = new SolidBrush(Color.White);
        g.FillPath(fill, path);
        g.DrawPath(border, path);
        DrawCentered(g, $"LANE {lane + 1}", labelFont, labelBrush, bounds.Left + bounds.Width / 2, bounds.Top + (int)ScaleY(20f));
        DrawCentered(g, FormatKeyLabel(key), keyFont, keyBrush, bounds.Left + bounds.Width / 2, bounds.Top + (int)ScaleY(58f));
    }

    private void DrawGhostingTestCell(Graphics g, int lane, Keys key, Font font)
    {
        Rectangle bounds = GetGhostingCellBounds(lane);
        Color accent = GetAccentColor();
        bool pressed = _keyTestPressed[lane];
        using var path = CreateRoundedRect(bounds, ScaleY(8f));
        using var fill = new SolidBrush(pressed ? Color.FromArgb(170, accent) : Color.FromArgb(34, 20, 30, 48));
        using var border = new Pen(pressed ? Color.White : Color.FromArgb(74, 92, 128, 176), Math.Max(1.2f, ScaleY(1.5f)));
        using var textBrush = new SolidBrush(Color.White);
        g.FillPath(fill, path);
        g.DrawPath(border, path);
        DrawCentered(g, FormatKeyLabel(key), font, textBrush, bounds.Left + bounds.Width / 2, bounds.Top + (int)ScaleY(16f));
    }

    private void DrawKeyBindingActionButton(Graphics g, Rectangle bounds, string label, bool hover, Font font)
    {
        Color accent = GetAccentColor();
        using var path = CreateRoundedRect(bounds, ScaleY(10f));
        using var fill = new LinearGradientBrush(bounds,
            hover ? Color.FromArgb(150, accent) : Color.FromArgb(105, accent),
            Color.FromArgb(62, accent),
            LinearGradientMode.Vertical);
        using var border = new Pen(hover ? Color.White : Color.FromArgb(140, accent), Math.Max(1.4f, ScaleY(1.8f)));
        using var textBrush = new SolidBrush(Color.White);
        g.FillPath(fill, path);
        g.DrawPath(border, path);
        DrawCentered(g, label, font, textBrush, bounds.Left + bounds.Width / 2, bounds.Top + (int)ScaleY(16f));
    }

    private bool UpdateKeyBindingsHover(Point location)
    {
        int oldLane = _hoverKeyBindingLane;
        int oldAction = _hoverKeyBindingAction;
        _hoverKeyBindingLane = -1;
        _hoverKeyBindingAction = -1;

        for (int i = 0; i < LaneModes[_keyBindingModeIndex].Count; i++)
        {
            if (GetKeyBindingLaneBounds(i).Contains(location))
            {
                _hoverKeyBindingLane = i;
                break;
            }
        }

        if (GetKeyBindingResetButtonBounds().Contains(location))
            _hoverKeyBindingAction = 10;
        else if (GetKeyBindingDoneButtonBounds().Contains(location))
            _hoverKeyBindingAction = 11;
        else if (GetKeyBindingBackButtonBounds().Contains(location))
            _hoverKeyBindingAction = 12;
        else if (GetKeyBindingModeTabBounds().Contains(location))
            _hoverKeyBindingAction = 13;

        return oldLane != _hoverKeyBindingLane || oldAction != _hoverKeyBindingAction;
    }

    private bool IsKeyBindingsInteractive(Point location)
    {
        if (GetKeyBindingBackButtonBounds().Contains(location) ||
            GetKeyBindingModeTabBounds().Contains(location) ||
            GetKeyBindingResetButtonBounds().Contains(location) ||
            GetKeyBindingDoneButtonBounds().Contains(location))
            return true;

        for (int i = 0; i < LaneModes[_keyBindingModeIndex].Count; i++)
        {
            if (GetKeyBindingLaneBounds(i).Contains(location))
                return true;
        }

        return false;
    }

    private void HandleKeyBindingsMouseDown(Point location)
    {
        if (GetKeyBindingBackButtonBounds().Contains(location) || GetKeyBindingDoneButtonBounds().Contains(location))
        {
            _keyBindingCaptureLane = -1;
            _screen = UiScreen.Settings;
            return;
        }

        int tab = GetSegmentHitIndex(GetKeyBindingModeTabBounds(), LaneModes.Length, location);
        if (tab >= 0)
        {
            _keyBindingModeIndex = tab;
            _keyBindingCaptureLane = -1;
            Array.Clear(_keyTestPressed);
            _keyBindingStatus = "SELECT A LANE";
            return;
        }

        if (GetKeyBindingResetButtonBounds().Contains(location))
        {
            _laneKeyBindings[_keyBindingModeIndex] = LaneModes[_keyBindingModeIndex].Keys.ToArray();
            _keyBindingCaptureLane = -1;
            Array.Clear(_keyTestPressed);
            _keyBindingStatus = $"{LaneModes[_keyBindingModeIndex].Count}K DEFAULT RESTORED";
            SaveUserSettings();
            return;
        }

        for (int i = 0; i < LaneModes[_keyBindingModeIndex].Count; i++)
        {
            if (GetKeyBindingLaneBounds(i).Contains(location))
            {
                _keyBindingCaptureLane = i;
                _keyBindingStatus = $"PRESS A KEY FOR LANE {i + 1}";
                return;
            }
        }
    }
}
