using System.Drawing.Drawing2D;

namespace RhythmGame;

public sealed partial class GameForm
{
    private const float CalibrationFirstBeatSeconds = 1.0f;
    private const float CalibrationBeatIntervalSeconds = 0.75f;
    private const float CalibrationFastBeatIntervalSeconds = 0.62f;
    private const float CalibrationSlowBeatIntervalSeconds = 0.84f;
    private const float CalibrationOffbeatShortIntervalSeconds = 0.54f;
    private const float CalibrationOffbeatLongIntervalSeconds = 0.90f;
    private const float CalibrationSteadyBeatIntervalSeconds = 0.72f;
    private const int CalibrationSampleTarget = 10;
    private const float CalibrationHitWindowSeconds = 0.32f;
    private const int AutoSyncMinimumSamples = 5;
    private const float AutoSyncOutlierFloorSeconds = 0.035f;
    private const float AutoSyncExcellentJitterSeconds = 0.012f;
    private const float AutoSyncMaxUsefulJitterSeconds = 0.085f;

    private void PrepareInputCalibrationScreen()
    {
        _audio.StopAllSounds();
        _calibrationOffsets.Clear();
        _calibrationHitTargets.Clear();
        BuildCalibrationBeatSchedule();
        _lastCalibrationOffsetSeconds = 0f;
        _lastCalibrationHitRate = 0f;
        ResetAutoSyncEstimate();
        _calibrationBeatCount = 0;
        _calibrationSaved = false;
        _nextCalibrationBeatSeconds = _calibrationTargetTimes.Count > 0 ? _calibrationTargetTimes[0] : CalibrationFirstBeatSeconds;
        _calibrationStopwatch.Reset();
    }

    private void StartInputCalibration()
    {
        _audio.StopAllSounds();
        _audio.PrepareHitSounds(_sfxVolume);
        EnterCalibrationLowLatencyMode();
        _calibrationOffsets.Clear();
        _calibrationHitTargets.Clear();
        BuildCalibrationBeatSchedule();
        _lastCalibrationOffsetSeconds = 0f;
        _lastCalibrationHitRate = 0f;
        ResetAutoSyncEstimate();
        _calibrationBeatCount = 0;
        _calibrationSaved = false;
        _nextCalibrationBeatSeconds = _calibrationTargetTimes.Count > 0 ? _calibrationTargetTimes[0] : CalibrationFirstBeatSeconds;
        _calibrationStopwatch.Restart();
        _timer.Start();
    }

    private void StopInputCalibration(bool save)
    {
        if (save)
            SaveInputCalibrationResult();

        _calibrationStopwatch.Reset();
        ExitCalibrationLowLatencyMode();
        _audio.PlayMainScreenBgm();
        if (!HasPendingAchievementToast())
            _timer.Stop();
    }

    private void UpdateInputCalibration()
    {
        if (!_calibrationStopwatch.IsRunning || _calibrationSaved)
            return;

        if (_calibrationTargetTimes.Count == 0)
            BuildCalibrationBeatSchedule();

        float elapsed = (float)_calibrationStopwatch.Elapsed.TotalSeconds;
        while (_calibrationBeatCount < _calibrationTargetTimes.Count && elapsed >= _calibrationTargetTimes[_calibrationBeatCount])
        {
            _audio.PlayHit(_sfxVolume, Judgment.Perfect);
            _calibrationBeatCount++;
            _nextCalibrationBeatSeconds = _calibrationBeatCount < _calibrationTargetTimes.Count
                ? _calibrationTargetTimes[_calibrationBeatCount]
                : _calibrationTargetTimes[^1];
        }

        if (_calibrationBeatCount >= _calibrationTargetTimes.Count &&
            _calibrationTargetTimes.Count > 0 &&
            elapsed >= _calibrationTargetTimes[^1] + CalibrationHitWindowSeconds &&
            _calibrationOffsets.Count > 0)
        {
            SaveInputCalibrationResult();
        }
    }

