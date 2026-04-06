using System.Media;
using System.Runtime.InteropServices;
using System.Text;

namespace RhythmGame;

internal sealed class AudioManager : IDisposable
{
    private readonly object _sync = new();
    private SoundPlayer? _bgmPlayer;
    private MemoryStream? _bgmStream;
    private int _bgmVolume;
    private CancellationTokenSource _hitCts = new();

    // winmm.dll P/Invoke — 히트 사운드용
    [DllImport("winmm.dll", SetLastError = true)]
    private static extern bool PlaySound(byte[]? pszSound, IntPtr hmod, uint fdwSound);

    private const uint SND_ASYNC  = 0x0001;
    private const uint SND_MEMORY = 0x0004;
    private const uint SND_NOSTOP = 0x0010;

    // MCI P/Invoke — 인게임 BGM 재생 (PlaySound/SoundPlayer와 완전히 독립)
    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    private static extern int mciSendString(string command, StringBuilder? returnString, int returnSize, IntPtr hwndCallback);

    private bool _mciOpen;

    // 히트 사운드 WAV 바이트 캐시
    private byte[]? _perfectHitWav;
    private byte[]? _goodHitWav;
    private int _lastHitVolume = -1;

    public void StartBgm(int volume)
    {
        lock (_sync)
        {
            _bgmVolume = Math.Clamp(volume, 0, 100);
            RestartBgmCore();
        }
    }

    public void StopBgm()
    {
        lock (_sync)
        {
            _bgmPlayer?.Stop();
            _bgmPlayer?.Dispose();
            _bgmPlayer = null;
            _bgmStream?.Dispose();
            _bgmStream = null;
        }
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

    public void PlayHit(int volume, bool isPerfect, bool mute = false)
    {
        if (mute)
            return;

        int clamped = Math.Clamp(volume, 0, 100);
        if (clamped <= 0)
            return;

        if (clamped != _lastHitVolume)
        {
            _lastHitVolume = clamped;
            float amp = 0.05f + clamped / 100f * 0.25f;
            _perfectHitWav = CreateDualToneWavBytes(990, 990 * 2f, 80, amp * 0.75f, amp * 0.45f);
            _goodHitWav = CreateDualToneWavBytes(760, 760 * 2f, 70, amp * 0.75f, amp * 0.45f);
        }

        byte[]? wav = isPerfect ? _perfectHitWav : _goodHitWav;
        if (wav is not null)
        {
            PlaySound(wav, IntPtr.Zero, SND_ASYNC | SND_MEMORY);
        }
    }

    public void StopAllSounds()
    {
        _hitCts.Cancel();
        _hitCts.Dispose();
        _hitCts = new CancellationTokenSource();
        PlaySound(null, IntPtr.Zero, 0);
        StopBgm();
        StopInGameBgm();
        StopMainScreenBgm();
    }

    public bool IsInGameBgmPlaying
    {
        get { lock (_sync) { return _mciOpen; } }
    }

    /// <summary>
    /// Songs/InGameBGM 폴더의 WAV 파일을 MCI로 재생한다.
    /// MCI는 PlaySound/SoundPlayer와 독립적인 채널을 사용하므로 히트 사운드와 동시 재생 가능.
    /// </summary>
    public void PlayInGameBgm(string wavPath)
    {
        StopInGameBgm();
        StopBgm();

        if (!File.Exists(wavPath))
            return;

        lock (_sync)
        {
            string safePath = $"\"{wavPath}\"";
            mciSendString($"open {safePath} type mpegvideo alias ingamebgm", null, 0, IntPtr.Zero);
            mciSendString("play ingamebgm", null, 0, IntPtr.Zero);
            int mciVol = _bgmVolume * 10;
            mciSendString($"setaudio ingamebgm volume to {mciVol}", null, 0, IntPtr.Zero);
            _mciOpen = true;
        }
    }

    public void StopInGameBgm()
    {
        lock (_sync)
        {
            if (_mciOpen)
            {
                mciSendString("stop ingamebgm", null, 0, IntPtr.Zero);
                mciSendString("close ingamebgm", null, 0, IntPtr.Zero);
                _mciOpen = false;
            }
        }
    }

    private bool _mainBgmOpen;

    /// <summary>
    /// 메인 화면 BGM을 MCI로 재생한다 (Songs/MainScreenBGM).
    /// </summary>
    public void PlayMainScreenBgm()
    {
        StopMainScreenBgm();

        string bgmDir = Path.Combine(AppContext.BaseDirectory, "Songs", "MainScreenBGM");
        if (!Directory.Exists(bgmDir))
            return;

        string[] wavFiles = Directory.GetFiles(bgmDir, "*.wav", SearchOption.TopDirectoryOnly);
        if (wavFiles.Length == 0)
            return;

        string wavPath = wavFiles[0];

        lock (_sync)
        {
            string safePath = $"\"{wavPath}\"";
            // mpegvideo 타입은 repeat를 지원함
            mciSendString($"open {safePath} type mpegvideo alias mainbgm", null, 0, IntPtr.Zero);
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

    private void RestartBgmCore()
    {
        _bgmPlayer?.Stop();
        _bgmPlayer?.Dispose();
        _bgmPlayer = null;
        _bgmStream?.Dispose();
        _bgmStream = null;
        // 합성 톤 BGM은 더 이상 사용하지 않음 — MainScreenBGM WAV로 대체됨
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

    public void Dispose()
    {
        _hitCts.Cancel();
        _hitCts.Dispose();
        PlaySound(null, IntPtr.Zero, 0);
        StopInGameBgm();
        StopMainScreenBgm();
        StopBgm();
    }
}
