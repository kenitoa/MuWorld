using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using MuWorld.Native.Controls;
using MuWorld.Native.Core;

namespace MuWorld.Native;
public partial class MainWindow
{
    FrameworkElement RecordsPage()
    {
        var p=Ui.Stack(Heading("YOUR PROGRESS","어제보다, 한 박자 더.","점수 너머의 변화를 살펴보세요. 실제 플레이에서 쌓인 데이터만 보여 드립니다."));
        var runs=store.Records.Where(r=>r.Completed&&!r.Interrupted&&r.Mode=="standard").ToList();
        p.Children.Add(Ui.Columns([-1,16,-1,16,-1],Ui.Card(Ui.Metric("완료한 플레이",store.Records.Count(r=>r.Completed).ToString(),"모든 모드")),new Border(),Ui.Card(Ui.Metric("최고 정확도",runs.Count>0?$"{runs.Max(r=>r.Summary.Accuracy):0.00}%":"—","일반 플레이",Ui.Lime)),new Border(),Ui.Card(Ui.Metric("최고 콤보",runs.Count>0?runs.Max(r=>r.Summary.MaxCombo).ToString():"—","일반 플레이"))));
        p.Children.Add(Ui.Gap(22));
        if(store.Records.Count==0){p.Children.Add(Ui.Card(Ui.Stack(Ui.Gap(20),Ui.Text("첫 리듬이, 첫 기록이 됩니다.",25,Ui.TextColor,true),Ui.Gap(12),Ui.Text("곡을 완료하면 입력 편차, 레인별 안정성, 패턴별 약점과 추천 연습을 확인할 수 있어요.",14,Ui.Muted),Ui.Gap(25),Ui.Button("음악 고르러 가기    →",()=>Navigate("music"),true),Ui.Gap(20))));return p;}
        var records=store.Records.OrderByDescending(r=>r.CreatedAt).ToList();
        var comparable=Analytics.CompareRuns(records).FirstOrDefault()??new List<PlayRecord>();
        p.Children.Add(Ui.Card(Ui.Stack(Ui.Text("같은 조건에서의 변화",20,Ui.TextColor,true),Ui.Gap(7),Ui.Text("최근 일반 채보 · 동일 장치 / 키 / 보정 / 속도 / 판정 · "+comparable.Count+"회",12,Ui.Muted),new AnalyticsPlot{Mode="growth",Records=comparable,Height=190})));p.Children.Add(Ui.Gap(23));
        var badges=new[]{("첫걸음",store.Records.Any(r=>r.Completed)),("흔들림 없이",runs.Any(r=>r.Summary.MaxCombo>=100)),("클린 플레이",runs.Any(r=>r.Summary.Counts.GetValueOrDefault("MISS")==0)),("정교한 리듬",runs.Any(r=>r.Summary.Accuracy>=95)),("꾸준한 연습",store.Records.Count(r=>r.Mode=="practice"&&r.Completed)>=5)};
        var badgeRow=Ui.Row();foreach(var (title,earned) in badges)badgeRow.Children.Add(Ui.Margin(Ui.Tag((earned?"✓  ":"○  ")+title,earned?Ui.Lime:Ui.Dim),0,0,10));p.Children.Add(badgeRow);p.Children.Add(Ui.Gap(28));p.Children.Add(Ui.Text("플레이 기록",22,Ui.TextColor,true));p.Children.Add(Ui.Gap(15));
        foreach(var r in records.Take(100)){var s=content.Songs.FirstOrDefault(s=>s.Id==r.SongId);var title=s?.Title??r.ChartId;var desc=$"{r.CreatedAt.ToLocalTime():MM.dd HH:mm}  ·  {ModeName(r.Mode)}  ·  {r.Difficulty.ToUpper()}  ·  {r.Rate*100:0}%  ·  판정 ×{r.Settings.WindowScale:0.##}";
            var b=Ui.Button("",()=>ShowResult(r));b.Content=Ui.Columns([-1,160,70],Ui.Stack(Ui.Text(title,16,Ui.TextColor,true),Ui.Gap(6),Ui.Text(desc,12,Ui.Muted)),Ui.Text($"{r.Summary.Accuracy:0.00}%",24,Ui.Lime,true),Ui.Text(r.Completed?(r.Interrupted?"일시정지":"분석 →"):"중단됨",12,Ui.Muted));b.HorizontalContentAlignment=HorizontalAlignment.Stretch;b.Margin=new Thickness(0,0,9,9);p.Children.Add(b);}
        return p;
    }
    static string ModeName(string mode)=>mode switch{"practice"=>"연습","arcade"=>"아케이드","tutorial"=>"가이드",_=>"일반"};
    void ShowResult(PlayRecord record,bool replay=false)
    {
        var a=record.Analysis;var s=record.Summary;var song=content.Songs.FirstOrDefault(t=>t.Id==record.SongId);currentPage="records";RenderNavigation();
        var p=Ui.Stack(Heading(replay?"REPLAY FINISHED":"SESSION REPORT",record.Completed?"좋은 리듬이었어요.":"잠깐 쉬어가도 괜찮아요.",$"{song?.Title??record.ChartId}  ·  {ModeName(record.Mode)}  ·  {record.Difficulty.ToUpper()}  ·  속도 {record.Rate*100:0}%  ·  판정 폭 ×{record.Settings.WindowScale:0.##}{(record.Interrupted?" · 일시정지/중단 포함":"")}"));
        var actions=Ui.Row(Ui.Button("다시 플레이",()=>ReplayChart(record,false),true),Ui.Margin(Ui.Button("입력 리플레이",()=>ReplayChart(record,true)),9),Ui.Margin(Ui.Button("CSV 내보내기",()=>ExportCsv(record)),9),Ui.Margin(Ui.Button("기록 목록",()=>Navigate("records")),9));p.Children.Add(actions);p.Children.Add(Ui.Gap(23));
        var metrics=Ui.Columns([-1,15,-1,15,-1,15,-1],Ui.Card(Ui.Metric("ACCURACY",$"{s.Accuracy:0.00}%","전체 판정의 가중 정확도",Ui.Lime)),new Border(),Ui.Card(Ui.Metric("MAX COMBO",s.MaxCombo.ToString(),$"{s.Score:N0} POINTS")),new Border(),Ui.Card(Ui.Metric("평균 입력 편차",Ui.Ms(a.Timing.MeanMs,true),$"EARLY {a.Timing.Early}  /  LATE {a.Timing.Late}")),new Border(),Ui.Card(Ui.Metric("안정성 · 표준편차",Ui.Ms(a.Timing.StdMs),$"유효 타격 {a.Timing.Count}개")));p.Children.Add(metrics);p.Children.Add(Ui.Gap(23));
        var grades=Ui.Row();foreach(var grade in new[]{"PERFECT","GREAT","GOOD","BAD","MISS"}){grades.Children.Add(Ui.Margin(Ui.Text($"{grade}   {s.Counts.GetValueOrDefault(grade)}",13,grade=="PERFECT"?Ui.Lime:Ui.Muted),0,0,28));}p.Children.Add(Ui.Card(grades,padding:18));p.Children.Add(Ui.Gap(23));
        var distribution=Ui.Stack(Ui.Text("입력 타이밍 분포",20,Ui.TextColor,true),Ui.Gap(6),Ui.Text("0 ms에 가까울수록 정확해요. 놓친 노트와 홀드 끝은 제외합니다.",12,Ui.Muted),new AnalyticsPlot{Analysis=a,Summary=s,Mode="histogram",Height=220},Ui.Columns([-1,-1,-1],Ui.Metric("중앙값",Ui.Ms(a.Timing.MedianMs,true)),Ui.Metric("평균 절대편차",Ui.Ms(a.Timing.MaeMs)),Ui.Metric("P95 절대편차",Ui.Ms(a.Timing.P95Ms))));
        var lanes=Ui.Stack(Ui.Text("레인별 안정성",20,Ui.TextColor,true),new AnalyticsPlot{Analysis=a,Summary=s,Mode="lanes",Height=220});foreach(var l in a.Lanes)lanes.Children.Add(Ui.Margin(Ui.Columns([50,-1,-1],Ui.Text(System.Windows.Input.KeyInterop.KeyFromVirtualKey(record.Settings.Keys[l.Lane]).ToString(),15,Ui.Lavender,true),Ui.Text($"놓침 {l.Misses} / {l.Count}",12,Ui.Muted),Ui.Text(Ui.Ms(l.MeanMs,true),12,Ui.Muted)),0,5,0,5));
        p.Children.Add(Ui.Columns([-1.5,22,-1],Ui.Card(distribution),new Border(),Ui.Card(lanes)));p.Children.Add(Ui.Gap(23));
        p.Children.Add(Ui.Card(Ui.Stack(Ui.Text("곡의 어느 부분이 어려웠을까요?",20,Ui.TextColor,true),new AnalyticsPlot{Analysis=a,Summary=s,Mode="timeline",Height=200})));p.Children.Add(Ui.Gap(23));
        var patterns=Ui.Stack(Ui.Text("패턴별 분석",20,Ui.TextColor,true),Ui.Gap(18));foreach(var pattern in a.Patterns)patterns.Children.Add(Ui.Margin(Ui.Columns([-1,-1,-1],Ui.Text(pattern.Pattern,13),Ui.Text($"놓침 {pattern.Misses} / {pattern.Count}",12,Ui.Muted),Ui.Text(Ui.Ms(pattern.MeanMs,true),12,Ui.Muted)),0,6,0,6));patterns.Children.Add(Ui.Rule());patterns.Children.Add(Ui.Text($"홀드 유지율  {(a.Holds.SuccessRate.HasValue?$"{a.Holds.SuccessRate:0.0}%":"—")}  ·  끊김 {a.Holds.Broken} / {a.Holds.Total}\n동시치기 간격 중앙값  {Ui.Ms(a.Chords.MedianSpreadMs)}\n추가 입력  {s.ExtraInputs}회  ·  MAD  {Ui.Ms(a.Timing.MadMs)}",13,Ui.Muted));
        var recs=Ui.Stack(Ui.Text("다음 연습을 위한 힌트",20,Ui.TextColor,true),Ui.Gap(18));foreach(var rec in a.Recommendations.Take(3)){recs.Children.Add(Ui.Text(rec.Title,15,Ui.Lime,true));recs.Children.Add(Ui.Gap(7));recs.Children.Add(Ui.Text(rec.Body,13,Ui.Muted));if(song!=null&&record.Mode!="arcade"){recs.Children.Add(Ui.Gap(9));recs.Children.Add(Ui.Button("이 구간 연습하기  →",()=>_=StartGame(song,ResolveChart(record),"practice",store.Settings.PracticeRate,rec.FromUs,Math.Min(rec.ToUs,ResolveChart(record).DurationUs))));}recs.Children.Add(Ui.Gap(18));}
        p.Children.Add(Ui.Columns([-1,22,-1],Ui.Card(patterns),new Border(),Ui.Card(recs)));p.Children.Add(Ui.Gap(20));p.Children.Add(Ui.Text($"입력 보정 {record.InputOffsetUs/1000.0:+0.###;-0.###;0} ms  ·  채보 v{record.ChartVersion}  ·  {record.Environment.GetValueOrDefault("clock","WASAPI")}\n소프트웨어 수신 시각 기준의 분석입니다. 키보드 스캔·디스플레이·물리적 출력 지연은 별도 측정이 필요합니다.",11,Ui.Dim));
        p.Margin=new Thickness(34,12,34,20);page.Content=new ScrollViewer{Content=p};
    }
    Chart ResolveChart(PlayRecord r)=>content.Drills.FirstOrDefault(c=>c.Id==r.ChartId)??(r.Mode=="arcade"?ContentLibrary.CreateArcade(r.Environment.GetValueOrDefault("arcade","echo")).Chart:content.GetChart(r.SongId,r.Difficulty));
    void ReplayChart(PlayRecord r,bool replay){var song=content.Songs.FirstOrDefault(s=>s.Id==r.SongId)??content.Songs[0];var arcade=r.Mode=="arcade"?ContentLibrary.CreateArcade(r.Environment.GetValueOrDefault("arcade","echo")):null;_=StartGame(song,ResolveChart(r),r.Mode,r.Rate,r.FromUs,r.ToUs,arcade,replay?r:null);}
    void ExportCsv(PlayRecord r){var dialog=new SaveFileDialog{Title="판정 분석 CSV 저장",FileName=$"MuWorld-{r.CreatedAt:yyyyMMdd-HHmm}.csv",Filter="CSV 파일|*.csv"};if(dialog.ShowDialog(this)==true){File.WriteAllText(dialog.FileName,Analytics.ToCsv(r),new System.Text.UTF8Encoding(false));Notify("판정별 데이터를 CSV로 저장했습니다.");}}
}