    private void CaptureInputCalibrationHit()
    {
        if (_calibrationSaved)
            return;

        if (!_calibrationStopwatch.IsRunning)
        {
            StartInputCalibration();
            return;
        }

        if (_calibrationTargetTimes.Count == 0)
            BuildCalibrationBeatSchedule();

        float elapsed = (float)_calibrationStopwatch.Elapsed.TotalSeconds;
        int beatIndex = FindNearestCalibrationTarget(elapsed);
        if (beatIndex < 0)
            return;

        float targetBeat = _calibrationTargetTimes[beatIndex];
        float signedOffset = elapsed - targetBeat;
        if (MathF.Abs(signedOffset) > CalibrationHitWindowSeconds || _calibrationHitTargets.Contains(beatIndex))
            return;

        _calibrationHitTargets.Add(beatIndex);
        _lastCalibrationOffsetSeconds = signedOffset;
        _calibrationOffsets.Add(signedOffset);
        UpdateAutoSyncEstimate();
        _audio.PlayHit(_sfxVolume, Judgment.Great);

        if (_calibrationOffsets.Count >= _calibrationTargetTimes.Count)
            SaveInputCalibrationResult();
    }

    private void SaveInputCalibrationResult()
    {
        if (_calibrationOffsets.Count < AutoSyncMinimumSamples)
            return;

        UpdateAutoSyncEstimate();
        float result = _autoSyncValidSampleCount >= AutoSyncMinimumSamples
            ? _autoSyncEstimateSeconds
            : _calibrationOffsets.Average();
        _audioOffsetMs = Math.Clamp((int)MathF.Round(result * 1000f), -150, 150);
        _lastCalibrationOffsetSeconds = result;
        _lastCalibrationHitRate = _calibrationTargetTimes.Count == 0 ? 0f : _calibrationOffsets.Count / (float)_calibrationTargetTimes.Count;
        _calibrationSaved = true;
        _calibrationStopwatch.Stop();
        ExitCalibrationLowLatencyMode();
        ApplySettingsToRuntime();
        SaveUserSettings();
    }

    private void EnterCalibrationLowLatencyMode()
    {
        if (_calibrationLowLatencyModeActive)
            return;

        _calibrationPreviousTimerInterval = _timer.Interval;
        _timer.Interval = 4;
        try { timeBeginPeriod(1); } catch { /* Timer precision is best-effort. */ }
        _calibrationLowLatencyModeActive = true;
    }

    private void ExitCalibrationLowLatencyMode()
    {
        if (!_calibrationLowLatencyModeActive)
            return;

        try { timeEndPeriod(1); } catch { /* Timer precision is best-effort. */ }
        _calibrationLowLatencyModeActive = false;
        _timer.Interval = _calibrationPreviousTimerInterval > 0 ? _calibrationPreviousTimerInterval : FrameRateIntervals[Math.Clamp(_frameRateMode, 0, FrameRateIntervals.Length - 1)];
    }

    private void ResetAutoSyncEstimate()
    {
        _autoSyncEstimateSeconds = 0f;
        _autoSyncJitterSeconds = 0f;
        _autoSyncConfidence = 0f;
        _autoSyncValidSampleCount = 0;
    }

    private void UpdateAutoSyncEstimate()
    {
        if (!TryCalculateAutoSyncEstimate(_calibrationOffsets, out float estimate, out float jitter, out int validSamples, out float confidence))
            return;

        _autoSyncEstimateSeconds = estimate;
        _autoSyncJitterSeconds = jitter;
        _autoSyncValidSampleCount = validSamples;
        _autoSyncConfidence = confidence;
        _lastCalibrationOffsetSeconds = estimate;
    }

