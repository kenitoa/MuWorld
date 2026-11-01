using System.Globalization;
using System.Windows;
using System.Windows.Media;
using MuWorld.Native.Core;

namespace MuWorld.Native.Controls;

/// <summary>Retained WPF vector artwork. No web renderer, SVG decoder or bitmap asset is involved.</summary>
public sealed class AlbumArtwork : FrameworkElement
{
    public static readonly DependencyProperty SongProperty = DependencyProperty.Register(nameof(Song), typeof(Song), typeof(AlbumArtwork), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public Song? Song { get => (Song?)GetValue(SongProperty); set => SetValue(SongProperty, value); }
    private static readonly string[][] Palettes = [["#c8c4ea", "#eee9fa", "#7c78b5"], ["#e4b995", "#fff0cb", "#b97855"], ["#acbea9", "#e6ead1", "#596c5b"], ["#d89e99", "#f3d0b8", "#8c4e65"], ["#a1bfd2", "#e2edf0", "#567b92"]];
    public AlbumArtwork() { SnapsToDevicePixels = true; UseLayoutRounding = true; IsHitTestVisible = false; }

    protected override Size MeasureOverride(Size availableSize) => new(double.IsInfinity(availableSize.Width) ? 96 : Math.Min(400, availableSize.Width), double.IsInfinity(availableSize.Height) ? 96 : Math.Min(400, availableSize.Height));
    private static SolidColorBrush Brush(string hex, double opacity = 1) { var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)) { Opacity = opacity }; brush.Freeze(); return brush; }
    private static StreamGeometry Path(Action<StreamGeometryContext> draw)
    {
        var geometry = new StreamGeometry(); using (var context = geometry.Open()) draw(context); geometry.Freeze(); return geometry;
    }
    private void Text(DrawingContext drawing, string value, double size, double x, double y, Brush color, FontWeight? weight = null)
    {
        var formatted = new FormattedText(value, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, weight ?? FontWeights.Normal, FontStretches.Normal), size, color, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        drawing.DrawText(formatted, new Point(x, y));
    }
    private void SpacedText(DrawingContext drawing, string value, double size, double x, double y, Brush color, double spacing)
    {
        foreach (var character in value)
        {
            var text = new FormattedText(character.ToString(), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), size, color, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            drawing.DrawText(text, new Point(x, y)); x += text.WidthIncludingTrailingWhitespace + spacing;
        }
    }
    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing);
        if (ActualWidth <= 0 || ActualHeight <= 0) return;
        var song = Song; var art = Math.Clamp(song?.Art ?? 0, 0, 4); var palette = Palettes[art];
        var background = Brush(palette[0]); var foreground = Brush(palette[1]); var dark = Brush(palette[2]);
        var gradient = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1), GradientStops = new GradientStopCollection { new((Color)ColorConverter.ConvertFromString(palette[1]), 0), new((Color)ColorConverter.ConvertFromString(palette[0]), .55), new((Color)ColorConverter.ConvertFromString(palette[2]), 1) } }; gradient.Freeze();
        drawing.PushClip(new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight), Math.Min(12, ActualWidth * .035), Math.Min(12, ActualHeight * .035)));
        var scale = Math.Max(ActualWidth / 400, ActualHeight / 400);
        drawing.PushTransform(new TranslateTransform((ActualWidth - 400 * scale) / 2, (ActualHeight - 400 * scale) / 2)); drawing.PushTransform(new ScaleTransform(scale, scale));
        drawing.DrawRectangle(background, null, new Rect(0, 0, 400, 400));
        if (art == 0)
        {
            drawing.DrawEllipse(Brush(palette[2], .15), null, new Point(202, 252), 125, 42);
            drawing.PushTransform(new RotateTransform(-22, 200, 180));
            drawing.DrawRoundedRectangle(gradient, null, new Rect(120, 75, 160, 227), 80, 80);
            drawing.DrawEllipse(null, new Pen(foreground, 1.5), new Point(200, 135), 80, 52);
            for (var index = 0; index < 14; index++) drawing.DrawLine(new Pen(Brush(palette[1], .22), 1), new Point(132 + index * 10, 125), new Point(132 + index * 10, 260));
            drawing.DrawRoundedRectangle(background, null, new Rect(154, 114, 92, 146), 46, 46);
            drawing.DrawGeometry(null, new Pen(Brush(palette[1], .65), 8), Path(path => { path.BeginFigure(new Point(166, 229), false, false); path.BezierTo(new Point(138, 169), new Point(163, 122), new Point(203, 120), true, false); }));
            drawing.Pop();
        }
        else if (art == 1)
        {
            drawing.DrawEllipse(gradient, null, new Point(200, 183), 101, 101);
            for (var index = 0; index < 16; index++)
            {
                var i = index;
                drawing.DrawGeometry(null, new Pen(foreground, 3), Path(path => { path.BeginFigure(new Point(75, 220 + i * 6), false, false); path.QuadraticBezierTo(new Point(200, 165 + i * 7), new Point(325, 220 + i * 6), true, false); }));
            }
            drawing.DrawLine(new Pen(Brush(palette[2], .15), 1), new Point(80, 184), new Point(320, 184));
        }
        else if (art == 2)
        {
            drawing.DrawEllipse(foreground, null, new Point(200, 189), 111, 111); drawing.DrawEllipse(background, null, new Point(242, 145), 96, 96);
            drawing.DrawGeometry(gradient, null, Path(path => { path.BeginFigure(new Point(88, 283), true, true); path.LineTo(new Point(185, 141), true, false); path.LineTo(new Point(315, 283), true, false); }));
            drawing.DrawGeometry(Brush(palette[1], .48), null, Path(path => { path.BeginFigure(new Point(185, 141), true, true); path.LineTo(new Point(189, 282), true, false); path.LineTo(new Point(315, 282), true, false); }));
            drawing.DrawLine(new Pen(Brush(palette[2], .4), 1), new Point(86, 284), new Point(313, 283));
        }
        else if (art == 3)
        {
            for (var index = 0; index < 11; index++) { var cy = 115 + index * 14; drawing.PushTransform(new RotateTransform(-20, 200, cy)); drawing.DrawEllipse(index % 2 == 0 ? foreground : background, new Pen(Brush(palette[2], .2), 1), new Point(200, cy), 108 - index * 3, 45); drawing.Pop(); }
        }
        else
        {
            drawing.DrawEllipse(gradient, null, new Point(200, 182), 94, 94); drawing.DrawRectangle(background, null, new Rect(57, 217, 286, 94));
            for (var index = 0; index < 12; index++) drawing.DrawLine(new Pen(foreground, 1 + index * .2), new Point(55 + index * 6, 221 + index * 7), new Point(345 - index * 6, 221 + index * 7));
            drawing.DrawEllipse(foreground, null, new Point(295, 88), 5, 5);
        }
        // Fixed, low-opacity vector grain preserves the soft cover texture without image decoding.
        uint seed = (uint)(7823 + art * 103);
        var grain = Brush("#ffffff", .045);
        for (var index = 0; index < 160; index++) { seed = seed * 1664525 + 1013904223; var x = seed % 400; seed = seed * 1664525 + 1013904223; drawing.DrawRectangle(grain, null, new Rect(x, seed % 400, 1, 1)); }
        SpacedText(drawing, "MUWORLD ORIGINALS", 9, 28, 27, dark, 3);
        Text(drawing, song?.Title ?? "MuWorld", 23, 28, 330, dark, FontWeights.Medium);
        SpacedText(drawing, $"VOL. 0{art + 1} / {song?.Bpm ?? 100} BPM", 8, 29, 367, dark, 3);
        drawing.DrawLine(new Pen(dark, 1), new Point(344, 351), new Point(371, 351)); drawing.DrawLine(new Pen(dark, 1), new Point(365, 345), new Point(371, 351)); drawing.DrawLine(new Pen(dark, 1), new Point(371, 351), new Point(365, 357));
        drawing.Pop(); drawing.Pop(); drawing.Pop();
    }
}
