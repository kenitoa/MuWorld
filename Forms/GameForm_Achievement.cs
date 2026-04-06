using System.Drawing.Drawing2D;

namespace RhythmGame;

public sealed partial class GameForm
{
    private void DrawAchievement(Graphics g)
    {
        DrawAchievementBackground(g);

        using var titleFont = new Font("Segoe UI", Math.Max(13f, ScaleY(31f)), FontStyle.Bold);
        using var cardTitleFont = new Font("Segoe UI", Math.Max(11f, ScaleY(18f)), FontStyle.Bold);
        using var titleBrush = new SolidBrush(GetAccentColor());
        using var cardTextBrush = new SolidBrush(AchCardText);
        using var dotPen = new Pen(AchDotPen, Math.Max(1.2f, ScaleY(1.5f)));

        string title = "ACHIEVEMENTS";
        DrawCentered(g, title, titleFont, titleBrush, (int)ScaleX(DesignWidth / 2f), (int)ScaleY(44f));

        for (int i = 0; i < 3; i++)
        {
            Rectangle card = GetAchievementCardBounds(i);
            bool hovered = i == _hoverAchievementCardIndex;
            DrawAchievementCard(g, card, i, hovered, cardTitleFont, cardTextBrush, dotPen);
        }

        DrawAchievementBackButton(g, GetAchievementBackButtonBounds(), _isAchievementBackHovered);
    }

    private void DrawAchievementBackground(Graphics g)
    {
        Rectangle layoutRect = new(0, 0, (int)ScaleX(DesignWidth), (int)ScaleY(DesignHeight));
        using var bgBrush = new LinearGradientBrush(layoutRect, BgColor1, BgColor2, LinearGradientMode.Vertical);
        g.FillRectangle(bgBrush, layoutRect);
    }

    private Rectangle GetAchievementCardBounds(int index)
    {
        float width = 610f;
        float height = 112f;
        float startY = 175f;
        float gap = 22f;
        float y = startY + index * (height + gap);
        return Rectangle.Round(new RectangleF(ScaleX((DesignWidth - width) / 2f), ScaleY(y), ScaleX(width), ScaleY(height)));
    }

    private Rectangle GetAchievementBackButtonBounds()
    {
        float width = 288f;
        float height = 76f;
        return Rectangle.Round(new RectangleF(ScaleX((DesignWidth - width) / 2f), ScaleY(620f), ScaleX(width), ScaleY(height)));
    }

    private void DrawAchievementCard(Graphics g, Rectangle bounds, int cardIndex, bool hovered, Font titleFont, Brush textBrush, Pen dotPen)
    {
        var shadow = bounds;
        shadow.Offset(0, (int)ScaleY(5f));
        using (var shadowPath = CreateRoundedRect(shadow, ScaleY(24f)))
        using (var shadowBrush = new SolidBrush(Color.FromArgb(22, 76, 101, 138)))
            g.FillPath(shadowBrush, shadowPath);

        Rectangle drawBounds = bounds;
        if (hovered)
            drawBounds.Offset(0, -(int)ScaleY(2f));

        using var cardPath = CreateRoundedRect(drawBounds, ScaleY(24f));
        using var fillBrush = new LinearGradientBrush(drawBounds, AchCardFill1, AchCardFill2, LinearGradientMode.Vertical);
        using var borderPen = new Pen(AchCardBorder, Math.Max(1.2f, ScaleY(1.5f)));
        g.FillPath(fillBrush, cardPath);
        g.DrawPath(borderPen, cardPath);

        Rectangle iconBounds = Rectangle.Round(new RectangleF(drawBounds.Left + ScaleX(24f), drawBounds.Top + ScaleY(16f), ScaleX(66f), ScaleY(66f)));
        switch (cardIndex)
        {
            case 0:
                DrawBronzeMedal(g, iconBounds);
                g.DrawString("BRONZE ACHIEVER", titleFont, textBrush, drawBounds.Left + ScaleX(130f), drawBounds.Top + ScaleY(26f));
                DrawAchievementDots(g, drawBounds.Left + ScaleX(130f), drawBounds.Top + ScaleY(74f), 4, dotPen);
                break;
            case 1:
                DrawSilverMedal(g, iconBounds);
                g.DrawString("SILVER ACHIEVER", titleFont, textBrush, drawBounds.Left + ScaleX(130f), drawBounds.Top + ScaleY(26f));
                DrawAchievementDots(g, drawBounds.Left + ScaleX(130f), drawBounds.Top + ScaleY(74f), 5, dotPen);
                break;
            default:
                DrawStarBadge(g, iconBounds);
                g.DrawString("STAR PLAYER", titleFont, textBrush, drawBounds.Left + ScaleX(130f), drawBounds.Top + ScaleY(26f));
                DrawAchievementDots(g, drawBounds.Left + ScaleX(130f), drawBounds.Top + ScaleY(74f), 5, dotPen);
                break;
        }

        DrawCardChevron(g, drawBounds);
    }

