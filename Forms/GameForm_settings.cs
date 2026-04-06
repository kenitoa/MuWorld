using System.Drawing.Drawing2D;

namespace RhythmGame;

public sealed partial class GameForm
{
    private void DrawSettings(Graphics g)
    {
        DrawSettingsBackground(g);

        Rectangle backBounds = GetBackButtonBounds();
        DrawBackButton(g, backBounds);

        using var titleFont = new Font("Segoe UI", Math.Max(11f, ScaleY(31f)), FontStyle.Bold);
        using var optionFont = new Font("Segoe UI", Math.Max(8.5f, ScaleY(12f)), FontStyle.Bold);
        using var labelFont = new Font("Segoe UI", Math.Max(8.5f, ScaleY(14f)), FontStyle.Bold);

        Color accent = GetAccentColor();
        int centerX = (int)ScaleX(DesignWidth / 2f);
        string settingsTitle = "SETTINGS";
        SizeF titleSize = g.MeasureString(settingsTitle, titleFont);
        float gearSize = ScaleY(34f);
        float gearGap = ScaleX(12f);
        float titleLeft = centerX - (titleSize.Width + gearGap + gearSize) / 2f;
        float titleY = ScaleY(44f);

        DrawGearGlyph(g, new RectangleF(titleLeft, titleY + ScaleY(4f), gearSize, gearSize), accent, BgColor1);
        using (var titleBrush = new SolidBrush(accent))
            g.DrawString(settingsTitle, titleFont, titleBrush, titleLeft + gearSize + gearGap, titleY);

        Rectangle soundPanel = GetSoundPanelBounds();
        Rectangle visualPanel = GetVisualPanelBounds();

        DrawCard(g, soundPanel);
        DrawCard(g, visualPanel);

        DrawPanelSeparators(g, soundPanel, 2);
        DrawPanelSeparators(g, visualPanel, 6);

        using var subBrush = new SolidBrush(LabelColor);

        DrawLeftCentered(g, "BGM VOLUME", labelFont, subBrush, GetRowLabelX(soundPanel), GetRowCenterY(soundPanel, 0, 2));
        DrawSoundIcon(g, GetRowIconBounds(soundPanel, 0, 2), IconColor);
        DrawSlider(g, SettingsSlider.Bgm, GetSliderTrackBounds(SettingsSlider.Bgm), _bgmVolume, $"{_bgmVolume}%");

        DrawLeftCentered(g, "SFX VOLUME", labelFont, subBrush, GetRowLabelX(soundPanel), GetRowCenterY(soundPanel, 1, 2));
        DrawSoundIcon(g, GetRowIconBounds(soundPanel, 1, 2), IconColor);
        DrawSlider(g, SettingsSlider.Sfx, GetSliderTrackBounds(SettingsSlider.Sfx), _sfxVolume, $"{_sfxVolume}%");

        DrawLeftCentered(g, "DISPLAY MODE", labelFont, subBrush, GetRowLabelX(visualPanel), GetRowCenterY(visualPanel, 0, 6));
        DrawMonitorIcon(g, GetRowIconBounds(visualPanel, 0, 6), IconColor);
        DrawSegmentedControl(g, GetSettingsSegmentBounds("display"), ["WINDOW", "FULL"], _displayMode == DisplayMode.Windowed ? 0 : 1);

        DrawLeftCentered(g, "THEME COLOR", labelFont, subBrush, GetRowLabelX(visualPanel), GetRowCenterY(visualPanel, 1, 6));
        DrawPaletteIcon(g, GetRowIconBounds(visualPanel, 1, 6), IconColor);
        DrawThemeOptions(g);

        DrawLeftCentered(g, "LANE BRIGHTNESS", labelFont, subBrush, GetRowLabelX(visualPanel), GetRowCenterY(visualPanel, 2, 6));
        DrawLaneBrightnessIcon(g, GetRowIconBounds(visualPanel, 2, 6), IconColor);
        DrawSlider(g, SettingsSlider.LaneBrightness, GetSliderTrackBounds(SettingsSlider.LaneBrightness), _laneBrightness, $"{_laneBrightness}%");

        DrawLeftCentered(g, "FRAME RATE", labelFont, subBrush, GetRowLabelX(visualPanel), GetRowCenterY(visualPanel, 3, 6));
        DrawMonitorIcon(g, GetRowIconBounds(visualPanel, 3, 6), IconColor);
        DrawSegmentedControl(g, GetSettingsSegmentBounds("framerate"), FrameRateLabels, _frameRateMode);

        DrawLeftCentered(g, "V-SYNC", labelFont, subBrush, GetRowLabelX(visualPanel), GetRowCenterY(visualPanel, 4, 6));
        DrawMonitorIcon(g, GetRowIconBounds(visualPanel, 4, 6), IconColor);
        DrawToggle(g, GetSettingsToggleBounds("vsync"), _vsyncEnabled);

        DrawLeftCentered(g, "DARK MODE", labelFont, subBrush, GetRowLabelX(visualPanel), GetRowCenterY(visualPanel, 5, 6));
        DrawMonitorIcon(g, GetRowIconBounds(visualPanel, 5, 6), IconColor);
        DrawToggle(g, GetSettingsToggleBounds("darkmode"), _darkModeEnabled);

        DrawResetButton(g, GetResetButtonBounds(), optionFont);
    }

