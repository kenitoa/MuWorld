using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MuWorld.Native.Audio;
using MuWorld.Native.Controls;
using MuWorld.Native.Core;

namespace MuWorld.Native;
public partial class MainWindow
{
    RhythmEngine? engine;
    GameSurface? surface;
    Grid? gameHost;
    Border? pauseOverlay;
    TextBlock? gameTime;
    Song? runSong;
    Chart? runChart;
    Settings runSettings=new();
    ArcadeSession? runArcade;
    PlayRecord? replayRecord;
    string runMode="standard";
    decimal runRate=1;
    long runFrom,runTo,lastHud;
    int replayCursor,tutorialStep=-1;
    int runRepeats=1;
    bool paused,finishing,resuming,hadPause;
    async Task StartGame(Song song,Chart chart,string mode="standard",decimal rate=1,long fromUs=0,long?toUs=null,ArcadeSession? arcade=null,PlayRecord? replay=null,int repeats=1)
    {
        if(starting||engine!=null)return;starting=true;runRepeats=repeats;repeatGeneration++;Notify("음악을 준비하고 있습니다…");
        try{
            runSong=song;runChart=chart;runMode=mode;runRate=rate;runFrom=fromUs;runTo=toUs??chart.DurationUs;runArcade=arcade;replayRecord=replay;replayCursor=0;lastHud=long.MinValue;paused=false;finishing=false;hadPause=false;
            runSettings=replay?.Settings.Clone()??store.Settings.Clone();if(mode=="standard"||mode=="arcade"||mode=="tutorial")runSettings.WindowScale=replay?.Settings.WindowScale??1;
            audio.Volume=runSettings.Volume;audio.EffectVolume=runSettings.EffectVolume;
            BuildGameHost(song,arcade?.Title??song.Title,replay!=null);
            if(arcade!=null)await audio.StartCuesAsync(arcade.Cues,runTo,mode=="tutorial"?1:2);else await audio.StartAsync(song,rate,fromUs,runTo,2,mode=="practice"&&runSettings.Metronome);
            while(keyQueue.TryDequeue(out _)){}
            engine=new RhythmEngine(chart,runSettings,rate,fromUs,runTo);surface!.Engine=engine;surface.Settings=runSettings;surface.Rate=rate;surface.FromUs=fromUs;surface.ToUs=runTo;surface.Arcade=arcade;surface.Focus();Notify("");
        }catch(Exception ex){audio.Stop();engine=null;RemoveGameHost();Notify("플레이를 시작하지 못했습니다. "+ex.Message);}finally{starting=false;}
    }
    void BuildGameHost(Song song,string title,bool replay)
    {
        shell.Visibility=Visibility.Collapsed;gameHost=new Grid{Background=Ui.Brush(Ui.Bg)};gameHost.RowDefinitions.Add(new RowDefinition{Height=new GridLength(85)});gameHost.RowDefinitions.Add(new RowDefinition());gameHost.RowDefinitions.Add(new RowDefinition{Height=new GridLength(40)});
        var titleBlock=Ui.Stack(Ui.Text(title,21,Ui.TextColor,true),Ui.Gap(5),Ui.Text($"{(replay?"REPLAY · ":"")}{ModeName(runMode)}  /  {runChart!.Difficulty.ToUpper()}  /  {runRate*100:0}% SPEED",10,Ui.Dim));gameTime=Ui.Text("PREPARING AUDIO…",12,Ui.Muted);gameTime.HorizontalAlignment=HorizontalAlignment.Center;
        var header=Ui.Columns([-1,-1,145],titleBlock,gameTime,Ui.Button("Ⅱ  일시정지",PauseGame));header.Margin=new Thickness(34,20,34,15);gameHost.Children.Add(header);
        surface=new GameSurface{Focusable=true};Grid.SetRow(surface,1);gameHost.Children.Add(surface);
        var foot=Ui.Columns([-1,-1],Ui.Text("ESC  일시정지    ·    F11  전체 화면",11,Ui.Dim),Ui.Text($"RAW INPUT  ·  WASAPI  ·  {runSettings.InputOffsetUs/1000.0:+0.###;-0.###;0} ms",10,Ui.Dim));((TextBlock)foot.Children[1]).HorizontalAlignment=HorizontalAlignment.Right;foot.Margin=new Thickness(34,10,34,10);Grid.SetRow(foot,2);gameHost.Children.Add(foot);root.Children.Add(gameHost);
    }
    void OnFrame(object? sender,EventArgs e)
    {
        if(closed)return;
        DrainRawKeys();
        if(calibrating){UpdateCalibration();return;}
        if(engine==null||paused||resuming||finishing)return;
        var now=audio.SongTimeUs();
        if(replayRecord!=null){var inputs=replayRecord.Summary.Inputs;while(replayCursor<inputs.Count&&inputs[replayCursor].TimeUs<=now){var input=inputs[replayCursor++];surface!.Pressed[input.Lane]=input.Pressed;Feed(engine.Input(input.Lane,input.Pressed,input.TimeUs),now);}}
        Feed(engine.Advance(now),now);surface!.TimeUs=now;surface.InvalidateVisual();
        if(lastHud==long.MinValue||now-lastHud>=100_000){gameTime!.Text=runArcade?.Kind=="silence"&&runArcade.PhaseAt(now)=="silent"?"무음 8박 · 박자를 유지하세요":now<runFrom?"READY":$"{Ui.Time(Math.Max(0,(now-runFrom)/1_000_000.0))}  /  {Ui.Time((runTo-runFrom)/1_000_000.0)}";lastHud=now;}
        var grace=(long)((Math.Max(0,runSettings.InputOffsetUs)+120_000m*runSettings.WindowScale+100_000)*runRate);if(now>=runTo+grace)FinishGame(true);
    }
    void DrainRawKeys()
    {
        while(keyQueue.TryDequeue(out var key)){
            if(calibrating){if(key.Pressed&&!calibrationHeld.Contains(key.VirtualKey)){calibrationHeld.Add(key.VirtualKey);CalibrationTap(key);}if(!key.Pressed)calibrationHeld.Remove(key.VirtualKey);continue;}
            if(engine==null||paused||resuming||finishing||replayRecord!=null)continue;var lane=Array.IndexOf(runSettings.Keys,key.VirtualKey);if(lane<0)continue;var t=audio.SongTimeUs(key.QpcTicks);
            if(runArcade!=null&&!runArcade.ActiveLanes.Contains(lane))continue;
            if(key.Pressed&&runArcade?.Kind is "echo" or "route") {var corrected=t-runSettings.InputOffsetUs;if(corrected<0||corrected%(runArcade.BeatUs*runArcade.RoundBeats)<runArcade.BeatUs*3.75)continue;}
            if(surface!.Pressed[lane]==key.Pressed)continue;surface.Pressed[lane]=key.Pressed;if(key.Pressed&&t>=runFrom&&runArcade?.Kind!="silence")audio.Hit(lane);Feed(engine.Input(lane,key.Pressed,t),audio.SongTimeUs());
        }
    }
    void Feed(IReadOnlyList<Judgment> judgments,long now){if(judgments.Count>0&&surface!=null){surface.LastJudgment=judgments.Last();surface.LastJudgmentAt=now;}}
    void PauseGame()
    {
        if(engine==null||paused||finishing||resuming)return;DrainRawKeys();var t=audio.SongTimeUs();if(replayRecord==null)for(int lane=0;lane<4;lane++)if(surface!.Pressed[lane]){engine.Input(lane,false,t);surface.Pressed[lane]=false;}audio.Pause();paused=true;hadPause=true;
        var inner=Ui.Stack(Ui.Label("TAKE A BREATH"),Ui.Gap(14),Ui.Text("잠시, 숨 고르기.",30,Ui.TextColor,true),Ui.Gap(12),Ui.Text("준비가 되면 다시 이어서 플레이하세요.",13,Ui.Muted),Ui.Gap(30),Ui.Button("계속 플레이    →",()=>_=ResumeGame(),true),Ui.Gap(10),Ui.Button("처음부터 다시",RestartGame),Ui.Gap(10),Ui.Button("플레이 마치기",()=>FinishGame(false)));
        var card=Ui.Card(inner,padding:35);card.Width=420;card.HorizontalAlignment=HorizontalAlignment.Center;card.VerticalAlignment=VerticalAlignment.Center;
        pauseOverlay=new Border{Background=new SolidColorBrush(Color.FromArgb(225,12,16,22)),Child=card};Grid.SetRowSpan(pauseOverlay,3);gameHost!.Children.Add(pauseOverlay);
    }
    async Task ResumeGame()
    {
        if(!paused||resuming||engine==null)return;resuming=true;
        try{var resumeAt=audio.SongTimeUs();var label=Ui.Text("준비",75,Ui.Lime,true);label.HorizontalAlignment=HorizontalAlignment.Center;label.VerticalAlignment=VerticalAlignment.Center;pauseOverlay!.Child=label;await audio.ResumeAsync();while(audio.SongTimeUs()<resumeAt){label.Text=Math.Max(1,(int)Math.Ceiling((resumeAt-audio.SongTimeUs())/1_000_000.0/(double)runRate)).ToString();await Task.Delay(16);if(engine==null)return;}while(keyQueue.TryDequeue(out _)){}paused=false;gameHost!.Children.Remove(pauseOverlay);pauseOverlay=null;surface!.Focus();}
        catch(Exception ex){Notify("재생을 이어갈 수 없습니다. "+ex.Message);FinishGame(false);}finally{resuming=false;}
    }
    void RestartGame(){var song=runSong!;var chart=runChart!;var mode=runMode;var rate=runRate;var from=runFrom;var to=runTo;var arcade=runArcade;var replay=replayRecord;audio.Stop();engine=null;paused=false;RemoveGameHost();_=StartGame(song,chart,mode,rate,from,to,arcade,replay);}
    void FinishGame(bool completed)
    {
        if(engine==null||finishing)return;finishing=true;var summary=completed?engine.Finish():engine.Snapshot();audio.Stop();
        var record=new PlayRecord{SongId=runSong!.Id,ChartId=runChart!.Id,ChartVersion=runChart.Version,Difficulty=runChart.Difficulty,Mode=runMode,Rate=runRate,InputOffsetUs=runSettings.InputOffsetUs,Settings=runSettings.Clone(),Summary=summary,Analysis=Analytics.Analyze(summary,runChart),Completed=completed,Interrupted=!completed||hadPause,FromUs=runFrom,ToUs=runTo,Environment=new(){["clock"]=audio.Info.ClockSource,["device"]=audio.Info.DeviceName,["sampleRate"]=audio.Info.SampleRate.ToString(),["arcade"]=runArcade?.Kind??"",["runtime"]="WPF / .NET 10 / Windows Raw Input"}};
        var wasReplay=replayRecord!=null;var wasTutorial=runMode=="tutorial";engine=null;paused=false;RemoveGameHost();
        if(!wasReplay&&!wasTutorial){try{store.AddRecord(record);}catch(Exception ex){Notify("기록 저장 실패: "+ex.Message);}if(record.Mode=="practice"&&record.Completed&&store.Settings.AutoStep){var recent=store.Records.Where(r=>r.ChartId==record.ChartId&&r.Mode=="practice"&&r.Rate==record.Rate&&r.Settings.WindowScale==record.Settings.WindowScale&&r.FromUs==record.FromUs&&r.ToUs==record.ToUs).OrderByDescending(r=>r.CreatedAt).Take(3).ToList();if(recent.Count==3&&recent.All(r=>r.Completed&&r.Summary.Accuracy>=95)){store.Settings.PracticeRate=Math.Min(1,record.Rate+.05m);SaveSettings();Notify($"안정적인 3회 연습! 다음 연습 속도를 {store.Settings.PracticeRate*100:0}%로 올렸어요.");}}}
        if(wasTutorial)ShowTutorialResult(summary,completed);else ShowResult(record,wasReplay);finishing=false;
        if(completed&&!wasReplay&&runMode=="practice"&&runRepeats>1)_=ScheduleRepeat(record,runRepeats-1);
    }
    async Task ScheduleRepeat(PlayRecord record,int remaining)
    {
        var generation=++repeatGeneration;var song=runSong!;var chart=runChart!;
        for(int i=3;i>0;i--){Notify($"{i}초 뒤 같은 구간을 이어서 연습합니다. 남은 반복 {remaining}회 · 다른 메뉴를 선택하면 반복을 멈춥니다.");await Task.Delay(1000);if(generation!=repeatGeneration||closed)return;}
        await StartGame(song,chart,"practice",store.Settings.AutoStep?store.Settings.PracticeRate:record.Rate,record.FromUs,record.ToUs,repeats:remaining);
    }
    void RemoveGameHost(){if(gameHost!=null)root.Children.Remove(gameHost);gameHost=null;pauseOverlay=null;surface=null;shell.Visibility=Visibility.Visible;}
    void StartTutorial(int step)
    {
        tutorialStep=step;var session=ContentLibrary.CreateTutorial(step);var p=Ui.Stack(Heading($"FIRST STEPS  /  {step+1:00} OF 04",session.Title,session.Description));
        p.Children.Add(Ui.Card(Ui.Stack(Ui.Text(string.Join("     ",Enumerable.Range(0,4).Select(KeyName)),40,Ui.Lime,true),Ui.Gap(26),Ui.Text(session.Description,19),Ui.Gap(24),Ui.Text("ESC로 언제든 일시정지할 수 있어요. 가이드는 일반 기록에 포함되지 않습니다.",13,Ui.Muted),Ui.Gap(30),Ui.Button("직접 해보기    →",()=>_=StartGame(content.Songs[0],session.Chart,"tutorial",1,0,session.Chart.DurationUs,session),true))));p.Margin=new Thickness(34);page.Content=p;
    }
    void ShowTutorialResult(RunSummary summary,bool completed){var p=Ui.Stack(Heading("FIRST STEPS",completed?"한 걸음 익숙해졌어요.":"다시 천천히 해봐요.",$"정확도 {summary.Accuracy:0.0}% · 최대 콤보 {summary.MaxCombo}"),Ui.Card(Ui.Stack(Ui.Text("노트가 판정선에 닿는 순간을 기억해 주세요.",21,Ui.TextColor,true),Ui.Gap(25),Ui.Button(tutorialStep<3?"다음 동작 배우기  →":"음악 고르러 가기  →",()=>{if(tutorialStep<3)StartTutorial(tutorialStep+1);else Navigate("music");},true),Ui.Gap(12),Ui.Button("한 번 더 연습",()=>StartTutorial(tutorialStep)))));p.Margin=new Thickness(34);page.Content=p;}
}
