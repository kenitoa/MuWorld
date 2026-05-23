using System.Drawing.Drawing2D;

namespace RhythmGame;

public sealed partial class GameForm
{
    private const float CalibrationFirstBeatSeconds = 1.0f;
    private const float CalibrationBeatIntervalSeconds = 0.75f;
    private const int CalibrationSampleTarget = 12;

    private void StartInputCalibration()
    {
        _audio.StopAllSounds();
        _calibrationOffsets.Clear();
        _lastCalibrationOffsetSeconds = 0f;
        _calibrationBeatCount = 0;
        _calibrationSaved = false;
        _nextCalibrationBeatSeconds = CalibrationFirstBeatSeconds;
        _calibrationStopwatch.Restart();
        _timer.Start();
    }

    private void StopInputCalibration(bool save)
    {
        if (save)
            SaveInputCalibrationResult();

        _calibrationStopwatch.Reset();
        _audio.PlayMainScreenBgm();
        if (!HasPendingAchievementToast())
            _timer.Stop();
    }

    private void UpdateInputCalibration()
    {
        if (!_calibrationStopwatch.IsRunning || _calibrationSaved)
            return;

        float elapsed = (float)_calibrationStopwatch.Elapsed.TotalSeconds;
        while (elapsed >= _nextCalibrationBeatSeconds)
        {
            _audio.PlayHit(_sfxVolume, Judgment.Perfect);
            _nextCalibrationBeatSeconds += CalibrationBeatIntervalSeconds;
            _calibrationBeatCount++;
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

        float elapsed = (float)_calibrationStopwatch.Elapsed.TotalSeconds;
        int beatIndex = (int)MathF.Round((elapsed - CalibrationFirstBeatSeconds) / CalibrationBeatIntervalSeconds);
        if (beatIndex < 0)
            return;

        float targetBeat = CalibrationFirstBeatSeconds + beatIndex * CalibrationBeatIntervalSeconds;
        float signedOffset = elapsed - targetBeat;
        if (MathF.Abs(signedOffset) > CalibrationBeatIntervalSeconds * 0.45f)
            return;

        _lastCalibrationOffsetSeconds = signedOffset;
        _calibrationOffsets.Add(signedOffset);
        _audio.PlayHit(_sfxVolume, Judgment.Great);

        if (_calibrationOffsets.Count >= CalibrationSampleTarget)
            SaveInputCalibrationResult();
    }

    private void SaveInputCalibrationResult()
    {
        if (_calibrationOffsets.Count == 0)
            return;

        float average = _calibrationOffsets.Average();
        _audioOffsetMs = Math.Clamp((int)MathF.Round(average * 1000f), -150, 150);
        _lastCalibrationOffsetSeconds = average;
        _calibrationSaved = true;
        _calibrationStopwatch.Stop();
        ApplySettingsToRuntime();
        SaveUserSettings();
    }

    private void DrawInputCalibration(Graphics g)
    {
        Rectangle layoutRect = new(0, 0, (int)ScaleX(DesignWidth), (int)ScaleY(DesignHeight));
        using (var bg = new LinearGradientBrush(layoutRect, Color.FromArgb(6, 10, 20), Color.FromArgb(18, 28, 48), LinearGradientMode.Vertical))
            g.FillRectangle(bg, layoutRect);

        DrawBackButton(g, GetCalibrationBackButtonBounds());

        using var titleFont = new Font("Segoe UI", Math.Max(14f, ScaleY(34f)), FontStyle.Bold);
        using var labelFont = new Font("Segoe UI", Math.Max(8f, ScaleY(13f)), FontStyle.Bold);
        using var valueFont = new Font("Segoe UI", Math.Max(18f, ScaleY(42f)), FontStyle.Bold);
        using var smallFont = new Font("Segoe UI", Math.Max(7f, ScaleY(11f)), FontStyle.Bold);
        using var titleBrush = new SolidBrush(Color.FromArgb(238, 246, 255));
        using var labelBrush = new SolidBrush(Color.FromArgb(170, 190, 222));
        using var valueBrush = new SolidBrush(Color.White);
        using var accentBrush = new SolidBrush(GetAccentColor());

        DrawCentered(g, "INPUT CALIBRATION", titleFont, titleBrush, (int)ScaleX(DesignWidth / 2f), (int)ScaleY(58f));

        Rectangle panel = GetCenteredDesignRect(720f, 430f, 150f);
        using (var panelPath = CreateRoundedRect(panel, ScaleY(18f)))
        using (var panelFill = new SolidBrush(Color.FromArgb(190, 12, 19, 34)))
        using (var panelBorder = new Pen(Color.FromArgb(80, 120, 170, 230), Math.Max(1.2f, ScaleY(1.5f))))
        {
            g.FillPath(panelFill, panelPath);
            g.DrawPath(panelBorder, panelPath);
        }

        float progress = Math.Clamp(_calibrationOffsets.Count / (float)CalibrationSampleTarget, 0f, 1f);
        Rectangle ring = Rectangle.Round(new RectangleF(panel.Left + ScaleX(238f), panel.Top + ScaleY(46f), ScaleX(244f), ScaleY(244f)));
        using (var ringPen = new Pen(Color.FromArgb(45, 170, 190, 230), Math.Max(10f, ScaleY(12f))) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            g.DrawArc(ringPen, ring, -90f, 360f);
        using (var progressPen = new Pen(GetAccentColor(), Math.Max(10f, ScaleY(12f))) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            g.DrawArc(progressPen, ring, -90f, 360f * progress);

        int beatPulse = _calibrationStopwatch.IsRunning
            ? (int)(Math.Sin(_calibrationStopwatch.Elapsed.TotalSeconds * Math.PI * 2.0 / CalibrationBeatIntervalSeconds) * 26 + 70)
            : 45;
        using (var pulseBrush = new SolidBrush(Color.FromArgb(beatPulse, GetAccentColor())))
            g.FillEllipse(pulseBrush, Rectangle.Inflate(ring, -58, -58));

        DrawCentered(g, $"{_calibrationOffsets.Count}/{CalibrationSampleTarget}", valueFont, valueBrush, ring.Left + ring.Width / 2, ring.Top + (int)ScaleY(76f));
        DrawCentered(g, "SAMPLES", labelFont, labelBrush, ring.Left + ring.Width / 2, ring.Top + (int)ScaleY(150f));

        int lastMs = (int)MathF.Round(_lastCalibrationOffsetSeconds * 1000f);
        string direction = lastMs < -3 ? "EARLY" : lastMs > 3 ? "LATE" : "SYNC";
        string currentText = _calibrationSaved ? $"SAVED {_audioOffsetMs:+0;-0;0} ms" : $"{direction} {Math.Abs(lastMs)} ms";
        DrawCentered(g, currentText, valueFont, accentBrush, panel.Left + panel.Width / 2, panel.Top + (int)ScaleY(312f));

        string sub = _calibrationSaved
            ? "OFFSET UPDATED"
            : _calibrationStopwatch.IsRunning ? "MATCH THE TICK" : "READY";
        DrawCentered(g, sub, smallFont, labelBrush, panel.Left + panel.Width / 2, panel.Top + (int)ScaleY(374f));

        DrawCalibrationStartButton(g, GetCalibrationStartButtonBounds(), smallFont);
    }

    private Rectangle GetCalibrationBackButtonBounds()
    {
        return Rectangle.Round(new RectangleF(ScaleX(37f), ScaleY(27f), ScaleX(58f), ScaleY(58f)));
    }

    private Rectangle GetCalibrationStartButtonBounds()
    {
        return Rectangle.Round(new RectangleF(ScaleX(DesignWidth / 2f - 120f), ScaleY(620f), ScaleX(240f), ScaleY(58f)));
    }

    private void DrawCalibrationStartButton(Graphics g, Rectangle bounds, Font font)
    {
        Color accent = GetAccentColor();
        using var path = CreateRoundedRect(bounds, ScaleY(12f));
        using var fill = new LinearGradientBrush(bounds,
            _isCalibrationStartHovered ? Color.FromArgb(170, accent) : Color.FromArgb(125, accent),
            Color.FromArgb(82, accent),
            LinearGradientMode.Vertical);
        using var border = new Pen(_isCalibrationStartHovered ? Color.White : Color.FromArgb(150, accent), Math.Max(1.5f, ScaleY(2f)));
        using var textBrush = new SolidBrush(Color.White);
        g.FillPath(fill, path);
        g.DrawPath(border, path);
        DrawCentered(g, _calibrationSaved ? "RUN AGAIN" : "START", font, textBrush, bounds.Left + bounds.Width / 2, bounds.Top + (int)ScaleY(18f));
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
