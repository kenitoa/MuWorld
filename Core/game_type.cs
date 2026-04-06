namespace RhythmGame;

public enum GameMode
{
    Normal,   // 일반
    Blind,    // 블라인드
    Fog,      // 안개
}

public sealed partial class GameForm
{
    private GameMode _gameMode = GameMode.Normal;

    private static readonly string[] GameModeLabels = ["일반", "블라인드", "안개"];

    private void CycleGameModeForward()
    {
        _gameMode = _gameMode switch
        {
            GameMode.Normal => GameMode.Blind,
            GameMode.Blind => GameMode.Fog,
            GameMode.Fog => GameMode.Normal,
            _ => GameMode.Normal,
        };
    }

    private void CycleGameModeBackward()
    {
        _gameMode = _gameMode switch
        {
            GameMode.Normal => GameMode.Fog,
            GameMode.Fog => GameMode.Blind,
            GameMode.Blind => GameMode.Normal,
            _ => GameMode.Normal,
        };
    }

    private string GetGameModeLabel() => GameModeLabels[(int)_gameMode];

    private static readonly Font _gameModeFont = new("Segoe UI", 11, FontStyle.Bold);

    private void DrawGameModeIndicator(Graphics g, Rectangle playArea)
    {
        float boxW = 80f * _layoutScale;
        float boxH = 36f * _layoutScale;
        float margin = 10f * _layoutScale;
        float x = playArea.Left - boxW - margin;
        float y = playArea.Bottom - boxH - 10f * _layoutScale;

        Rectangle bounds = new((int)x, (int)y, (int)boxW, (int)boxH);

        using var path = CreateRoundedRect(bounds, 6f);
        g.FillPath(_indicatorBgBrush, path);
        g.DrawPath(_indicatorBorderPen, path);

        string text = GetGameModeLabel();
        SizeF textSize = g.MeasureString(text, _gameModeFont);
        g.DrawString(text, _gameModeFont, _indicatorTextBrush,
            bounds.Left + (bounds.Width - textSize.Width) / 2f,
            bounds.Top + (bounds.Height - textSize.Height) / 2f);
    }

    private void ApplyGameModeEffect(Graphics g, Rectangle playArea, int hitY)
    {
        switch (_gameMode)
        {
            case GameMode.Blind:
                // 블라인드: 노트가 히트존 근처에서만 보이도록 상단을 가림
                int blindCover = hitY - 120;
                if (blindCover > 0)
                {
                    g.FillRectangle(_blindBrush, playArea.Left, playArea.Top, playArea.Width, blindCover);
                }
                break;

            case GameMode.Fog:
                // 안개: 반투명 안개 효과로 가시성 저하
                g.FillRectangle(_fogBrush1, playArea.Left, playArea.Top, playArea.Width, playArea.Height);

                // 히트존 주변만 살짝 클리어
                int clearZone = 160;
                Rectangle clearRect = new(playArea.Left, hitY - clearZone, playArea.Width, clearZone + 40);
                using (var clearBrush = new System.Drawing.Drawing2D.LinearGradientBrush(
                    clearRect,
                    Color.FromArgb(0, 0, 0, 0),
                    Color.FromArgb(140, 15, 18, 30),
                    System.Drawing.Drawing2D.LinearGradientMode.Vertical))
                    g.FillRectangle(clearBrush, clearRect);
                break;
        }
    }
}