    private bool TryCalculateAutoSyncEstimate(
        IReadOnlyList<float> offsets,
        out float estimate,
        out float jitter,
        out int validSamples,
        out float confidence)
    {
        estimate = 0f;
        jitter = 0f;
        validSamples = 0;
        confidence = 0f;

        if (offsets.Count < AutoSyncMinimumSamples)
            return false;

        float[] sorted = offsets.Order().ToArray();
        float median = GetMedian(sorted);
        float[] deviations = sorted.Select(value => MathF.Abs(value - median)).Order().ToArray();
        float mad = GetMedian(deviations);
        float outlierWindow = Math.Max(AutoSyncOutlierFloorSeconds, mad * 2.5f);
        float[] filtered = sorted
            .Where(value => MathF.Abs(value - median) <= outlierWindow)
            .ToArray();

        if (filtered.Length < AutoSyncMinimumSamples)
            filtered = sorted;

        float[] stable = filtered.Order().ToArray();
        int trim = stable.Length >= 20 ? Math.Max(1, (int)MathF.Floor(stable.Length * 0.15f)) : 0;
        float[] trimmed = stable.Skip(trim).Take(stable.Length - trim * 2).ToArray();
        if (trimmed.Length == 0)
            trimmed = stable;

        estimate = trimmed.Average();
        float estimatedOffset = estimate;
        jitter = MathF.Sqrt(trimmed.Sum(value => MathF.Pow(value - estimatedOffset, 2f)) / trimmed.Length);
        validSamples = trimmed.Length;

        float hitCoverage = _calibrationTargetTimes.Count == 0
            ? Math.Clamp(offsets.Count / (float)CalibrationSampleTarget, 0f, 1f)
            : Math.Clamp(offsets.Count / (float)_calibrationTargetTimes.Count, 0f, 1f);
        float sampleConfidence = Math.Clamp((validSamples - AutoSyncMinimumSamples) / Math.Max(1f, CalibrationSampleTarget - AutoSyncMinimumSamples), 0f, 1f);
        float stabilityConfidence = 1f - Math.Clamp((jitter - AutoSyncExcellentJitterSeconds) / (AutoSyncMaxUsefulJitterSeconds - AutoSyncExcellentJitterSeconds), 0f, 1f);
        confidence = Math.Clamp((sampleConfidence * 0.45f) + (stabilityConfidence * 0.4f) + (hitCoverage * 0.15f), 0f, 1f);
        return true;
    }

    private static float GetMedian(float[] sorted)
    {
        if (sorted.Length == 0)
            return 0f;

        int middle = sorted.Length / 2;
        return sorted.Length % 2 == 1
            ? sorted[middle]
            : (sorted[middle - 1] + sorted[middle]) / 2f;
    }

    private void BuildCalibrationBeatSchedule()
    {
        _calibrationTargetTimes.Clear();
        float time = CalibrationFirstBeatSeconds;
        for (int i = 0; i < CalibrationSampleTarget; i++)
        {
            int pattern = (i / 3) % 4;
            int beatInPattern = i % 3;
            _calibrationTargetTimes.Add(time);
            time += GetCalibrationPatternInterval(pattern, beatInPattern);
        }
    }

    private static float GetCalibrationPatternInterval(int pattern, int beatInPattern)
    {
        return pattern switch
        {
            0 => CalibrationFastBeatIntervalSeconds,
            1 => CalibrationSlowBeatIntervalSeconds,
            2 => beatInPattern == 0
                ? CalibrationOffbeatShortIntervalSeconds
                : beatInPattern == 1
                    ? CalibrationOffbeatLongIntervalSeconds
                    : CalibrationSteadyBeatIntervalSeconds,
            _ => CalibrationSteadyBeatIntervalSeconds,
        };
    }

    private int FindNearestCalibrationTarget(float elapsed)
    {
        int nearest = -1;
        float best = float.MaxValue;
        for (int i = 0; i < _calibrationTargetTimes.Count; i++)
        {
            if (_calibrationHitTargets.Contains(i))
                continue;

            float distance = MathF.Abs(elapsed - _calibrationTargetTimes[i]);
            if (distance < best)
            {
                best = distance;
                nearest = i;
            }
        }

        return best <= CalibrationHitWindowSeconds ? nearest : -1;
    }

