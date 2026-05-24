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
        if (!IsNoFailPlayMode && _gameFailedByGauge)
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
        if (!IsNoFailPlayMode && _gameFailedByGauge)
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
        DrawSettingsBackground(g);

        using var titleFont = new Font("Segoe UI", Math.Max(24f, MenuS(50f)), FontStyle.Regular);
        using var tabFont = new Font("Segoe UI", Math.Max(8f, MenuS(15f)), FontStyle.Regular);
        using var labelFont = new Font("Segoe UI", Math.Max(8f, MenuS(16f)), FontStyle.Regular);
        using var keyFont = new Font("Segoe UI", Math.Max(18f, MenuS(35f)), FontStyle.Bold);
        using var ghostFont = new Font("Segoe UI", Math.Max(14f, MenuS(25f)), FontStyle.Bold);
        using var smallFont = new Font("Segoe UI", Math.Max(8f, MenuS(16f)), FontStyle.Regular);
        using var actionFont = new Font("Segoe UI", Math.Max(10f, MenuS(18f)), FontStyle.Regular);
        using var titleBrush = new SolidBrush(Color.FromArgb(238, 246, 255));
        using var labelBrush = new SolidBrush(Color.FromArgb(210, 220, 238));
        using var valueBrush = new SolidBrush(Color.White);
        using var hintBrush = new SolidBrush(Color.FromArgb(176, 186, 208));
        using var accentBrush = new SolidBrush(GetAccentColor());

        DrawKeyBindingBackButton(g, GetKeyBindingBackButtonBounds());
        DrawSpacedString(g, "KEY BINDINGS", titleFont, titleBrush, MenuX(840f), MenuY(60f), MenuS(20f), centered: true);
        DrawModeTabs(g, GetKeyBindingModeTabBounds(), tabFont);

        Rectangle panel = GetKeyBindingPanelBounds();
        using (var panelPath = CreateRoundedRect(panel, MenuS(16f)))
        using (var panelFill = new LinearGradientBrush(panel, Color.FromArgb(40, 14, 20, 34), Color.FromArgb(22, 7, 11, 22), LinearGradientMode.Vertical))
        using (var panelBorder = new Pen(Color.FromArgb(190, 100, 158, 255), Math.Max(1.2f, MenuS(1.2f))))
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
        DrawKeyBindingStatus(g, status, labelFont, accentBrush);

        DrawSpacedString(g, "GHOSTING TEST", labelFont, labelBrush, panel.Left + MenuS(80f), panel.Top + MenuS(346f), MenuS(8f), centered: false);
        g.DrawString("Hold several lane keys together. Missing highlights mean your keyboard may be ghosting.", smallFont, hintBrush, panel.Left + MenuS(80f), panel.Top + MenuS(389f));
        for (int i = 0; i < keys.Length; i++)
            DrawGhostingTestCell(g, i, keys[i], ghostFont);

        DrawKeyBindingActionButton(g, GetKeyBindingResetButtonBounds(), "RESET", _hoverKeyBindingAction == 10, actionFont);
        DrawKeyBindingActionButton(g, GetKeyBindingDoneButtonBounds(), "DONE", _hoverKeyBindingAction == 11, actionFont);
    }

    private void DrawModeTabs(Graphics g, Rectangle bounds, Font font)
    {
        string[] labels = ["4K", "5K", "7K"];
        Color accent = GetAccentColor();
        using var path = CreateRoundedRect(bounds, MenuS(9f));
        using var fill = new SolidBrush(Color.FromArgb(30, 8, 12, 22));
        using var border = new Pen(Color.FromArgb(96, 190, 204, 235), Math.Max(1f, MenuS(1f)));
        using var divider = new Pen(Color.FromArgb(52, 210, 222, 255), Math.Max(1f, MenuS(1f)));
        g.FillPath(fill, path);
        g.DrawPath(border, path);

        int itemWidth = bounds.Width / labels.Length;
        for (int i = 0; i < labels.Length; i++)
        {
            Rectangle item = new(bounds.Left + i * itemWidth, bounds.Top, i == labels.Length - 1 ? bounds.Right - (bounds.Left + i * itemWidth) : itemWidth, bounds.Height);
            if (i == _keyBindingModeIndex)
            {
                using var selectedPath = CreateRoundedRect(item, MenuS(7f));
                using var selectedFill = new LinearGradientBrush(item, Color.FromArgb(58, 48, 66, 112), Color.FromArgb(24, 10, 14, 30), LinearGradientMode.Vertical);
                using var selectedBorder = new Pen(Color.FromArgb(220, accent), Math.Max(1f, MenuS(1.2f)));
                g.FillPath(selectedFill, selectedPath);
                g.DrawPath(selectedBorder, selectedPath);
                using var glow = new Pen(Color.FromArgb(70, accent), Math.Max(4f, MenuS(4f)));
                g.DrawLine(glow, item.Left + MenuS(14f), item.Top, item.Right - MenuS(14f), item.Top);
            }
            else if (i > 0)
            {
                g.DrawLine(divider, item.Left, item.Top + MenuS(5f), item.Left, item.Bottom - MenuS(5f));
            }

            using var textBrush = new SolidBrush(i == _keyBindingModeIndex ? Color.White : Color.FromArgb(188, 198, 218));
            DrawSpacedString(g, labels[i], font, textBrush, item.Left + item.Width / 2f, item.Top + MenuS(14f), MenuS(4f), centered: true);
        }
    }

    private void DrawKeyBindingBackButton(Graphics g, Rectangle bounds)
    {
        Color accent = GetAccentColor();
        Rectangle shadowBounds = bounds;
        shadowBounds.Offset(0, Math.Max(1, (int)MenuS(4f)));

        using var shadowBrush = new SolidBrush(Color.FromArgb(64, 0, 0, 0));
        using var fill = new LinearGradientBrush(bounds, Color.FromArgb(44, 26, 38, 70), Color.FromArgb(16, 6, 10, 24), LinearGradientMode.Vertical);
        using var border = new Pen(Color.FromArgb(210, 136, 190, 255), Math.Max(1f, MenuS(1.2f)));
        using var glow = new Pen(Color.FromArgb(58, accent), Math.Max(3f, MenuS(3.5f)));
        g.FillEllipse(shadowBrush, shadowBounds);
        g.FillEllipse(fill, bounds);
        g.DrawEllipse(glow, bounds);
        g.DrawEllipse(border, bounds);

        float middleY = bounds.Top + bounds.Height / 2f;
        float left = bounds.Left + bounds.Width * 0.33f;
        float right = bounds.Left + bounds.Width * 0.68f;
        float head = bounds.Width * 0.18f;
        using var arrow = new Pen(Color.FromArgb(238, 245, 255), Math.Max(2f, MenuS(2.4f)))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round,
        };
        g.DrawLine(arrow, right, middleY, left, middleY);
        g.DrawLine(arrow, left, middleY, left + head, middleY - head);
        g.DrawLine(arrow, left, middleY, left + head, middleY + head);
    }

    private void DrawKeyBindingStatus(Graphics g, string status, Font font, Brush brush)
    {
        Rectangle panel = GetKeyBindingPanelBounds();
        float centerX = panel.Left + panel.Width / 2f;
        float y = panel.Top + MenuS(280f);
        Color accent = GetAccentColor();

        using var line = new Pen(Color.FromArgb(128, accent), Math.Max(1f, MenuS(1f)));
        g.DrawLine(line, centerX - MenuS(250f), y + MenuS(10f), centerX - MenuS(160f), y + MenuS(10f));
        g.DrawLine(line, centerX + MenuS(160f), y + MenuS(10f), centerX + MenuS(250f), y + MenuS(10f));
        DrawSpacedString(g, status, font, brush, centerX, y, MenuS(10f), centered: true);
    }

    private Rectangle GetKeyBindingBackButtonBounds()
    {
        return MenuRect(26f, 28f, 74f, 74f);
    }

    private Rectangle GetKeyBindingModeTabBounds()
    {
        return MenuRect(470f, 150f, 610f, 50f);
    }

    private Rectangle GetKeyBindingPanelBounds()
    {
        return MenuRect(136f, 222f, 1280f, 576f);
    }

    private Rectangle GetKeyBindingLaneBounds(int lane)
    {
        Rectangle panel = GetKeyBindingPanelBounds();
        int count = LaneModes[_keyBindingModeIndex].Count;
        float gap = MenuS(24f);
        float width = Math.Min(MenuS(150f), (panel.Width - MenuS(100f) - gap * (count - 1)) / count);
        float totalWidth = width * count + gap * (count - 1);
        float x = panel.Left + (panel.Width - totalWidth) / 2f + lane * (width + gap);
        return Rectangle.Round(new RectangleF(x, panel.Top + MenuS(58f), width, MenuS(162f)));
    }

    private Rectangle GetGhostingCellBounds(int lane)
    {
        Rectangle panel = GetKeyBindingPanelBounds();
        int count = LaneModes[_keyBindingModeIndex].Count;
        float gap = MenuS(18f);
        float width = Math.Min(MenuS(150f), (panel.Width - MenuS(120f) - gap * (count - 1)) / count);
        float totalWidth = width * count + gap * (count - 1);
        float x = panel.Left + (panel.Width - totalWidth) / 2f + lane * (width + gap);
        return Rectangle.Round(new RectangleF(x, panel.Top + MenuS(438f), width, MenuS(86f)));
    }

    private Rectangle GetKeyBindingResetButtonBounds()
    {
        return MenuRect(458f, 812f, 292f, 72f);
    }

    private Rectangle GetKeyBindingDoneButtonBounds()
    {
        return MenuRect(798f, 812f, 292f, 72f);
    }

    private void DrawKeyBindingLaneButton(Graphics g, int lane, Keys key, Font labelFont, Font keyFont)
    {
        Rectangle bounds = GetKeyBindingLaneBounds(lane);
        bool capture = _keyBindingCaptureLane == lane;
        bool hover = _hoverKeyBindingLane == lane;
        Color accent = GetAccentColor();

        using var path = CreateRoundedRect(bounds, MenuS(12f));
        using var fill = new LinearGradientBrush(bounds,
            capture ? Color.FromArgb(82, accent) : hover ? Color.FromArgb(50, 42, 62, 98) : Color.FromArgb(30, 12, 18, 32),
            Color.FromArgb(18, 5, 8, 18),
            LinearGradientMode.Vertical);
        using var border = new Pen(capture || hover ? Color.FromArgb(230, accent) : Color.FromArgb(170, 128, 188, 255), Math.Max(1.1f, MenuS(1.2f)));
        using var labelBrush = new SolidBrush(Color.FromArgb(210, 220, 238));
        using var keyBrush = new SolidBrush(Color.White);
        using var glow = new Pen(Color.FromArgb(capture ? 80 : 34, accent), Math.Max(3f, MenuS(3f)));
        g.FillPath(fill, path);
        g.DrawPath(glow, path);
        g.DrawPath(border, path);
        DrawSpacedString(g, $"LANE {lane + 1}", labelFont, labelBrush, bounds.Left + bounds.Width / 2f, bounds.Top + MenuS(33f), MenuS(6f), centered: true);
        DrawKeyBindingCenteredText(g, FormatKeyLabel(key), keyFont, keyBrush, bounds, bounds.Top + MenuS(84f));
    }

    private void DrawGhostingTestCell(Graphics g, int lane, Keys key, Font font)
    {
        Rectangle bounds = GetGhostingCellBounds(lane);
        Color accent = GetAccentColor();
        bool pressed = _keyTestPressed[lane];
        using var path = CreateRoundedRect(bounds, MenuS(8f));
        using var fill = new LinearGradientBrush(bounds,
            pressed ? Color.FromArgb(112, accent) : Color.FromArgb(26, 10, 15, 28),
            pressed ? Color.FromArgb(68, accent) : Color.FromArgb(18, 6, 9, 18),
            LinearGradientMode.Vertical);
        using var border = new Pen(pressed ? Color.White : Color.FromArgb(172, 120, 185, 255), Math.Max(1.1f, MenuS(1.2f)));
        using var textBrush = new SolidBrush(Color.White);
        g.FillPath(fill, path);
        g.DrawPath(border, path);
        DrawKeyBindingCenteredText(g, FormatKeyLabel(key), font, textBrush, bounds, bounds.Top + bounds.Height / 2f);
    }

    private void DrawKeyBindingActionButton(Graphics g, Rectangle bounds, string label, bool hover, Font font)
    {
        Color accent = GetAccentColor();
        using var path = CreateRoundedRect(bounds, MenuS(8f));
        using var fill = new LinearGradientBrush(bounds,
            hover ? Color.FromArgb(50, 42, 58, 98) : Color.FromArgb(24, 10, 14, 28),
            Color.FromArgb(16, 5, 8, 18),
            LinearGradientMode.Vertical);
        using var border = new Pen(hover ? Color.White : Color.FromArgb(205, accent), Math.Max(1f, MenuS(1.2f)));
        using var textBrush = new SolidBrush(Color.White);
        g.FillPath(fill, path);
        if (hover)
        {
            using var glow = new Pen(Color.FromArgb(70, accent), Math.Max(4f, MenuS(4f)));
            g.DrawPath(glow, path);
        }
        g.DrawPath(border, path);
        DrawSpacedString(g, label, font, textBrush, bounds.Left + bounds.Width / 2f, bounds.Top + MenuS(25f), MenuS(12f), centered: true);
    }

    private void DrawKeyBindingCenteredText(Graphics g, string text, Font font, Brush brush, Rectangle bounds, float centerY)
    {
        float maxWidth = Math.Max(1f, bounds.Width - MenuS(22f));
        Font drawFont = font;
        bool ownsFont = false;
        try
        {
            while (g.MeasureString(text, drawFont).Width > maxWidth && drawFont.Size > 8f)
            {
                if (ownsFont)
                    drawFont.Dispose();

                drawFont = new Font(font.FontFamily, drawFont.Size - 1f, font.Style, font.Unit);
                ownsFont = true;
            }

            SizeF size = g.MeasureString(text, drawFont);
            g.DrawString(text, drawFont, brush, bounds.Left + (bounds.Width - size.Width) / 2f, centerY - size.Height / 2f);
        }
        finally
        {
            if (ownsFont)
                drawFont.Dispose();
        }
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