    private Rectangle GetBackButtonBounds()
    {
        return Rectangle.Round(new RectangleF(ScaleX(37f), ScaleY(27f), ScaleX(58f), ScaleY(58f)));
    }

    private Rectangle GetSoundPanelBounds()
    {
        return GetCenteredDesignRect(846f, 116f, 138f);
    }

    private Rectangle GetVisualPanelBounds()
    {
        return GetCenteredDesignRect(846f, 282f, 266f);
    }

    private Rectangle GetResetButtonBounds()
    {
        return GetCenteredDesignRect(278f, 42f, 742f);
    }

    private Rectangle GetCenteredDesignRect(float designWidth, float designHeight, float designY)
    {
        float x = (DesignWidth - designWidth) / 2f;
        return Rectangle.Round(new RectangleF(ScaleX(x), ScaleY(designY), ScaleX(designWidth), ScaleY(designHeight)));
    }

    private int GetRowCenterY(Rectangle panelBounds, int rowIndex, int rowCount)
    {
        int rowHeight = panelBounds.Height / rowCount;
        return panelBounds.Top + rowIndex * rowHeight + rowHeight / 2;
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
        Rectangle panelBounds = GetPanelBoundsForSlider(slider);
        Rectangle valueBounds = GetSliderValueBounds(slider);
        float x = panelBounds.Left + ScaleX(290f);
        float width = Math.Max(ScaleX(180f), valueBounds.Left - x - ScaleX(34f));
        int rowCount = slider == SettingsSlider.LaneBrightness ? 5 : 2;
        float y = slider switch
        {
            SettingsSlider.Bgm => GetRowCenterY(panelBounds, 0, 2) - ScaleY(4f),
            SettingsSlider.Sfx => GetRowCenterY(panelBounds, 1, 2) - ScaleY(4f),
            SettingsSlider.LaneBrightness => GetRowCenterY(panelBounds, 2, 6) - ScaleY(4f),
            _ => 0f,
        };

        return Rectangle.Round(new RectangleF(x, y, width, ScaleY(8f)));
    }

    private Rectangle GetSliderKnobBounds(SettingsSlider slider)
    {
        Rectangle track = GetSliderTrackBounds(slider);
        int value = slider switch
        {
            SettingsSlider.Bgm => _bgmVolume,
            SettingsSlider.Sfx => _sfxVolume,
            SettingsSlider.LaneBrightness => _laneBrightness,
            _ => 0,
        };

        float ratio = value / 100f;
        int knobSize = (int)ScaleY(24f);
        int knobX = track.Left + (int)(track.Width * ratio) - knobSize / 2;
        int knobY = track.Top + track.Height / 2 - knobSize / 2;
        return new Rectangle(knobX, knobY, knobSize, knobSize);
    }

    private Rectangle GetSliderValueBounds(SettingsSlider slider)
    {
        Rectangle panelBounds = GetPanelBoundsForSlider(slider);
        float width = ScaleX(78f);
        float x = panelBounds.Right - ScaleX(44f) - width;
        float height = ScaleY(30f);
        float y = slider switch
        {
            SettingsSlider.Bgm => GetRowCenterY(panelBounds, 0, 2) - height / 2f,
            SettingsSlider.Sfx => GetRowCenterY(panelBounds, 1, 2) - height / 2f,
            SettingsSlider.LaneBrightness => GetRowCenterY(panelBounds, 2, 6) - height / 2f,
            _ => 0f,
        };

        return Rectangle.Round(new RectangleF(x, y, width, height));
    }

