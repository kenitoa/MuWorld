using System.Collections.Concurrent;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using MuWorld.Native.Audio;
using MuWorld.Native.Core;

namespace MuWorld.Native;
public partial class MainWindow : Window
{
    readonly LocalStore store;
    readonly ContentLibrary content = new();
    readonly NativeAudio audio = new();
    readonly RawKeyboard keyboard = new();
    readonly ConcurrentQueue<RawKeyEvent> keyQueue = new();
    readonly Grid root = new();
    readonly ContentControl page = new();
    readonly Grid shell = new();
    readonly StackPanel nav = new();
    readonly TextBlock toast = Ui.Text("",13,Ui.Lime);
    readonly string[] args;
    Song selected;
    string difficulty="normal", currentPage="music",search="";
    bool onlyFavorites,starting,closed;
    int practiceRepeats=1,repeatGeneration;
    int bindingLane=-1;
    public MainWindow(string[] arguments)
    {
        args=arguments;
        var testDir=args.Contains("--capture") || args.Contains("--smoke") ? Path.Combine(Path.GetTempPath(),"MuWorldNative-QA") : null;
        store=new LocalStore(testDir);selected=content.Songs[0];
        Title="MuWorld · Native rhythm studio"; Width=Math.Min(1500,SystemParameters.WorkArea.Width-20);Height=Math.Min(920,SystemParameters.WorkArea.Height-20);MinWidth=1100;MinHeight=720;WindowStartupLocation=WindowStartupLocation.CenterScreen;
        Background=Ui.Brush(Ui.Bg);Foreground=Ui.Brush(Ui.TextColor);root.Background=Background;
        FontFamily=new FontFamily("pack://application:,,,/MuWorld;component/Assets/Fonts/#Pretendard, Segoe UI, Malgun Gothic");FontSize=14;
        try { Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/Assets/icon.ico")); } catch { }
        Content=root;BuildShell();Navigate("music");
        SourceInitialized+=(_,_)=>{var handle=new WindowInteropHelper(this).Handle;var dark=1;try{DwmSetWindowAttribute(handle,20,ref dark,sizeof(int));}catch{}keyboard.KeyChanged+=e=>keyQueue.Enqueue(e);keyboard.Start(handle);};
        Loaded+=async(_,_)=>{CompositionTarget.Rendering+=OnFrame;if(args.Contains("--capture")||args.Contains("--smoke"))await RunSmokeAsync();else {try{await audio.InitializeAsync();}catch(Exception ex){Notify("오디오 초기화: "+ex.Message);}if(store.LoadWarning!=null)Notify(store.LoadWarning);}};
        Deactivated+=(_,_)=>{if(engine!=null&&!paused&&!finishing)PauseGame();if(calibrating)CancelCalibration("창이 비활성화되어 보정을 중단했습니다.");};
        PreviewKeyDown+=OnWindowKey;
        audio.Faulted+=message=>Dispatcher.BeginInvoke(()=>{if(engine!=null)FinishGame(false);if(calibrating||calibrationPreparing){CancelCalibration();Navigate("settings");}Notify("오디오 재생을 중단했습니다. 장치를 확인한 뒤 다시 시작해 주세요. "+message);});
        keyboard.Faulted+=message=>Dispatcher.BeginInvoke(()=>{if(engine!=null)FinishGame(false);if(calibrating||calibrationPreparing)CancelCalibration();Notify("키보드 입력을 시작하지 못했습니다. 앱을 다시 실행해 주세요. "+message);});
        Closed+=(_,_)=>{closed=true;CompositionTarget.Rendering-=OnFrame;keyboard.Dispose();audio.Dispose();try{store.Save();}catch{}};
    }
    void BuildShell()
    {
        shell.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(204)});shell.ColumnDefinitions.Add(new ColumnDefinition());root.Children.Add(shell);
        var side=new DockPanel{LastChildFill=true};
        var logo=Ui.Stack(Ui.Text("∿  MuWorld",25,Ui.TextColor,true),Ui.Gap(7),Ui.Text("FIND YOUR OWN RHYTHM",9,Ui.Dim,true));logo.Margin=new Thickness(25,35,12,38);DockPanel.SetDock(logo,Dock.Top);side.Children.Add(logo);
        var foot=Ui.Stack(Ui.Tag("●  OFFLINE STUDIO"),Ui.Gap(12),Ui.Text("음악에 집중하는 시간.\n기록은 이 기기에만 저장돼요.",11,Ui.Dim),Ui.Gap(16),Ui.Text("WINDOWS NATIVE  /  0.2",9,Ui.Dim));foot.Margin=new Thickness(24);DockPanel.SetDock(foot,Dock.Bottom);side.Children.Add(foot);
        nav.Margin=new Thickness(13,0,13,0);side.Children.Add(nav);shell.Children.Add(new Border{Child=side,Background=Ui.Brush("#10141B"),BorderBrush=Ui.Brush("#252D38"),BorderThickness=new Thickness(0,0,1,0)});
        var right=new DockPanel();var top=Ui.Columns([-1,-1],Ui.Text("YOUR PERSONAL RHYTHM STUDIO",10,Ui.Dim,true),Ui.Text("로컬 프로필    ·    F11 전체 화면",11,Ui.Muted));((TextBlock)top.Children[1]).HorizontalAlignment=HorizontalAlignment.Right;top.Margin=new Thickness(34,24,34,17);DockPanel.SetDock(top,Dock.Top);right.Children.Add(top);
        toast.Margin=new Thickness(34,6,34,12);DockPanel.SetDock(toast,Dock.Bottom);right.Children.Add(toast);right.Children.Add(page);Grid.SetColumn(right,1);shell.Children.Add(right);
    }
    void Navigate(string name)
    {
        repeatGeneration++;
        if(calibrating||calibrationPreparing)CancelCalibration();
        currentPage=name;RenderNavigation();
        var body=name switch {"practice"=>PracticePage(),"records"=>RecordsPage(),"arcade"=>ArcadePage(),"settings"=>SettingsPage(),_=>LibraryPage()};
        body.Margin=new Thickness(34,12,34,20);body.LayoutTransform=new ScaleTransform(store.Settings.UiScale/100,store.Settings.UiScale/100);page.Content=new ScrollViewer{Content=body};
    }
    void RenderNavigation()
    {
        nav.Children.Clear();foreach(var (id,icon,title) in new[]{("music","♫","나의 음악"),("practice","▤","연습 스튜디오"),("records","▥","나의 기록"),("arcade","◇","리듬 아케이드"),("settings","⚙","환경 설정")}) {
            var b=Ui.Button($"{icon}     {title}",()=>Navigate(id));b.HorizontalContentAlignment=HorizontalAlignment.Left;b.Margin=new Thickness(0,0,0,8);b.Padding=new Thickness(17,14,10,14);b.BorderThickness=new Thickness(0);b.Background=Ui.Brush(id==currentPage?"#293329":"#10141B");b.Foreground=Ui.Brush(id==currentPage?Ui.Lime:Ui.Muted);nav.Children.Add(b);
        }
        nav.Children.Add(Ui.Gap(26));var tutorial=Ui.Button("처음이라면 · 플레이 가이드",()=>StartTutorial(0));tutorial.FontSize=11;tutorial.Background=Ui.Brush("#151B24");tutorial.Padding=new Thickness(8,12,8,12);nav.Children.Add(tutorial);
    }
    void Notify(string text){toast.Text=text;}
    void SaveSettings(){try{store.Save();audio.Volume=store.Settings.Volume;audio.EffectVolume=store.Settings.EffectVolume;}catch(Exception ex){Notify("설정을 저장하지 못했습니다. "+ex.Message);}}
    void OnWindowKey(object sender,KeyEventArgs e)
    {
        var key=e.Key==Key.System?e.SystemKey:e.Key;
        if(bindingLane>=0){e.Handled=true;if(key==Key.Escape){bindingLane=-1;Navigate("settings");return;}var vk=KeyInterop.VirtualKeyFromKey(key);if(key is Key.Escape or Key.F11 or Key.Tab || store.Settings.Keys.Where((_,i)=>i!=bindingLane).Contains(vk)){Notify("다른 레인과 겹치지 않는 키를 선택해 주세요.");return;}store.Settings.Keys[bindingLane]=vk;bindingLane=-1;SaveSettings();Navigate("settings");return;}
        if(key==Key.F11){WindowStyle=WindowStyle==WindowStyle.None?WindowStyle.SingleBorderWindow:WindowStyle.None;WindowState=WindowStyle==WindowStyle.None?WindowState.Maximized:WindowState.Normal;e.Handled=true;}
        if(key==Key.Escape){if(calibrating||calibrationPreparing){CancelCalibration();Navigate("settings");}else if(engine!=null){if(paused)_=ResumeGame();else PauseGame();}e.Handled=true;}
        if(engine==null&&!starting&&currentPage=="music"&&Keyboard.FocusedElement is not TextBox){if(key is Key.Up or Key.Down){var index=content.Songs.ToList().FindIndex(s=>s.Id==selected.Id);selected=content.Songs[(index+(key==Key.Down?1:content.Songs.Count-1))%content.Songs.Count];Navigate("music");e.Handled=true;}if(key==Key.Enter){_=StartGame(selected,content.GetChart(selected.Id,difficulty));e.Handled=true;}}
        if(engine!=null&&store.Settings.Keys.Contains(KeyInterop.VirtualKeyFromKey(key)))e.Handled=true;
    }
    string KeyName(int lane)=>KeyInterop.KeyFromVirtualKey(store.Settings.Keys[lane]).ToString();
    FrameworkElement Heading(string eyebrow,string title,string description)=>Ui.Stack(Ui.Label(eyebrow),Ui.Gap(10),Ui.Text(title,32,Ui.TextColor,true),Ui.Gap(9),Ui.Text(description,14,Ui.Muted),Ui.Gap(27));
    [System.Runtime.InteropServices.DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd,int attribute,ref int value,int size);
}