    private void DrawInputCalibration(Graphics g)
    {
        DrawSettingsBackground(g);

        using var titleFont = new Font("Segoe UI", Math.Max(24f, MenuS(44f)), FontStyle.Regular);
        using var sampleFont = new Font("Segoe UI", Math.Max(20f, MenuS(40f)), FontStyle.Regular);
        using var labelFont = new Font("Segoe UI", Math.Max(8f, MenuS(16f)), FontStyle.Regular);
        using var syncFont = new Font("Segoe UI", Math.Max(22f, MenuS(44f)), FontStyle.Regular);
        using var buttonFont = new Font("Segoe UI", Math.Max(12f, MenuS(26f)), FontStyle.Regular);
        using var titleBrush = new SolidBrush(Color.FromArgb(238, 246, 255));
        using var labelBrush = new SolidBrush(Color.FromArgb(200, 213, 238));
        using var valueBrush = new SolidBrush(Color.White);
        using var accentBrush = new SolidBrush(GetAccentColor());

        DrawKeyBindingBackButton(g, GetCalibrationBackButtonBounds());
        DrawSpacedString(g, "INPUT CALIBRATION", titleFont, titleBrush, MenuX(840f), MenuY(74f), MenuS(14f), centered: true);
        DrawCalibrationSignalLine(g);

        Rectangle panel = GetCalibrationPanelBounds();
        using (var panelPath = CreateRoundedRect(panel, MenuS(22f)))
        using (var panelFill = new LinearGradientBrush(panel, Color.FromArgb(42, 13, 22, 42), Color.FromArgb(22, 6, 10, 23), LinearGradientMode.Vertical))
        using (var panelBorder = new Pen(Color.FromArgb(215, 122, 181, 255), Math.Max(1f, MenuS(1.2f))))
        {
            g.FillPath(panelFill, panelPath);
            DrawCalibrationPanelTexture(g, panel);
            g.DrawPath(panelBorder, panelPath);
        }

        float progress = Math.Clamp(_calibrationOffsets.Count / (float)CalibrationSampleTarget, 0f, 1f);
        PointF ringCenter = new(MenuX(770f), MenuY(420f));
        DrawCalibrationProgressRing(g, ringCenter, MenuS(204f), progress);

        if (_calibrationStopwatch.IsRunning)
        {
            DrawGlowCenteredString(g, $"{_calibrationOffsets.Count}/{CalibrationSampleTarget}", sampleFont, valueBrush, ringCenter.X, ringCenter.Y, Color.FromArgb(160, 118, 183, 255));
        }
        else
        {
            DrawCalibrationStartButton(g, GetCalibrationStartButtonBounds(), buttonFont);
        }

        int lastMs = (int)MathF.Round(_lastCalibrationOffsetSeconds * 1000f);
        string direction = lastMs < -3 ? "EARLY" : lastMs > 3 ? "LATE" : "SYNC";
        string currentText = _calibrationSaved ? $"SAVED {_audioOffsetMs:+0;-0;0} ms" : $"{direction} {Math.Abs(lastMs)} ms";
        DrawGlowSpacedString(g, currentText, syncFont, accentBrush, panel.Left + panel.Width / 2f, panel.Top + MenuS(462f), MenuS(8f), Color.FromArgb(180, GetAccentColor()));

        string sub = _calibrationSaved
            ? $"타율 {MathF.Round(_lastCalibrationHitRate * 100f)}%|저장 완료"
            : _calibrationStopwatch.IsRunning ? GetAutoSyncStatusText() : "측정 준비";
        DrawCalibrationStatus(g, panel, labelFont, labelBrush, sub);
    }

    private string GetAutoSyncStatusText()
    {
        if (_autoSyncValidSampleCount < AutoSyncMinimumSamples)
            return "박자에 맞춰 입력";

        int confidence = (int)MathF.Round(_autoSyncConfidence * 100f);
        int jitter = (int)MathF.Round(_autoSyncJitterSeconds * 1000f);
        return $"맞춤률 {confidence}%|흔들림 {jitter} ms";
    }

