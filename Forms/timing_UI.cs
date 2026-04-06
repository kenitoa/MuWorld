namespace RhythmGame;

public sealed partial class GameForm
{
    private float _speedMultiplier = 1.0f;
    private const float SpeedMin = 0.5f;
    private const float SpeedMax = 5.0f;
    private const float SpeedStep = 0.1f;

    private void IncreaseSpeed()
    {
        _speedMultiplier = MathF.Min(SpeedMax, MathF.Round((_speedMultiplier + SpeedStep) * 10f) / 10f);
        ApplySpeedToEngine();
    }

    private void DecreaseSpeed()
    {
        _speedMultiplier = MathF.Max(SpeedMin, MathF.Round((_speedMultiplier - SpeedStep) * 10f) / 10f);
        ApplySpeedToEngine();
    }

    private void ApplySpeedToEngine()
    {
        _engine.NoteSpeedMultiplier = _speedMultiplier;
    }

    private static readonly SolidBrush _indicatorBgBrush = new(Color.FromArgb(180, 20, 22, 35));
    private static readonly Pen _indicatorBorderPen = new(Color.FromArgb(120, 180, 190, 220), 1.5f);
    private static readonly Font _speedFont = new("Segoe UI", 13, FontStyle.Bold);
    private static readonly SolidBrush _indicatorTextBrush = new(Color.FromArgb(240, 255, 255, 255));

    private void DrawSpeedIndicator(Graphics g, Rectangle playArea)
    {
        float boxW = 80f * _layoutScale;
        float boxH = 36f * _layoutScale;
        float margin = 10f * _layoutScale;
        float x = playArea.Left - boxW - margin;
        float y = playArea.Bottom - boxH - 50f * _layoutScale;

        Rectangle bounds = new((int)x, (int)y, (int)boxW, (int)boxH);

        using var path = CreateRoundedRect(bounds, 6f);
        g.FillPath(_indicatorBgBrush, path);
        g.DrawPath(_indicatorBorderPen, path);

        string text = $"X{_speedMultiplier:F1}";
        SizeF textSize = g.MeasureString(text, _speedFont);
        g.DrawString(text, _speedFont, _indicatorTextBrush,
            bounds.Left + (bounds.Width - textSize.Width) / 2f,
            bounds.Top + (bounds.Height - textSize.Height) / 2f);
    }
}
