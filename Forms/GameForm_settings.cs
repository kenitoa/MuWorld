using System.Drawing.Drawing2D;

namespace RhythmGame;

public sealed partial class GameForm
{
    private int _settingsTabIndex;
    private static readonly string[] SettingsTabLabels = ["AUDIO", "GAMEPLAY", "CONTROLS", "DISPLAY", "SYSTEM"];

    private void DrawSettings(Graphics g)
    {
        DrawSettingsBackground(g);

        Color accent = GetAccentColor();
        using var brandFont = new Font("Segoe UI", Math.Max(8f, MenuS(15f)), FontStyle.Regular);
        using var titleFont = new Font("Segoe UI", Math.Max(17f, MenuS(35f)), FontStyle.Regular);
        using var subtitleFont = new Font("Segoe UI", Math.Max(8f, MenuS(14f)), FontStyle.Regular);
        using var navFont = new Font("Segoe UI", Math.Max(8.5f, MenuS(13.5f)), FontStyle.Regular);
        using var labelFont = new Font("Segoe UI", Math.Max(8.5f, MenuS(14f)), FontStyle.Regular);
        using var valueFont = new Font("Segoe UI", Math.Max(8f, MenuS(12.5f)), FontStyle.Regular);
        using var actionFont = new Font("Segoe UI", Math.Max(8f, MenuS(13f)), FontStyle.Regular);
        using var titleBrush = new SolidBrush(Color.FromArgb(238, 244, 255));
        using var textBrush = new SolidBrush(Color.FromArgb(215, 220, 234));
        using var dimBrush = new SolidBrush(Color.FromArgb(150, 158, 178));

        DrawSettingsBrand(g, MenuX(32f), MenuY(30f), brandFont, dimBrush);
        DrawSpacedString(g, "SETTINGS", titleFont, titleBrush, MenuX(840f), MenuY(96f), MenuS(22f), centered: true);
        DrawSettingsSubtitle(g, subtitleFont, dimBrush, accent);

        Rectangle panel = GetSettingsPanelBounds();
        DrawSettingsShell(g, panel, accent);
        DrawSettingsTabs(g, panel, navFont, accent);
        DrawSettingsRows(g, panel, labelFont, valueFont, textBrush, dimBrush, accent);
        DrawSettingsActionButton(g, GetResetButtonBounds(), "RESET", false, actionFont, accent);
        DrawSettingsActionButton(g, GetSettingsCancelButtonBounds(), "CANCEL", false, actionFont, accent);
        DrawSettingsActionButton(g, GetSettingsApplyButtonBounds(), "APPLY", true, actionFont, accent);
        DrawSettingsBackHint(g, GetBackButtonBounds(), actionFont, dimBrush);
    }

    private Rectangle GetBackButtonBounds()
    {
        return MenuRect(1525f, 862f, 130f, 32f);
    }

    private Rectangle GetSoundPanelBounds()
    {
        return GetSettingsPanelBounds();
    }

    private Rectangle GetVisualPanelBounds()
    {
        return GetSettingsPanelBounds();
    }

    private Rectangle GetResetButtonBounds()
    {
        return MenuRect(360f, 734f, 190f, 40f);
    }

    private Rectangle GetSettingsCancelButtonBounds()
    {
        return MenuRect(908f, 734f, 190f, 40f);
    }

    private Rectangle GetSettingsApplyButtonBounds()
    {
        return MenuRect(1154f, 734f, 190f, 40f);
    }

    private Rectangle GetSettingsSystemResetButtonBounds()
    {
        return MenuRect(1096f, 414f, 214f, 35f);
    }

    private Rectangle GetSettingsPanelBounds()
    {
        return MenuRect(326f, 230f, 1034f, 568f);
    }

    private Rectangle GetCalibrationEntryButtonBounds()
    {
        return _settingsTabIndex == 2 ? MenuRect(1096f, 414f, 214f, 35f) : Rectangle.Empty;
    }

    private Rectangle GetKeyBindingEntryButtonBounds()
    {
        return _settingsTabIndex == 2 ? MenuRect(1096f, 361f, 214f, 35f) : Rectangle.Empty;
    }

    private Rectangle GetSettingsLaneModeBounds()
    {
        return MenuRect(1096f, 308f, 214f, 35f);
    }

    private Rectangle GetCenteredDesignRect(float designWidth, float designHeight, float designY)
    {
        float x = (DesignWidth - designWidth) / 2f;
        return Rectangle.Round(new RectangleF(ScaleX(x), ScaleY(designY), ScaleX(designWidth), ScaleY(designHeight)));
    }

    private int GetRowCenterY(Rectangle panelBounds, int rowIndex, int rowCount)
    {
        return (int)Math.Round(GetSettingsRowCenterY(rowIndex));
    }

    private Rectangle GetRowIconBounds(Rectangle panelBounds, int rowIndex, int rowCount)
    {
        int centerY = GetRowCenterY(panelBounds, rowIndex, rowCount);
        return Rectangle.Round(new RectangleF(panelBounds.Left + ScaleX(26f), centerY - ScaleY(12f), ScaleX(24f), ScaleY(24f)));
    }

    private float GetRowLabelX(Rectangle panelBounds)
    {
        return panelBounds.Left + ScaleX(74f);
    }

    private Rectangle GetSliderTrackBounds(SettingsSlider slider)
    {
        if (!IsVisibleSettingsSlider(slider))
            return Rectangle.Empty;

        float x = MenuX(904f);
        float width = MenuS(slider == SettingsSlider.AudioOffset ? 330f : 360f);
        float y = slider switch
        {
            SettingsSlider.Bgm => GetSettingsRowCenterY(0) - MenuS(4f),
            SettingsSlider.Preview => GetSettingsRowCenterY(1) - MenuS(4f),
            SettingsSlider.Sfx => GetSettingsRowCenterY(2) - MenuS(4f),
            SettingsSlider.NoteSpeed => GetSettingsRowCenterY(0) - MenuS(4f),
            SettingsSlider.AudioOffset => GetSettingsRowCenterY(1) - MenuS(4f),
            SettingsSlider.LaneBrightness => GetSettingsRowCenterY(8) - MenuS(4f),
            SettingsSlider.TextScale => GetSettingsRowCenterY(7) - MenuS(4f),
            SettingsSlider.SplashDuration => GetSettingsRowCenterY(0) - MenuS(4f),
            _ => 0f,
        };

        return Rectangle.Round(new RectangleF(x, y, width, MenuS(8f)));
    }