    private void DrawCalibrationStatus(Graphics g, Rectangle panel, Font font, Brush textBrush, string status)
    {
        float y = panel.Top + MenuS(535f);
        if (status.Contains('|'))
        {
            string[] parts = status.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            float totalWidth = MenuS(500f);
            float gap = MenuS(24f);
            float badgeWidth = (totalWidth - gap) / 2f;
            float centerX = panel.Left + panel.Width / 2f;
            for (int i = 0; i < Math.Min(parts.Length, 2); i++)
            {
                RectangleF badge = new(centerX - totalWidth / 2f + i * (badgeWidth + gap), y - MenuS(6f), badgeWidth, MenuS(46f));
                DrawCalibrationStatusBadge(g, badge, parts[i], font);
            }

            return;
        }

        SizeF textSize = g.MeasureString(status, font);
        RectangleF mask = new(
            panel.Left + panel.Width / 2f - textSize.Width / 2f - MenuS(18f),
            y + MenuS(1f),
            textSize.Width + MenuS(36f),
            textSize.Height + MenuS(10f));
        using var maskBrush = new SolidBrush(Color.FromArgb(210, 7, 12, 27));
        g.FillRectangle(maskBrush, mask);
        DrawCenteredGlowString(g, status, font, textBrush, panel.Left + panel.Width / 2f, y + MenuS(13f), Color.FromArgb(110, GetAccentColor()));
    }

    private void DrawCalibrationStatusBadge(Graphics g, RectangleF bounds, string text, Font font)
    {
        Color accent = GetAccentColor();
        using var path = CreateRoundedRect(Rectangle.Round(bounds), MenuS(10f));
        using var fill = new LinearGradientBrush(bounds, Color.FromArgb(34, 20, 34, 70), Color.FromArgb(15, 5, 10, 25), LinearGradientMode.Vertical);
        using var border = new Pen(Color.FromArgb(132, accent), Math.Max(1f, MenuS(1f)));
        using var valueBrush = new SolidBrush(Color.FromArgb(232, 243, 255));
        using var glowBrush = new SolidBrush(Color.FromArgb(54, accent));
        g.FillPath(fill, path);
        g.DrawPath(border, path);

        string[] pieces = text.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        string label = pieces.Length > 0 ? pieces[0] : text;
        string value = pieces.Length > 1 ? pieces[1] : string.Empty;
        using var labelFont = new Font(font.FontFamily, Math.Max(8f, font.Size * 0.74f), FontStyle.Regular);
        using var valueFont = new Font(font.FontFamily, Math.Max(10f, font.Size * 1.02f), FontStyle.Regular);

        SizeF labelSize = g.MeasureString(label, labelFont);
        SizeF valueSize = g.MeasureString(value, valueFont);
        float total = labelSize.Width + MenuS(14f) + valueSize.Width;
        float x = bounds.Left + (bounds.Width - total) / 2f;
        float labelY = bounds.Top + (bounds.Height - labelSize.Height) / 2f + MenuS(1f);
        float valueY = bounds.Top + (bounds.Height - valueSize.Height) / 2f;
        g.DrawString(label, labelFont, glowBrush, x + MenuS(1f), labelY);
        g.DrawString(label, labelFont, valueBrush, x, labelY);
        g.DrawString(value, valueFont, glowBrush, x + labelSize.Width + MenuS(15f), valueY);
        g.DrawString(value, valueFont, valueBrush, x + labelSize.Width + MenuS(14f), valueY);
    }

    private Rectangle GetCalibrationBackButtonBounds()
    {
        return MenuRect(24f, 28f, 80f, 80f);
    }

    private Rectangle GetCalibrationPanelBounds()
    {
        return MenuRect(200f, 190f, 1136f, 610f);
    }

    private Rectangle GetCalibrationStartButtonBounds()
    {
        return MenuRect(620f, 376f, 300f, 88f);
    }

