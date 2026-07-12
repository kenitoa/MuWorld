using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace RhythmGame;

internal sealed class AudioManager : IDisposable
{
    private readonly object _sync = new();
    private int _bgmVolume;
    private int _previewVolume = 45;
    private string _hitSoundSkin = "SYNTH";
    private int _hitSoundPitch;
    private bool _hitSoundMuted;
    private CancellationTokenSource _hitCts = new();

    // winmm.dll P/Invoke — 히트 사운드용
    [DllImport("winmm.dll", SetLastError = true)]
    private static extern bool PlaySound(byte[]? pszSound, IntPtr hmod, uint fdwSound);

    private const uint SND_ASYNC  = 0x0001;
    private const uint SND_MEMORY = 0x0004;
    // MCI P/Invoke — 인게임 BGM 재생 (PlaySound/SoundPlayer와 완전히 독립)
    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    private static extern int mciSendString(string command, StringBuilder? returnString, int returnSize, IntPtr hwndCallback);

    private bool _mciOpen;
    private int _inGameBgmLengthMs;
    private readonly Stopwatch _inGameClockStopwatch = new();
    private readonly AudioClockDiagnostics _clockDiagnostics = new();
    private const double PositionQueryIntervalSeconds = 0.05d;
    private float _positionClockAnchorSeconds;
    private double _positionClockAnchorWallSeconds;
    private double _lastPositionQueryWallSeconds;
    private double _lastFinishQueryWallSeconds;
    private bool _lastKnownFinished;

    internal AudioClockSnapshot ClockDiagnosticsSnapshot => _clockDiagnostics.Snapshot();

    // 히트 사운드 WAV 바이트 캐시
    private readonly Dictionary<Judgment, byte[]> _hitWavCache = [];
    private int _lastHitVolume = -1;
    private string _lastHitSkin = string.Empty;
    private int _lastHitPitch = int.MinValue;

    public AudioEngineReview ActiveEngine => AudioEngineCatalog.ActiveReview;

    public void StartBgm(int volume)
    {
        lock (_sync)
        {
            _bgmVolume = Math.Clamp(volume, 0, 100);
        }
    }

    public void StopBgm()
    {
        StopMainScreenBgm();
        StopInGameBgm();
    }

    public void SetBgmVolume(int volume)
    {
        lock (_sync)
        {
            int clamped = Math.Clamp(volume, 0, 100);
            if (_bgmVolume == clamped)
                return;

            _bgmVolume = clamped;
            int mciVol = clamped * 10; // 0-100 -> 0-1000
            if (_mainBgmOpen)
                mciSendString($"setaudio mainbgm volume to {mciVol}", null, 0, IntPtr.Zero);
            if (_mciOpen)
                mciSendString($"setaudio ingamebgm volume to {mciVol}", null, 0, IntPtr.Zero);
        }
    }

    public void SetPreviewVolume(int volume)
    {
        lock (_sync)
        {
            _previewVolume = Math.Clamp(volume, 0, 100);
            if (_previewBgmOpen)
                mciSendString($"setaudio previewbgm volume to {_previewVolume * 10}", null, 0, IntPtr.Zero);
        }
    }

    public void ConfigureHitSound(string skinName, int pitch, bool muted)
    {
        lock (_sync)
        {
            _hitSoundSkin = string.IsNullOrWhiteSpace(skinName) ? "SYNTH" : skinName.Trim();
            _hitSoundPitch = Math.Clamp(pitch, -1, 1);
            _hitSoundMuted = muted;
            _hitWavCache.Clear();
            _lastHitVolume = -1;
            _lastHitSkin = string.Empty;
            _lastHitPitch = int.MinValue;
        }
    }

    public void PlayHit(int volume, Judgment judgment, bool mute = false)
    {
        if (mute || _hitSoundMuted)
            return;

        PrepareHitSounds(volume);

        if (_hitWavCache.TryGetValue(judgment, out byte[]? wav))
        {
            PlaySound(wav, IntPtr.Zero, SND_ASYNC | SND_MEMORY);
        }
    }

