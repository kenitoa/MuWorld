using System.Drawing.Drawing2D;

namespace RhythmGame;

public sealed partial class GameForm
{
    private const int AchievementDetailVisibleRows = 5;

    private void DrawAchievementDetail(Graphics g)
    {
        DrawAchievementBackground(g);

        using var titleFont = new Font("Segoe UI", Math.Max(13f, ScaleY(31f)), FontStyle.Bold);
        using var tabFont = new Font("Malgun Gothic", Math.Max(10.5f, ScaleY(15f)), FontStyle.Bold);
        using var titleBrush = new SolidBrush(GetAccentColor());

        DrawCentered(g, "ACHIEVEMENTS", titleFont, titleBrush, (int)ScaleX(DesignWidth / 2f), (int)ScaleY(44f));
        DrawAchievementDetailTabs(g, GetAchievementDetailTabContainerBounds(), tabFont);
        DrawAchievementDetailPanel(g, GetAchievementDetailPanelBounds());
        DrawAchievementBackButton(g, GetAchievementBackButtonBounds(), _isAchievementDetailBackHovered);
    }

    private Rectangle GetAchievementDetailTabContainerBounds()
    {
        return Rectangle.Round(new RectangleF(ScaleX(347f), ScaleY(121f), ScaleX(462f), ScaleY(60f)));
    }

    private Rectangle GetAchievementDetailTabBounds(int index)
    {
        Rectangle container = GetAchievementDetailTabContainerBounds();
        int halfWidth = container.Width / 2;
        return index == 0
            ? new Rectangle(container.Left, container.Top, halfWidth, container.Height)
            : new Rectangle(container.Left + halfWidth, container.Top, container.Width - halfWidth, container.Height);
    }

    private Rectangle GetAchievementDetailPanelBounds()
    {
        return Rectangle.Round(new RectangleF(ScaleX(250f), ScaleY(192f), ScaleX(652f), ScaleY(392f)));
    }

    private void DrawAchievementDetailTabs(Graphics g, Rectangle bounds, Font font)
    {
        Rectangle shadowBounds = bounds;
        shadowBounds.Offset(0, (int)ScaleY(6f));
        using (var shadowPath = CreateRoundedRect(shadowBounds, shadowBounds.Height / 2f))
        using (var shadowBrush = new SolidBrush(Color.FromArgb(26, 58, 93, 145)))
            g.FillPath(shadowBrush, shadowPath);

        using var outerPath = CreateRoundedRect(bounds, bounds.Height / 2f);
        using var outerBrush = new LinearGradientBrush(bounds, AchDetailTabFill1, AchDetailTabFill2, LinearGradientMode.Vertical);
        using var outerPen = new Pen(AchDetailTabBorder, Math.Max(1.2f, ScaleY(1.4f)));
        g.FillPath(outerBrush, outerPath);
        g.DrawPath(outerPen, outerPath);

        for (int i = 0; i < 2; i++)
        {
            Rectangle tabBounds = GetAchievementDetailTabBounds(i);
            bool selected = _achievementDetailTabIndex == i;
            bool hovered = _hoverAchievementDetailTabIndex == i;
            DrawAchievementDetailTab(g, tabBounds, i == 0 ? "전체 업적" : "완료한 업적", selected, hovered, font, i == 0, i == 1);
        }
    }

