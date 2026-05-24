using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace RhythmGame;

internal static class AppLogger
{
    private static readonly object Gate = new();
    private static readonly string LogDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RhythmGame",
        "logs");

    public static void Info(string message)
    {
        Write("INFO", message, null);
    }

    public static void Error(string message, Exception? exception = null)
    {
        Write("ERROR", message, exception);
    }

    private static void Write(string level, string message, Exception? exception)
    {
        try
        {
            Directory.CreateDirectory(LogDirectory);
            string path = Path.Combine(LogDirectory, $"rhythmgame_{DateTime.Now:yyyyMMdd}.log");
            var builder = new StringBuilder()
                .Append(DateTime.Now.ToString("O"))
                .Append(" [")
                .Append(level)
                .Append("] ")
                .Append(message);

            if (exception is not null)
                builder.AppendLine().Append(exception);

            lock (Gate)
                File.AppendAllText(path, builder.AppendLine().ToString());
        }
        catch
        {
            // Logging must never break gameplay or persistence.
        }
    }
}

internal static class SafeJsonFile
{
    public static bool TryReadWithBackup(string path, out string json)
    {
        json = string.Empty;
        try
        {
            if (!File.Exists(path))
                return false;

            json = File.ReadAllText(path);
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Error($"Failed to read {Path.GetFileName(path)}; trying backup.", ex);
            return TryReadBackup(path, restore: true, out json);
        }
    }

    public static bool TryReadBackup(string path, bool restore, out string json)
    {
        json = string.Empty;
        string backupPath = path + ".bak";
        try
        {
            if (!File.Exists(backupPath))
                return false;

            json = File.ReadAllText(backupPath);
            if (restore)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, json);
                AppLogger.Info($"Recovered {Path.GetFileName(path)} from backup.");
            }

            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Error($"Failed to read backup {Path.GetFileName(backupPath)}.", ex);
            json = string.Empty;
            return false;
        }
    }

    public static void WriteWithBackup(string path, string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string backupPath = path + ".bak";
        string tempPath = path + ".tmp";

        try
        {
            if (File.Exists(path))
                File.Copy(path, backupPath, overwrite: true);

            File.WriteAllText(tempPath, json);
            File.Move(tempPath, path, overwrite: true);
        }
        catch (Exception ex)
        {
            AppLogger.Error($"Failed to save {Path.GetFileName(path)}.", ex);
            try
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
            catch (Exception cleanupEx)
            {
                AppLogger.Error($"Failed to remove temp save file {Path.GetFileName(tempPath)}.", cleanupEx);
            }

            throw;
        }
    }
}

internal sealed class RenderResourceCache : IDisposable
{
    private readonly Dictionary<string, Font> _fonts = [];
    private readonly Dictionary<int, SolidBrush> _brushes = [];
    private readonly Dictionary<string, Pen> _pens = [];

    public Font Font(string family, float size, FontStyle style)
    {
        int scaledSize = Math.Max(1, (int)MathF.Round(size * 10f));
        string key = $"{family}|{scaledSize}|{(int)style}";
        if (!_fonts.TryGetValue(key, out Font? font))
        {
            font = new Font(family, scaledSize / 10f, style);
            _fonts[key] = font;
        }

        return font;
    }

    public SolidBrush Brush(Color color)
    {
        int key = color.ToArgb();
        if (!_brushes.TryGetValue(key, out SolidBrush? brush))
        {
            brush = new SolidBrush(color);
            _brushes[key] = brush;
        }

        return brush;
    }

    public Pen Pen(Color color, float width)
    {
        int scaledWidth = Math.Max(1, (int)MathF.Round(width * 10f));
        string key = $"{color.ToArgb()}|{scaledWidth}";
        if (!_pens.TryGetValue(key, out Pen? pen))
        {
            pen = new Pen(color, scaledWidth / 10f);
            _pens[key] = pen;
        }

        return pen;
    }

    public void Dispose()
    {
        foreach (Font font in _fonts.Values)
            font.Dispose();
        foreach (SolidBrush brush in _brushes.Values)
            brush.Dispose();
        foreach (Pen pen in _pens.Values)
            pen.Dispose();

        _fonts.Clear();
        _brushes.Clear();
        _pens.Clear();
    }
}

internal sealed class GdiResourceMonitor
{
    private const int GdiObjectIndex = 0;
    private int _startCount;
    private int _lastCount;
    private DateTime _lastLogTime;

    public void Start(string context)
    {
        _startCount = GetCurrentGdiObjectCount();
        _lastCount = _startCount;
        _lastLogTime = DateTime.Now;
        AppLogger.Info($"GDI monitor start {context}: {_startCount}");
    }

    public void Sample(string context, int growthWarningThreshold = 64)
    {
        if ((DateTime.Now - _lastLogTime).TotalSeconds < 5)
            return;

        _lastLogTime = DateTime.Now;
        int current = GetCurrentGdiObjectCount();
        int growth = current - _lastCount;
        if (growth >= growthWarningThreshold || current - _startCount >= growthWarningThreshold)
            AppLogger.Info($"GDI monitor sample {context}: current={current}, sinceLast={growth}, sinceStart={current - _startCount}");

        _lastCount = current;
    }

    public void Stop(string context)
    {
        int current = GetCurrentGdiObjectCount();
        AppLogger.Info($"GDI monitor stop {context}: current={current}, delta={current - _startCount}");
    }

    public static int GetCurrentGdiObjectCount()
    {
        try
        {
            return GetGuiResources(Process.GetCurrentProcess().Handle, GdiObjectIndex);
        }
        catch
        {
            return -1;
        }
    }

    [DllImport("user32.dll")]
    private static extern int GetGuiResources(IntPtr hProcess, int uiFlags);
}
