using System.Diagnostics;

namespace RhythmGame;

internal sealed class FrameDiagnostics
{
    private readonly long[] _histogram = new long[501];
    private long _frames;
    private double _totalMilliseconds;
    private double _maximum;
    private long _initialAllocation;
    private int _initialGdi;

    internal void Start()
    {
        Array.Clear(_histogram);
        _frames = 0; _totalMilliseconds = 0; _maximum = 0;
        _initialAllocation = GC.GetTotalAllocatedBytes(false);
        _initialGdi = GdiResourceMonitor.GetCurrentGdiObjectCount();
    }

    internal void Record(double milliseconds)
    {
        if (!double.IsFinite(milliseconds) || milliseconds < 0) return;
        _histogram[(int)Math.Clamp(Math.Ceiling(milliseconds), 0, 500)]++;
        _frames++; _totalMilliseconds += milliseconds; _maximum = Math.Max(_maximum, milliseconds);
    }

    internal double Percentile(double fraction)
    {
        long target = (long)Math.Ceiling(_frames * fraction);
        if (_frames == 0) return 0;
        long seen = 0;
        for (int i = 0; i < _histogram.Length; i++)
        {
            seen += _histogram[i];
            if (seen >= target) return i;
        }
        return 500;
    }

    internal void Log(string context)
    {
        using Process process = Process.GetCurrentProcess();
        AppLogger.Info($"Frame summary context={context}, frames={_frames}, " +
            $"fps={(_totalMilliseconds > 0 ? _frames * 1000 / _totalMilliseconds : 0):F2}, " +
            $"p95Ms={Percentile(0.95)}, p99Ms={Percentile(0.99)}, maxMs={_maximum:F2}, " +
            $"allocatedBytes={GC.GetTotalAllocatedBytes(false) - _initialAllocation}, " +
            $"privateBytes={process.PrivateMemorySize64}, gdiDelta={GdiResourceMonitor.GetCurrentGdiObjectCount() - _initialGdi}");
    }
}