    private void DrawAchievementDetailTab(Graphics g, Rectangle bounds, string label, bool selected, bool hovered, Font font, bool roundLeft, bool roundRight)
    {
        Rectangle drawBounds = bounds;
        if (selected)
            drawBounds = Rectangle.Inflate(drawBounds, 0, -(int)ScaleY(1f));

        using var path = CreateRoundedSegmentPath(drawBounds, drawBounds.Height / 2f, roundLeft, roundRight);
        using var fillBrush = new LinearGradientBrush(
            drawBounds,
            selected ? AchDetailSelectedFill : hovered ? AchDetailTabFill1 : AchDetailTabFill2,
            selected ? AchDetailSelectedFill2 : hovered ? AchDetailTabFill2 : AchDetailTabFill2,
            LinearGradientMode.Vertical);
        using var borderPen = new Pen(
            selected ? AchDetailSelectedBorder : Color.FromArgb(0, 0, 0, 0),
            Math.Max(1f, ScaleY(1.2f)));
        using var textBrush = new SolidBrush(selected ? AchDetailSelectedText : AchDetailUnselectedText);

        if (selected)
        {
            Rectangle shadowBounds = drawBounds;
            shadowBounds.Offset(0, (int)ScaleY(3f));
            using var shadowPath = CreateRoundedSegmentPath(shadowBounds, shadowBounds.Height / 2f, roundLeft, roundRight);
            using var shadowBrush = new SolidBrush(Color.FromArgb(18, 54, 85, 132));
            g.FillPath(shadowBrush, shadowPath);
        }

        g.FillPath(fillBrush, path);
        if (selected)
            g.DrawPath(borderPen, path);

        DrawCentered(g, label, font, textBrush, drawBounds.Left + drawBounds.Width / 2, drawBounds.Top + (int)ScaleY(14f));
    }

    private void DrawAchievementDetailPanel(Graphics g, Rectangle bounds)
    {
        Rectangle shadowBounds = bounds;
        shadowBounds.Offset(0, (int)ScaleY(7f));
        using (var shadowPath = CreateRoundedRect(shadowBounds, ScaleY(24f)))
        using (var shadowBrush = new SolidBrush(Color.FromArgb(22, 60, 91, 140)))
            g.FillPath(shadowBrush, shadowPath);

        using var panelPath = CreateRoundedRect(bounds, ScaleY(24f));
        using var panelBrush = new LinearGradientBrush(bounds, _darkModeEnabled ? Color.FromArgb(30, 34, 44) : Color.FromArgb(251, 252, 254), _darkModeEnabled ? Color.FromArgb(24, 28, 36) : Color.FromArgb(236, 243, 252), LinearGradientMode.Vertical);
        using var panelPen = new Pen(_darkModeEnabled ? Color.FromArgb(48, 55, 68) : Color.FromArgb(206, 217, 232), Math.Max(1.2f, ScaleY(1.5f)));
        g.FillPath(panelBrush, panelPath);
        g.DrawPath(panelPen, panelPath);

        DrawAchievementDetailContent(g, bounds);
    }

