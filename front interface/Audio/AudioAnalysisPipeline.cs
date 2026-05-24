using System.Diagnostics;

namespace RhythmGame;

internal sealed record AudioAnalysisResult(
    string SourcePath,
    bool IsSupported,
    bool UsedExternalDecoder,
    string Message,
    List<WavAnalyzer.BeatInfo> Beats,
    float DurationSeconds);

internal static class AudioAnalysisPipeline
{
    private const int DecodeTimeoutMilliseconds = 30000;
    private static string? _cachedFfmpegPath;
    private static bool _searchedFfmpeg;

    public static bool CanAnalyze(string audioPath)
    {
        return AudioFileCatalog.IsWav(audioPath) || FindFfmpeg() is not null;
    }

    public static AudioAnalysisResult Analyze(string audioPath)
    {
        if (!File.Exists(audioPath))
            return Unsupported(audioPath, "Audio file not found.");

        if (AudioFileCatalog.IsWav(audioPath))
        {
            List<WavAnalyzer.BeatInfo> beats = WavAnalyzer.Analyze(audioPath);
            return new AudioAnalysisResult(
                audioPath,
                beats.Count > 0,
                UsedExternalDecoder: false,
                beats.Count > 0 ? "WAV PCM analysis complete." : "WAV analysis produced no beats.",
                beats,
                WavAnalyzer.GetDuration(audioPath));
        }

        string? ffmpegPath = FindFfmpeg();
        if (ffmpegPath is null)
        {
            string extension = Path.GetExtension(audioPath).TrimStart('.').ToUpperInvariant();
            return Unsupported(audioPath, $"{extension} analysis requires ffmpeg on PATH or Tools/ffmpeg.exe.");
        }

        string tempDir = Path.Combine(Path.GetTempPath(), "RhythmGameAudioAnalysis");
        Directory.CreateDirectory(tempDir);
        string tempWav = Path.Combine(tempDir, $"{Guid.NewGuid():N}.wav");

        try
        {
            if (!DecodeToPcmWav(ffmpegPath, audioPath, tempWav, out string error))
                return Unsupported(audioPath, $"Decode failed: {error}");

            List<WavAnalyzer.BeatInfo> beats = WavAnalyzer.Analyze(tempWav);
            return new AudioAnalysisResult(
                audioPath,
                beats.Count > 0,
                UsedExternalDecoder: true,
                beats.Count > 0 ? "Decoded through ffmpeg and analyzed as PCM." : "Decoded audio produced no beats.",
                beats,
                WavAnalyzer.GetDuration(tempWav));
        }
        finally
        {
            TryDelete(tempWav);
        }
    }

    private static AudioAnalysisResult Unsupported(string audioPath, string message)
    {
        return new AudioAnalysisResult(audioPath, false, UsedExternalDecoder: false, message, [], 0f);
    }

    private static bool DecodeToPcmWav(string ffmpegPath, string inputPath, string outputPath, out string error)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
            },
        };

        process.StartInfo.ArgumentList.Add("-y");
        process.StartInfo.ArgumentList.Add("-hide_banner");
        process.StartInfo.ArgumentList.Add("-loglevel");
        process.StartInfo.ArgumentList.Add("error");
        process.StartInfo.ArgumentList.Add("-i");
        process.StartInfo.ArgumentList.Add(inputPath);
        process.StartInfo.ArgumentList.Add("-ac");
        process.StartInfo.ArgumentList.Add("1");
        process.StartInfo.ArgumentList.Add("-ar");
        process.StartInfo.ArgumentList.Add("44100");
        process.StartInfo.ArgumentList.Add("-f");
        process.StartInfo.ArgumentList.Add("wav");
        process.StartInfo.ArgumentList.Add(outputPath);

        try
        {
            process.Start();
            if (!process.WaitForExit(DecodeTimeoutMilliseconds))
            {
                TryKill(process);
                error = "decoder timeout";
                return false;
            }

            string stderr = process.StandardError.ReadToEnd().Trim();
            error = string.IsNullOrWhiteSpace(stderr) ? $"exit code {process.ExitCode}" : stderr;
            return process.ExitCode == 0 && File.Exists(outputPath);
        }
        catch (Exception ex)
        {
            AppLogger.Error($"ffmpeg decode failed for {Path.GetFileName(inputPath)}.", ex);
            error = ex.Message;
            return false;
        }
        finally
        {
            process.Dispose();
        }
    }

    private static string? FindFfmpeg()
    {
        if (_searchedFfmpeg)
            return _cachedFfmpegPath;

        _searchedFfmpeg = true;
        string localToolsPath = Path.Combine(AppContext.BaseDirectory, "Tools", "ffmpeg.exe");
        if (File.Exists(localToolsPath))
            return _cachedFfmpegPath = localToolsPath;

        string? pathVariable = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathVariable))
            return null;

        foreach (string rawDirectory in pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string directory = rawDirectory.Trim().Trim('"');
            if (directory.Length == 0)
                continue;

            string candidate = Path.Combine(directory, OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg");
            if (File.Exists(candidate))
                return _cachedFfmpegPath = candidate;
        }

        return null;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex)
        {
            AppLogger.Error($"Temp cleanup failed for {Path.GetFileName(path)}.", ex);
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (Exception ex)
        {
            AppLogger.Error("Failed to terminate ffmpeg process.", ex);
        }
    }
}