    private Rectangle GetSettingsToggleBounds(string toggleKey)
    {
        Rectangle visualPanel = GetVisualPanelBounds();

        if (toggleKey == "vsync")
        {
            float centerY = GetRowCenterY(visualPanel, 4, 6);
            float width = ScaleX(62f);
            return Rectangle.Round(new RectangleF(visualPanel.Right - ScaleX(44f) - width, centerY - ScaleY(16f), width, ScaleY(32f)));
        }
        else if (toggleKey == "darkmode")
        {
            float centerY = GetRowCenterY(visualPanel, 5, 6);
            float width = ScaleX(62f);
            return Rectangle.Round(new RectangleF(visualPanel.Right - ScaleX(44f) - width, centerY - ScaleY(16f), width, ScaleY(32f)));
        }
        else
        {
            float centerY = GetRowCenterY(visualPanel, 5, 6);
            float width = ScaleX(62f);
            return Rectangle.Round(new RectangleF(visualPanel.Right - ScaleX(44f) - width, centerY - ScaleY(16f), width, ScaleY(32f)));
        }
    }

    private Rectangle GetSettingsSegmentBounds(string key)
    {
        Rectangle visualPanel = GetVisualPanelBounds();
        float rightMargin = ScaleX(44f);
        return key switch
        {
            "display" => Rectangle.Round(new RectangleF(visualPanel.Right - rightMargin - ScaleX(182f), GetRowCenterY(visualPanel, 0, 6) - ScaleY(16f), ScaleX(182f), ScaleY(32f))),
            "framerate" => Rectangle.Round(new RectangleF(visualPanel.Right - rightMargin - ScaleX(350f), GetRowCenterY(visualPanel, 3, 6) - ScaleY(16f), ScaleX(350f), ScaleY(32f))),
            _ => Rectangle.Empty,
        };
    }

    private Rectangle GetThemeOptionBounds(int index)
    {
        Rectangle visualPanel = GetVisualPanelBounds();
        float dotSize = ScaleX(32f);
        float spacing = ScaleX(19f);
        float totalWidth = dotSize * 4f + spacing * 3f;
        float startX = visualPanel.Right - ScaleX(56f) - totalWidth;
        float y = GetRowCenterY(visualPanel, 1, 6) - ScaleY(16f);
        return Rectangle.Round(new RectangleF(startX + index * (dotSize + spacing), y, dotSize, ScaleY(32f)));
    }

    private Rectangle GetPanelBoundsForSlider(SettingsSlider slider)
    {
        return slider switch
        {
            SettingsSlider.Bgm => GetSoundPanelBounds(),
            SettingsSlider.Sfx => GetSoundPanelBounds(),
            SettingsSlider.LaneBrightness => GetVisualPanelBounds(),
            _ => Rectangle.Empty,
        };
    }