    private void DrawAchievementDetailContent(Graphics g, Rectangle bounds)
    {
        List<AchievementDefinition> achievements = AchievementCatalog.Definitions
            .Where(definition => definition.Tier == _selectedAchievementCardIndex)
            .Where(definition => _achievementDetailTabIndex == 0 ? !_playerProgress.IsUnlocked(definition.Id) : _playerProgress.IsUnlocked(definition.Id))
            .ToList();

        int totalPages = Math.Max(1, (achievements.Count + AchievementDetailVisibleRows - 1) / AchievementDetailVisibleRows);
        _achievementDetailPageIndex = Math.Clamp(_achievementDetailPageIndex, 0, totalPages - 1);

        Rectangle innerBounds = Rectangle.Inflate(bounds, -(int)ScaleX(24f), -(int)ScaleY(22f));

        using var summaryFont = new Font("Malgun Gothic", Math.Max(6.8f, ScaleY(8.8f)), FontStyle.Bold);
        using var titleFont = new Font("Segoe UI", Math.Max(8.2f, ScaleY(12f)), FontStyle.Bold);
        using var bodyFont = new Font("Malgun Gothic", Math.Max(6.6f, ScaleY(9.2f)), FontStyle.Regular);
        using var conditionFont = new Font("Malgun Gothic", Math.Max(6.4f, ScaleY(8.8f)), FontStyle.Bold);
        using var summaryBrush = new SolidBrush(Color.FromArgb(112, 132, 162));
        using var emptyFont = new Font("Malgun Gothic", Math.Max(9f, ScaleY(14f)), FontStyle.Bold);
        using var emptyBrush = new SolidBrush(Color.FromArgb(148, 167, 194));

        var tierAll = AchievementCatalog.Definitions.Where(d => d.Tier == _selectedAchievementCardIndex).ToList();
        int tierUnlocked = tierAll.Count(d => _playerProgress.IsUnlocked(d.Id));
        string summary = $"해제 {tierUnlocked}/{tierAll.Count}";
        g.DrawString(summary, summaryFont, summaryBrush, innerBounds.Left, innerBounds.Top);

        // Page indicator - centered between arrows
        using var pageFont = new Font("Malgun Gothic", Math.Max(6.8f, ScaleY(8.8f)), FontStyle.Bold);
        string pageText = $"{_achievementDetailPageIndex + 1} / {totalPages}";
        Rectangle leftArrowArea = GetAchievementPageArrowBounds(innerBounds, 0);
        Rectangle rightArrowArea = GetAchievementPageArrowBounds(innerBounds, 1);
        float pageCenterX = (leftArrowArea.Right + rightArrowArea.Left) / 2f;
        SizeF pageSize = g.MeasureString(pageText, pageFont);
        g.DrawString(pageText, pageFont, summaryBrush, pageCenterX - pageSize.Width / 2f, innerBounds.Top);

        // Page arrows
        if (totalPages > 1)
        {
            DrawAchievementPageArrows(g, innerBounds, totalPages);
        }

        if (achievements.Count == 0)
        {
            DrawCentered(g, _achievementDetailTabIndex == 0 ? "미달성 업적이 없습니다." : "아직 완료한 업적이 없습니다.", emptyFont, emptyBrush, innerBounds.Left + innerBounds.Width / 2, innerBounds.Top + (int)ScaleY(122f));
            DrawCentered(g, "플레이 기록을 남기면 조건 달성 시 자동으로 해제됩니다.", bodyFont, emptyBrush, innerBounds.Left + innerBounds.Width / 2, innerBounds.Top + (int)ScaleY(154f));
            return;
        }

        int startIndex = _achievementDetailPageIndex * AchievementDetailVisibleRows;
        int count = Math.Min(achievements.Count - startIndex, AchievementDetailVisibleRows);

        float top = innerBounds.Top + ScaleY(36f);
        float rowHeight = ScaleY(62f);
        for (int i = 0; i < count; i++)
        {
            Rectangle rowBounds = Rectangle.Round(new RectangleF(innerBounds.Left, top + i * rowHeight, innerBounds.Width, rowHeight));
            DrawAchievementDetailRow(g, rowBounds, achievements[startIndex + i], titleFont, bodyFont, conditionFont);
        }
    }

    private void DrawAchievementPageArrows(Graphics g, Rectangle innerBounds, int totalPages)
    {
        using var arrowFont = new Font("Segoe UI", Math.Max(9f, ScaleY(13f)), FontStyle.Bold);

        // Left arrow
        Rectangle leftArrow = GetAchievementPageArrowBounds(innerBounds, 0);
        bool leftEnabled = _achievementDetailPageIndex > 0;
        bool leftHovered = _hoverAchievementDetailPageArrow == 0 && leftEnabled;
        using var leftBrush = new SolidBrush(leftEnabled
            ? (leftHovered ? Color.FromArgb(63, 104, 164) : Color.FromArgb(112, 132, 162))
            : Color.FromArgb(195, 210, 228));
        DrawCentered(g, "<", arrowFont, leftBrush, leftArrow.Left + leftArrow.Width / 2, leftArrow.Top + (int)ScaleY(1f));

        // Right arrow
        Rectangle rightArrow = GetAchievementPageArrowBounds(innerBounds, 1);
        bool rightEnabled = _achievementDetailPageIndex < totalPages - 1;
        bool rightHovered = _hoverAchievementDetailPageArrow == 1 && rightEnabled;
        using var rightBrush = new SolidBrush(rightEnabled
            ? (rightHovered ? Color.FromArgb(63, 104, 164) : Color.FromArgb(112, 132, 162))
            : Color.FromArgb(195, 210, 228));
        DrawCentered(g, ">", arrowFont, rightBrush, rightArrow.Left + rightArrow.Width / 2, rightArrow.Top + (int)ScaleY(1f));
    }