    private void DrawCalibrationSignalLine(Graphics g)
    {
        float y = MenuY(142f);
        float left = MenuX(384f);
        float right = MenuX(1296f);
        float center = MenuX(770f);
        Color accent = GetAccentColor();

        using var basePen = new Pen(Color.FromArgb(58, 96, 150, 230), Math.Max(1f, MenuS(1f)));
        g.DrawLine(basePen, left, y, right, y);

        using var glowPen = new Pen(Color.FromArgb(130, accent), Math.Max(1f, MenuS(1.2f)));
        g.DrawLine(glowPen, center - MenuS(72f), y, center + MenuS(72f), y);
        using var flarePath = new GraphicsPath();
        flarePath.AddEllipse(center - MenuS(92f), y - MenuS(16f), MenuS(184f), MenuS(32f));
        using var flare = new PathGradientBrush(flarePath)
        {
            CenterColor = Color.FromArgb(155, accent),
            SurroundColors = [Color.FromArgb(0, accent)]
        };
        g.FillPath(flare, flarePath);
    }

    private void DrawCalibrationPanelTexture(Graphics g, Rectangle panel)
    {
        using var gridPen = new Pen(Color.FromArgb(16, 120, 170, 255), Math.Max(1f, MenuS(1f)));
        float step = MenuS(21f);
        for (float x = panel.Left + step; x < panel.Right; x += step)
            g.DrawLine(gridPen, x, panel.Top + MenuS(26f), x, panel.Bottom - MenuS(26f));

        for (float y = panel.Top + step; y < panel.Bottom; y += step)
            g.DrawLine(gridPen, panel.Left + MenuS(26f), y, panel.Right - MenuS(26f), y);

        using var dotBrush = new SolidBrush(Color.FromArgb(28, 120, 178, 255));
        for (float x = panel.Left + MenuS(32f); x < panel.Right - MenuS(30f); x += step)
        {
            for (float y = panel.Top + MenuS(32f); y < panel.Bottom - MenuS(30f); y += step)
                g.FillRectangle(dotBrush, x, y, Math.Max(1f, MenuS(1.2f)), Math.Max(1f, MenuS(1.2f)));
        }
    }

    private void DrawCalibrationProgressRing(Graphics g, PointF center, float radius, float progress)
    {
        Color accent = GetAccentColor();
        RectangleF outer = new(center.X - radius, center.Y - radius, radius * 2f, radius * 2f);
        RectangleF middle = RectangleF.Inflate(outer, -MenuS(36f), -MenuS(36f));
        RectangleF inner = RectangleF.Inflate(outer, -MenuS(72f), -MenuS(72f));

        using (var haloPath = new GraphicsPath())
        {
            haloPath.AddEllipse(outer);
            using var halo = new PathGradientBrush(haloPath)
            {
                CenterColor = Color.FromArgb(68, accent),
                SurroundColors = [Color.FromArgb(0, accent)]
            };
            g.FillPath(halo, haloPath);
        }

        using var outerPen = new Pen(Color.FromArgb(185, 126, 181, 255), Math.Max(1f, MenuS(2f)));
        using var middlePen = new Pen(Color.FromArgb(80, 105, 144, 255), Math.Max(1f, MenuS(1f)));
        using var innerPen = new Pen(Color.FromArgb(220, 162, 126, 255), Math.Max(1f, MenuS(1.5f)));
        g.DrawEllipse(outerPen, outer);
        g.DrawEllipse(middlePen, middle);
        g.DrawEllipse(innerPen, inner);

        using var tickPen = new Pen(Color.FromArgb(62, 126, 172, 255), Math.Max(1f, MenuS(1f)));
        for (int i = 0; i < 96; i++)
        {
            double angle = (-90 + i * 360.0 / 96.0) * Math.PI / 180.0;
            float length = i % 8 == 0 ? MenuS(17f) : MenuS(9f);
            float x1 = center.X + MathF.Cos((float)angle) * (radius - MenuS(20f));
            float y1 = center.Y + MathF.Sin((float)angle) * (radius - MenuS(20f));
            float x2 = center.X + MathF.Cos((float)angle) * (radius - MenuS(20f) - length);
            float y2 = center.Y + MathF.Sin((float)angle) * (radius - MenuS(20f) - length);
            g.DrawLine(tickPen, x1, y1, x2, y2);
        }

        using var progressPen = new Pen(accent, Math.Max(5f, MenuS(6f))) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawArc(progressPen, outer, -90f, Math.Max(2f, 360f * progress));
        DrawCalibrationStartReference(g, center, radius, accent);
        DrawCalibrationTempoCursor(g, center, radius, accent);

        int beatPulse = _calibrationStopwatch.IsRunning
            ? (int)(Math.Sin(_calibrationStopwatch.Elapsed.TotalSeconds * Math.PI * 2.0 / CalibrationBeatIntervalSeconds) * 32 + 96)
            : 54;
        using var pulseBrush = new SolidBrush(Color.FromArgb(beatPulse, accent));
        g.FillEllipse(pulseBrush, RectangleF.Inflate(inner, -MenuS(7f), -MenuS(7f)));

        using var corePath = new GraphicsPath();
        corePath.AddEllipse(RectangleF.Inflate(inner, -MenuS(18f), -MenuS(18f)));
        using var coreFill = new PathGradientBrush(corePath)
        {
            CenterColor = Color.FromArgb(44, 55, 96, 186),
            SurroundColors = [Color.FromArgb(12, 4, 8, 22)]
        };
        g.FillPath(coreFill, corePath);
    }

