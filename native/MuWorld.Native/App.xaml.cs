using System.IO;
using System.Windows;

namespace MuWorld.Native;
public partial class App : Application
{
    Mutex? instanceMutex;
    bool ownsMutex;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if(!e.Args.Contains("--smoke")&&!e.Args.Contains("--capture")){
            instanceMutex=new Mutex(true,"Local\\MuWorld.Native",out ownsMutex);
            if(!ownsMutex){var existing=FindWindow(null,"MuWorld · Native rhythm studio");if(existing!=IntPtr.Zero){if(IsIconic(existing))ShowWindow(existing,9);SetForegroundWindow(existing);}Shutdown();return;}
        }
        DispatcherUnhandledException += (_, args) => {
            try { Directory.CreateDirectory(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MuWorldNative")); File.AppendAllText(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MuWorldNative", "error.log"), $"{DateTime.Now:O}\n{args.Exception}\n"); } catch { }
            MessageBox.Show("작업을 완료하지 못했습니다.\n" + args.Exception.Message, "MuWorld", MessageBoxButton.OK, MessageBoxImage.Warning);
            args.Handled = true;
        };
        MainWindow = new MainWindow(e.Args);
        MainWindow.Show();
    }
    protected override void OnExit(ExitEventArgs e){if(ownsMutex)instanceMutex?.ReleaseMutex();instanceMutex?.Dispose();base.OnExit(e);}
    [System.Runtime.InteropServices.DllImport("user32.dll",CharSet=System.Runtime.InteropServices.CharSet.Unicode)] static extern IntPtr FindWindow(string? className,string windowName);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr window);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool IsIconic(IntPtr window);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool ShowWindow(IntPtr window,int command);
}