    public void PrepareHitSounds(int volume)
    {
        if (_hitSoundMuted)
            return;

        int clamped = Math.Clamp(volume, 0, 100);
        if (clamped <= 0)
            return;

        if (clamped == _lastHitVolume &&
            string.Equals(_lastHitSkin, _hitSoundSkin, StringComparison.Ordinal) &&
            _lastHitPitch == _hitSoundPitch &&
            _hitWavCache.Count > 0)
        {
            return;
        }

        _lastHitVolume = clamped;
        _lastHitSkin = _hitSoundSkin;
        _lastHitPitch = _hitSoundPitch;
        RebuildHitSoundCache(clamped);
    }

    public void StopAllSounds()
    {
        _hitCts.Cancel();
        _hitCts.Dispose();
        _hitCts = new CancellationTokenSource();
        PlaySound(null, IntPtr.Zero, 0);
        StopInGameBgm();
        StopMainScreenBgm();
        StopSongPreview();
    }

    public bool IsInGameBgmPlaying
    {
        get { lock (_sync) { return _mciOpen; } }
    }

    /// <summary>
    /// Songs/InGameBGM 폴더의 오디오 파일을 MCI로 재생한다.
    /// MCI는 PlaySound/SoundPlayer와 독립적인 채널을 사용하므로 히트 사운드와 동시 재생 가능.
    /// </summary>
    public void PlayInGameBgm(string audioPath)
    {
        StopInGameBgm();
        StopMainScreenBgm();

        if (!File.Exists(audioPath))
            return;

        lock (_sync)
        {
            string safePath = $"\"{audioPath}\"";
            int openResult = mciSendString($"open {safePath} type mpegvideo alias ingamebgm", null, 0, IntPtr.Zero);
            if (openResult != 0)
            {
                _mciOpen = false;
                AppLogger.Info($"MCI open failed code={openResult}, format={Path.GetExtension(audioPath)}");
                return;
            }

            int formatResult = mciSendString("set ingamebgm time format milliseconds", null, 0, IntPtr.Zero);
            int playResult = mciSendString("play ingamebgm", null, 0, IntPtr.Zero);
            if (formatResult != 0 || playResult != 0)
            {
                AppLogger.Info($"MCI play failed formatCode={formatResult}, playCode={playResult}, format={Path.GetExtension(audioPath)}");
                mciSendString("close ingamebgm", null, 0, IntPtr.Zero);
                _mciOpen = false;
                return;
            }

            int mciVol = _bgmVolume * 10;
            int volumeResult = mciSendString($"setaudio ingamebgm volume to {mciVol}", null, 0, IntPtr.Zero);
            if (volumeResult != 0)
                AppLogger.Info($"MCI volume command failed code={volumeResult}");
            _inGameBgmLengthMs = QueryMciInt("status ingamebgm length");
            _mciOpen = true;
            _inGameClockStopwatch.Restart();
            ResetPositionClock(0f, 0d);
            _clockDiagnostics.Start(Path.GetExtension(audioPath), 0f, 0d);
            AppLogger.Info($"Audio clock monitor start format={Path.GetExtension(audioPath).TrimStart('.').ToLowerInvariant()}, lengthMs={_inGameBgmLengthMs}");
        }
    }

    public void StopInGameBgm()
    {
        lock (_sync)
        {
            if (_mciOpen)
            {
                if (TryQueryInGamePosition(out float positionSeconds))
                    _clockDiagnostics.Record(positionSeconds, _inGameClockStopwatch.Elapsed.TotalSeconds);
                else
                    _clockDiagnostics.RecordQueryFailure();

                int stopResult = mciSendString("stop ingamebgm", null, 0, IntPtr.Zero);
                int closeResult = mciSendString("close ingamebgm", null, 0, IntPtr.Zero);
                if (stopResult != 0 || closeResult != 0)
                    AppLogger.Info($"MCI stop failed stopCode={stopResult}, closeCode={closeResult}");
                AppLogger.Info(_clockDiagnostics.Snapshot().ToLogMessage());
                _mciOpen = false;
                _inGameBgmLengthMs = 0;
                _inGameClockStopwatch.Reset();
                ResetPositionClock(0f, 0d);
            }
        }
    }