    private void DrawCalibrationStartReference(Graphics g, PointF center, float radius, Color accent)
    {
        float markerRadius = MenuS(6f);
        PointF marker = new(center.X, center.Y - radius);
        using var guidePen = new Pen(Color.FromArgb(120, accent), Math.Max(1f, MenuS(1.2f)))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        g.DrawLine(guidePen, marker.X, marker.Y - MenuS(22f), marker.X, marker.Y + MenuS(18f));

        using var markerGlow = new SolidBrush(Color.FromArgb(120, accent));
        using var markerFill = new SolidBrush(Color.FromArgb(235, 138, 190, 255));
        g.FillEllipse(markerGlow, marker.X - markerRadius * 2f, marker.Y - markerRadius * 2f, markerRadius * 4f, markerRadius * 4f);
        g.FillEllipse(markerFill, marker.X - markerRadius, marker.Y - markerRadius, markerRadius * 2f, markerRadius * 2f);
    }

    private void DrawCalibrationTempoCursor(Graphics g, PointF center, float radius, Color accent)
    {
        if (!_calibrationStopwatch.IsRunning || _calibrationSaved || _calibrationTargetTimes.Count == 0)
            return;

        float elapsed = (float)_calibrationStopwatch.Elapsed.TotalSeconds;
        if (!TryGetCalibrationOrbitPhase(elapsed, out float phase))
            return;

        float angle = (-90f + 360f * phase) * MathF.PI / 180f;
        float cursorRadius = radius - MenuS(2f);
        PointF cursor = new(
            center.X + MathF.Cos(angle) * cursorRadius,
            center.Y + MathF.Sin(angle) * cursorRadius);

        using var trailPen = new Pen(Color.FromArgb(95, accent), Math.Max(3f, MenuS(3.5f)))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        g.DrawArc(trailPen, center.X - cursorRadius, center.Y - cursorRadius, cursorRadius * 2f, cursorRadius * 2f, -90f, Math.Max(4f, 360f * phase));

        float glowRadius = MenuS(18f);
        using var glowPath = new GraphicsPath();
        glowPath.AddEllipse(cursor.X - glowRadius, cursor.Y - glowRadius, glowRadius * 2f, glowRadius * 2f);
        using var glow = new PathGradientBrush(glowPath)
        {
            CenterColor = Color.FromArgb(210, accent),
            SurroundColors = [Color.FromArgb(0, accent)]
        };
        g.FillPath(glow, glowPath);

        using var cursorFill = new SolidBrush(Color.FromArgb(245, 232, 244, 255));
        float dot = MenuS(7f);
        g.FillEllipse(cursorFill, cursor.X - dot, cursor.Y - dot, dot * 2f, dot * 2f);
    }