    private Rectangle GetSliderKnobBounds(SettingsSlider slider)
    {
        Rectangle track = GetSliderTrackBounds(slider);
        int value = slider switch
        {
            SettingsSlider.Bgm => _bgmVolume,
            SettingsSlider.Preview => _previewVolume,
            SettingsSlider.Sfx => _sfxVolume,
            SettingsSlider.NoteSpeed => (int)Math.Round(_speedMultiplier * 100f),
            SettingsSlider.AudioOffset => _audioOffsetMs,
            SettingsSlider.LaneBrightness => _laneBrightness,
            SettingsSlider.TextScale => _textScalePercent,
            SettingsSlider.SplashDuration => _splashDurationMs,
            _ => 0,
        };

        float ratio = GetSliderRatio(slider, value);
        int knobSize = (int)MenuS(18f);
        int knobX = track.Left + (int)(track.Width * ratio) - knobSize / 2;
        int knobY = track.Top + track.Height / 2 - knobSize / 2;
        return new Rectangle(knobX, knobY, knobSize, knobSize);
    }

    private Rectangle GetSliderValueBounds(SettingsSlider slider)
    {
        if (!IsVisibleSettingsSlider(slider))
            return Rectangle.Empty;

        float width = slider == SettingsSlider.AudioOffset || slider == SettingsSlider.NoteSpeed ? MenuS(78f) : MenuS(56f);
        float x = MenuX(1272f);
        float height = MenuS(30f);
        float y = slider switch
        {
            SettingsSlider.Bgm => GetSettingsRowCenterY(0) - height / 2f,
            SettingsSlider.Preview => GetSettingsRowCenterY(1) - height / 2f,
            SettingsSlider.Sfx => GetSettingsRowCenterY(2) - height / 2f,
            SettingsSlider.NoteSpeed => GetSettingsRowCenterY(0) - height / 2f,
            SettingsSlider.AudioOffset => GetSettingsRowCenterY(1) - height / 2f,
            SettingsSlider.LaneBrightness => GetSettingsRowCenterY(8) - height / 2f,
            SettingsSlider.TextScale => GetSettingsRowCenterY(7) - height / 2f,
            SettingsSlider.SplashDuration => GetSettingsRowCenterY(0) - height / 2f,
            _ => 0f,
        };

        return Rectangle.Round(new RectangleF(x, y, width, height));
    }

    private Rectangle GetSettingsToggleBounds(string toggleKey)
    {
        if (toggleKey == "fullscreen" && _settingsTabIndex == 3)
            return MenuRect(1266f, 306f, 48f, 26f);

        if (toggleKey == "vsync" && _settingsTabIndex == 3)
            return MenuRect(1266f, 518f, 48f, 26f);

        if (toggleKey == "darkmode" && _settingsTabIndex == 3)
            return MenuRect(1266f, 571f, 48f, 26f);

        if (toggleKey == "highcontrast" && _settingsTabIndex == 3)
            return MenuRect(1266f, 624f, 48f, 26f);

        if (toggleKey == "reducedmotion" && _settingsTabIndex == 1)
            return MenuRect(1266f, 465f, 48f, 26f);

        if (toggleKey == "hitmute" && _settingsTabIndex == 0)
        {
            return MenuRect(1266f, 571f, 48f, 26f);
        }

        return Rectangle.Empty;
    }

    private Rectangle GetSettingsSegmentBounds(string key)
    {
        return key switch
        {
            "lanemode" when _settingsTabIndex == 2 => GetSettingsLaneModeBounds(),
            "hitskin" when _settingsTabIndex == 0 => MenuRect(1096f, 467f, 214f, 35f),
            "hitpitch" when _settingsTabIndex == 0 => MenuRect(1096f, 520f, 214f, 35f),
            "display" when _settingsTabIndex == 3 => MenuRect(1036f, 361f, 274f, 35f),
            "framerate" when _settingsTabIndex == 3 => MenuRect(960f, 414f, 350f, 35f),
            "render" when _settingsTabIndex == 3 => MenuRect(1096f, 467f, 214f, 35f),
            "colorvision" when _settingsTabIndex == 4 => MenuRect(960f, 361f, 350f, 35f),
            "playmode" when _settingsTabIndex == 1 => MenuRect(1096f, 414f, 214f, 35f),
            _ => Rectangle.Empty,
        };
    }

    private Rectangle GetThemeOptionBounds(int index)
    {
        return Rectangle.Empty;
    }

    private Rectangle GetPanelBoundsForSlider(SettingsSlider slider)
    {
        return slider switch
        {
            SettingsSlider.Bgm => GetSoundPanelBounds(),
            SettingsSlider.Preview => GetSoundPanelBounds(),
            SettingsSlider.Sfx => GetSoundPanelBounds(),
            SettingsSlider.NoteSpeed => _settingsTabIndex == 1 ? GetSettingsPanelBounds() : Rectangle.Empty,
            SettingsSlider.AudioOffset => _settingsTabIndex == 1 ? GetSettingsPanelBounds() : Rectangle.Empty,
            SettingsSlider.LaneBrightness => _settingsTabIndex == 3 ? GetSettingsPanelBounds() : Rectangle.Empty,
            SettingsSlider.TextScale => _settingsTabIndex == 3 ? GetSettingsPanelBounds() : Rectangle.Empty,
            SettingsSlider.SplashDuration => _settingsTabIndex == 4 ? GetSettingsPanelBounds() : Rectangle.Empty,
            _ => Rectangle.Empty,
        };
    }

    private void DrawSettingsBackground(Graphics g)
    {
        DrawMainMenuBackground(g, MenuRect(0f, 0f, MainMenuDesignWidth, MainMenuDesignHeight));
    }

    private void DrawSettingsBrand(Graphics g, float x, float y, Font font, Brush brush)
    {
        using var pen = new Pen(Color.FromArgb(220, 232, 238, 252), Math.Max(1f, MenuS(1.5f)))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };

        float barY = y + MenuS(3f);
        float[] heights = [MenuS(20f), MenuS(31f), MenuS(44f), MenuS(31f), MenuS(20f)];
        for (int i = 0; i < heights.Length; i++)
        {
            float bx = x + MenuS(i * 7.2f);
            g.DrawLine(pen, bx, barY + (MenuS(44f) - heights[i]) / 2f, bx, barY + (MenuS(44f) + heights[i]) / 2f);
        }

