using System.Globalization;
using System.Windows;
using System.Windows.Media;
using MuWorld.Native.Core;

namespace MuWorld.Native.Controls;

/// <summary>Retained native charts of observed judgments and comparable local records.</summary>
public sealed class AnalyticsPlot : FrameworkElement
{
    public static readonly DependencyProperty AnalysisProperty = DependencyProperty.Register(nameof(Analysis), typeof(AnalysisResult), typeof(AnalyticsPlot), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty SummaryProperty = DependencyProperty.Register(nameof(Summary), typeof(RunSummary), typeof(AnalyticsPlot), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ModeProperty = DependencyProperty.Register(nameof(Mode), typeof(string), typeof(AnalyticsPlot), new FrameworkPropertyMetadata("histogram", FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty RecordsProperty = DependencyProperty.Register(nameof(Records), typeof(IReadOnlyList<PlayRecord>), typeof(AnalyticsPlot), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public AnalysisResult? Analysis { get => (AnalysisResult?)GetValue(AnalysisProperty); set => SetValue(AnalysisProperty, value); }
    public RunSummary? Summary { get => (RunSummary?)GetValue(SummaryProperty); set => SetValue(SummaryProperty, value); }
    public string Mode { get => (string)GetValue(ModeProperty); set => SetValue(ModeProperty, value); }
    public IReadOnlyList<PlayRecord>? Records { get => (IReadOnlyList<PlayRecord>?)GetValue(RecordsProperty); set => SetValue(RecordsProperty, value); }
    private static readonly Brush Muted = MakeBrush("#8c98a9");
    private static readonly Brush TextBrush = MakeBrush("#d9e0e1");
    private static readonly Brush Lime = MakeBrush("#bdd798");
    private static readonly Brush Lavender = MakeBrush("#b8aed7");
    private static readonly Brush Rose = MakeBrush("#d5a09c");
    private static readonly Brush GridBrush = MakeBrush("#35404d");

    public AnalyticsPlot() { SnapsToDevicePixels = true; UseLayoutRounding = true; MinHeight = 130; IsHitTestVisible = false; }
    public void Refresh() => InvalidateVisual();
    protected override Size MeasureOverride(Size availableSize) => new(double.IsInfinity(availableSize.Width) ? 380 : availableSize.Width, double.IsInfinity(availableSize.Height) ? 200 : Math.Min(240, availableSize.Height));
    private static SolidColorBrush MakeBrush(string color, double opacity = 1) { var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)) { Opacity = opacity }; brush.Freeze(); return brush; }
    private void Text(DrawingContext drawing, string value, double x, double y, Brush? brush = null, double size = 10, bool centered = false)
    {
        var formatted = new FormattedText(value, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface("Segoe UI, Malgun Gothic"), size, brush ?? Muted, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        drawing.DrawText(formatted, new Point(centered ? x - formatted.Width / 2 : x, y));
    }
    private void Empty(DrawingContext drawing, string title = "아직 분석할 입력이 없어요", string detail = "플레이한 기록으로 그래프를 채워갑니다.")
    {
        Text(drawing, title, ActualWidth / 2, ActualHeight / 2 - 19, TextBrush, 12, true);
        Text(drawing, detail, ActualWidth / 2, ActualHeight / 2 + 5, Muted, 10, true);
    }
    private static Pen GridPen(bool dashed = true) => new(GridBrush, 1) { DashStyle = dashed ? new DashStyle([3, 5], 0) : DashStyles.Solid };
    private Rect Bounds => new(39, 16, Math.Max(1, ActualWidth - 57), Math.Max(1, ActualHeight - 52));
    private void PercentAxes(DrawingContext drawing, Rect plot)
    {
        foreach (var percent in new[] { 0, 25, 50, 75, 100 })
        {
            var y = plot.Bottom - percent / 100d * plot.Height;
            drawing.DrawLine(GridPen(), new Point(plot.Left, y), new Point(plot.Right, y));
            Text(drawing, percent.ToString(CultureInfo.InvariantCulture), 5, y - 6);
        }
        Text(drawing, "%", 7, 0);
    }
    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing);
        if (ActualWidth < 120 || ActualHeight < 90) return;
        switch (Mode)
        {
            case "timeline": Timeline(drawing); break;
            case "lanes": Lanes(drawing); break;
            case "growth": Growth(drawing); break;
            default: Histogram(drawing); break;
        }
    }
    private void Histogram(DrawingContext drawing)
    {
        var bins = Analysis?.Histogram;
        if (bins is null || bins.Count == 0 || bins.All(bin => bin.Count == 0)) { Empty(drawing, "분석할 타격 편차가 없어요", "MISS와 홀드 유지 판정은 타이밍 분포에서 제외합니다."); return; }
        var plot = Bounds;
        var minimum = bins.Min(bin => bin.FromMs); var maximum = bins.Max(bin => bin.ToMs);
        var span = Math.Max(1, maximum - minimum); var peak = Math.Max(1, bins.Max(bin => bin.Count));
        foreach (var fraction in new[] { 0d, .5, 1d })
        {
            var y = plot.Bottom - fraction * plot.Height;
            drawing.DrawLine(GridPen(), new Point(plot.Left, y), new Point(plot.Right, y));
            Text(drawing, Math.Round(peak * fraction).ToString(CultureInfo.InvariantCulture), 8, y - 6);
        }
        drawing.PushClip(new RectangleGeometry(plot));
        foreach (var bin in bins)
        {
            var x = plot.Left + (bin.FromMs - minimum) / span * plot.Width;
            var width = Math.Max(.5, (bin.ToMs - bin.FromMs) / span * plot.Width - 1);
            var height = bin.Count / (double)peak * plot.Height;
            if (height > 0) drawing.DrawRoundedRectangle(bin.ToMs <= 0 ? Lavender : Lime, null, new Rect(x, plot.Bottom - height, width, height), 1, 1);
        }
        var zero = plot.Left + (0 - minimum) / span * plot.Width;
        drawing.DrawLine(new Pen(MakeBrush("#d4dfc7", .7), 1), new Point(zero, plot.Top), new Point(zero, plot.Bottom));
        drawing.Pop();
        foreach (var value in new[] { minimum, minimum / 2, 0, maximum / 2, maximum })
        {
            var x = plot.Left + (value - minimum) / span * plot.Width;
            Text(drawing, value.ToString("0.#", CultureInfo.InvariantCulture), x, plot.Bottom + 10, centered: true);
        }
        Text(drawing, "FAST", plot.Left, ActualHeight - 14, Lavender, 9);
        Text(drawing, "5ms 간격 · LATE →", Math.Max(plot.Left, plot.Right - 108), ActualHeight - 14, Muted, 9);
    }
    private void Timeline(DrawingContext drawing)
    {
        var heads = Summary?.Judgments.Where(judgment => judgment.Kind == "head").ToList();
        if (heads is null || heads.Count == 0) { SegmentTimeline(drawing); return; }
        var plot = Bounds;
        plot.Height = Math.Max(1, plot.Height - 13);
        var timed = heads.Where(judgment => judgment.Grade != "MISS" && judgment.DeltaUs.HasValue && double.IsFinite(judgment.DeltaUs.Value)).ToList();
        var extent = Math.Max(120, timed.Count > 0 ? Math.Ceiling(timed.Max(judgment => Math.Abs(judgment.DeltaUs!.Value) / 1000) / 20) * 20 : 120);
        var duration = Math.Max(1, Math.Max(heads.Max(judgment => judgment.TargetUs), Analysis?.Segments.LastOrDefault()?.EndUs ?? 0));
        foreach (var value in new[] { -extent, 0, extent })
        {
            var y = plot.Top + (value + extent) / (extent * 2) * plot.Height;
            drawing.DrawLine(value == 0 ? new Pen(MakeBrush("#a2b38d", .65), 1) : GridPen(), new Point(plot.Left, y), new Point(plot.Right, y));
            Text(drawing, value.ToString("0", CultureInfo.InvariantCulture), 1, y - 6);
        }
        drawing.PushClip(new RectangleGeometry(new Rect(plot.Left - 3, plot.Top - 3, plot.Width + 6, plot.Height + 18)));
        foreach (var judgment in heads)
        {
            var x = plot.Left + judgment.TargetUs / (double)duration * plot.Width;
            if (judgment.Grade == "MISS")
            {
                drawing.DrawLine(new Pen(Rose, 1.4), new Point(x - 2, plot.Bottom + 8), new Point(x + 2, plot.Bottom + 12));
                drawing.DrawLine(new Pen(Rose, 1.4), new Point(x - 2, plot.Bottom + 12), new Point(x + 2, plot.Bottom + 8));
            }
            else if (judgment.DeltaUs.HasValue)
            {
                var y = plot.Top + (judgment.DeltaUs.Value / 1000 + extent) / (extent * 2) * plot.Height;
                drawing.DrawEllipse(judgment.Grade == "PERFECT" ? Lime : judgment.Grade == "GREAT" ? Lavender : MakeBrush("#d8bd8b"), null, new Point(x, y), 2.2, 2.2);
            }
        }
        drawing.Pop();
        foreach (var fraction in new[] { 0d, .25, .5, .75, 1d }) Text(drawing, $"{duration / 1_000_000d * fraction:0}s", plot.Left + fraction * plot.Width, plot.Bottom + 19, centered: true);
        Text(drawing, "FAST ↑  ms  ↓ LATE", plot.Left, 0, Muted, 9);
        Text(drawing, "× MISS", Math.Max(plot.Left, plot.Right - 48), 0, Rose, 9);
    }
    private void SegmentTimeline(DrawingContext drawing)
    {
        var segments = Analysis?.Segments;
        if (segments is null || segments.All(segment => segment.Count == 0 || !segment.Accuracy.HasValue)) { Empty(drawing); return; }
        var plot = Bounds; PercentAxes(drawing, plot);
        var duration = Math.Max(1, segments.Max(segment => segment.EndUs));
        Point? prior = null;
        foreach (var segment in segments)
        {
            if (segment.Count == 0 || !segment.Accuracy.HasValue) { prior = null; continue; }
            var point = new Point(plot.Left + (segment.StartUs + segment.EndUs) / 2d / duration * plot.Width, plot.Bottom - segment.Accuracy.Value / 100 * plot.Height);
            if (prior.HasValue) drawing.DrawLine(new Pen(Lime, 1.7), prior.Value, point);
            drawing.DrawEllipse(Lime, null, point, 3, 3); prior = point;
        }
        Text(drawing, "0s", plot.Left, plot.Bottom + 10); Text(drawing, $"{duration / 1_000_000d:0}s", plot.Right - 22, plot.Bottom + 10);
    }
    private void Lanes(DrawingContext drawing)
    {
        var lanes = Analysis?.Lanes;
        if (lanes is null || lanes.All(lane => lane.Count == 0)) { Empty(drawing); return; }
        var plot = Bounds; PercentAxes(drawing, plot);
        var step = plot.Width / 4; var width = Math.Min(62, step * .6);
        for (var laneIndex = 0; laneIndex < 4; laneIndex++)
        {
            var lane = lanes.FirstOrDefault(item => item.Lane == laneIndex);
            var center = plot.Left + step * (laneIndex + .5);
            if (lane is null || lane.Count == 0) { Text(drawing, "입력 없음", center, plot.Bottom - 18, Muted, 9, true); }
            else
            {
                var hitRate = Math.Clamp(100 - (lane.MissRate ?? lane.Misses / (double)lane.Count * 100), 0, 100);
                drawing.DrawRoundedRectangle(MakeBrush("#35404b"), null, new Rect(center - width / 2, plot.Top, width, plot.Height), 3, 3);
                var height = hitRate / 100 * plot.Height;
                if (height > 0) drawing.DrawRoundedRectangle(laneIndex is 0 or 3 ? Lime : Lavender, null, new Rect(center - width / 2, plot.Bottom - height, width, height), 3, 3);
                Text(drawing, $"{hitRate:0.#}%", center, height >= 24 ? plot.Bottom - height + 7 : plot.Bottom - height - 17, height >= 24 ? MakeBrush("#172016") : TextBrush, 10, true);
                Text(drawing, $"{lane.Count} notes", center, ActualHeight - 13, Muted, 9, true);
            }
            Text(drawing, $"LANE {laneIndex + 1}", center, plot.Bottom + 9, Muted, 9, true);
        }
        Text(drawing, "누락 없이 입력한 비율", plot.Left + 20, 0, Muted, 9);
    }
    private void Growth(DrawingContext drawing)
    {
        var records = Records?.Where(record => record.Mode == "standard" && record.Completed && !record.Interrupted).OrderBy(record => record.CreatedAt).ToList();
        if (records is null || records.Count == 0) { Empty(drawing, "같은 조건의 기록을 모아보세요", "정규 완주 기록의 실제 정확도 변화를 보여줍니다."); return; }
        var latest = records[^1];
        records = records.Where(record => record.ChartId == latest.ChartId && record.ChartVersion == latest.ChartVersion && record.Difficulty == latest.Difficulty && record.Rate == latest.Rate
            && record.InputOffsetUs == latest.InputOffsetUs && record.Settings.WindowScale == latest.Settings.WindowScale && record.Settings.Keys.SequenceEqual(latest.Settings.Keys)
            && record.Environment.Count == latest.Environment.Count && record.Environment.All(pair => latest.Environment.TryGetValue(pair.Key, out var value) && value == pair.Value)).TakeLast(20).ToList();
        var plot = Bounds; PercentAxes(drawing, plot);
        Point? prior = null;
        for (var index = 0; index < records.Count; index++)
        {
            var x = records.Count == 1 ? plot.Left + plot.Width / 2 : plot.Left + index / (double)(records.Count - 1) * plot.Width;
            var point = new Point(x, plot.Bottom - Math.Clamp(records[index].Summary.Accuracy, 0, 100) / 100 * plot.Height);
            if (prior.HasValue) drawing.DrawLine(new Pen(Lime, 1.8), prior.Value, point);
            drawing.DrawEllipse(Lime, new Pen(MakeBrush("#202a24"), 1), point, 3.5, 3.5); prior = point;
            if (records.Count <= 7 || index == 0 || index == records.Count - 1) Text(drawing, $"{index + 1}", x, plot.Bottom + 9, centered: true);
        }
        Text(drawing, $"동일 조건 · 최근 {records.Count}회", plot.Left + 17, 0, Muted, 9);
    }
}