    public bool PauseInGameBgm()
    {
        lock (_sync)
        {
            if (!_mciOpen)
                return true;

            float position = 0f;
            if (TryQueryInGamePosition(out float measuredPosition))
            {
                position = measuredPosition;
                ResetPositionClock(position, _inGameClockStopwatch.Elapsed.TotalSeconds);
                _clockDiagnostics.Record(position, _inGameClockStopwatch.Elapsed.TotalSeconds);
            }
            else
            {
                _clockDiagnostics.RecordQueryFailure();
            }
            int result = mciSendString("pause ingamebgm", null, 0, IntPtr.Zero);
            AppLogger.Info($"Audio pause positionMs={(int)MathF.Round(position * 1000f)}, result={result}");
            return result == 0;
        }
    }

    public bool ResumeInGameBgm()
    {
        lock (_sync)
        {
            if (!_mciOpen)
                return true;

            int result = mciSendString("resume ingamebgm", null, 0, IntPtr.Zero);
            float position = 0f;
            if (TryQueryInGamePosition(out float measuredPosition))
            {
                position = measuredPosition;
                ResetPositionClock(position, _inGameClockStopwatch.Elapsed.TotalSeconds);
                _clockDiagnostics.ResetBaseline(position, _inGameClockStopwatch.Elapsed.TotalSeconds);
            }
            else
            {
                _clockDiagnostics.RecordQueryFailure();
            }
            AppLogger.Info($"Audio resume positionMs={(int)MathF.Round(position * 1000f)}, result={result}");
            return result == 0;
        }
    }

    public float? GetInGameBgmPositionSeconds()
    {
        lock (_sync)
        {
            if (!_mciOpen)
                return null;

            double wallSeconds = _inGameClockStopwatch.Elapsed.TotalSeconds;
            float positionSeconds = GetPredictedPosition(wallSeconds);

            // MCI position calls are synchronous and can block the WinForms UI
            // thread. Sample MCI at a bounded rate, then use the local monotonic
            // clock between samples so notes never freeze on repeated MCI values.
            if (wallSeconds - _lastPositionQueryWallSeconds >= PositionQueryIntervalSeconds)
            {
                _lastPositionQueryWallSeconds = wallSeconds;
                if (TryQueryInGamePosition(out float measuredPosition))
                {
                    // A coarse MCI sample may lag behind the already-predicted
                    // position. Only a forward sample is allowed to re-anchor the
                    // clock; rewinding would make judged notes reversible.
                    if (measuredPosition > positionSeconds)
                    {
                        ResetPositionClock(measuredPosition, wallSeconds);
                        positionSeconds = measuredPosition;
                    }

                    _clockDiagnostics.Record(measuredPosition, wallSeconds);
                }
                else
                {
                    _clockDiagnostics.RecordQueryFailure();
                }
            }

            if (_inGameBgmLengthMs > 0)
                positionSeconds = Math.Min(positionSeconds, _inGameBgmLengthMs / 1000f);

            return Math.Max(0f, positionSeconds);
        }
    }

    private float GetPredictedPosition(double wallSeconds)
    {
        double elapsed = Math.Max(0d, wallSeconds - _positionClockAnchorWallSeconds);
        return _positionClockAnchorSeconds + (float)elapsed;
    }

    private void ResetPositionClock(float positionSeconds, double wallSeconds)
    {
        _positionClockAnchorSeconds = Math.Max(0f, positionSeconds);
        _positionClockAnchorWallSeconds = Math.Max(0d, wallSeconds);
        _lastPositionQueryWallSeconds = _positionClockAnchorWallSeconds;
        _lastFinishQueryWallSeconds = _positionClockAnchorWallSeconds;
        _lastKnownFinished = false;
    }

    private static bool TryQueryInGamePosition(out float positionSeconds)
    {
        positionSeconds = 0f;
        var buffer = new StringBuilder(64);
        int result = mciSendString("status ingamebgm position", buffer, buffer.Capacity, IntPtr.Zero);
        return result == 0 && int.TryParse(buffer.ToString().Trim(), out int milliseconds) &&
               (positionSeconds = Math.Max(0, milliseconds) / 1000f) >= 0f;
    }