    private Rectangle GetAchievementPageArrowBounds(Rectangle innerBounds, int arrowIndex)
    {
        int arrowW = (int)ScaleX(28f);
        int arrowH = (int)ScaleY(20f);
        int y = innerBounds.Top - (int)ScaleY(2f);
        if (arrowIndex == 0)
            return new Rectangle(innerBounds.Right - (int)ScaleX(100f), y, arrowW, arrowH);
        else
            return new Rectangle(innerBounds.Right - (int)ScaleX(28f), y, arrowW, arrowH);
    }

    private void DrawAchievementDetailRow(Graphics g, Rectangle bounds, AchievementDefinition achievement, Font titleFont, Font bodyFont, Font conditionFont)
    {
        bool unlocked = _playerProgress.IsUnlocked(achievement.Id);

        Rectangle rowBounds = Rectangle.Inflate(bounds, 0, -(int)ScaleY(2f));
        using var fillBrush = new LinearGradientBrush(
            rowBounds,
            unlocked ? Color.FromArgb(255, 255, 255) : Color.FromArgb(245, 249, 254),
            unlocked ? Color.FromArgb(239, 246, 253) : Color.FromArgb(233, 240, 250),
            LinearGradientMode.Vertical);
        using var borderPen = new Pen(
            unlocked ? Color.FromArgb(189, 212, 231) : Color.FromArgb(209, 221, 236),
            Math.Max(1f, ScaleY(1.2f)));
        using var rowPath = CreateRoundedRect(rowBounds, ScaleY(16f));
        using var titleBrush = new SolidBrush(unlocked ? Color.FromArgb(63, 104, 164) : Color.FromArgb(119, 141, 174));
        using var bodyBrush = new SolidBrush(Color.FromArgb(128, 146, 175));
        using var progressBrush = new SolidBrush(unlocked ? Color.FromArgb(63, 125, 98) : Color.FromArgb(136, 151, 174));

        g.FillPath(fillBrush, rowPath);
        g.DrawPath(borderPen, rowPath);

        Rectangle iconBounds = Rectangle.Round(new RectangleF(rowBounds.Left + ScaleX(14f), rowBounds.Top + ScaleY(13f), ScaleX(32f), ScaleY(32f)));
        DrawAchievementBadge(g, iconBounds, achievement, unlocked);

        float textLeft = rowBounds.Left + ScaleX(60f);
        g.DrawString(achievement.Title, titleFont, titleBrush, textLeft, rowBounds.Top + ScaleY(8f));
        g.DrawString(achievement.Description, bodyFont, bodyBrush, textLeft, rowBounds.Top + ScaleY(31f));

        string conditionText = unlocked
            ? "해제 완료"
            : $"진행도 {AchievementCatalog.GetProgressText(achievement, _playerProgress)} · {achievement.ConditionText}";
        SizeF conditionSize = g.MeasureString(conditionText, conditionFont);
        g.DrawString(conditionText, conditionFont, progressBrush, rowBounds.Right - conditionSize.Width - ScaleX(14f), rowBounds.Top + ScaleY(12f));
    }

    private void DrawAchievementBadge(Graphics g, Rectangle bounds, AchievementDefinition achievement, bool unlocked, float opacity = 1f)
    {
        (Color topColor, Color bottomColor) = GetAchievementBadgeColors(achievement.Id, unlocked, opacity);
        using var fillBrush = new LinearGradientBrush(
            bounds,
            topColor,
            bottomColor,
            LinearGradientMode.Vertical);
        using var borderPen = new Pen(
            unlocked ? ApplyOpacity(Color.FromArgb(91, 141, 188), opacity) : ApplyOpacity(Color.FromArgb(155, 171, 192), opacity),
            Math.Max(1f, ScaleY(1.2f)));
        g.FillEllipse(fillBrush, bounds);
        g.DrawEllipse(borderPen, bounds);

        using var pen = new Pen(ApplyOpacity(Color.White, opacity), Math.Max(2.1f, ScaleY(2.4f)))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };
        DrawAchievementGlyph(g, achievement.Id, bounds, pen, ApplyOpacity(Color.White, opacity));

