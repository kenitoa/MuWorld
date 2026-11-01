using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MuWorld.Native.Audio;

public readonly record struct RawKeyEvent(int VirtualKey, bool Pressed, long QpcTicks);

/// <summary>WM_INPUT on its own native message thread. Timestamps are receipt QPC, not physical switch measurements.</summary>
public sealed class RawKeyboard : IDisposable
{
    private const uint WmInput = 0x00FF, WmClose = 0x0010, WmDestroy = 0x0002, RidInput = 0x10000003;
    private readonly ManualResetEventSlim _ready = new(false);
    private readonly HashSet<int> _pressed = [];
    private readonly string _className = $"MuWorld.RawKeyboard.{Guid.NewGuid():N}";
    private Thread? _thread;
    private IntPtr _window, _foregroundWindow, _data;
    private int _capacity, _disposed;
    private Exception? _startupError;
    private WindowProcedure? _procedure;
    public event Action<RawKeyEvent>? KeyChanged;
    public event Action<string>? Faulted;

    public void Start(IntPtr foregroundWindow)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        if (foregroundWindow == IntPtr.Zero) throw new ArgumentException("대상 Windows 창 핸들이 필요합니다.", nameof(foregroundWindow));
        Interlocked.Exchange(ref _foregroundWindow, foregroundWindow);
        if (_thread != null) return;
        _thread = new Thread(MessageLoop) { IsBackground = true, Name = "MuWorld Raw Input", Priority = ThreadPriority.AboveNormal };
        _thread.SetApartmentState(ApartmentState.MTA); _thread.Start();
        if (!_ready.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("키보드 입력 스레드가 준비되지 않았습니다.");
        if (_startupError != null) throw new InvalidOperationException("Raw Input 키보드를 준비할 수 없습니다.", _startupError);
    }