    public bool IsInGameBgmFinished(float graceSeconds = 0.15f)
    {
        lock (_sync)
        {
            if (!_mciOpen)
                return true;

            double wallSeconds = _inGameClockStopwatch.Elapsed.TotalSeconds;
            if (_inGameBgmLengthMs > 0)
            {
                float predictedPosition = GetPredictedPosition(wallSeconds);
                return predictedPosition >= Math.Max(0f, _inGameBgmLengthMs / 1000f - Math.Max(0f, graceSeconds));
            }

            // Unknown-duration codecs still require an MCI mode query, but never
            // issue that synchronous command on every UI frame.
            if (wallSeconds - _lastFinishQueryWallSeconds < 0.25d)
                return _lastKnownFinished;

            _lastFinishQueryWallSeconds = wallSeconds;
            string mode = QueryMciString("status ingamebgm mode");
            _lastKnownFinished = string.Equals(mode, "stopped", StringComparison.OrdinalIgnoreCase);
            return _lastKnownFinished;
        }
    }

    public float? GetInGameBgmDurationSeconds()
    {
        lock (_sync)
        {
            int length = _inGameBgmLengthMs > 0 ? _inGameBgmLengthMs : QueryMciInt("status ingamebgm length");
            return length > 0 ? length / 1000f : null;
        }
    }

    private bool _mainBgmOpen;
    private bool _previewBgmOpen;

    /// <summary>
    /// 메인 화면 BGM을 MCI로 재생한다 (Songs/MainScreenBGM).
    /// </summary>
    public void PlayMainScreenBgm()
    {
        StopSongPreview();
        StopMainScreenBgm();

        string bgmDir = Path.Combine(AppContext.BaseDirectory, "Songs", "MainScreenBGM");
        if (!Directory.Exists(bgmDir))
            return;

        string[] audioFiles = AudioFileCatalog.DiscoverSongFiles(bgmDir);
        if (audioFiles.Length == 0)
            return;

        string audioPath = audioFiles[0];

        lock (_sync)
        {
            string safePath = $"\"{audioPath}\"";
            // mpegvideo 타입은 repeat를 지원함
            int openResult = mciSendString($"open {safePath} type mpegvideo alias mainbgm", null, 0, IntPtr.Zero);
            if (openResult != 0)
            {
                _mainBgmOpen = false;
                return;
            }

            mciSendString("play mainbgm repeat", null, 0, IntPtr.Zero);
            int mciVol = _bgmVolume * 10;
            mciSendString($"setaudio mainbgm volume to {mciVol}", null, 0, IntPtr.Zero);
            _mainBgmOpen = true;
        }
    }

    public void StopMainScreenBgm()
    {
        lock (_sync)
        {
            if (_mainBgmOpen)
            {
                mciSendString("stop mainbgm", null, 0, IntPtr.Zero);
                mciSendString("close mainbgm", null, 0, IntPtr.Zero);
                _mainBgmOpen = false;
            }
        }
    }

    public void PlaySongPreview(string audioPath, float startSeconds, float durationSeconds, int volume)
    {
        StopSongPreview();
        StopMainScreenBgm();

        if (!File.Exists(audioPath))
            return;

        lock (_sync)
        {
            string safePath = $"\"{audioPath}\"";
            int openResult = mciSendString($"open {safePath} type mpegvideo alias previewbgm", null, 0, IntPtr.Zero);
            if (openResult != 0)
            {
                _previewBgmOpen = false;
                return;
            }

            int startMs = Math.Max(0, (int)MathF.Round(startSeconds * 1000f));
            int endMs = Math.Max(startMs + 1000, startMs + (int)MathF.Round(durationSeconds * 1000f));
            int mciVol = Math.Clamp(volume > 0 ? volume : _previewVolume, 0, 100) * 10;
            mciSendString("set previewbgm time format milliseconds", null, 0, IntPtr.Zero);
            mciSendString($"setaudio previewbgm volume to {mciVol}", null, 0, IntPtr.Zero);
            mciSendString($"play previewbgm from {startMs} to {endMs}", null, 0, IntPtr.Zero);
            _previewBgmOpen = true;
        }
    }

