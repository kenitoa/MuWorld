using System.Windows;
using System.Windows.Controls;
using MuWorld.Native.Audio;

namespace MuWorld.Native;
public partial class MainWindow
{
    bool calibrating;
    bool calibrationPreparing;
    int calibrationGeneration;
    readonly HashSet<int> calibrationHeld=[];
    readonly Dictionary<int,long> calibrationTaps=[];
    TextBlock? calibrationProgress,calibrationBeat;
    const long calibrationFirst=1_000_000,calibrationInterval=500_000,calibrationEnd=17_500_000;
    async Task StartCalibration()
    {
        if(starting||engine!=null||calibrating)return;starting=true;calibrationPreparing=true;var generation=++calibrationGeneration;
        var p=Ui.Stack(Heading("TIMING CALIBRATION","귀로 듣고, 박자에 맞춰.","32번의 소리를 들으며 SPACE를 누르세요. 손의 평균적인 반응까지 포함한 보정값을 제안합니다."));
        calibrationProgress=Ui.Text("오디오 준비 중…",15,Ui.Muted);calibrationProgress.HorizontalAlignment=HorizontalAlignment.Center;calibrationBeat=Ui.Text("32",86,Ui.Lime,true);calibrationBeat.HorizontalAlignment=HorizontalAlignment.Center;
        p.Children.Add(Ui.Card(Ui.Stack(Ui.Gap(30),calibrationBeat,Ui.Gap(25),calibrationProgress,Ui.Gap(24),Ui.Text("소리가 들리는 순간을 기준으로 누르세요. 화면은 박자 판정 기준이 아닙니다.",14,Ui.Muted),Ui.Gap(25),Ui.Button("보정 취소",()=>{CancelCalibration();Navigate("settings");}),Ui.Gap(25))));p.Margin=new Thickness(34);page.Content=p;
        try{calibrationTaps.Clear();calibrationHeld.Clear();audio.Volume=store.Settings.Volume;var cues=Enumerable.Range(0,32).Select(i=>new AudioCue { TimeUs=calibrationFirst+i*calibrationInterval, Lane=0, DurationUs=35_000, Accent=i%4==0 }).ToList();await audio.StartCuesAsync(cues,calibrationEnd,2);if(generation!=calibrationGeneration)return;while(keyQueue.TryDequeue(out _)){}calibrating=true;}
        catch(Exception ex){if(generation==calibrationGeneration){CancelCalibration();Navigate("settings");Notify("보정을 시작하지 못했습니다. "+ex.Message);}}finally{if(generation==calibrationGeneration){starting=false;calibrationPreparing=false;}}
    }
    void CalibrationTap(RawKeyEvent key){if(key.VirtualKey!=0x20)return;var time=audio.SongTimeUs(key.QpcTicks);var beat=(int)Math.Round((time-calibrationFirst)/(double)calibrationInterval,MidpointRounding.AwayFromZero);if(beat is <0 or >=32||calibrationTaps.ContainsKey(beat))return;var delta=time-(calibrationFirst+beat*calibrationInterval);if(Math.Abs(delta)>240_000)return;calibrationTaps[beat]=delta;}
    void UpdateCalibration(){var now=audio.SongTimeUs();var beat=(int)Math.Floor((now-calibrationFirst)/(double)calibrationInterval)+1;calibrationBeat!.Text=beat<=0?"준비":$"{Math.Clamp(beat,0,32):00}";calibrationProgress!.Text=$"32박 중 {Math.Clamp(beat,0,32)}박  ·  유효 입력 {calibrationTaps.Count}회";if(now>=calibrationEnd){calibrating=false;audio.Stop();ShowCalibrationResult();}}
    void CancelCalibration(string message=""){if(!calibrating&&!calibrationPreparing)return;calibrationGeneration++;calibrating=false;calibrationPreparing=false;starting=false;audio.Stop();calibrationHeld.Clear();if(message.Length>0)Notify(message);}
    void ShowCalibrationResult()
    {
        var deltas=calibrationTaps.Values.Order().ToArray();var valid=deltas.Length>=24;var median=deltas.Length==0?0:deltas.Length%2==1?deltas[deltas.Length/2]:(deltas[deltas.Length/2-1]+deltas[deltas.Length/2])/2;var deviations=deltas.Select(x=>Math.Abs(x-median)/1000.0).Order().ToArray();var mad=deviations.Length==0?0:deviations[deviations.Length/2];
        var p=Ui.Stack(Heading("CALIBRATION RESULT",valid?"나에게 맞는 타이밍.":"박자를 조금 더 모아볼게요.",valid?"현재 값을 자동으로 바꾸지 않습니다. 결과를 확인한 뒤 적용해 주세요.":"32박 중 24개 이상의 유효 입력이 필요합니다."));
        var card=Ui.Stack(Ui.Columns([-1,-1,-1],Ui.Metric("추천 입력 보정",$"{median/1000.0:+0.000;-0.000;0.000} ms","원시 입력 편차의 중앙값",Ui.Lime),Ui.Metric("유효 입력",$"{deltas.Length} / 32","±240 ms 안의 첫 입력"),Ui.Metric("안정성 · MAD",$"{mad:0.00} ms","중앙값 기준 절대편차")),Ui.Gap(30),Ui.Text("이 값에는 입력 장치·출력 장치·사람의 반응이 함께 포함됩니다. 손의 편차가 큰 경우 한 번 더 측정해 주세요. 하드웨어 지연의 직접 측정값은 아닙니다.",14,Ui.Muted),Ui.Gap(24));
        if(valid)card.Children.Add(Ui.Button("이 보정값 적용하기",()=>{store.Settings.InputOffsetUs=median;SaveSettings();Navigate("settings");Notify($"입력 보정 {median/1000.0:+0.000;-0.000;0.000} ms를 적용했습니다.");},true));card.Children.Add(Ui.Gap(12));card.Children.Add(Ui.Button("다시 측정",()=>_=StartCalibration()));card.Children.Add(Ui.Gap(12));card.Children.Add(Ui.Button("현재 설정 유지",()=>Navigate("settings")));p.Children.Add(Ui.Card(card));p.Margin=new Thickness(34);page.Content=p;
    }
}
