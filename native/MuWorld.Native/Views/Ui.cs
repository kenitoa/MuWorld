using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MuWorld.Native;
internal static class Ui
{
    public const string Bg = "#12161E", PanelColor = "#1B212B", TextColor = "#EDF0EE", Muted = "#A6AFBE", Dim = "#7F8B9E", Lime = "#D2ED9D", Lavender = "#BDB4EB";
    public static SolidColorBrush Brush(string color) { var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)); b.Freeze(); return b; }
    public static TextBlock Text(string text, double size=14, string color=TextColor, bool bold=false) => new() { Text=text, FontSize=size, Foreground=Brush(color), FontWeight=bold?FontWeights.SemiBold:FontWeights.Normal, TextWrapping=TextWrapping.Wrap, VerticalAlignment=VerticalAlignment.Center };
    public static StackPanel Stack(params UIElement[] children) { var s=new StackPanel(); foreach(var c in children)s.Children.Add(c); return s; }
    public static StackPanel Row(params UIElement[] children) { var s=Stack(children);s.Orientation=Orientation.Horizontal;return s; }
    public static Border Card(UIElement child, string color=PanelColor, double padding=22) => new() { Background=Brush(color), BorderBrush=Brush("#2B3340"), BorderThickness=new Thickness(1), CornerRadius=new CornerRadius(13), Padding=new Thickness(padding), Child=child };
    public static Border Gap(double height=16) => new() { Height=height };
    public static Button Button(string title,Action click,bool primary=false) { var b=new Button { Content=title };if(primary){ b.Background=Brush(Lime); b.Foreground=Brush("#19221B");b.BorderBrush=Brush(Lime);b.FontWeight=FontWeights.SemiBold; } b.Click+=(_,_)=>click();return b; }
    public static Border Tag(string title,string color=Lime) => new() { Child=Text(title,11,color,true),Background=Brush("#262F2B"),CornerRadius=new CornerRadius(4),Padding=new Thickness(8,4,8,4),VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Left };
    public static Grid Columns(double[] widths, params UIElement[] items) { var g=new Grid();foreach(var w in widths)g.ColumnDefinitions.Add(new ColumnDefinition{Width=w<0?new GridLength(-w,GridUnitType.Star):new GridLength(w)});for(int i=0;i<items.Length;i++){Grid.SetColumn(items[i],i);g.Children.Add(items[i]);}return g; }
    public static T Margin<T>(T element,double left=0,double top=0,double right=0,double bottom=0) where T:FrameworkElement {element.Margin=new Thickness(left,top,right,bottom);return element;}
    public static Border Rule() => new() {Height=1,Background=Brush("#2B3340"),Margin=new Thickness(0,15,0,15)};
    public static TextBlock Label(string text) => Text(text,11,Dim,true);
    public static StackPanel Metric(string label,string value,string hint="",string color=TextColor) => Stack(Label(label),Gap(9),Text(value,30,color,true),Gap(5),Text(hint,12,Muted));
    public static string Ms(double? value,bool signed=false)=>value.HasValue?value.Value.ToString(signed?"+0.00;-0.00;0.00":"0.00",CultureInfo.InvariantCulture)+" ms":"—";
    public static string Time(double sec)=>$"{(int)sec/60}:{(int)sec%60:00}";
}