    private void DrawAchievementDots(Graphics g, float startX, float y, int count, Pen pen)
    {
        float size = ScaleX(12f);
        float gap = ScaleX(10f);
        for (int i = 0; i < count; i++)
        {
            g.DrawEllipse(pen, startX + i * (size + gap), y, size, size);
        }
    }

    private void DrawCardChevron(Graphics g, Rectangle cardBounds)
    {
        using var pen = new Pen(ChevronColor, Math.Max(2.6f, ScaleY(3.2f)))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };

        float cx = cardBounds.Right - ScaleX(34f);
        float cy = cardBounds.Top + cardBounds.Height / 2f;
        float w = ScaleX(13f);
        float h = ScaleY(18f);
        g.DrawLine(pen, cx - w / 2f, cy - h / 2f, cx + w / 2f, cy);
        g.DrawLine(pen, cx + w / 2f, cy, cx - w / 2f, cy + h / 2f);
    }

    private void DrawAchievementBackButton(Graphics g, Rectangle bounds, bool hovered)
    {
        Color accent = GetAccentColor();
        Rectangle shadow = bounds;
        shadow.Offset(0, (int)ScaleY(6f));
        using (var shadowPath = CreateRoundedRect(shadow, shadow.Height / 2f))
        using (var shadowBrush = new SolidBrush(Color.FromArgb(40, 60, 96, 144)))
            g.FillPath(shadowBrush, shadowPath);

        Rectangle drawBounds = bounds;
        if (hovered)
            drawBounds.Offset(0, -(int)ScaleY(2f));

        using var path = CreateRoundedRect(drawBounds, drawBounds.Height / 2f);
        using var fillBrush = new LinearGradientBrush(drawBounds, Color.FromArgb(140, accent), Color.FromArgb(98, accent), LinearGradientMode.Vertical);
        using var borderPen = new Pen(Color.FromArgb(128, accent), Math.Max(2f, ScaleY(2.5f)));
        using var innerPen = new Pen(Color.FromArgb(170, 196, 224, 255), Math.Max(1.5f, ScaleY(1.8f)));
        using var textBrush = new SolidBrush(Color.White);

        g.FillPath(fillBrush, path);
        g.DrawPath(borderPen, path);
        using (var inner = CreateRoundedRect(new Rectangle(drawBounds.X + 4, drawBounds.Y + 4, drawBounds.Width - 8, drawBounds.Height - 8), (drawBounds.Height - 8) / 2f))
            g.DrawPath(innerPen, inner);

        using var font = new Font("Segoe UI", Math.Max(12f, ScaleY(24f)), FontStyle.Bold);
        DrawCentered(g, "BACK", font, textBrush, drawBounds.Left + drawBounds.Width / 2, drawBounds.Top + (int)ScaleY(16f));
    }

    private bool IsAchievementBackButtonHit(Point location)
    {
        return GetAchievementBackButtonBounds().Contains(location);
    }

    private int GetHoveredAchievementCardIndex(Point location)
    {
        for (int i = 0; i < 3; i++)
            if (GetAchievementCardBounds(i).Contains(location))
                return i;

        return -1;
    }

    private void HandleAchievementMouseDown(Point location)
    {
        if (IsAchievementBackButtonHit(location))
        {
            _screen = UiScreen.MainMenu;
            Invalidate();
            return;
        }

        int card = GetHoveredAchievementCardIndex(location);
        if (card >= 0)
        {
            _selectedAchievementCardIndex = card;
            _achievementDetailTabIndex = 0;
            _achievementDetailPageIndex = 0;
            _hoverAchievementDetailTabIndex = -1;
            _hoverAchievementDetailPageArrow = -1;
            _isAchievementDetailBackHovered = false;
            _screen = UiScreen.AchievementDetail;
            Invalidate();
        }
    }

    private void DrawBronzeMedal(Graphics g, Rectangle bounds)
    {
        using var ribbonBrush = new SolidBrush(Color.FromArgb(128, 142, 172));
        g.FillPolygon(ribbonBrush,
        [
            new PointF(bounds.Left + ScaleX(8f), bounds.Top + ScaleY(2f)),
            new PointF(bounds.Left + ScaleX(25f), bounds.Top + ScaleY(2f)),
            new PointF(bounds.Left + ScaleX(32f), bounds.Top + ScaleY(18f)),
            new PointF(bounds.Left + ScaleX(15f), bounds.Top + ScaleY(18f)),
        ]);
        g.FillPolygon(ribbonBrush,
        [
            new PointF(bounds.Left + ScaleX(42f), bounds.Top + ScaleY(2f)),
            new PointF(bounds.Left + ScaleX(59f), bounds.Top + ScaleY(2f)),
            new PointF(bounds.Left + ScaleX(52f), bounds.Top + ScaleY(18f)),
            new PointF(bounds.Left + ScaleX(35f), bounds.Top + ScaleY(18f)),
        ]);

        using var medalBrush = new LinearGradientBrush(bounds, Color.FromArgb(225, 236, 246), Color.FromArgb(168, 192, 215), LinearGradientMode.Vertical);
        using var borderPen = new Pen(Color.FromArgb(156, 178, 203), Math.Max(1.3f, ScaleY(1.6f)));
        g.FillEllipse(medalBrush, bounds);
        g.DrawEllipse(borderPen, bounds);

        Rectangle inner = Rectangle.Inflate(bounds, -(int)ScaleX(10f), -(int)ScaleY(10f));
        using var innerPen = new Pen(Color.FromArgb(180, 198, 216), Math.Max(1.1f, ScaleY(1.2f)));
        g.DrawEllipse(innerPen, inner);
        using var font = new Font("Segoe UI", Math.Max(10f, ScaleY(20f)), FontStyle.Bold);
        using var textBrush = new SolidBrush(Color.FromArgb(151, 170, 195));
        DrawCentered(g, "C", font, textBrush, bounds.Left + bounds.Width / 2, bounds.Top + (int)ScaleY(17f));
    }

    private void DrawSilverMedal(Graphics g, Rectangle bounds)
    {
        using var ribbonBrush = new SolidBrush(Color.FromArgb(92, 120, 200));
        g.FillPolygon(ribbonBrush,
        [
            new PointF(bounds.Left + ScaleX(8f), bounds.Top + ScaleY(2f)),
            new PointF(bounds.Left + ScaleX(25f), bounds.Top + ScaleY(2f)),
            new PointF(bounds.Left + ScaleX(32f), bounds.Top + ScaleY(18f)),
            new PointF(bounds.Left + ScaleX(15f), bounds.Top + ScaleY(18f)),
        ]);
        g.FillPolygon(ribbonBrush,
        [
            new PointF(bounds.Left + ScaleX(42f), bounds.Top + ScaleY(2f)),
            new PointF(bounds.Left + ScaleX(59f), bounds.Top + ScaleY(2f)),
            new PointF(bounds.Left + ScaleX(52f), bounds.Top + ScaleY(18f)),
            new PointF(bounds.Left + ScaleX(35f), bounds.Top + ScaleY(18f)),
        ]);

        using var medalBrush = new LinearGradientBrush(bounds, Color.FromArgb(251, 225, 140), Color.FromArgb(220, 178, 66), LinearGradientMode.Vertical);
        using var borderPen = new Pen(Color.FromArgb(201, 160, 66), Math.Max(1.3f, ScaleY(1.6f)));
        g.FillEllipse(medalBrush, bounds);
        g.DrawEllipse(borderPen, bounds);

        Rectangle inner = Rectangle.Inflate(bounds, -(int)ScaleX(10f), -(int)ScaleY(10f));
        using var innerPen = new Pen(Color.FromArgb(230, 196, 96), Math.Max(1.1f, ScaleY(1.2f)));
        g.DrawEllipse(innerPen, inner);
        using var font = new Font("Segoe UI", Math.Max(10f, ScaleY(20f)), FontStyle.Bold);
        using var textBrush = new SolidBrush(Color.FromArgb(225, 197, 118));
        DrawCentered(g, "B", font, textBrush, bounds.Left + bounds.Width / 2, bounds.Top + (int)ScaleY(17f));
    }

    private void DrawStarBadge(Graphics g, Rectangle bounds)
    {
        using var fill = new LinearGradientBrush(bounds, Color.FromArgb(188, 168, 241), Color.FromArgb(114, 95, 214), LinearGradientMode.Vertical);
        using var borderPen = new Pen(Color.FromArgb(140, 127, 220), Math.Max(1.2f, ScaleY(1.5f)));
        g.FillEllipse(fill, bounds);
        g.DrawEllipse(borderPen, bounds);

        PointF[] star =
        [
            new(bounds.Left + ScaleX(33f), bounds.Top + ScaleY(13f)),
            new(bounds.Left + ScaleX(40f), bounds.Top + ScaleY(26f)),
            new(bounds.Left + ScaleX(55f), bounds.Top + ScaleY(28f)),
            new(bounds.Left + ScaleX(44f), bounds.Top + ScaleY(39f)),
            new(bounds.Left + ScaleX(47f), bounds.Top + ScaleY(54f)),
            new(bounds.Left + ScaleX(33f), bounds.Top + ScaleY(46f)),
            new(bounds.Left + ScaleX(19f), bounds.Top + ScaleY(54f)),
            new(bounds.Left + ScaleX(22f), bounds.Top + ScaleY(39f)),
            new(bounds.Left + ScaleX(11f), bounds.Top + ScaleY(28f)),
            new(bounds.Left + ScaleX(26f), bounds.Top + ScaleY(26f)),
        ];
        using var starBrush = new SolidBrush(Color.FromArgb(245, 245, 255));
        g.FillPolygon(starBrush, star);
    }
}