    private void DrawSettingsBackground(Graphics g)
    {
        Rectangle layoutRect = new(0, 0, (int)ScaleX(DesignWidth), (int)ScaleY(DesignHeight));
        using var bgBrush = new LinearGradientBrush(layoutRect, BgColor1, BgColor2, LinearGradientMode.ForwardDiagonal);
        g.FillRectangle(bgBrush, layoutRect);

        using var hazeBrush = new SolidBrush(HazeTint);
        for (int i = 0; i < 7; i++)
        {
            float x = ScaleX(40f + i * 135f);
            float y = ScaleY(30f + (i % 3) * 140f);
            g.FillEllipse(hazeBrush, x, y, ScaleX(190f), ScaleY(120f));
        }
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
        using (var basePen = new Pen(SliderTrackColor, ScaleY(4f))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        })
        using (var fillPen = new Pen(accent, ScaleY(4f))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        })
        {
            int centerY = trackBounds.Top + trackBounds.Height / 2;
            g.DrawLine(basePen, trackBounds.Left, centerY, trackBounds.Right, centerY);
            int fillX = trackBounds.Left + (int)(trackBounds.Width * (value / 100f));
            g.DrawLine(fillPen, trackBounds.Left, centerY, fillX, centerY);
        }

        Rectangle knobBounds = GetSliderKnobBounds(slider);
        var knobShadow = knobBounds;
        knobShadow.Offset(0, 2);
        using (var shadowBrush = new SolidBrush(SliderKnobShadow))
            g.FillEllipse(shadowBrush, knobShadow);

        using (var knobBrush = new SolidBrush(accent))
            g.FillEllipse(knobBrush, knobBounds);

        Rectangle valueBounds = GetSliderValueBounds(slider);
        using var pillPath = CreateRoundedRect(valueBounds, valueBounds.Height / 2f);
        using var fillBrush = new SolidBrush(ValuePillBg);
        using var borderPen = new Pen(ValuePillBorder, 1.5f);
        using var valueFont = new Font("Segoe UI", 11.5f, FontStyle.Bold);
        using var valueBrush = new SolidBrush(ValuePillText);
        g.FillPath(fillBrush, pillPath);
        g.DrawPath(borderPen, pillPath);
        DrawCentered(g, valueText, valueFont, valueBrush, valueBounds.Left + valueBounds.Width / 2, valueBounds.Top + 4);
    }

    private void DrawToggle(Graphics g, Rectangle bounds, bool isOn)
    {
        Color fill = isOn ? GetAccentColor() : ToggleOffColor;
        using var path = CreateRoundedRect(bounds, bounds.Height / 2f);
        using var fillBrush = new SolidBrush(fill);
        g.FillPath(fillBrush, path);

        int knobSize = bounds.Height - 4;
        int knobX = isOn ? bounds.Right - knobSize - 2 : bounds.Left + 2;
        var knob = new Rectangle(knobX, bounds.Top + 2, knobSize, knobSize);
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

    private void HandleSettingsMouseDown(Point location)
    {
        if (GetBackButtonBounds().Contains(location))
        {
            _screen = UiScreen.MainMenu;
            Invalidate();
            return;
        }

        if (TryBeginSliderDrag(location, SettingsSlider.Bgm) ||
            TryBeginSliderDrag(location, SettingsSlider.Sfx) ||
            TryBeginSliderDrag(location, SettingsSlider.LaneBrightness))
        {
            Invalidate();
            return;
        }

        int displayHit = GetSegmentHitIndex(GetSettingsSegmentBounds("display"), 2, location);
        if (displayHit >= 0)
        {
            _displayMode = displayHit == 0 ? DisplayMode.Windowed : DisplayMode.Fullscreen;
            ApplySettingsToRuntime();
            Invalidate();
            return;
        }

        int frameRateHit = GetSegmentHitIndex(GetSettingsSegmentBounds("framerate"), FrameRateLabels.Length, location);
        if (frameRateHit >= 0)
        {
            _frameRateMode = frameRateHit;
            ApplySettingsToRuntime();
            Invalidate();
            return;
        }

        if (GetSettingsToggleBounds("vsync").Contains(location))
        {
            _vsyncEnabled = !_vsyncEnabled;
            Invalidate();
            return;
        }

        if (GetSettingsToggleBounds("darkmode").Contains(location))
        {
            _darkModeEnabled = !_darkModeEnabled;
            Invalidate();
            return;
        }

        for (int i = 0; i < 4; i++)
        {
            if (Rectangle.Inflate(GetThemeOptionBounds(i), 8, 8).Contains(location))
            {
                _themeColorIndex = i;
                ApplySettingsToRuntime();
                Invalidate();
                return;
            }
        }

        if (GetResetButtonBounds().Contains(location))
        {
            ResetSettingsToDefault();
            ApplySettingsToRuntime();
            Invalidate();
        }
    }

    private bool TryBeginSliderDrag(Point location, SettingsSlider slider)
    {
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
            case SettingsSlider.Sfx:
                _sfxVolume = value;
                break;
            case SettingsSlider.LaneBrightness:
                _laneBrightness = value;
                break;
        }
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
        if (GetBackButtonBounds().Contains(location) || GetResetButtonBounds().Contains(location))
            return true;

        if (GetSettingsSegmentBounds("display").Contains(location))
            return true;
        if (GetSettingsSegmentBounds("framerate").Contains(location))
            return true;
        if (GetSettingsToggleBounds("vsync").Contains(location))
            return true;
        if (GetSettingsToggleBounds("darkmode").Contains(location))
            return true;

        if (Rectangle.Union(GetSliderTrackBounds(SettingsSlider.Bgm), Rectangle.Inflate(GetSliderKnobBounds(SettingsSlider.Bgm), 8, 8)).Contains(location))
            return true;
        if (Rectangle.Union(GetSliderTrackBounds(SettingsSlider.Sfx), Rectangle.Inflate(GetSliderKnobBounds(SettingsSlider.Sfx), 8, 8)).Contains(location))
            return true;
        if (Rectangle.Union(GetSliderTrackBounds(SettingsSlider.LaneBrightness), Rectangle.Inflate(GetSliderKnobBounds(SettingsSlider.LaneBrightness), 8, 8)).Contains(location))
            return true;

        for (int i = 0; i < 4; i++)
            if (Rectangle.Inflate(GetThemeOptionBounds(i), 8, 8).Contains(location))
                return true;

        return false;
    }

    private void ResetSettingsToDefault()
    {
        _bgmVolume = 80;
        _sfxVolume = 60;
        _themeColorIndex = 0;
        _laneBrightness = 70;
        _displayMode = DisplayMode.Windowed;
        _frameRateMode = 2;
        _vsyncEnabled = false;
        _darkModeEnabled = false;
    }
}