    public void StopSongPreview()
    {
        lock (_sync)
        {
            if (_previewBgmOpen)
            {
                mciSendString("stop previewbgm", null, 0, IntPtr.Zero);
                mciSendString("close previewbgm", null, 0, IntPtr.Zero);
                _previewBgmOpen = false;
            }
        }
    }

    private void RebuildHitSoundCache(int volume)
    {
        _hitWavCache.Clear();
        if (TryLoadHitSoundSkin(volume))
            return;

        string style = _hitSoundSkin.Trim().ToUpperInvariant();
        bool edm = string.Equals(style, "EDM", StringComparison.OrdinalIgnoreCase);
        bool lofi = string.Equals(style, "LO-FI", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(style, "LOFI", StringComparison.OrdinalIgnoreCase);

        float amp = 0.05f + volume / 100f * 0.25f;
        if (edm)
            amp *= 1.08f;
        else if (lofi)
            amp *= 0.72f;

        float pitchScale = _hitSoundPitch switch
        {
            < 0 => 0.84f,
            > 0 => 1.19f,
            _ => 1f,
        };

        float toneScale = edm ? 1.32f : lofi ? 0.64f : 1f;
        float overtoneScale = edm ? 2.35f : lofi ? 1.52f : 2f;
        int durationAdjust = edm ? -18 : lofi ? 32 : 0;

        _hitWavCache[Judgment.Perfect] = CreateDualToneWavBytes(1040 * toneScale * pitchScale, 1040 * toneScale * overtoneScale * pitchScale, 70 + durationAdjust, amp * 0.80f, amp * 0.50f);
        _hitWavCache[Judgment.Great] = CreateDualToneWavBytes(920 * toneScale * pitchScale, 920 * toneScale * overtoneScale * pitchScale, 66 + durationAdjust, amp * 0.74f, amp * 0.42f);
        _hitWavCache[Judgment.Better] = CreateDualToneWavBytes(780 * toneScale * pitchScale, 780 * toneScale * (lofi ? 1.45f : 1.50f) * pitchScale, 62 + durationAdjust, amp * 0.68f, amp * 0.34f);
        _hitWavCache[Judgment.Good] = CreateDualToneWavBytes(660 * toneScale * pitchScale, 660 * toneScale * (lofi ? 1.40f : 1.50f) * pitchScale, 58 + durationAdjust, amp * 0.62f, amp * 0.28f);
        _hitWavCache[Judgment.Bad] = CreateDualToneWavBytes(360 * toneScale * pitchScale, 360 * toneScale * (lofi ? 1.38f : 1.50f) * pitchScale, 80 + durationAdjust, amp * 0.55f, amp * 0.22f);
    }

    private bool TryLoadHitSoundSkin(int volume)
    {
        if (string.Equals(_hitSoundSkin, "SYNTH", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(_hitSoundSkin, "CLASSIC", StringComparison.OrdinalIgnoreCase))
            return false;

        string skinDirectory = Path.Combine(AppContext.BaseDirectory, "Songs", "HitSounds", _hitSoundSkin);
        if (!Directory.Exists(skinDirectory))
            return false;

        bool loadedAny = false;
        foreach (Judgment judgment in Enum.GetValues<Judgment>())
        {
            string path = Path.Combine(skinDirectory, judgment.ToString().ToLowerInvariant() + ".wav");
            if (!File.Exists(path))
                continue;

            byte[] source = File.ReadAllBytes(path);
            _hitWavCache[judgment] = volume >= 98 ? source : ScalePcm16WavVolume(source, volume / 100f);
            loadedAny = true;
        }

        return loadedAny;
    }

    public static string[] DiscoverHitSoundSkins()
    {
        string skinRoot = Path.Combine(AppContext.BaseDirectory, "Songs", "HitSounds");
        var skins = new List<string> { "SYNTH" };
        if (Directory.Exists(skinRoot))
        {
            foreach (string directory in Directory.GetDirectories(skinRoot).OrderBy(Path.GetFileName))
            {
                if (Directory.GetFiles(directory, "*.wav", SearchOption.TopDirectoryOnly).Length > 0)
                    skins.Add(Path.GetFileName(directory));
            }
        }

        return skins.Distinct(StringComparer.OrdinalIgnoreCase).Take(4).ToArray();
    }

    private static byte[] CreateDualToneWavBytes(float freqA, float freqB, int durationMs, float ampA, float ampB)
    {
        using var ms = CreateDualToneWav(freqA, freqB, durationMs, ampA, ampB);
        return ms.ToArray();
    }

    private static MemoryStream CreateDualToneWav(float freqA, float freqB, int durationMs, float ampA, float ampB)
    {
        const int sampleRate = 44100;
        const short channels = 1;
        const short bitsPerSample = 16;
        int samples = (int)(sampleRate * durationMs / 1000f);
        int byteRate = sampleRate * channels * (bitsPerSample / 8);
        short blockAlign = (short)(channels * (bitsPerSample / 8));
        int dataSize = samples * blockAlign;

        var ms = new MemoryStream(44 + dataSize);
        using (var writer = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write("RIFF"u8.ToArray());
            writer.Write(36 + dataSize);
            writer.Write("WAVE"u8.ToArray());
            writer.Write("fmt "u8.ToArray());
            writer.Write(16);
            writer.Write((short)1);
            writer.Write(channels);
            writer.Write(sampleRate);
            writer.Write(byteRate);
            writer.Write(blockAlign);
            writer.Write(bitsPerSample);
            writer.Write("data"u8.ToArray());
            writer.Write(dataSize);

            double twoPi = Math.PI * 2.0;
            for (int i = 0; i < samples; i++)
            {
                double t = i / (double)sampleRate;
                double env = 0.65 + 0.35 * Math.Sin(twoPi * 0.5 * t);
                double value = Math.Sin(twoPi * freqA * t) * ampA + Math.Sin(twoPi * freqB * t) * ampB;
                value *= env;
                value = Math.Clamp(value, -1.0, 1.0);
                short sample = (short)(value * short.MaxValue);
                writer.Write(sample);
            }
        }

        ms.Position = 0;
        return ms;
    }

    private static byte[] ScalePcm16WavVolume(byte[] source, float scale)
    {
        byte[] copy = source.ToArray();
        if (copy.Length < 44)
            return copy;

        int dataOffset = FindDataChunkOffset(copy);
        if (dataOffset < 0)
            return copy;

        for (int i = dataOffset; i + 1 < copy.Length; i += 2)
        {
            short sample = BitConverter.ToInt16(copy, i);
            short scaled = (short)Math.Clamp((int)MathF.Round(sample * Math.Clamp(scale, 0f, 1f)), short.MinValue, short.MaxValue);
            byte[] bytes = BitConverter.GetBytes(scaled);
            copy[i] = bytes[0];
            copy[i + 1] = bytes[1];
        }

        return copy;
    }

    private static int FindDataChunkOffset(byte[] wav)
    {
        for (int i = 12; i + 8 < wav.Length; i++)
        {
            if (wav[i] == (byte)'d' && wav[i + 1] == (byte)'a' && wav[i + 2] == (byte)'t' && wav[i + 3] == (byte)'a')
                return i + 8;
        }

        return -1;
    }

    private int QueryMciInt(string command)
    {
        string text = QueryMciString(command);
        return int.TryParse(text, out int value) ? value : 0;
    }

    private static string QueryMciString(string command)
    {
        var buffer = new StringBuilder(64);
        int result = mciSendString(command, buffer, buffer.Capacity, IntPtr.Zero);
        return result == 0 ? buffer.ToString().Trim() : string.Empty;
    }

    public void Dispose()
    {
        _hitCts.Cancel();
        _hitCts.Dispose();
        PlaySound(null, IntPtr.Zero, 0);
        StopInGameBgm();
        StopMainScreenBgm();
        StopSongPreview();
    }
}