        DrawSpacedString(g, "MUWORLD", font, brush, x + MenuS(52f), y + MenuS(11f), MenuS(8.5f), centered: false);
    }

    private void DrawSettingsSubtitle(Graphics g, Font font, Brush brush, Color accent)
    {
        float centerX = MenuX(840f);
        float y = MenuY(154f);
        using var pen = new Pen(Color.FromArgb(125, accent), Math.Max(1f, MenuS(1f)));
        g.DrawLine(pen, centerX - MenuS(246f), y + MenuS(8f), centerX - MenuS(214f), y + MenuS(8f));
        g.DrawLine(pen, centerX + MenuS(214f), y + MenuS(8f), centerX + MenuS(246f), y + MenuS(8f));
        DrawSpacedString(g, "CUSTOMIZE YOUR EXPERIENCE", font, brush, centerX, y, MenuS(8f), centered: true);
    }

    private void DrawSettingsShell(Graphics g, Rectangle panel, Color accent)
    {
        using var shadowPath = CreateRoundedRect(new Rectangle(panel.X, panel.Y + (int)MenuS(8f), panel.Width, panel.Height), MenuS(8f));
        using var shadowBrush = new SolidBrush(Color.FromArgb(42, 0, 0, 0));
        g.FillPath(shadowBrush, shadowPath);

        using var path = CreateRoundedRect(panel, MenuS(8f));
        using var fill = new LinearGradientBrush(panel, Color.FromArgb(38, 19, 25, 40), Color.FromArgb(18, 8, 11, 22), LinearGradientMode.Vertical);
        using var border = new Pen(Color.FromArgb(205, 100, 158, 255), Math.Max(1f, MenuS(1.2f)));
        g.FillPath(fill, path);
        g.DrawPath(border, path);

        using var divider = new Pen(Color.FromArgb(48, 210, 222, 255), Math.Max(1f, MenuS(1f)));
        g.DrawLine(divider, MenuX(604f), MenuY(264f), MenuX(604f), MenuY(684f));
        g.DrawLine(divider, MenuX(362f), MenuY(704f), MenuX(1328f), MenuY(704f));
    }

    private void DrawSettingsTabs(Graphics g, Rectangle panel, Font font, Color accent)
    {
        using var activeBrush = new SolidBrush(Color.FromArgb(238, 244, 255));
        using var inactiveBrush = new SolidBrush(Color.FromArgb(145, 153, 174));

        for (int i = 0; i < SettingsTabLabels.Length; i++)
        {
            Rectangle tab = GetSettingsTabBounds(i);
            DrawSpacedString(g, SettingsTabLabels[i], font, i == _settingsTabIndex ? activeBrush : inactiveBrush, tab.Left + MenuS(18f), tab.Top + MenuS(13f), MenuS(8f), centered: false);
        }

        using var glowPath = new GraphicsPath();
        Rectangle active = GetSettingsTabBounds(_settingsTabIndex);
        glowPath.AddEllipse(active.Left - MenuS(28f), active.Top + MenuS(29f), MenuS(188f), MenuS(14f));
        using var glow = new PathGradientBrush(glowPath)
        {
            CenterColor = Color.FromArgb(150, accent),
            SurroundColors = [Color.FromArgb(0, accent)]
        };
        g.FillPath(glow, glowPath);
        using var line = new Pen(Color.FromArgb(200, accent), Math.Max(1f, MenuS(1f)));
        g.DrawLine(line, active.Left - MenuS(6f), active.Top + MenuS(36f), active.Left + MenuS(150f), active.Top + MenuS(36f));
    }

    private void DrawSettingsRows(Graphics g, Rectangle panel, Font labelFont, Font valueFont, Brush textBrush, Brush dimBrush, Color accent)
    {
        using var separator = new Pen(Color.FromArgb(34, 210, 222, 255), Math.Max(1f, MenuS(1f)));
        int rows = GetSettingsRowCountForCurrentTab();
        for (int i = 0; i <= rows; i++)
        {
            float y = MenuY(330f + i * 53f);
            g.DrawLine(separator, MenuX(636f), y, MenuX(1324f), y);
        }

        switch (_settingsTabIndex)
        {
            case 0:
                DrawSettingsLabel(g, "Master Volume", 0, labelFont, textBrush);
                DrawSlider(g, SettingsSlider.Bgm, GetSliderTrackBounds(SettingsSlider.Bgm), _bgmVolume, _bgmVolume.ToString());
                DrawSettingsLabel(g, "Music Volume", 1, labelFont, textBrush);
                DrawSlider(g, SettingsSlider.Preview, GetSliderTrackBounds(SettingsSlider.Preview), _previewVolume, _previewVolume.ToString());
                DrawSettingsLabel(g, "Effect Volume", 2, labelFont, textBrush);
                DrawSlider(g, SettingsSlider.Sfx, GetSliderTrackBounds(SettingsSlider.Sfx), _sfxVolume, _sfxVolume.ToString());
                DrawSettingsLabel(g, "Hit Sound", 3, labelFont, textBrush);
                DrawDropdown(g, GetSettingsSegmentBounds("hitskin"), GetCurrentHitSoundLabel(), valueFont, textBrush, dimBrush);
                DrawSettingsLabel(g, "Hit Pitch", 4, labelFont, textBrush);
                DrawDropdown(g, GetSettingsSegmentBounds("hitpitch"), HitSoundPitchLabels[Math.Clamp(_hitSoundPitch + 1, 0, HitSoundPitchLabels.Length - 1)], valueFont, textBrush, dimBrush);
                DrawSettingsLabel(g, "Mute Hit Sound", 5, labelFont, textBrush);
                DrawToggle(g, GetSettingsToggleBounds("hitmute"), _hitSoundMuted);
                break;
            case 1:
                DrawSettingsLabel(g, "Note Speed", 0, labelFont, textBrush);
                DrawSlider(g, SettingsSlider.NoteSpeed, GetSliderTrackBounds(SettingsSlider.NoteSpeed), (int)Math.Round(_speedMultiplier * 100f), $"{_speedMultiplier:F2}x");
                DrawSettingsLabel(g, "Input Offset", 1, labelFont, textBrush);
                DrawSlider(g, SettingsSlider.AudioOffset, GetSliderTrackBounds(SettingsSlider.AudioOffset), _audioOffsetMs, $"{_audioOffsetMs:+0;-0;0}ms");
                DrawSettingsLabel(g, "Play Mode", 2, labelFont, textBrush);
                DrawDropdown(g, GetSettingsSegmentBounds("playmode"), PlayModeLabels[Math.Clamp(_playModeIndex, 0, PlayModeLabels.Length - 1)], valueFont, textBrush, dimBrush);
                DrawSettingsLabel(g, "Reduced Motion", 3, labelFont, textBrush);
                DrawToggle(g, GetSettingsToggleBounds("reducedmotion"), _reducedMotionEnabled);
                break;
            case 2:
                DrawSettingsLabel(g, "Key Layout", 0, labelFont, textBrush);
                DrawDropdown(g, GetSettingsSegmentBounds("lanemode"), string.Join("  ", LaneLabels), valueFont, textBrush, dimBrush);
                DrawSettingsLabel(g, "Key Bindings", 1, labelFont, textBrush);
                DrawSettingsActionButton(g, GetKeyBindingEntryButtonBounds(), "EDIT KEYS", false, valueFont, accent);
                DrawSettingsLabel(g, "Input Calibration", 2, labelFont, textBrush);
                DrawSettingsActionButton(g, GetCalibrationEntryButtonBounds(), "CALIBRATE", false, valueFont, accent);
                break;
            case 3:
                DrawSettingsLabel(g, "Fullscreen", 0, labelFont, textBrush);
                DrawToggle(g, GetSettingsToggleBounds("fullscreen"), _displayMode == DisplayMode.Fullscreen);
                DrawSettingsLabel(g, "Resolution", 1, labelFont, textBrush);
                DrawDropdown(g, GetSettingsSegmentBounds("display"), $"{ClientSize.Width} x {ClientSize.Height}", valueFont, textBrush, dimBrush);
                DrawSettingsLabel(g, "Frame Rate", 2, labelFont, textBrush);
                DrawDropdown(g, GetSettingsSegmentBounds("framerate"), FrameRateLabels[Math.Clamp(_frameRateMode, 0, FrameRateLabels.Length - 1)], valueFont, textBrush, dimBrush);
                DrawSettingsLabel(g, "Render Quality", 3, labelFont, textBrush);
                DrawDropdown(g, GetSettingsSegmentBounds("render"), RenderQualityLabels[Math.Clamp(_renderQualityMode, 0, RenderQualityLabels.Length - 1)], valueFont, textBrush, dimBrush);
                DrawSettingsLabel(g, "V-Sync", 4, labelFont, textBrush);
                DrawToggle(g, GetSettingsToggleBounds("vsync"), _vsyncEnabled);
                DrawSettingsLabel(g, "Dark Mode", 5, labelFont, textBrush);
                DrawToggle(g, GetSettingsToggleBounds("darkmode"), _darkModeEnabled);
                DrawSettingsLabel(g, "High Contrast", 6, labelFont, textBrush);
                DrawToggle(g, GetSettingsToggleBounds("highcontrast"), _highContrastEnabled);
                DrawSettingsLabel(g, "Text Size", 7, labelFont, textBrush);
                DrawSlider(g, SettingsSlider.TextScale, GetSliderTrackBounds(SettingsSlider.TextScale), _textScalePercent, $"{_textScalePercent}%");
                break;
            case 4:
                DrawSettingsLabel(g, "Splash Time", 0, labelFont, textBrush);
                DrawSlider(g, SettingsSlider.SplashDuration, GetSliderTrackBounds(SettingsSlider.SplashDuration), _splashDurationMs, $"{_splashDurationMs / 1000f:F1}s");
                DrawSettingsLabel(g, "Color Vision", 1, labelFont, textBrush);
                DrawDropdown(g, GetSettingsSegmentBounds("colorvision"), ColorVisionLabels[Math.Clamp(_colorVisionMode, 0, ColorVisionLabels.Length - 1)], valueFont, textBrush, dimBrush);
                DrawSettingsLabel(g, "Reset Settings", 2, labelFont, textBrush);
                DrawSettingsActionButton(g, GetSettingsSystemResetButtonBounds(), "RESET ALL", false, valueFont, accent);
                break;
        }
    }

    private void DrawSettingsLabel(Graphics g, string label, int row, Font font, Brush brush)
    {
        g.DrawString(label, font, brush, MenuX(668f), GetSettingsRowCenterY(row) - MenuS(11f));
    }

    private float GetSettingsRowCenterY(int row)
    {
        return MenuY(304f + row * 53f);
    }

    private Rectangle GetSettingsTabBounds(int index)
    {
        return MenuRect(388f, 300f + index * 68f, 185f, 44f);
    }

    private int GetSettingsRowCountForCurrentTab()
    {
        return _settingsTabIndex switch
        {
            0 => 6,
            1 => 4,
            2 => 3,
            3 => 8,
            4 => 3,
            _ => 6,
        };
    }

    private string GetCurrentHitSoundLabel()
    {
        string skin = string.IsNullOrWhiteSpace(_hitSoundSkin) ? "CLASSIC" : _hitSoundSkin;
        if (string.Equals(skin, "SYNTH", StringComparison.OrdinalIgnoreCase))
            skin = "CLASSIC";
        return skin.Length > 18 ? skin[..18].ToUpperInvariant() : skin.ToUpperInvariant();
    }

    private bool IsVisibleSettingsSlider(SettingsSlider slider)
    {
        return slider switch
        {
            SettingsSlider.Bgm or SettingsSlider.Preview or SettingsSlider.Sfx => _settingsTabIndex == 0,
            SettingsSlider.NoteSpeed or SettingsSlider.AudioOffset => _settingsTabIndex == 1,
            SettingsSlider.TextScale => _settingsTabIndex == 3,
            SettingsSlider.SplashDuration => _settingsTabIndex == 4,
            _ => false,
        };
    }

    private void DrawDropdown(Graphics g, Rectangle bounds, string value, Font font, Brush textBrush, Brush dimBrush)
    {
        using var path = CreateRoundedRect(bounds, MenuS(4f));
        using var fill = new SolidBrush(Color.FromArgb(18, 4, 7, 16));
        using var border = new Pen(Color.FromArgb(72, 188, 204, 235), Math.Max(1f, MenuS(1f)));
        g.FillPath(fill, path);
        g.DrawPath(border, path);
        DrawSpacedString(g, value, font, textBrush, bounds.Left + MenuS(14f), bounds.Top + MenuS(9f), MenuS(4f), centered: false);

        using var chevron = new Pen(Color.FromArgb(190, 215, 223, 240), Math.Max(1f, MenuS(1.4f)))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        float cx = bounds.Right - MenuS(18f);
        float cy = bounds.Top + bounds.Height / 2f - MenuS(2f);
        g.DrawLine(chevron, cx - MenuS(5f), cy, cx, cy + MenuS(5f));
        g.DrawLine(chevron, cx, cy + MenuS(5f), cx + MenuS(5f), cy);
    }

    private void DrawSettingsActionButton(Graphics g, Rectangle bounds, string text, bool primary, Font font, Color accent)
    {
        using var path = CreateRoundedRect(bounds, MenuS(4f));
        using var fill = new LinearGradientBrush(bounds,
            primary ? Color.FromArgb(34, 35, 55, 96) : Color.FromArgb(16, 5, 8, 17),
            Color.FromArgb(14, 4, 6, 13),
            LinearGradientMode.Vertical);
        using var border = new Pen(primary ? Color.FromArgb(205, accent) : Color.FromArgb(70, 190, 204, 235), Math.Max(1f, MenuS(1f)));
        g.FillPath(fill, path);
        g.DrawPath(border, path);

        if (primary)
        {
            using var glow = new Pen(Color.FromArgb(60, accent), Math.Max(4f, MenuS(4f)));
            g.DrawPath(glow, path);
        }

        using var brush = new SolidBrush(Color.FromArgb(228, 236, 252));
        DrawSpacedString(g, text, font, brush, bounds.Left + bounds.Width / 2f, bounds.Top + MenuS(11f), MenuS(8f), centered: true);
    }

    private void DrawSettingsBackHint(Graphics g, Rectangle bounds, Font font, Brush textBrush)
    {
        Rectangle keyBounds = new(bounds.Left, bounds.Top + (int)MenuS(2f), (int)MenuS(44f), (int)MenuS(27f));
        using var keyPath = CreateRoundedRect(keyBounds, MenuS(4f));
        using var keyFill = new SolidBrush(Color.FromArgb(22, 225, 232, 244));
        using var keyPen = new Pen(Color.FromArgb(120, 225, 232, 244), Math.Max(1f, MenuS(1f)));
        g.FillPath(keyFill, keyPath);
        g.DrawPath(keyPen, keyPath);
        using var keyBrush = new SolidBrush(Color.FromArgb(185, 225, 232, 244));
        DrawCentered(g, "ESC", font, keyBrush, keyBounds.Left + keyBounds.Width / 2, keyBounds.Top + (int)MenuS(5f));
        DrawSpacedString(g, "BACK", font, textBrush, bounds.Left + MenuS(58f), bounds.Top + MenuS(8f), MenuS(8f), centered: false);
    }

    private void DrawBackButton(Graphics g, Rectangle bounds)
    {
        var shadow = bounds;
        shadow.Offset(0, 4);
        using (var shadowBrush = new SolidBrush(Color.FromArgb(22, 72, 84, 106)))
            g.FillEllipse(shadowBrush, shadow);

        using (var fillBrush = new SolidBrush(BackBtnFill))
        using (var borderPen = new Pen(BackBtnBorder, 2f))
        {
            g.FillEllipse(fillBrush, bounds);
            g.DrawEllipse(borderPen, bounds);
        }

        using var pen = new Pen(BackBtnArrow, 5f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };
        int midY = bounds.Top + bounds.Height / 2;
        g.DrawLine(pen, bounds.Left + 34, midY - 12, bounds.Left + 18, midY);
        g.DrawLine(pen, bounds.Left + 18, midY, bounds.Left + 34, midY + 12);
        g.DrawLine(pen, bounds.Left + 20, midY, bounds.Right - 16, midY);
    }

    private void DrawCard(Graphics g, Rectangle bounds)
    {
        var shadow = bounds;
        shadow.Offset(0, 7);
        using (var shadowPath = CreateRoundedRect(shadow, ScaleY(14f)))
        using (var shadowBrush = new SolidBrush(CardShadow))
        {
            g.FillPath(shadowBrush, shadowPath);
        }

        using var cardPath = CreateRoundedRect(bounds, ScaleY(14f));
        using var cardBrush = new SolidBrush(CardFill);
        using var borderPen = new Pen(CardBorder, 1.5f);
        g.FillPath(cardBrush, cardPath);
        g.DrawPath(borderPen, cardPath);
    }

    private void DrawPanelSeparators(Graphics g, Rectangle bounds, int rowCount)
    {
        using var pen = new Pen(SeparatorColor, 1f);
        int rowHeight = bounds.Height / rowCount;
        for (int i = 1; i < rowCount; i++)
        {
            int y = bounds.Top + i * rowHeight;
            g.DrawLine(pen, bounds.Left, y, bounds.Right, y);
        }
    }

    private void DrawSlider(Graphics g, SettingsSlider slider, Rectangle trackBounds, int value, string valueText)
    {
        Color accent = GetAccentColor();
        using (var basePen = new Pen(Color.FromArgb(66, 205, 214, 234), Math.Max(1f, MenuS(1.2f)))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        })
        using (var fillPen = new Pen(Color.FromArgb(210, 120, 157, 255), Math.Max(1f, MenuS(1.8f)))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        })
        {
            int centerY = trackBounds.Top + trackBounds.Height / 2;
            g.DrawLine(basePen, trackBounds.Left, centerY, trackBounds.Right, centerY);
            int fillX = trackBounds.Left + (int)(trackBounds.Width * GetSliderRatio(slider, value));
            g.DrawLine(fillPen, trackBounds.Left, centerY, fillX, centerY);
        }

        Rectangle knobBounds = GetSliderKnobBounds(slider);
        using (var glowBrush = new SolidBrush(Color.FromArgb(56, accent)))
            g.FillEllipse(glowBrush, Rectangle.Inflate(knobBounds, (int)MenuS(7f), (int)MenuS(7f)));

        using (var knobBrush = new SolidBrush(Color.FromArgb(235, 235, 244, 255)))
            g.FillEllipse(knobBrush, knobBounds);

        Rectangle valueBounds = GetSliderValueBounds(slider);
        using var valueFont = new Font("Segoe UI", Math.Max(8.5f, MenuS(14f)), FontStyle.Regular);
        using var valueBrush = new SolidBrush(Color.FromArgb(212, 218, 235));
        DrawCentered(g, valueText, valueFont, valueBrush, valueBounds.Left + valueBounds.Width / 2, valueBounds.Top + (int)MenuS(6f));
    }

    private void DrawToggle(Graphics g, Rectangle bounds, bool isOn)
    {
        Color fill = isOn ? Color.FromArgb(130, 70, 104, 210) : Color.FromArgb(70, 48, 52, 66);
        using var path = CreateRoundedRect(bounds, bounds.Height / 2f);
        using var fillBrush = new SolidBrush(fill);
        using var borderPen = new Pen(isOn ? Color.FromArgb(190, 122, 162, 255) : Color.FromArgb(105, 120, 128, 150), Math.Max(1f, MenuS(1f)));
        g.FillPath(fillBrush, path);
        g.DrawPath(borderPen, path);

        int knobSize = bounds.Height - (int)MenuS(4f);
        int knobX = isOn ? bounds.Right - knobSize - (int)MenuS(2f) : bounds.Left + (int)MenuS(2f);
        var knob = new Rectangle(knobX, bounds.Top + (int)MenuS(2f), knobSize, knobSize);
        using var glowBrush = new SolidBrush(Color.FromArgb(isOn ? 70 : 18, 120, 162, 255));
        g.FillEllipse(glowBrush, Rectangle.Inflate(knob, (int)MenuS(6f), (int)MenuS(6f)));
        using var knobBrush = new SolidBrush(Color.White);
        g.FillEllipse(knobBrush, knob);
    }

    private void DrawSegmentedControl(Graphics g, Rectangle bounds, string[] labels, int selectedIndex)
    {
        Color accent = GetAccentColor();
        using var outerPath = CreateRoundedRect(bounds, ScaleY(10f));
        using var outerBrush = new SolidBrush(SegmentBg);
        using var borderPen = new Pen(SegmentBorder, 1.5f);
        using var textFont = new Font("Segoe UI", 10.5f, FontStyle.Bold);
        g.FillPath(outerBrush, outerPath);
        g.DrawPath(borderPen, outerPath);

        int itemWidth = bounds.Width / labels.Length;
        using var dividerPen = new Pen(SegmentDivider, 1f);
        for (int i = 0; i < labels.Length; i++)
        {
            var item = new Rectangle(bounds.Left + i * itemWidth, bounds.Top, itemWidth, bounds.Height);
            if (i == selectedIndex)
            {
                using var selectedPath = CreateRoundedRect(item, ScaleY(6f));
                using var selectedBrush = new LinearGradientBrush(item, Color.FromArgb(180, accent), Color.FromArgb(115, accent), LinearGradientMode.Vertical);
                g.FillPath(selectedBrush, selectedPath);
            }
            else if (i > 0)
            {
                g.DrawLine(dividerPen, item.Left, bounds.Top + 4, item.Left, bounds.Bottom - 4);
            }

            using var textBrush = new SolidBrush(i == selectedIndex ? Color.White : SegmentText);
            DrawCentered(g, labels[i], textFont, textBrush, item.Left + item.Width / 2, item.Top + 6);
        }
    }

    private string[] GetHitSoundSkinLabels()
    {
        string[] labels = AudioManager.DiscoverHitSoundSkins();
        _hitSoundSkinIndex = Array.FindIndex(labels, s => string.Equals(s, _hitSoundSkin, StringComparison.OrdinalIgnoreCase));
        if (_hitSoundSkinIndex < 0)
        {
            _hitSoundSkinIndex = 0;
            _hitSoundSkin = labels[0];
        }

        return labels;
    }

    private void DrawThemeOptions(Graphics g)
    {
        Color[] themeColors = ThemeColors;

        for (int i = 0; i < themeColors.Length; i++)
        {
            Rectangle bounds = GetThemeOptionBounds(i);
            if (i == _themeColorIndex)
            {
                using var ringPen = new Pen(ThemeRingColor, 2f);
                g.DrawEllipse(ringPen, Rectangle.Inflate(bounds, 6, 6));
            }

            using var fillBrush = new SolidBrush(themeColors[i]);
            g.FillEllipse(fillBrush, bounds);

            if (i == _themeColorIndex)
            {
                using var pen = new Pen(Color.White, 3.5f)
                {
                    StartCap = LineCap.Round,
                    EndCap = LineCap.Round,
                    LineJoin = LineJoin.Round
                };
                int midY = bounds.Top + bounds.Height / 2;
                g.DrawLine(pen, bounds.Left + 7, midY, bounds.Left + 13, midY + 6);
                g.DrawLine(pen, bounds.Left + 13, midY + 6, bounds.Right - 7, bounds.Top + 9);
            }
        }
    }

    private void DrawResetButton(Graphics g, Rectangle bounds, Font font)
    {
        Color accent = GetAccentColor();
        var shadow = bounds;
        shadow.Offset(0, 4);
        using (var shadowPath = CreateRoundedRect(shadow, bounds.Height / 2f))
        using (var shadowBrush = new SolidBrush(Color.FromArgb(30, 62, 101, 160)))
        {
            g.FillPath(shadowBrush, shadowPath);
        }

        using var path = CreateRoundedRect(bounds, bounds.Height / 2f);
        using var fillBrush = new LinearGradientBrush(bounds, Color.FromArgb(180, accent), Color.FromArgb(115, accent), LinearGradientMode.Vertical);
        using var borderPen = new Pen(Color.FromArgb(140, accent), 2f);
        using var textBrush = new SolidBrush(Color.White);
        g.FillPath(fillBrush, path);
        g.DrawPath(borderPen, path);
        DrawCentered(g, "RESET TO DEFAULT", font, textBrush, bounds.Left + bounds.Width / 2, bounds.Top + 8);
    }

    private void DrawCalibrationEntryButton(Graphics g, Rectangle bounds, Font font)
    {
        Color accent = GetAccentColor();
        using var path = CreateRoundedRect(bounds, ScaleY(8f));
        using var fillBrush = new LinearGradientBrush(bounds, Color.FromArgb(135, accent), Color.FromArgb(80, accent), LinearGradientMode.Vertical);
        using var borderPen = new Pen(Color.FromArgb(150, accent), Math.Max(1.2f, ScaleY(1.5f)));
        using var textBrush = new SolidBrush(Color.White);
        g.FillPath(fillBrush, path);
        g.DrawPath(borderPen, path);
        DrawCentered(g, "CALIBRATE", font, textBrush, bounds.Left + bounds.Width / 2, bounds.Top + (int)ScaleY(8f));
    }

    private void DrawKeyBindingEntryButton(Graphics g, Rectangle bounds, Font font)
    {
        Color accent = GetAccentColor();
        using var path = CreateRoundedRect(bounds, ScaleY(8f));
        using var fillBrush = new LinearGradientBrush(bounds, Color.FromArgb(115, accent), Color.FromArgb(68, accent), LinearGradientMode.Vertical);
        using var borderPen = new Pen(Color.FromArgb(145, accent), Math.Max(1.2f, ScaleY(1.5f)));
        using var textBrush = new SolidBrush(Color.White);
        g.FillPath(fillBrush, path);
        g.DrawPath(borderPen, path);
        DrawCentered(g, "KEYS", font, textBrush, bounds.Left + bounds.Width / 2, bounds.Top + (int)ScaleY(8f));
    }

    private void DrawLaneModeSettingsControl(Graphics g, Rectangle soundPanel, Font font)
    {
        Rectangle labelBounds = Rectangle.Round(new RectangleF(
            soundPanel.Left + ScaleX(350f),
            soundPanel.Bottom + ScaleY(22f),
            ScaleX(82f),
            ScaleY(20f)));
        using var labelBrush = new SolidBrush(Color.FromArgb(170, 188, 220));
        g.DrawString("LANE MODE", font, labelBrush, labelBounds.Left, labelBounds.Top);
        DrawSegmentedControl(g, GetSettingsLaneModeBounds(), ["4K", "5K", "7K"], _laneModeIndex);
    }

    private void DrawAudioOffsetGuide(Graphics g, Rectangle soundPanel, Font font)
    {
        Rectangle bounds = Rectangle.Round(new RectangleF(
            soundPanel.Left + ScaleX(46f),
            soundPanel.Bottom + ScaleY(21f),
            ScaleX(300f),
            ScaleY(30f)));
        using var textBrush = new SolidBrush(Color.FromArgb(170, 188, 220));
        string guide = _audioOffsetMs < 0 ? "EARLY BIAS" : _audioOffsetMs > 0 ? "LATE BIAS" : "SYNC BIAS";
        g.DrawString(guide, font, textBrush, bounds.Left, bounds.Top + ScaleY(5f));
    }

    private void HandleSettingsMouseDown(Point location)
    {
        if (GetBackButtonBounds().Contains(location))
        {
            _screen = UiScreen.MainMenu;
            Invalidate();
            return;
        }

        for (int i = 0; i < SettingsTabLabels.Length; i++)
        {
            if (GetSettingsTabBounds(i).Contains(location))
            {
                _settingsTabIndex = i;
                _draggedSlider = SettingsSlider.None;
                Invalidate();
                return;
            }
        }

        if (TryBeginSliderDrag(location, SettingsSlider.Bgm) ||
            TryBeginSliderDrag(location, SettingsSlider.Preview) ||
            TryBeginSliderDrag(location, SettingsSlider.Sfx) ||
            TryBeginSliderDrag(location, SettingsSlider.NoteSpeed) ||
            TryBeginSliderDrag(location, SettingsSlider.AudioOffset) ||
            TryBeginSliderDrag(location, SettingsSlider.LaneBrightness) ||
            TryBeginSliderDrag(location, SettingsSlider.TextScale) ||
            TryBeginSliderDrag(location, SettingsSlider.SplashDuration))
        {
            Invalidate();
            return;
        }

        if (GetSettingsCancelButtonBounds().Contains(location))
        {
            _screen = UiScreen.MainMenu;
            Invalidate();
            return;
        }

        if (GetSettingsApplyButtonBounds().Contains(location))
        {
            ApplySettingsToRuntime();
            SaveUserSettings();
            Invalidate();
            return;
        }

        if (GetCalibrationEntryButtonBounds().Contains(location))
        {
            _screen = UiScreen.InputCalibration;
            StartInputCalibration();
            Invalidate();
            return;
        }

        if (GetKeyBindingEntryButtonBounds().Contains(location))
        {
            _keyBindingModeIndex = _laneModeIndex;
            _screen = UiScreen.KeyBindings;
            _keyBindingStatus = "SELECT A LANE";
            Invalidate();
            return;
        }

        int laneModeHit = GetSegmentHitIndex(GetSettingsSegmentBounds("lanemode"), LaneModes.Length, location);
        if (laneModeHit >= 0)
        {
            _laneModeIndex = laneModeHit;
            _keyBindingModeIndex = _laneModeIndex;
            SaveUserSettings();
            Invalidate();
            return;
        }

        if (GetSettingsToggleBounds("fullscreen").Contains(location))
        {
            _displayMode = _displayMode == DisplayMode.Fullscreen ? DisplayMode.Windowed : DisplayMode.Fullscreen;
            ApplySettingsToRuntime();
            SaveUserSettings();
            Invalidate();
            return;
        }

        int displayHit = GetSegmentHitIndex(GetSettingsSegmentBounds("display"), 2, location);
        if (displayHit >= 0)
        {
            Invalidate();
            return;
        }

        int frameRateHit = GetSegmentHitIndex(GetSettingsSegmentBounds("framerate"), FrameRateLabels.Length, location);
        if (frameRateHit >= 0)
        {
            _frameRateMode = frameRateHit;
            ApplySettingsToRuntime();
            SaveUserSettings();
            Invalidate();
            return;
        }

        int renderHit = GetSegmentHitIndex(GetSettingsSegmentBounds("render"), RenderQualityLabels.Length, location);
        if (renderHit >= 0)
        {
            _renderQualityMode = renderHit;
            SaveUserSettings();
            Invalidate();
            return;
        }

        string[] hitSkinLabels = GetHitSoundSkinLabels();
        int hitSkinHit = GetSegmentHitIndex(GetSettingsSegmentBounds("hitskin"), hitSkinLabels.Length, location);
        if (hitSkinHit >= 0)
        {
            _hitSoundSkinIndex = hitSkinHit;
            _hitSoundSkin = hitSkinLabels[hitSkinHit];
            ApplySettingsToRuntime();
            SaveUserSettings();
            Invalidate();
            return;
        }

        int hitPitchHit = GetSegmentHitIndex(GetSettingsSegmentBounds("hitpitch"), HitSoundPitchLabels.Length, location);
        if (hitPitchHit >= 0)
        {
            _hitSoundPitch = hitPitchHit - 1;
            ApplySettingsToRuntime();
            SaveUserSettings();
            Invalidate();
            return;
        }

        int colorVisionHit = GetSegmentHitIndex(GetSettingsSegmentBounds("colorvision"), ColorVisionLabels.Length, location);
        if (colorVisionHit >= 0)
        {
            _colorVisionMode = colorVisionHit;
            SaveUserSettings();
            Invalidate();
            return;
        }

        int playModeHit = GetSegmentHitIndex(GetSettingsSegmentBounds("playmode"), PlayModeLabels.Length, location);
        if (playModeHit >= 0)
        {
            _playModeIndex = playModeHit;
            SaveUserSettings();
            Invalidate();
            return;
        }

        if (GetSettingsToggleBounds("vsync").Contains(location))
        {
            _vsyncEnabled = !_vsyncEnabled;
            SaveUserSettings();
            Invalidate();
            return;
        }

        if (GetSettingsToggleBounds("darkmode").Contains(location))
        {
            _darkModeEnabled = !_darkModeEnabled;
            SaveUserSettings();
            Invalidate();
            return;
        }

        if (GetSettingsToggleBounds("highcontrast").Contains(location))
        {
            _highContrastEnabled = !_highContrastEnabled;
            SaveUserSettings();
            Invalidate();
            return;
        }

        if (GetSettingsToggleBounds("reducedmotion").Contains(location))
        {
            _reducedMotionEnabled = !_reducedMotionEnabled;
            SaveUserSettings();
            Invalidate();
            return;
        }

        if (GetSettingsToggleBounds("hitmute").Contains(location))
        {
            _hitSoundMuted = !_hitSoundMuted;
            ApplySettingsToRuntime();
            SaveUserSettings();
            Invalidate();
            return;
        }

        for (int i = 0; i < 4; i++)
        {
            if (Rectangle.Inflate(GetThemeOptionBounds(i), 8, 8).Contains(location))
            {
                _themeColorIndex = i;
                ApplySettingsToRuntime();
                SaveUserSettings();
                Invalidate();
                return;
            }
        }

        if (GetResetButtonBounds().Contains(location))
        {
            ResetSettingsToDefault();
            ApplySettingsToRuntime();
            SaveUserSettings();
            Invalidate();
            return;
        }

        if (_settingsTabIndex == 4 && GetSettingsSystemResetButtonBounds().Contains(location))
        {
            ResetSettingsToDefault();
            ApplySettingsToRuntime();
            SaveUserSettings();
            Invalidate();
        }
    }

    private bool TryBeginSliderDrag(Point location, SettingsSlider slider)
    {
        if (!IsVisibleSettingsSlider(slider))
            return false;

        Rectangle hitBounds = Rectangle.Union(GetSliderTrackBounds(slider), Rectangle.Inflate(GetSliderKnobBounds(slider), 8, 8));
        if (!hitBounds.Contains(location))
            return false;

        _draggedSlider = slider;
        UpdateSliderFromPoint(slider, location.X);
        return true;
    }

    private void UpdateSliderFromPoint(SettingsSlider slider, int x)
    {
        Rectangle track = GetSliderTrackBounds(slider);
        float ratio = Math.Clamp((x - track.Left) / (float)track.Width, 0f, 1f);
        int value = (int)Math.Round(ratio * 100f);

        switch (slider)
        {
            case SettingsSlider.Bgm:
                _bgmVolume = value;
                _audio.SetBgmVolume(value);
                break;
            case SettingsSlider.Preview:
                _previewVolume = value;
                _audio.SetPreviewVolume(value);
                break;
            case SettingsSlider.Sfx:
                _sfxVolume = value;
                _audio.PlayHit(value, Judgment.Great);
                break;
            case SettingsSlider.NoteSpeed:
                _speedMultiplier = MathF.Round((0.5f + ratio * 2.0f) * 100f) / 100f;
                ApplySpeedToEngine();
                break;
            case SettingsSlider.AudioOffset:
                _audioOffsetMs = (int)Math.Round(-150 + ratio * 300);
                ApplySettingsToRuntime();
                SaveUserSettings();
                return;
            case SettingsSlider.LaneBrightness:
                _laneBrightness = value;
                break;
            case SettingsSlider.TextScale:
                _textScalePercent = (int)Math.Round(90 + ratio * 50);
                break;
            case SettingsSlider.SplashDuration:
                _splashDurationMs = (int)Math.Round(600 + ratio * 4400);
                break;
        }

        SaveUserSettings();
    }

    private int GetSegmentHitIndex(Rectangle bounds, int count, Point location)
    {
        if (!bounds.Contains(location))
            return -1;

        int width = bounds.Width / count;
        return Math.Min(count - 1, Math.Max(0, (location.X - bounds.Left) / width));
    }

    private bool IsSettingsInteractive(Point location)
    {
        if (GetBackButtonBounds().Contains(location) || GetResetButtonBounds().Contains(location) || GetSettingsCancelButtonBounds().Contains(location) || GetSettingsApplyButtonBounds().Contains(location) || GetCalibrationEntryButtonBounds().Contains(location) || GetKeyBindingEntryButtonBounds().Contains(location))
            return true;

        for (int i = 0; i < SettingsTabLabels.Length; i++)
            if (GetSettingsTabBounds(i).Contains(location))
                return true;

        if (_settingsTabIndex == 4 && GetSettingsSystemResetButtonBounds().Contains(location))
            return true;

        if (GetSettingsSegmentBounds("lanemode").Contains(location))
            return true;
        if (GetSettingsSegmentBounds("display").Contains(location))
            return true;
        if (GetSettingsSegmentBounds("framerate").Contains(location))
            return true;
        if (GetSettingsToggleBounds("vsync").Contains(location))
            return true;
        if (GetSettingsToggleBounds("fullscreen").Contains(location))
            return true;
        if (GetSettingsToggleBounds("darkmode").Contains(location))
            return true;
        if (GetSettingsToggleBounds("highcontrast").Contains(location))
            return true;
        if (GetSettingsToggleBounds("reducedmotion").Contains(location))
            return true;
        if (GetSettingsToggleBounds("hitmute").Contains(location))
            return true;
        if (GetSettingsSegmentBounds("render").Contains(location))
            return true;
        if (GetSettingsSegmentBounds("hitskin").Contains(location))
            return true;
        if (GetSettingsSegmentBounds("hitpitch").Contains(location))
            return true;
        if (GetSettingsSegmentBounds("colorvision").Contains(location))
            return true;
        if (GetSettingsSegmentBounds("playmode").Contains(location))
            return true;

        if (IsSettingsSliderHit(SettingsSlider.Bgm, location))
            return true;
        if (IsSettingsSliderHit(SettingsSlider.Preview, location))
            return true;
        if (IsSettingsSliderHit(SettingsSlider.Sfx, location))
            return true;
        if (IsSettingsSliderHit(SettingsSlider.NoteSpeed, location))
            return true;
        if (IsSettingsSliderHit(SettingsSlider.AudioOffset, location))
            return true;
        if (IsSettingsSliderHit(SettingsSlider.LaneBrightness, location))
            return true;
        if (IsSettingsSliderHit(SettingsSlider.TextScale, location))
            return true;
        if (IsSettingsSliderHit(SettingsSlider.SplashDuration, location))
            return true;

        for (int i = 0; i < 4; i++)
            if (Rectangle.Inflate(GetThemeOptionBounds(i), 8, 8).Contains(location))
                return true;

        return false;
    }

    private bool IsSettingsSliderHit(SettingsSlider slider, Point location)
    {
        if (!IsVisibleSettingsSlider(slider))
            return false;

        return Rectangle.Union(GetSliderTrackBounds(slider), Rectangle.Inflate(GetSliderKnobBounds(slider), 8, 8)).Contains(location);
    }

    private void ResetSettingsToDefault()
    {
        _bgmVolume = 80;
        _previewVolume = 45;
        _sfxVolume = 60;
        _hitSoundSkin = "SYNTH";
        _visualSkinName = VisualSkin.DefaultName;
        _hitSoundPitch = 0;
        _hitSoundMuted = false;
        _audioOffsetMs = 0;
        _themeColorIndex = 0;
        _laneBrightness = 70;
        _displayMode = DisplayMode.Windowed;
        _frameRateMode = 2;
        _vsyncEnabled = false;
        _darkModeEnabled = false;
        _laneModeIndex = 0;
        ResetKeyBindingsToDefault();
        _splashDurationMs = 1600;
        _highContrastEnabled = false;
        _colorVisionMode = 0;
        _reducedMotionEnabled = false;
        _textScalePercent = 100;
        _renderQualityMode = 1;
        _playModeIndex = (int)PlayMode.Normal;
    }

    private static float GetSliderRatio(SettingsSlider slider, int value)
    {
        return slider switch
        {
            SettingsSlider.AudioOffset => Math.Clamp((value + 150) / 300f, 0f, 1f),
            SettingsSlider.NoteSpeed => Math.Clamp((value - 50) / 200f, 0f, 1f),
            SettingsSlider.TextScale => Math.Clamp((value - 90) / 50f, 0f, 1f),
            SettingsSlider.SplashDuration => Math.Clamp((value - 600) / 4400f, 0f, 1f),
            _ => Math.Clamp(value / 100f, 0f, 1f),
        };
    }
}