    private bool TryGetCalibrationOrbitPhase(float elapsed, out float phase)
    {
        phase = 0f;
        if (_calibrationTargetTimes.Count == 0)
            return false;

        int targetIndex = 0;
        while (targetIndex < _calibrationTargetTimes.Count && elapsed > _calibrationTargetTimes[targetIndex])
            targetIndex++;

        if (targetIndex >= _calibrationTargetTimes.Count)
            return false;

        float previous = targetIndex == 0 ? 0f : _calibrationTargetTimes[targetIndex - 1];
        float target = _calibrationTargetTimes[targetIndex];
        float interval = Math.Max(0.001f, target - previous);
        phase = Math.Clamp((elapsed - previous) / interval, 0f, 1f);
        return true;
    }

    private void DrawCalibrationStartButton(Graphics g, Rectangle bounds, Font font)
    {
        Color accent = GetAccentColor();
        using var path = CreateRoundedRect(bounds, MenuS(12f));
        using var fill = new LinearGradientBrush(bounds,
            _isCalibrationStartHovered ? Color.FromArgb(42, 70, 112, 180) : Color.FromArgb(24, 42, 76, 140),
            Color.FromArgb(12, 10, 16, 38),
            LinearGradientMode.Vertical);
        using var textBrush = new SolidBrush(Color.White);
        g.FillPath(fill, path);
        using var textGlow = new SolidBrush(Color.FromArgb(_isCalibrationStartHovered ? 80 : 54, accent));
        string label = _calibrationSaved ? "RUN AGAIN" : _calibrationStopwatch.IsRunning ? "RESTART" : "START";
        DrawSpacedString(g, label, font, textGlow, bounds.Left + bounds.Width / 2f + MenuS(1f), bounds.Top + MenuS(34f), MenuS(14f), centered: true);
        DrawSpacedString(g, label, font, textBrush, bounds.Left + bounds.Width / 2f, bounds.Top + MenuS(34f), MenuS(14f), centered: true);
    }

    private void DrawGlowSpacedString(Graphics g, string text, Font font, Brush brush, float x, float y, float spacing, Color glowColor)
    {
        for (int i = 4; i >= 1; i--)
        {
            using var glow = new SolidBrush(Color.FromArgb(22 * i, glowColor));
            DrawSpacedString(g, text, font, glow, x + MenuS(i * 0.4f), y, spacing, centered: true);
        }

        DrawSpacedString(g, text, font, brush, x, y, spacing, centered: true);
    }

    private void DrawGlowCenteredString(Graphics g, string text, Font font, Brush brush, float centerX, float centerY, Color glowColor)
    {
        SizeF size = g.MeasureString(text, font);
        float x = centerX - size.Width / 2f;
        float y = centerY - size.Height / 2f;
        for (int i = 4; i >= 1; i--)
        {
            using var glow = new SolidBrush(Color.FromArgb(20 * i, glowColor));
            g.DrawString(text, font, glow, x + MenuS(i * 0.35f), y);
        }

        g.DrawString(text, font, brush, x, y);
    }

    private void DrawCenteredGlowString(Graphics g, string text, Font font, Brush brush, float centerX, float centerY, Color glowColor)
    {
        SizeF size = g.MeasureString(text, font);
        float x = centerX - size.Width / 2f;
        float y = centerY - size.Height / 2f;
        using var glow = new SolidBrush(glowColor);
        g.DrawString(text, font, glow, x + MenuS(1f), y);
        g.DrawString(text, font, brush, x, y);
    }

    private void HandleInputCalibrationMouseDown(Point location)
    {
        if (GetCalibrationBackButtonBounds().Contains(location))
        {
            StopInputCalibration(save: false);
            _screen = UiScreen.Settings;
            Invalidate();
            return;
        }

        if (GetCalibrationStartButtonBounds().Contains(location))
        {
            StartInputCalibration();
            Invalidate();
        }
    }
}