    private void MessageLoop()
    {
        IntPtr module = GetModuleHandle(null);
        try
        {
            _procedure = WndProc;
            var windowClass = new WndClass { Size = (uint)Marshal.SizeOf<WndClass>(), Instance = module, ClassName = _className, Procedure = Marshal.GetFunctionPointerForDelegate(_procedure) };
            if (RegisterClassEx(ref windowClass) == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            _window = CreateWindowEx(0, _className, "", 0, 0, 0, 0, 0, new IntPtr(-3), IntPtr.Zero, module, IntPtr.Zero);
            if (_window == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            var devices = new[] { new RawDevice { UsagePage = 1, Usage = 6, Flags = 0x100, Target = _window } };
            if (!RegisterRawInputDevices(devices, 1, (uint)Marshal.SizeOf<RawDevice>())) throw new Win32Exception(Marshal.GetLastWin32Error());
            _capacity = 512; _data = Marshal.AllocHGlobal(_capacity); _ready.Set();
            int result;
            while ((result = GetMessage(out var message, IntPtr.Zero, 0, 0)) > 0) { TranslateMessage(ref message); DispatchMessage(ref message); }
            if (result < 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        catch (Exception exception)
        {
            _startupError = exception; _ready.Set();
            try { Faulted?.Invoke(exception.Message); } catch { }
        }
        finally
        {
            var remove = new[] { new RawDevice { UsagePage = 1, Usage = 6, Flags = 1, Target = IntPtr.Zero } };
            RegisterRawInputDevices(remove, 1, (uint)Marshal.SizeOf<RawDevice>());
            if (_window != IntPtr.Zero) { DestroyWindow(_window); _window = IntPtr.Zero; }
            if (_data != IntPtr.Zero) { Marshal.FreeHGlobal(_data); _data = IntPtr.Zero; }
            UnregisterClass(_className, module);
        }
    }

    private IntPtr WndProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (message == WmInput)
        {
            long qpc = Stopwatch.GetTimestamp();
            try { ReadInput(lParam, qpc); } catch (Exception exception) { try { Faulted?.Invoke(exception.Message); } catch { } }
        }
        else if (message == WmClose) { DestroyWindow(window); return IntPtr.Zero; }
        else if (message == WmDestroy) { PostQuitMessage(0); return IntPtr.Zero; }
        return DefWindowProc(window, message, wParam, lParam);
    }

    private bool IsOurForeground()
    {
        IntPtr foreground = GetForegroundWindow(), target = Interlocked.CompareExchange(ref _foregroundWindow, IntPtr.Zero, IntPtr.Zero);
        for (int depth = 0; foreground != IntPtr.Zero && depth < 8; depth++, foreground = GetWindow(foreground, 4))
            if (foreground == target) return true;
        return false;
    }

    private void ReadInput(IntPtr handle, long qpc)
    {
        if (!IsOurForeground()) { _pressed.Clear(); return; }
        uint headerSize = (uint)(IntPtr.Size == 8 ? 24 : 16), size = 0;
        if (GetRawInputData(handle, RidInput, IntPtr.Zero, ref size, headerSize) == uint.MaxValue || size == 0) return;
        if (size > 65_536) throw new InvalidOperationException("키보드 입력 데이터가 예상 범위를 벗어났습니다.");
        if (size > _capacity) { _data = Marshal.ReAllocHGlobal(_data, new IntPtr(size)); _capacity = (int)size; }
        if (GetRawInputData(handle, RidInput, _data, ref size, headerSize) != size || Marshal.ReadInt32(_data) != 1) return;
        var key = Marshal.PtrToStructure<RawKeyboardData>(IntPtr.Add(_data, (int)headerSize));
        int virtualKey = key.VirtualKey;
        if (virtualKey is 0 or 255) return;
        bool extended = (key.Flags & 2) != 0;
        if (virtualKey == 16) virtualKey = key.MakeCode == 0x36 ? 0xA1 : 0xA0;
        else if (virtualKey == 17) virtualKey = extended ? 0xA3 : 0xA2;
        else if (virtualKey == 18) virtualKey = extended ? 0xA5 : 0xA4;
        bool pressed = (key.Flags & 1) == 0;
        if (pressed) { if (!_pressed.Add(virtualKey)) return; }
        else _pressed.Remove(virtualKey);
        KeyChanged?.Invoke(new(virtualKey, pressed, qpc));
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        if (_window != IntPtr.Zero) PostMessage(_window, WmClose, IntPtr.Zero, IntPtr.Zero);
        if (_thread?.IsAlive == true && Thread.CurrentThread != _thread) _thread.Join(2_000);
        if (_thread?.IsAlive != true) _ready.Dispose();
    }

    private delegate IntPtr WindowProcedure(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct WndClass { public uint Size, Style; public IntPtr Procedure; public int ClassExtra, WindowExtra; public IntPtr Instance, Icon, Cursor, Background; public string? MenuName; public string ClassName; public IntPtr SmallIcon; }
    [StructLayout(LayoutKind.Sequential)] private struct RawDevice { public ushort UsagePage, Usage; public uint Flags; public IntPtr Target; }
    [StructLayout(LayoutKind.Sequential)] private struct RawKeyboardData { public ushort MakeCode, Flags, Reserved, VirtualKey; public uint Message, ExtraInformation; }
    [StructLayout(LayoutKind.Sequential)] private struct Message { public IntPtr Window; public uint Id; public UIntPtr WParam; public IntPtr LParam; public uint Time; public int X, Y; public uint Private; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern ushort RegisterClassEx(ref WndClass windowClass);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CreateWindowEx(uint extendedStyle, string className, string title, uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool UnregisterClass(string className, IntPtr instance);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterRawInputDevices([In] RawDevice[] devices, uint count, uint size);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint GetRawInputData(IntPtr rawInput, uint command, IntPtr data, ref uint size, uint headerSize);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint command);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr DefWindowProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", SetLastError = true)] private static extern int GetMessage(out Message message, IntPtr window, uint minimum, uint maximum);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref Message message);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr DispatchMessage(ref Message message);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern void PostQuitMessage(int code);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
}
