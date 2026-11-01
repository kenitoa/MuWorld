using System.Windows;
using System.Windows.Controls;
using MuWorld.Native.Controls;
using MuWorld.Native.Core;

namespace MuWorld.Native;
public partial class MainWindow
{
    FrameworkElement LibraryPage()
    {
        var all=Ui.Stack(Ui.Stack(Ui.Label("MUSIC LIBRARY"),Ui.Gap(6),Ui.Text("나만의 리듬으로.",30,Ui.TextColor,true),Ui.Gap(8),Ui.Text("서두르지 않아도 괜찮아요. 좋아하는 음악에서 시작해요.",13,Ui.Muted),Ui.Gap(20)));
        var heroText=Ui.Stack(Ui.Tag("TODAY’S PICK  /  ORIGINAL TRACK"),Ui.Gap(12),Ui.Text("오늘은, 가볍게 시작해요.",23,Ui.TextColor,true),Ui.Gap(10),Ui.Text("유리처럼 맑은 건반, 편안한 100 BPM.\nGlasshouse와 함께 첫 리듬을 찾아보세요.",13,Ui.Muted));
        var heroArt=new AlbumArtwork{Song=content.Songs[0],Width=92,Height=92};
        var hero=Ui.Card(Ui.Columns([-1,170],heroText,heroArt),"#20272C",20);
        var list=Ui.Stack(hero,Ui.Gap(27));
        var searchBox=new TextBox{Text=search,Width=185,ToolTip="곡 제목 또는 장르 검색"}; var searchWrap=new Grid();searchWrap.Children.Add(searchBox);var placeholder=Ui.Text("음악 검색",12,Ui.Dim);placeholder.IsHitTestVisible=false;placeholder.Margin=new Thickness(12,0,0,0);placeholder.Visibility=search.Length==0?Visibility.Visible:Visibility.Collapsed;searchWrap.Children.Add(placeholder);
        var rows=new StackPanel();
        void FillRows(){rows.Children.Clear();foreach(var song in content.Songs.Where(s=>(!onlyFavorites||store.Favorites.Contains(s.Id))&&(s.Title.Contains(search,StringComparison.OrdinalIgnoreCase)||s.Genre.Contains(search,StringComparison.OrdinalIgnoreCase)))){
            var name=Ui.Stack(Ui.Text(song.Title,16,Ui.TextColor,true),Ui.Gap(6),Ui.Text(song.Genre,11,Ui.Muted));
            var record=store.Records.Where(r=>r.SongId==song.Id&&r.Mode=="standard"&&r.Completed&&!r.Interrupted).OrderByDescending(r=>r.Summary.Accuracy).FirstOrDefault();
            var row=Ui.Columns([54,-1,65,65,72],new AlbumArtwork{Song=song,Width=38,Height=38},name,Ui.Text(song.Bpm.ToString(),12,Ui.Muted),Ui.Text(Ui.Time(song.DurationSec),12,Ui.Muted),Ui.Text(record==null?"—":$"{record.Summary.Accuracy:0.0}%",12,record==null?Ui.Dim:Ui.Lime));
            var b=Ui.Button("",()=>{selected=song;Navigate("music");});b.Content=row;b.HorizontalContentAlignment=HorizontalAlignment.Stretch;b.Padding=new Thickness(12,6,12,6);b.Margin=new Thickness(0,0,5,7);b.Background=Ui.Brush(selected.Id==song.Id?"#272F32":"#191F29");b.BorderBrush=Ui.Brush(selected.Id==song.Id?"#647052":"#272F3C");rows.Children.Add(b);
        }if(rows.Children.Count==0)rows.Children.Add(Ui.Card(Ui.Text("검색 조건에 맞는 음악이 없어요.",14,Ui.Muted)));}
        searchBox.TextChanged+=(_,_)=>{search=searchBox.Text;placeholder.Visibility=search.Length==0?Visibility.Visible:Visibility.Collapsed;FillRows();};
        var filters=Ui.Row(Ui.Button("전체 음악",()=>{onlyFavorites=false;Navigate("music");}),Ui.Margin(Ui.Button("♡ 즐겨찾기",()=>{onlyFavorites=true;Navigate("music");}),8));
        list.Children.Add(Ui.Columns([-1,185],filters,searchWrap));list.Children.Add(Ui.Gap(18));
        list.Children.Add(Ui.Margin(Ui.Columns([54,-1,65,65,72],Ui.Label(""),Ui.Label("TRACK  /  05"),Ui.Label("BPM"),Ui.Label("TIME"),Ui.Label("BEST")),12,0,17,10));FillRows();list.Children.Add(rows);
        var detail=Ui.Stack(new AlbumArtwork{Song=selected,Height=150},Ui.Gap(12),Ui.Label("SELECTED TRACK"),Ui.Gap(9),Ui.Text(selected.Title,23,Ui.TextColor,true),Ui.Gap(7),Ui.Text(selected.Genre,12,Ui.Muted),Ui.Rule());
        var chart=content.GetChart(selected.Id,difficulty);detail.Children.Add(Ui.Columns([-1,-1,-1],MiniMetric("BPM",selected.Bpm.ToString()),MiniMetric("LENGTH",Ui.Time(selected.DurationSec)),MiniMetric("NOTES",chart.Notes.Count.ToString())));detail.Children.Add(Ui.Rule());detail.Children.Add(Ui.Label("DIFFICULTY"));detail.Children.Add(Ui.Gap(10));
        var diffs=Ui.Columns([-1,-1,-1]);foreach(var (id,i) in new[]{("easy",0),("normal",1),("hard",2)}){var b=Ui.Button(id.ToUpper(),()=>{difficulty=id;Navigate("music");},difficulty==id);b.FontSize=10;b.Padding=new Thickness(6,10,6,10);b.Margin=new Thickness(i==0?0:5,0,0,0);Grid.SetColumn(b,i);diffs.Children.Add(b);}detail.Children.Add(diffs);detail.Children.Add(Ui.Gap(9));detail.Children.Add(Ui.Text($"LEVEL {selected.Level.GetValueOrDefault(difficulty)}   ·   4 KEYS   ·   {string.Join("  ",Enumerable.Range(0,4).Select(KeyName))}",11,Ui.Dim));detail.Children.Add(Ui.Gap(22));
        var start=Ui.Button("플레이 시작    →",()=>_=StartGame(selected,chart),true);start.Padding=new Thickness(20,15,20,15);var favorite=Ui.Button(store.Favorites.Contains(selected.Id)?"♥":"♡",()=>{if(!store.Favorites.Add(selected.Id))store.Favorites.Remove(selected.Id);store.Save();Navigate("music");});favorite.ToolTip="즐겨찾기";favorite.Padding=new Thickness(12);detail.Children.Add(Ui.Columns([-1,8,46],start,new Border(),favorite));
        var columns=Ui.Columns([-1,28,315],list,new Border(),Ui.Card(detail));all.Children.Add(columns);return all;
    }
    FrameworkElement MiniMetric(string label,string value)=>Ui.Stack(Ui.Label(label),Ui.Gap(9),Ui.Text(value,26,Ui.TextColor,true));
    FrameworkElement PracticePage()
    {
        var p=Ui.Stack(Heading("PRACTICE STUDIO","조금씩, 더 정확하게.","느린 템포에서 익히고, 익숙해지면 한 단계 올라가세요."));
        var choices=Ui.Stack(Ui.Text("나의 연습 루틴",21,Ui.TextColor,true),Ui.Gap(8),Ui.Text("선택한 곡의 구간을 같은 음높이로 반복합니다.",13,Ui.Muted),Ui.Gap(22));
        var songs=Ui.Row();foreach(var s in content.Songs){var b=Ui.Button(s.Title,()=>{selected=s;Navigate("practice");},s==selected);b.Padding=new Thickness(10,9,10,9);b.FontSize=11;b.Margin=new Thickness(0,0,6,0);songs.Children.Add(b);}choices.Children.Add(new ScrollViewer{Content=songs,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollBarVisibility=ScrollBarVisibility.Disabled});choices.Children.Add(Ui.Gap(21));
        choices.Children.Add(Choice("채보 난이도",new[]{("easy","EASY"),("normal","NORMAL"),("hard","HARD")},difficulty,v=>{difficulty=v;Navigate("practice");}));
        choices.Children.Add(Ui.Gap(17));choices.Children.Add(SliderField("재생 속도",50,100,(double)store.Settings.PracticeRate*100,5,v=>{store.Settings.PracticeRate=(decimal)v/100;SaveSettings();},"% · 음높이 유지"));
        choices.Children.Add(Ui.Columns([-1,20,-1],NumberField("시작 구간 · 초",store.Settings.PracticeFromSec,0,selected.DurationSec-1,v=>store.Settings.PracticeFromSec=v),new Border(),NumberField("끝 구간 · 초",store.Settings.PracticeToSec,1,selected.DurationSec,v=>store.Settings.PracticeToSec=v)));
        choices.Children.Add(Ui.Gap(20));choices.Children.Add(Choice("판정 폭",new[]{("0.25","정밀 · ±5 ms"),("1","기본 · ±20 ms"),("1.5","여유 · ±30 ms"),("2","입문 · ±40 ms")},store.Settings.WindowScale.ToString(System.Globalization.CultureInfo.InvariantCulture),v=>{store.Settings.WindowScale=decimal.Parse(v,System.Globalization.CultureInfo.InvariantCulture);SaveSettings();Navigate("practice");}));
        choices.Children.Add(Ui.Gap(16));choices.Children.Add(Choice("자동 반복",new[]{("1","1회"),("3","3회"),("5","5회")},practiceRepeats.ToString(),v=>{practiceRepeats=int.Parse(v);Navigate("practice");}));choices.Children.Add(Ui.Gap(12));choices.Children.Add(Check("박자 가이드 함께 듣기",store.Settings.Metronome,v=>{store.Settings.Metronome=v;SaveSettings();}));choices.Children.Add(Check("3회 연속 정확도 95% 달성 시 속도 +5%",store.Settings.AutoStep,v=>{store.Settings.AutoStep=v;SaveSettings();}));choices.Children.Add(Ui.Gap(20));
        choices.Children.Add(Ui.Button("이 구간 연습하기    →",()=>{var from=(long)(store.Settings.PracticeFromSec*1_000_000);var to=(long)(Math.Min(selected.DurationSec,store.Settings.PracticeToSec)*1_000_000);if(to<=from){Notify("끝 구간은 시작 구간보다 길어야 합니다.");return;}_=StartGame(selected,content.GetChart(selected.Id,difficulty),"practice",store.Settings.PracticeRate,from,to,repeats:practiceRepeats);},true));
        var guide=Ui.Stack(Ui.Label("SMALL STEPS, REAL PROGRESS"),Ui.Gap(15),Ui.Text("속도보다 먼저,\n편안한 리듬.",27,Ui.Lime,true),Ui.Gap(22),Ui.Text("01  70–80% 속도로 패턴 익히기\n\n02  입력 편차와 놓친 레인 확인하기\n\n03  같은 구간을 다시 연습하기\n\n04  안정되면 조금씩 속도 올리기",14,Ui.Muted),Ui.Rule(),Ui.Text("연습 기록은 일반 플레이와 구분됩니다. 각 기록에 속도, 판정 폭, 보정값이 함께 남아요.",12,Ui.Dim));
        p.Children.Add(Ui.Columns([-1,24,280],Ui.Card(choices),new Border(),Ui.Card(guide)));p.Children.Add(Ui.Gap(28));p.Children.Add(Ui.Text("패턴별 작은 연습",22,Ui.TextColor,true));p.Children.Add(Ui.Gap(9));p.Children.Add(Ui.Text("교대 · 동시치기 · 홀드 · 리듬 밀도. 12개의 짧은 구간으로 집중 연습해요.",13,Ui.Muted));p.Children.Add(Ui.Gap(20));
        var drills=new System.Windows.Controls.Primitives.UniformGrid{Columns=3};foreach(var d in content.Drills){var s=content.GetSong(d.SongId);var c=Ui.Stack(Ui.Label(d.Focus.ToUpper()),Ui.Gap(10),Ui.Text(d.Title,17,Ui.TextColor,true),Ui.Gap(9),Ui.Text(d.Description,12,Ui.Muted),Ui.Gap(17),Ui.Button("연습 시작  →",()=>_=StartGame(s,d,"practice",store.Settings.PracticeRate,d.FromUs,d.ToUs>0?d.ToUs:d.DurationUs)));drills.Children.Add(Ui.Margin(Ui.Card(c),0,0,12,12));}p.Children.Add(drills);return p;
    }
    FrameworkElement ArcadePage()
    {
        var p=Ui.Stack(Heading("RHYTHM ARCADE","리듬으로 노는 다섯 가지 방법.","짧고 가벼운 도전 속에서도 타이밍은 정확하게. 같은 판정 엔진으로 플레이해요."));
        var cards=new System.Windows.Controls.Primitives.UniformGrid{Columns=3};
        foreach(var (kind,num,title,desc,color) in new[]{("echo","01","에코 메모리","4박을 듣고, 다음 4박에 되돌려 주세요.\n청음과 리듬 기억을 함께 연습해요.",Ui.Lavender),("silence","02","사일런트 스텝","음악이 멈춰도, 내 박자는 계속.\n침묵 구간의 박자 감각을 확인해요.",Ui.Lime),("workshop","03","리듬 공방","두 레인에서 재료를 두드리고 이어 붙여요.\n짧게 누르기와 길게 누르기를 익혀요.","#E9BC9F"),("guard","04","펄스 가드","박자에 맞춰 두 방향을 방어해요.\n좌우 전환의 안정감을 길러요.","#A5CBD8"),("route","05","비트 루트","네 방향의 리듬으로 길을 만들어요.\n정확한 입력으로 한 칸씩 나아가요.","#D3ACC1")}) {
            var start=Ui.Button("플레이    →",()=>{var session=ContentLibrary.CreateArcade(kind);_=StartGame(selected,session.Chart,"arcade",1,0,session.Chart.DurationUs,session);},true);
            var c=Ui.Stack(Ui.Text(num,44,color,true),Ui.Gap(31),Ui.Text(title,23,Ui.TextColor,true),Ui.Gap(12),Ui.Text(desc,13,Ui.Muted),Ui.Gap(32),start);cards.Children.Add(Ui.Margin(Ui.Card(c),0,0,16,16));
        }
        cards.Children.Add(Ui.Margin(Ui.Card(Ui.Stack(Ui.Label("TAKE YOUR TIME"),Ui.Gap(35),Ui.Text("경쟁보다,\n나의 리듬에 집중.",25,Ui.Lime,true),Ui.Gap(20),Ui.Text("모든 모드는 오프라인으로 동작해요.\n성장 기록은 이 기기에 보관합니다.",13,Ui.Muted))),0,0,16,16));p.Children.Add(cards);return p;
    }
    FrameworkElement Choice(string title,(string Id,string Label)[] items,string value,Action<string> changed)
    {
        var row=Ui.Row();foreach(var item in items){var b=Ui.Button(item.Label,()=>changed(item.Id),value==item.Id);b.FontSize=12;b.Padding=new Thickness(12,6,12,6);b.Margin=new Thickness(0,0,7,0);row.Children.Add(b);}return Ui.Stack(Ui.Label(title),Ui.Gap(9),row);
    }
    FrameworkElement SliderField(string title,double min,double max,double value,double step,Action<double> change,string unit="")
    {
        var label=Ui.Text($"{value:0.##}{unit}",12,Ui.Lime);label.HorizontalAlignment=HorizontalAlignment.Right;var s=new Slider{Minimum=min,Maximum=max,Value=value,TickFrequency=step,SmallChange=step,LargeChange=step*5};s.ValueChanged+=(_,_)=>{label.Text=$"{s.Value:0.##}{unit}";change(s.Value);};return Ui.Stack(Ui.Columns([-1,-1],Ui.Text(title,13,Ui.Muted),label),s,Ui.Gap(12));
    }
    FrameworkElement NumberField(string title,double value,double min,double max,Action<double> change)
    {
        var box=new TextBox{Text=value.ToString("0.###",System.Globalization.CultureInfo.InvariantCulture)};
        void Commit(){if(double.TryParse(box.Text,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var n)&&double.IsFinite(n)&&n>=min&&n<=max){change(n);SaveSettings();box.BorderBrush=Ui.Brush("#394353");}else{box.BorderBrush=Ui.Brush("#E6AB9F");Notify($"{title}: {min}–{max} 범위의 숫자를 입력해 주세요.");}}
        box.LostKeyboardFocus+=(_,_)=>Commit();box.KeyDown+=(_,e)=>{if(e.Key==System.Windows.Input.Key.Enter){Commit();e.Handled=true;}};return Ui.Stack(Ui.Label(title),Ui.Gap(9),box);
    }
    CheckBox Check(string title,bool value,Action<bool> changed){var b=new CheckBox{Content=title,IsChecked=value};b.Checked+=(_,_)=>changed(true);b.Unchecked+=(_,_)=>changed(false);return b;}
}