        if (!unlocked)
            DrawAchievementLockOverlay(g, bounds, opacity);
    }

    private (Color topColor, Color bottomColor) GetAchievementBadgeColors(string achievementId, bool unlocked, float opacity)
    {
        if (!unlocked)
        {
            return (
                ApplyOpacity(Color.FromArgb(224, 231, 241), opacity),
                ApplyOpacity(Color.FromArgb(169, 183, 203), opacity));
        }

        return achievementId switch
        {
            "first_stage" => (ApplyOpacity(Color.FromArgb(186, 223, 255), opacity), ApplyOpacity(Color.FromArgb(82, 152, 223), opacity)),
            "combo_apprentice" => (ApplyOpacity(Color.FromArgb(255, 225, 183), opacity), ApplyOpacity(Color.FromArgb(235, 154, 63), opacity)),
            "score_chaser" => (ApplyOpacity(Color.FromArgb(194, 235, 209), opacity), ApplyOpacity(Color.FromArgb(77, 180, 124), opacity)),
            "perfect_collector" => (ApplyOpacity(Color.FromArgb(225, 213, 255), opacity), ApplyOpacity(Color.FromArgb(146, 114, 226), opacity)),
            "clean_finish" => (ApplyOpacity(Color.FromArgb(205, 234, 249), opacity), ApplyOpacity(Color.FromArgb(79, 165, 198), opacity)),
            _ => (ApplyOpacity(Color.FromArgb(160, 206, 237), opacity), ApplyOpacity(Color.FromArgb(89, 148, 204), opacity)),
        };
    }

    private void DrawAchievementGlyph(Graphics g, string achievementId, Rectangle bounds, Pen pen, Color fillColor)
    {
        using var fillBrush = new SolidBrush(fillColor);
        switch (achievementId)
        {
            case "first_stage":
                g.DrawLine(pen, bounds.Left + ScaleX(11f), bounds.Top + ScaleY(8f), bounds.Left + ScaleX(11f), bounds.Top + ScaleY(24f));
                PointF[] flag =
                [
                    new(bounds.Left + ScaleX(12f), bounds.Top + ScaleY(9f)),
                    new(bounds.Left + ScaleX(23f), bounds.Top + ScaleY(12f)),
                    new(bounds.Left + ScaleX(16f), bounds.Top + ScaleY(18f)),
                    new(bounds.Left + ScaleX(23f), bounds.Top + ScaleY(24f)),
                    new(bounds.Left + ScaleX(12f), bounds.Top + ScaleY(21f)),
                ];
                g.FillPolygon(fillBrush, flag);
                break;
            case "combo_apprentice":
                g.DrawArc(pen, bounds.Left + ScaleX(7f), bounds.Top + ScaleY(10f), ScaleX(11f), ScaleY(11f), 310f, 300f);
                g.DrawArc(pen, bounds.Left + ScaleX(15f), bounds.Top + ScaleY(10f), ScaleX(11f), ScaleY(11f), 130f, 300f);
                break;
            case "score_chaser":
                g.DrawEllipse(pen, bounds.Left + ScaleX(9f), bounds.Top + ScaleY(9f), ScaleX(14f), ScaleY(14f));
                g.DrawLine(pen, bounds.Left + ScaleX(16f), bounds.Top + ScaleY(6f), bounds.Left + ScaleX(16f), bounds.Top + ScaleY(12f));
                g.DrawLine(pen, bounds.Left + ScaleX(16f), bounds.Top + ScaleY(20f), bounds.Left + ScaleX(16f), bounds.Top + ScaleY(26f));
                g.DrawLine(pen, bounds.Left + ScaleX(6f), bounds.Top + ScaleY(16f), bounds.Left + ScaleX(12f), bounds.Top + ScaleY(16f));
                g.DrawLine(pen, bounds.Left + ScaleX(20f), bounds.Top + ScaleY(16f), bounds.Left + ScaleX(26f), bounds.Top + ScaleY(16f));
                g.FillEllipse(fillBrush, bounds.Left + ScaleX(14f), bounds.Top + ScaleY(14f), ScaleX(4f), ScaleY(4f));
                break;
            case "perfect_collector":
                PointF[] star =
                [
                    new(bounds.Left + ScaleX(16f), bounds.Top + ScaleY(7f)),
                    new(bounds.Left + ScaleX(19.5f), bounds.Top + ScaleY(13f)),
                    new(bounds.Left + ScaleX(26f), bounds.Top + ScaleY(14f)),
                    new(bounds.Left + ScaleX(21f), bounds.Top + ScaleY(18.5f)),
                    new(bounds.Left + ScaleX(22.5f), bounds.Top + ScaleY(25f)),
                    new(bounds.Left + ScaleX(16f), bounds.Top + ScaleY(21.5f)),
                    new(bounds.Left + ScaleX(9.5f), bounds.Top + ScaleY(25f)),
                    new(bounds.Left + ScaleX(11f), bounds.Top + ScaleY(18.5f)),
                    new(bounds.Left + ScaleX(6f), bounds.Top + ScaleY(14f)),
                    new(bounds.Left + ScaleX(12.5f), bounds.Top + ScaleY(13f)),
                ];
                g.FillPolygon(fillBrush, star);
                break;
            case "clean_finish":
                PointF[] shield =
                [
                    new(bounds.Left + ScaleX(16f), bounds.Top + ScaleY(7f)),
                    new(bounds.Left + ScaleX(24f), bounds.Top + ScaleY(10f)),
                    new(bounds.Left + ScaleX(22f), bounds.Top + ScaleY(21f)),
                    new(bounds.Left + ScaleX(16f), bounds.Top + ScaleY(25f)),
                    new(bounds.Left + ScaleX(10f), bounds.Top + ScaleY(21f)),
                    new(bounds.Left + ScaleX(8f), bounds.Top + ScaleY(10f)),
                ];
                g.DrawPolygon(pen, shield);
                g.DrawLine(pen, bounds.Left + ScaleX(12f), bounds.Top + ScaleY(16f), bounds.Left + ScaleX(15f), bounds.Top + ScaleY(20f));
                g.DrawLine(pen, bounds.Left + ScaleX(15f), bounds.Top + ScaleY(20f), bounds.Left + ScaleX(21f), bounds.Top + ScaleY(12f));
                break;
            default:
                g.DrawLine(pen, bounds.Left + ScaleX(11f), bounds.Top + ScaleY(15f), bounds.Left + ScaleX(15f), bounds.Top + ScaleY(20f));
                g.DrawLine(pen, bounds.Left + ScaleX(15f), bounds.Top + ScaleY(20f), bounds.Left + ScaleX(22f), bounds.Top + ScaleY(11f));
                break;
        }
    }

    private void DrawAchievementLockOverlay(Graphics g, Rectangle bounds, float opacity)
    {
        Rectangle lockBounds = new(bounds.Right - (int)ScaleX(12f), bounds.Bottom - (int)ScaleY(12f), (int)ScaleX(14f), (int)ScaleY(14f));
        using var bgBrush = new SolidBrush(Color.FromArgb((int)(235 * opacity), 245, 248, 252));
        using var borderPen = new Pen(Color.FromArgb((int)(180 * opacity), 158, 171, 190), Math.Max(1f, ScaleY(1f)));
        using var pen = new Pen(Color.FromArgb((int)(200 * opacity), 158, 171, 190), Math.Max(1f, ScaleY(1.1f)));
        g.FillEllipse(bgBrush, lockBounds);
        g.DrawEllipse(borderPen, lockBounds);

        g.DrawArc(pen, lockBounds.Left + ScaleX(3f), lockBounds.Top + ScaleY(2f), ScaleX(8f), ScaleY(7f), 200f, 140f);
        g.DrawRectangle(pen, lockBounds.Left + ScaleX(3.5f), lockBounds.Top + ScaleY(6.5f), ScaleX(7f), ScaleY(4.5f));
    }

    private static Color ApplyOpacity(Color color, float opacity)
    {
        int alpha = (int)Math.Round(color.A * Math.Clamp(opacity, 0f, 1f));
        return Color.FromArgb(alpha, color.R, color.G, color.B);
    }

    private bool IsAchievementDetailBackButtonHit(Point location)
    {
        return GetAchievementBackButtonBounds().Contains(location);
    }

    private int GetHoveredAchievementDetailTabIndex(Point location)
    {
        for (int i = 0; i < 2; i++)
        {
            if (GetAchievementDetailTabBounds(i).Contains(location))
                return i;
        }

        return -1;
    }

    private void HandleAchievementDetailMouseDown(Point location)
    {
        if (IsAchievementDetailBackButtonHit(location))
        {
            _screen = UiScreen.Achievement;
            Invalidate();
            return;
        }

        int hoveredTab = GetHoveredAchievementDetailTabIndex(location);
        if (hoveredTab >= 0 && hoveredTab != _achievementDetailTabIndex)
        {
            _achievementDetailTabIndex = hoveredTab;
            _achievementDetailPageIndex = 0;
            Invalidate();
            return;
        }

        int arrow = GetHoveredAchievementPageArrow(location);
        if (arrow == 0 && _achievementDetailPageIndex > 0)
        {
            _achievementDetailPageIndex--;
            Invalidate();
            return;
        }
        if (arrow == 1)
        {
            int totalItems = AchievementCatalog.Definitions
                .Where(d => d.Tier == _selectedAchievementCardIndex)
                .Where(d => _achievementDetailTabIndex == 0 || _playerProgress.IsUnlocked(d.Id))
                .Count();
            int totalPages = Math.Max(1, (totalItems + AchievementDetailVisibleRows - 1) / AchievementDetailVisibleRows);
            if (_achievementDetailPageIndex < totalPages - 1)
            {
                _achievementDetailPageIndex++;
                Invalidate();
            }
        }
    }

    private int GetHoveredAchievementPageArrow(Point location)
    {
        Rectangle panelBounds = GetAchievementDetailPanelBounds();
        Rectangle innerBounds = Rectangle.Inflate(panelBounds, -(int)ScaleX(24f), -(int)ScaleY(22f));
        for (int i = 0; i < 2; i++)
        {
            if (GetAchievementPageArrowBounds(innerBounds, i).Contains(location))
                return i;
        }
        return -1;
    }

    private static GraphicsPath CreateRoundedSegmentPath(Rectangle bounds, float radius, bool roundLeft, bool roundRight)
    {
        float diameter = radius * 2f;
        float left = bounds.Left;
        float top = bounds.Top;
        float right = bounds.Right;
        float bottom = bounds.Bottom;

        var path = new GraphicsPath();
        path.StartFigure();

        if (roundLeft)
            path.AddArc(left, top, diameter, diameter, 90, 180);
        else
            path.AddLine(left, bottom, left, top);

        path.AddLine(left + (roundLeft ? radius : 0f), top, right - (roundRight ? radius : 0f), top);

        if (roundRight)
            path.AddArc(right - diameter, top, diameter, diameter, 270, 180);
        else
            path.AddLine(right, top, right, bottom);

        path.AddLine(right - (roundRight ? radius : 0f), bottom, left + (roundLeft ? radius : 0f), bottom);
        path.CloseFigure();
        return path;
    }
}