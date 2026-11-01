using System.Globalization;
using System.Windows;
using System.Windows.Media;
using MuWorld.Native.Core;

namespace MuWorld.Native.Controls;
public sealed class GameSurface : FrameworkElement
{
    public RhythmEngine? Engine {get;set;}
    public Settings Settings {get;set;}=new();
    public decimal Rate {get;set;}=1;
    public long TimeUs {get;set;}
    public long FromUs {get;set;}
    public long ToUs {get;set;}
    public ArcadeSession? Arcade {get;set;}
    public Judgment? LastJudgment {get;set;}
    public long LastJudgmentAt {get;set;}
    public bool[] Pressed {get;}=new bool[4];
    static readonly string[] colors=["#BDB4EB","#D2ED9D","#D2ED9D","#BDB4EB"];
    static readonly Typeface font=new(new FontFamily("pack://application:,,,/MuWorld;component/Assets/Fonts/#Pretendard, Segoe UI, Malgun Gothic"),FontStyles.Normal,FontWeights.Normal,FontStretches.Normal);
    void Txt(DrawingContext dc,string text,double x,double y,double size,string color,bool center=false){var f=new FormattedText(text,CultureInfo.CurrentCulture,FlowDirection.LeftToRight,font,size,Ui.Brush(color),VisualTreeHelper.GetDpi(this).PixelsPerDip);dc.DrawText(f,new Point(center?x-f.Width/2:x,y));}
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);var w=ActualWidth;var h=ActualHeight;if(w<=0||h<180||Engine==null)return;
        var railMaximum=Math.Max(160,w-(Arcade==null?400:520));var trackW=Math.Clamp(Math.Min(w*.48,460)*Settings.LaneWidth/100,Math.Min(240,railMaximum),railMaximum);var left=(w-trackW)/2;var laneW=trackW/4;var line=h*.79;var top=25.0;var approach=Math.Max(1,Settings.ApproachMs*1000*(double)Rate);var visualNow=TimeUs+Settings.VisualOffsetUs*(double)Rate;var brightness=Math.Clamp(Settings.BackgroundBrightness/100,0,1.5);
        var bg=Color.FromRgb((byte)(18*brightness),(byte)(23*brightness),(byte)(31*brightness));dc.DrawRectangle(new SolidColorBrush(bg),null,new Rect(0,0,w,h));
        dc.DrawRoundedRectangle(Ui.Brush("#171D27"),new Pen(Ui.Brush("#303A49"),1),new Rect(left,top,trackW,h-top-25),10,10);
        for(int lane=0;lane<4;lane++){
            if(lane%2==1)dc.DrawRectangle(Ui.Brush("#1A212C"),null,new Rect(left+lane*laneW,top,laneW,h-top-25));
            if(lane>0)dc.DrawLine(new Pen(Ui.Brush("#2C3543"),1),new Point(left+laneW*lane,top),new Point(left+laneW*lane,h-25));
            if(Pressed[lane]&&Settings.Effects>0)dc.DrawRectangle(new SolidColorBrush(Color.FromArgb((byte)(16+Settings.Effects*.35),189,205,164)),null,new Rect(left+lane*laneW,line-90,laneW,145));
            var active=Arcade==null||Arcade.ActiveLanes.Contains(lane);Txt(dc,active?System.Windows.Input.KeyInterop.KeyFromVirtualKey(Settings.Keys[lane]).ToString():"·",left+laneW*(lane+.5),line+37,20,active&&Pressed[lane]?Ui.Lime:active?"#A7B1C0":"#525E6C",true);
            if(Settings.HighContrast)Txt(dc,new[]{"○","◇","△","□"}[lane],left+laneW*(lane+.5),line+69,15,Ui.Muted,true);
        }
        var phase=Arcade?.PhaseAt(TimeUs)??"play";var hide=Arcade?.Kind=="silence"&&phase=="silent";
        if(!hide&&Arcade?.Kind is not ("echo" or "route")){
            dc.PushClip(new RectangleGeometry(new Rect(left,top,trackW,Math.Max(1,line-top+15))));
            foreach(var n in Engine.Notes){
                if(n.State=="miss"||n.HoldState is "complete" or "broken")continue;
                var y=line-(n.TimeUs-visualNow)/approach*(line-top);var x=left+n.Lane*laneW+8;var width=laneW-16;
                var color=(Color)ColorConverter.ConvertFromString(Settings.HighContrast?(n.Lane%2==0?"#D4CCFF":"#E3FFB1"):colors[n.Lane]);var level=Math.Clamp(Settings.NoteBrightness/100,0,1.5);color=Color.FromRgb((byte)Math.Clamp(Math.Round(color.R*level),0,255),(byte)Math.Clamp(Math.Round(color.G*level),0,255),(byte)Math.Clamp(Math.Round(color.B*level),0,255));var brush=new SolidColorBrush(color);
                if(n.EndUs.HasValue){var tailY=line-(n.EndUs.Value-visualNow)/approach*(line-top);if(y<top-20||tailY>line+15)continue;var head=Math.Clamp(n.State=="hit"?line:y,top,line+12);var tail=Math.Clamp(tailY,top,line+12);if(head>tail){var holdBrush=new SolidColorBrush(Color.FromArgb(75,color.R,color.G,color.B));dc.DrawRoundedRectangle(holdBrush,null,new Rect(x+width*.18,tail,width*.64,head-tail),3,3);}if(tailY>=top&&tailY<=line+12)dc.DrawRoundedRectangle(brush,null,new Rect(x+width*.18,tailY-2,width*.64,5),2,2);}
                if(n.State=="hit")continue;if(y<top||y>line+40)continue;
                if(Arcade?.Kind=="guard"){var shape=new StreamGeometry();using(var path=shape.Open()){path.BeginFigure(new Point(x,y-6),true,true);path.LineTo(new Point(x+width,y-6),true,false);path.LineTo(new Point(x+width*.85,y+7),true,false);path.LineTo(new Point(x+width*.15,y+7),true,false);}dc.DrawGeometry(brush,null,shape);}else dc.DrawRoundedRectangle(brush,null,new Rect(x,y-5,width,11),3,3);dc.DrawLine(new Pen(Ui.Brush("#EBF3DF"),1),new Point(x+4,y-4),new Point(x+width-4,y-4));
                if(Settings.HighContrast)dc.DrawEllipse(Ui.Brush("#18201D"),null,new Point(x+width/2,y),2+n.Lane*.4,2+n.Lane*.4);
            }
            dc.Pop();
        }else if(hide)Txt(dc,"내 안의 박자를 따라가요",w/2,line-110,16,Ui.Muted,true);
        dc.DrawLine(new Pen(Ui.Brush("#D5DECA"),2),new Point(left+1,line),new Point(left+trackW-1,line));
        dc.DrawRectangle(Ui.Brush("#111720"),null,new Rect(left+1,0,trackW-2,24));
        var summary=Engine.Snapshot();
        var hudX=Arcade==null?left-155:left+trackW+35;var hudY=Arcade==null?line-150:185;
        if(!hide){Txt(dc,"ACCURACY",hudX,hudY,10,Ui.Dim);Txt(dc,summary.JudgedUnits>0?$"{summary.Accuracy:0.00}%":"—",hudX,hudY+22,27,Ui.Lime);}
        if(!hide&&Settings.ShowCombo){var comboY=Arcade==null?line-150:285;Txt(dc,"COMBO",left+trackW+35,comboY,10,Ui.Dim);Txt(dc,summary.Combo.ToString("D3"),left+trackW+35,comboY+22,36,Ui.TextColor);}
        if(!hide&&LastJudgment!=null&&TimeUs>=LastJudgmentAt&&TimeUs-LastJudgmentAt<(long)(650_000*(double)Rate)){
            Txt(dc,LastJudgment.Grade,w/2,line-69,20,LastJudgment.Grade=="MISS"?"#DFAB9D":Ui.Lime,true);
            if(Settings.ShowTiming&&LastJudgment.DeltaUs.HasValue)Txt(dc,$"{LastJudgment.DeltaUs/1000:+0.0;-0.0;0.0} ms",w/2,line-42,12,Ui.Muted,true);
        }
        if(Arcade!=null)DrawArcade(dc,left,trackW,h,summary,phase);
        if(TimeUs<FromUs){var countdownY=Arcade==null?h*.35:line-70;Txt(dc,Math.Max(1,(int)Math.Ceiling((FromUs-TimeUs)/1_000_000.0/(double)Rate)).ToString(),w/2,countdownY,Arcade==null?76:38,Ui.Lime,true);Txt(dc,"편안하게, 준비해요",w/2,countdownY+(Arcade==null?92:48),12,Ui.Muted,true);}
        var progressTime=hide?((TimeUs/Arcade!.BeatUs<24?16:32)*Arcade.BeatUs):TimeUs;var progress=Math.Clamp((progressTime-FromUs)/(double)Math.Max(1,ToUs-FromUs),0,1);dc.DrawRectangle(Ui.Brush("#26303B"),null,new Rect(0,h-3,w,3));dc.DrawRectangle(Ui.Brush(Ui.Lime),null,new Rect(0,h-3,w*progress,3));
    }
    void DrawArcade(DrawingContext dc,double left,double width,double h,RunSummary summary,string phase)
    {
        if(Arcade!.Kind is "echo" or "route"){DrawMemoryArcade(dc,left,width,h,summary,phase);return;}
        var a=Arcade!;var x=35.0;var y=56.0;Txt(dc,a.Title,x,y,22,Ui.Lavender);Txt(dc,$"ROUND {Math.Min(a.RoundCount,a.RoundAt(TimeUs)+1):00} / {a.RoundCount:00}",x,y+38,11,Ui.Dim);
        var label=phase switch{"listen"=>"먼저, 귀 기울여 들어요","respond"=>"이제 같은 리듬으로 답해요","silent"=>"소리 없이 박자를 이어가요","plan"=>"이번에 갈 길을 살펴봐요","execute"=>"리듬에 맞춰 한 칸씩","prepare"=>"준비","complete"=>"완료",_=>"박자에 맞춰 플레이해요"};Txt(dc,label,x,y+69,13,Ui.Muted);
        if(a.Kind=="echo"){var cue=a.CallCues.LastOrDefault(c=>c.TimeUs<=TimeUs&&TimeUs-c.TimeUs<200_000);for(int i=0;i<4;i++){dc.DrawEllipse(Ui.Brush(cue?.Lane==i?colors[i]:"#303847"),null,new Point(x+18+i*43,y+130),12,12);Txt(dc,(i+1).ToString(),x+18+i*43,y+153,11,Ui.Dim,true);}}
        if(a.Kind=="guard"){var misses=summary.Counts.GetValueOrDefault("MISS");Txt(dc,$"방어 성공  {summary.Judgments.Count(j=>j.Kind=="head"&&j.Grade!="MISS")}",x,y+122,18,Ui.Lime);Txt(dc,$"놓친 신호  {misses}",x,y+153,12,Ui.Muted);}
        if(a.Kind=="workshop"){Txt(dc,$"정확한 판정  {summary.Judgments.Count(j=>j.Grade!="MISS")}",x,y+122,18,Ui.Lime);Txt(dc,"짧게 두드리고, 길게 이어 주세요",x,y+154,12,Ui.Muted);}
        if(a.Kind=="route"&&a.Routes.Count>0){var round=Math.Clamp(a.RoundAt(TimeUs),0,a.Routes.Count-1);var route=a.Routes[round];var pos=new[]{2,2};var begin=round*a.RoundBeats*a.BeatUs;foreach(var j in summary.Judgments.Where(j=>j.Kind=="head"&&j.Grade!="MISS"&&j.TargetUs>=begin&&j.TargetUs<begin+a.RoundBeats*a.BeatUs)){if(j.Lane==0)pos[0]--;if(j.Lane==3)pos[0]++;if(j.Lane==1)pos[1]++;if(j.Lane==2)pos[1]--;pos[0]=Math.Clamp(pos[0],0,4);pos[1]=Math.Clamp(pos[1],0,4);}for(int row=0;row<5;row++)for(int col=0;col<5;col++){var r=new Rect(x+col*31,y+120+row*31,26,26);dc.DrawRoundedRectangle(Ui.Brush(col==route.Goal[0]&&row==route.Goal[1]?"#414D37":"#27303C"),null,r,4,4);if(col==pos[0]&&row==pos[1])dc.DrawEllipse(Ui.Brush(Ui.Lime),null,new Point(r.X+13,r.Y+13),7,7);}Txt(dc,string.Join("  ",route.Commands.Select(n=>new[]{"←","↓","↑","→"}[n])),x,y+300,22,Ui.Lavender);}
    }
    void DrawMemoryArcade(DrawingContext dc,double left,double width,double h,RunSummary summary,string phase)
    {
        var a=Arcade!;var line=h*.79;var center=left+width/2;
        Txt(dc,a.Title,35,56,22,Ui.Lavender);
        Txt(dc,$"ROUND {a.RoundAt(TimeUs)+1:00} / {a.RoundCount:00}",35,94,11,Ui.Dim);
        Txt(dc,phase switch{"listen"=>"먼저 듣고 순서를 기억하세요","respond"=>"같은 순서와 간격으로 답해요","plan"=>"네 방향을 왼쪽부터 기억하세요","execute"=>"한 박에 한 명령씩 실행해요","prepare"=>"준비","complete"=>"완료",_=>"리듬에 맞춰 플레이해요"},35,125,13,Ui.Muted);
        Txt(dc,"사용 키  "+string.Join(" · ",a.ActiveLanes.Select(l=>System.Windows.Input.KeyInterop.KeyFromVirtualKey(Settings.Keys[l]).ToString())),35,154,11,Ui.Dim);
        if(a.Kind=="echo")
        {
            var padW=Math.Min(145,(width-38)/2);var padLeft=center-padW-9;var padY=Math.Max(100,line-220);
            var visual=TimeUs+Settings.VisualOffsetUs*(double)Rate;
            var cue=a.CallCues.LastOrDefault(c=>c.TimeUs<=visual&&visual-c.TimeUs<150_000);
            for(var index=0;index<2;index++)
            {
                var lane=index==0?0:2;var lit=cue?.Lane==lane;var r=new Rect(padLeft+index*(padW+18),padY,padW,105);
                dc.DrawRoundedRectangle(Ui.Brush(lit?"#3B4554":Pressed[lane]?"#33412E":"#252E3C"),new Pen(Ui.Brush(lit?Ui.Lavender:"#465366"),1),r,11,11);
                Txt(dc,System.Windows.Input.KeyInterop.KeyFromVirtualKey(Settings.Keys[lane]).ToString(),r.X+padW/2,r.Y+25,29,lit?Ui.Lavender:Ui.TextColor,true);
                Txt(dc,index==0?"LEFT":"RIGHT",r.X+padW/2,r.Y+72,10,Ui.Muted,true);
            }
            Txt(dc,phase=="listen"?"LISTEN · 4 BEATS":phase=="respond"?"YOUR TURN · 4 BEATS":"LISTEN & REPEAT",center,padY-40,13,Ui.Muted,true);
            DrawBeatDots(dc,center,padY+130);
            return;
        }
        if(a.Routes.Count==0)return;
        var round=Math.Clamp(a.RoundAt(TimeUs),0,a.Routes.Count-1);var route=a.Routes[round];var begin=round*a.RoundBeats*a.BeatUs;
        var heads=summary.Judgments.Where(j=>j.Kind=="head"&&j.TargetUs>=begin&&j.TargetUs<begin+a.RoundBeats*a.BeatUs)
            .GroupBy(j=>j.NoteId).Select(group=>group.Last()).OrderBy(j=>j.TargetUs).Take(route.Commands.Length).ToList();
        int[][] moves=[[-1,0],[0,1],[0,-1],[1,0]];string[] arrows=["←","↓","↑","→"];var pos=new[]{2,2};
        // Rebuild from distinct actual judgments. Re-rendering never advances a route.
        foreach(var j in heads.Where(j=>j.Grade!="MISS")){pos[0]=Math.Clamp(pos[0]+moves[j.Lane][0],0,4);pos[1]=Math.Clamp(pos[1]+moves[j.Lane][1],0,4);}
        var arrived=heads.Count==route.Commands.Length&&heads.All(j=>j.Grade!="MISS")&&pos.SequenceEqual(route.Goal);
        var gridX=center-75;var gridY=Math.Max(80,line-345);
        for(int row=0;row<5;row++)for(int column=0;column<5;column++)
        {
            var goal=column==route.Goal[0]&&row==route.Goal[1];var r=new Rect(gridX+column*31,gridY+row*31,26,26);
            dc.DrawRoundedRectangle(Ui.Brush(goal?"#414D37":"#27303C"),goal?new Pen(Ui.Brush(arrived?Ui.Lime:"#A5AE82"),1):null,r,4,4);
            if(goal)Txt(dc,"◇",r.X+13,r.Y+2,16,"#C4C99D",true);
            if(column==pos[0]&&row==pos[1])dc.DrawEllipse(Ui.Brush(Ui.Lime),null,new Point(r.X+13,r.Y+13),7,7);
        }
        var commandY=gridY+175;var commandX=center-94;
        for(int index=0;index<route.Commands.Length;index++)
        {
            var judgment=heads.FirstOrDefault(j=>j.TargetUs==begin+(4+index)*a.BeatUs);
            var reveal=phase is "plan" or "prepare" or "complete"||judgment!=null;
            var color=judgment?.Grade=="MISS"?"#DBA29E":judgment!=null?Ui.Lime:Ui.Lavender;
            var r=new Rect(commandX+index*50,commandY,38,37);
            dc.DrawRoundedRectangle(Ui.Brush(judgment==null?"#28313E":judgment.Grade=="MISS"?"#3B2D32":"#303C2A"),new Pen(Ui.Brush(judgment==null?"#455165":color),1),r,6,6);
            Txt(dc,reveal?arrows[route.Commands[index]]:"·",r.X+19,r.Y+4,22,color,true);
        }
        DrawBeatDots(dc,center,commandY+57);
        Txt(dc,arrived?"목적지 도착":phase=="plan"?"네 방향을 기억하세요":"한 박에 한 방향",35,225,16,arrived?Ui.Lime:Ui.Muted);
    }
    void DrawBeatDots(DrawingContext dc,double center,double y)
    {
        var beat=TimeUs<0?-1:(int)(TimeUs/Arcade!.BeatUs)%4;
        for(int index=0;index<4;index++)dc.DrawEllipse(Ui.Brush(index==beat?Ui.Lime:"#3A4552"),null,new Point(center-30+index*20,y),3,3);
    }
}
