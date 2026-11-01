using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MuWorld.Native.Core;

namespace MuWorld.Native;
public partial class MainWindow
{
    async Task RunSmokeAsync()
    {
        var dir=Path.Combine(Environment.CurrentDirectory,"artifacts","native");Directory.CreateDirectory(dir);var checks=new List<string>();
        try{
            await audio.InitializeAsync();checks.Add($"WASAPI {audio.Info.DeviceName} {audio.Info.SampleRate} Hz / {audio.Info.ClockSource}");
            foreach(var name in new[]{"music","practice","records","arcade","settings"}){Navigate(name);await Capture(name);}
            if(args.Contains("--capture")){File.WriteAllText(Path.Combine(dir,"capture-result.txt"),"PASS");return;}
            await StartGame(selected,content.GetChart(selected.Id,"easy"),"practice",.8m,0,4_000_000);if(engine==null)throw new Exception("Native game start failed");await Task.Delay(3100);await Capture("game");
            var a=audio.SongTimeUs();await Task.Delay(100);var b=audio.SongTimeUs();if(b<=a)throw new Exception("Audio clock did not advance");checks.Add($"Audio tempo clock advanced {b-a} us");
            PauseGame();var frozen=audio.SongTimeUs();await Task.Delay(150);if(audio.SongTimeUs()!=frozen)throw new Exception("Pause clock moved");await Capture("pause");await ResumeGame();checks.Add("Pause / resume / countdown passed");
            for(int i=0;i<100&&engine!=null;i++)await Task.Delay(100);if(engine!=null)throw new Exception("Native run did not finish");checks.Add("Natural completion / native record persistence passed");await Capture("result");
            var record=store.Records.Last();ReplayChart(record,true);await Task.Delay(100);for(int i=0;i<100&&(starting||engine!=null);i++)await Task.Delay(100);if(engine!=null)throw new Exception("Replay did not finish");checks.Add("Replay transport completed");
            foreach(var kind in new[]{"echo","silence","workshop","guard","route"}){var session=ContentLibrary.CreateArcade(kind);await StartGame(selected,session.Chart,"arcade",1,0,session.Chart.DurationUs,session);if(engine==null)throw new Exception(kind+" failed to start");await Task.Delay(100);surface!.TimeUs=5_000_000;surface.InvalidateVisual();await Capture("arcade-"+kind);audio.Stop();engine=null;RemoveGameHost();checks.Add(kind+" native transport started");}
            await StartCalibration();if(!calibrating)throw new Exception("Calibration failed to start");await Capture("calibration");CancelCalibration();checks.Add("Calibration native cues started");
            var beforeRepeats=store.Records.Count;var repeatChart=content.GetChart(selected.Id,"easy");var begin=repeatChart.Notes.First().TimeUs;
            await StartGame(selected,repeatChart,"practice",1,begin,begin+600_000,repeats:2);
            for(int i=0;i<160&&(store.Records.Count<beforeRepeats+2||engine!=null);i++)await Task.Delay(100);
            if(store.Records.Count!=beforeRepeats+2||engine!=null)throw new Exception("Automatic practice repeat failed");checks.Add("Automatic practice repeat saved two independent runs");
            Navigate("music");await Capture("music");
            File.WriteAllLines(Path.Combine(dir,"smoke-result.txt"),checks.Append("PASS"));
        }catch(Exception ex){File.WriteAllText(Path.Combine(dir,"smoke-result.txt"),string.Join("\n",checks)+"\nFAIL\n"+ex);}
        finally{Close();}
        async Task Capture(string name){await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);await Task.Delay(120);UpdateLayout();var bitmap=new RenderTargetBitmap((int)ActualWidth,(int)ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(this);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var output=File.Create(Path.Combine(dir,name+".png"));encoder.Save(output);}
    }
}
