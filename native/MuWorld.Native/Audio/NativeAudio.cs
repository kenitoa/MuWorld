using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using MuWorld.Native.Core;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.Wave;

namespace MuWorld.Native.Audio;

public sealed record AudioInfo
{
    public int SampleRate { get; init; }
    public int EndpointSampleRate { get; init; }
    public string DeviceId { get; init; } = "";
    public string DeviceName { get; init; } = "오디오 준비 전";
    public string ClockSource { get; init; } = "uninitialized";
    public double? RequestedLatencyMs { get; init; }
    public double? ReportedLatencyMs { get; init; }
    public double? BufferDurationMs { get; init; }
    public string State { get; init; } = "uninitialized";
    public string? Error { get; init; }
}

/// <summary>Native, event-driven WASAPI stream. The endpoint clock, never a UI timer, is authoritative.</summary>
public sealed class NativeAudio : IDisposable
{
    private const int SampleRate = 48_000;
    private const double RequestedLatency = 10;
    private sealed record ClockAnchor(ulong Position, ulong Frequency, ulong Qpc100Ns, long ObservedQpc);
    private sealed record TimedTone(long Frame, float[] Samples);
    private sealed record Transport(SynthBuffer? Buffer, long StartFrame, long SourceFrame, long FromUs, long ToUs,
        decimal Rate, bool IsCue, TimedTone[] Tones, int Bpm = 0, bool Metronome = false, bool Paused = false, long PausedUs = 0);
    private sealed class Voice { public float[]? Samples; public int Position; }
    private readonly object _lifecycleGate = new(), _cacheGate = new();
    private readonly SemaphoreSlim _renderGate = new(1, 1);
    private readonly ManualResetEvent _shutdown = new(false);
    private readonly ConcurrentQueue<int> _hits = new();
    private readonly Dictionary<string, SynthBuffer> _cache = new();
    private readonly Dictionary<string, Task<SynthBuffer>> _pending = new();
    private readonly LinkedList<string> _recent = new();
    private readonly float[][] _hitBuffers = Enumerable.Range(0, 4).Select(lane => Synthesizer.ToneBuffer(lane)).ToArray();
    private readonly float[] _strongClick = Synthesizer.ToneBuffer(0, 35_000, true), _weakClick = Synthesizer.ToneBuffer(1, 35_000, true);
    private readonly Voice[] _voices = Enumerable.Range(0, 32).Select(_ => new Voice()).ToArray();
    private Thread? _thread;
    private TaskCompletionSource<bool>? _ready;
    private ClockAnchor? _clock;
    private Transport? _transport;
    private AudioInfo _deviceInfo = new();
    private string _state = "uninitialized";
    private string? _error;
    private long _submittedFrames, _generation, _lastPositionUs;
    private int _nextVoice, _disposed;
    private double _volume = .7, _effectVolume = .3;

    public event Action<string>? Faulted;
    public double Volume { get => Volatile.Read(ref _volume); set => Volatile.Write(ref _volume, Math.Clamp(value, 0, 1)); }
    public double EffectVolume { get => Volatile.Read(ref _effectVolume); set => Volatile.Write(ref _effectVolume, Math.Clamp(value, 0, 1)); }
    public AudioInfo Info => Volatile.Read(ref _deviceInfo) with { State = Volatile.Read(ref _state), Error = Volatile.Read(ref _error) };

    public async Task InitializeAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        while (true)
        {
            Task<bool>? ready = null; Thread? stopping = null;
            lock (_lifecycleGate)
            {
                ObjectDisposedException.ThrowIf(_disposed != 0, this);
                if (_thread?.IsAlive == true && _shutdown.WaitOne(0)) stopping = _thread;
                else if (_thread?.IsAlive == true) ready = _ready!.Task;
                else
                {
                    _shutdown.Reset(); _clock = null; _submittedFrames = 0; _error = null;
                    Volatile.Write(ref _transport, null);
                    if (Volatile.Read(ref _state) != "preparing") Volatile.Write(ref _state, "initializing");
                    _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    _thread = new Thread(AudioThread) { IsBackground = true, Name = "MuWorld WASAPI", Priority = ThreadPriority.AboveNormal };
                    _thread.SetApartmentState(ApartmentState.MTA);
                    _thread.Start(); ready = _ready.Task;
                }
            }
            // A device failure can race a new play request. Do not reuse the completed
            // readiness task from the failed endpoint while its COM objects are closing.
            if (stopping != null)
            {
                if (!await Task.Run(() => stopping.Join(2_000)).ConfigureAwait(false))
                    throw new InvalidOperationException("이전 오디오 장치를 정리하고 있습니다. 잠시 뒤 다시 시작해 주세요.");
                continue;
            }
            await ready!.ConfigureAwait(false); return;
        }
    }

    private Task<SynthBuffer> GetBufferAsync(Song song, decimal rate)
    {
        if (rate is < .5m or > 1.5m) throw new ArgumentOutOfRangeException(nameof(rate));
        string key = $"{song.Id}:{rate.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
        lock (_cacheGate)
        {
            if (_cache.TryGetValue(key, out var cached)) { _recent.Remove(key); _recent.AddLast(key); return Task.FromResult(cached); }
            if (_pending.TryGetValue(key, out var existing)) return existing;
            var task = Task.Run(async () =>
            {
                // Navigation can request several previews quickly. Render one PCM buffer at
                // a time to bound transient memory and keep the audio thread responsive.
                await _renderGate.WaitAsync().ConfigureAwait(false);
                try { ObjectDisposedException.ThrowIf(_disposed != 0, this); return Synthesizer.Render(song, rate); }
                finally { _renderGate.Release(); }
            });
            _pending[key] = task;
            _ = task.ContinueWith(completed =>
            {
                lock (_cacheGate)
                {
                    _pending.Remove(key);
                    if (completed.IsCompletedSuccessfully && _disposed == 0)
                    {
                        _cache[key] = completed.Result; _recent.Remove(key); _recent.AddLast(key);
                        while (_recent.Count > 3) { string oldest = _recent.First!.Value; _recent.RemoveFirst(); _cache.Remove(oldest); }
                    }
                }
            }, TaskScheduler.Default);
            return task;
        }
    }

    public async Task PrepareAsync(Song song, decimal rate) => await GetBufferAsync(song, rate).ConfigureAwait(false);

    public async Task StartAsync(Song song, decimal rate = 1m, long fromUs = 0, long? toUs = null, double leadInSec = 2, bool metronome = false)
    {
        long generation = Interlocked.Increment(ref _generation);
        Volatile.Write(ref _state, "preparing");
        Volatile.Write(ref _transport, null);
        try
        {
            var buffer = await GetBufferAsync(song, rate).ConfigureAwait(false);
            await InitializeAsync().ConfigureAwait(false);
            if (generation != Volatile.Read(ref _generation)) return;
            long duration = (long)Math.Round(song.DurationSec * 1_000_000);
            long end = toUs ?? duration;
            if (fromUs < 0 || end <= fromUs || end > duration) throw new ArgumentOutOfRangeException(nameof(fromUs));
            Schedule(buffer, rate, fromUs, end, leadInSec, false, song.Bpm, metronome);
        }
        catch (Exception exception) { if (generation == Volatile.Read(ref _generation)) SignalFault(exception.Message); throw; }
    }

    public async Task StartCuesAsync(IReadOnlyList<AudioCue> cues, long durationUs, double leadInSec = 1)
    {
        long generation = Interlocked.Increment(ref _generation);
        Volatile.Write(ref _state, "preparing"); Volatile.Write(ref _transport, null);
        try
        {
            var buffer = await Task.Run(() => Synthesizer.RenderCues(cues, durationUs)).ConfigureAwait(false);
            await InitializeAsync().ConfigureAwait(false);
            if (generation != Volatile.Read(ref _generation)) return;
            Schedule(buffer, 1m, 0, durationUs, leadInSec, true, 0, false);
        }
        catch (Exception exception) { if (generation == Volatile.Read(ref _generation)) SignalFault(exception.Message); throw; }
    }

    private void Schedule(SynthBuffer buffer, decimal rate, long fromUs, long toUs, double leadInSec, bool cue, int bpm, bool metronome)
    {
        leadInSec = Math.Clamp(leadInSec, 0, 8);
        // Use the queued stream frame, so no guessed output latency enters the timing equation.
        long startFrame = Volatile.Read(ref _submittedFrames) + (long)Math.Round((leadInSec + .03) * SampleRate);
        var tones = new List<TimedTone>();
        if (bpm > 0)
        {
            double beatFrames = 60d / bpm / (double)rate * SampleRate;
            int count = (int)Math.Floor(leadInSec * SampleRate / beatFrames);
            for (int beat = count; beat > 0; beat--) tones.Add(new(startFrame - (long)Math.Round(beat * beatFrames), beat == count ? _strongClick : _weakClick));
            if (metronome)
            {
                long firstBeat = (long)Math.Ceiling(fromUs * bpm / 60_000_000m), lastBeat = (long)Math.Floor(toUs * bpm / 60_000_000m);
                for (long beat = firstBeat; beat <= lastBeat; beat++)
                    tones.Add(new(startFrame + (long)Math.Round((beat * 60_000_000m / bpm - fromUs) / rate * SampleRate / 1_000_000m), beat % 4 == 0 ? _strongClick : _weakClick));
            }
        }
        long sourceFrame = (long)Math.Round(fromUs / rate * SampleRate / 1_000_000m);
        Volatile.Write(ref _transport, new(buffer, startFrame, sourceFrame, fromUs, toUs, rate, cue, tones.ToArray(), bpm, metronome));
        Volatile.Write(ref _lastPositionUs, fromUs);
        Volatile.Write(ref _state, "running");
    }

    public long SongTimeUs(long? qpcTicks = null)
    {
        var transport = Volatile.Read(ref _transport);
        if (transport == null) return Volatile.Read(ref _lastPositionUs);
        if (transport.Paused) return transport.PausedUs;
        if (Volatile.Read(ref _state) == "error") return Volatile.Read(ref _lastPositionUs);
        var anchor = Volatile.Read(ref _clock);
        if (anchor == null) throw new InvalidOperationException("WASAPI 출력 시계가 준비되지 않았습니다.");
        long current = Stopwatch.GetTimestamp();
        if ((double)(current - anchor.ObservedQpc) / Stopwatch.Frequency > .5)
        {
            SignalFault("오디오 장치 시계의 갱신이 중단되었습니다. 출력 장치를 확인하고 다시 시작해 주세요.");
            return Volatile.Read(ref _lastPositionUs);
        }
        long time = AudioClockMath.SongTimeUs(anchor.Position, anchor.Frequency, anchor.Qpc100Ns, qpcTicks ?? current,
            Stopwatch.Frequency, transport.StartFrame, transport.FromUs, transport.Rate, SampleRate);
        if (!qpcTicks.HasValue) Volatile.Write(ref _lastPositionUs, time);
        return time;
    }

    public void Pause()
    {
        var transport = Volatile.Read(ref _transport);
        if (transport == null || transport.Paused || Volatile.Read(ref _state) != "running") return;
        long time = Math.Max(transport.FromUs, SongTimeUs());
        if (Volatile.Read(ref _state) != "running") return;
        Interlocked.Increment(ref _generation);
        Volatile.Write(ref _transport, transport with { Paused = true, PausedUs = time });
        Volatile.Write(ref _lastPositionUs, time); Volatile.Write(ref _state, "paused"); _hits.Enqueue(-1);
    }

    public async Task ResumeAsync()
    {
        var transport = Volatile.Read(ref _transport);
        if (transport == null || !transport.Paused) return;
        long generation = Interlocked.Increment(ref _generation);
        await InitializeAsync().ConfigureAwait(false);
        if (generation != Volatile.Read(ref _generation)) return;
        if (transport.PausedUs >= transport.ToUs)
        {
            var anchor = Volatile.Read(ref _clock) ?? throw new InvalidOperationException("출력 시계가 없습니다.");
            long frame = (long)Math.Round(AudioClockMath.StreamFrame(anchor.Position, anchor.Frequency, anchor.Qpc100Ns, Stopwatch.GetTimestamp(), SampleRate));
            Volatile.Write(ref _transport, transport with { Buffer = null, StartFrame = frame, SourceFrame = 0, FromUs = transport.PausedUs, Paused = false, Tones = [] });
            Volatile.Write(ref _state, "running");
        }
        else Schedule(transport.Buffer!, transport.Rate, transport.PausedUs, transport.ToUs, 1.5, transport.IsCue, transport.Bpm, transport.Metronome);
    }

    public void Stop()
    {
        Interlocked.Increment(ref _generation); Volatile.Write(ref _transport, null); Volatile.Write(ref _lastPositionUs, 0);
        if (_disposed == 0) Volatile.Write(ref _state, "idle"); _hits.Enqueue(-1);
    }

    public void Hit(int lane) { if (lane is >= 0 and < 4 && _disposed == 0 && Volatile.Read(ref _clock) != null) _hits.Enqueue(lane); }

    private void Fill(float[] data, int frames, long firstFrame)
    {
        Array.Clear(data, 0, frames * 2);
        var transport = Volatile.Read(ref _transport);
        float musicGain = (float)Volume, effectsGain = (float)EffectVolume;
        if (transport is { Paused: false } && Volatile.Read(ref _state) == "running")
        {
            if (transport.Buffer is { } source)
            {
                long maxFrames = (long)Math.Round((transport.ToUs - transport.FromUs) / transport.Rate * SampleRate / 1_000_000m);
                for (int frame = 0; frame < frames; frame++)
                {
                    long relative = firstFrame + frame - transport.StartFrame, sourceFrame = transport.SourceFrame + relative;
                    if (relative < 0 || relative >= maxFrames || sourceFrame < 0 || sourceFrame >= source.FrameCount) continue;
                    float gain = musicGain;
                    // Small edge ramps prevent a click when practice starts or ends mid-waveform.
                    if (!transport.IsCue) gain *= (float)Math.Min(1, Math.Min(relative / 240d, (maxFrames - 1 - relative) / 240d));
                    data[frame * 2] += source.Samples[sourceFrame * 2] * gain;
                    data[frame * 2 + 1] += source.Samples[sourceFrame * 2 + 1] * gain;
                }
            }
            foreach (var tone in transport.Tones)
            {
                long begin = Math.Max(firstFrame, tone.Frame), end = Math.Min(firstFrame + frames, tone.Frame + tone.Samples.Length);
                for (long frame = begin; frame < end; frame++)
                {
                    float value = tone.Samples[frame - tone.Frame] * effectsGain;
                    int offset = (int)(frame - firstFrame) * 2; data[offset] += value; data[offset + 1] += value;
                }
            }
        }
        while (_hits.TryDequeue(out int lane))
        {
            if (lane < 0) { foreach (var oldVoice in _voices) oldVoice.Samples = null; continue; }
            var voice = _voices[_nextVoice++ % _voices.Length]; voice.Samples = _hitBuffers[lane]; voice.Position = 0;
        }
        foreach (var voice in _voices)
        {
            if (voice.Samples is not { } samples) continue;
            int count = Math.Min(frames, samples.Length - voice.Position);
            for (int frame = 0; frame < count; frame++) { float value = samples[voice.Position++] * effectsGain; data[frame * 2] += value; data[frame * 2 + 1] += value; }
            if (voice.Position >= samples.Length) voice.Samples = null;
        }
        for (int sample = 0; sample < frames * 2; sample++) data[sample] = Math.Clamp(data[sample], -.98f, .98f);
    }

    private void AudioThread()
    {
        MMDeviceEnumerator? enumerator = null; MMDevice? device = null; AudioClient? client = null; EndpointNotifications? notifications = null;
        IntPtr mmcss = IntPtr.Zero; uint taskIndex = 0;
        try
        {
            mmcss = AvSetMmThreadCharacteristics("Pro Audio", ref taskIndex);
            enumerator = new MMDeviceEnumerator(); device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            client = device.AudioClient;
            int endpointRate = client.MixFormat.SampleRate;
            var format = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, 2);
            client.Initialize(AudioClientShareMode.Shared, AudioClientStreamFlags.EventCallback | AudioClientStreamFlags.AutoConvertPcm | AudioClientStreamFlags.SrcDefaultQuality,
                (long)(RequestedLatency * 10_000), 0, format, Guid.Empty);
            using var frameReady = new AutoResetEvent(false);
            client.SetEventHandle(frameReady.SafeWaitHandle.DangerousGetHandle());
            int capacity = client.BufferSize;
            var renderer = client.AudioRenderClient; var clock = client.AudioClockClient;
            ulong frequency = clock.Frequency;
            if (frequency == 0) throw new InvalidOperationException("오디오 장치가 유효한 시계를 제공하지 않습니다.");
            var buffer = new float[capacity * 2];
            var pointer = renderer.GetBuffer(capacity); Marshal.Copy(buffer, 0, pointer, buffer.Length); renderer.ReleaseBuffer(capacity, AudioClientBufferFlags.Silent);
            Volatile.Write(ref _submittedFrames, capacity);
            long latency = client.StreamLatency;
            Volatile.Write(ref _deviceInfo, new AudioInfo { SampleRate = SampleRate, EndpointSampleRate = endpointRate, DeviceId = device.ID, DeviceName = device.FriendlyName,
                ClockSource = "WASAPI IAudioClock + QPC", RequestedLatencyMs = RequestedLatency, ReportedLatencyMs = latency > 0 ? latency / 10_000d : null, BufferDurationMs = capacity * 1000d / SampleRate });
            notifications = new EndpointNotifications(this, device.ID); enumerator.RegisterEndpointNotificationCallback(notifications);
            client.Start();
            var handles = new WaitHandle[] { _shutdown, frameReady };
            long started = Stopwatch.GetTimestamp();
            while (!_shutdown.WaitOne(0))
            {
                if (WaitHandle.WaitAny(handles, 100) == 0) break;
                if (clock.GetPosition(out ulong position, out ulong qpc) && position > 0 && qpc > 0)
                {
                    // A drained endpoint inserts silence. Continuing with the old PCM frame origin
                    // would make the visible chart and audible song disagree, so end this run.
                    decimal deviceFrame = (decimal)position * SampleRate / frequency;
                    if (deviceFrame > Volatile.Read(ref _submittedFrames) + 2)
                        throw new InvalidOperationException("오디오 버퍼가 비어 재생 연속성이 끊겼습니다. 현재 플레이를 다시 시작해 주세요.");
                    Volatile.Write(ref _clock, new(position, frequency, qpc, Stopwatch.GetTimestamp()));
                    Interlocked.CompareExchange(ref _state, "idle", "initializing");
                    _ready?.TrySetResult(true);
                }
                else if (Stopwatch.GetElapsedTime(started).TotalSeconds > 3 && _clock == null) throw new InvalidOperationException("오디오 출력 시계가 시작되지 않았습니다.");
                int available = capacity - client.CurrentPadding;
                if (available > 0)
                {
                    long first = Volatile.Read(ref _submittedFrames);
                    Fill(buffer, available, first);
                    pointer = renderer.GetBuffer(available); Marshal.Copy(buffer, 0, pointer, available * 2); renderer.ReleaseBuffer(available, AudioClientBufferFlags.None);
                    Volatile.Write(ref _submittedFrames, first + available);
                }
            }
        }
        catch (Exception exception) { _ready?.TrySetException(new InvalidOperationException($"WASAPI 오디오를 시작하거나 유지할 수 없습니다: {exception.Message}", exception)); SignalFault(exception.Message); }
        finally
        {
            try { client?.Stop(); } catch { }
            if (notifications != null) try { enumerator?.UnregisterEndpointNotificationCallback(notifications); } catch { }
            client?.Dispose(); device?.Dispose(); enumerator?.Dispose();
            if (mmcss != IntPtr.Zero) AvRevertMmThreadCharacteristics(mmcss);
        }
    }

    private void SignalFault(string message)
    {
        if (_disposed != 0 || Volatile.Read(ref _state) == "error") return;
        Volatile.Write(ref _error, message); Volatile.Write(ref _state, "error"); _shutdown.Set();
        _ready?.TrySetException(new InvalidOperationException(message));
        try { Faulted?.Invoke(message); } catch { /* UI subscriptions must dispatch; an exception cannot kill the audio thread. */ }
    }

    private sealed class EndpointNotifications(NativeAudio owner, string id) : IMMNotificationClient
    {
        public void OnDeviceStateChanged(string deviceId, DeviceState state) { if (deviceId == id && state != DeviceState.Active) owner.SignalFault("오디오 장치가 비활성화되었습니다. 장치를 확인한 뒤 다시 시작하세요."); }
        public void OnDeviceAdded(string deviceId) { }
        public void OnDeviceRemoved(string deviceId) { if (deviceId == id) owner.SignalFault("사용 중인 오디오 장치가 분리되었습니다."); }
        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId) { if (flow == DataFlow.Render && role == Role.Multimedia && defaultDeviceId != id) owner.SignalFault("기본 출력 장치가 변경되었습니다. 보정을 확인하고 다시 시작하세요."); }
        public void OnPropertyValueChanged(string deviceId, PropertyKey key) { }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        Interlocked.Increment(ref _generation); Volatile.Write(ref _transport, null); _shutdown.Set();
        if (_thread?.IsAlive == true && Thread.CurrentThread != _thread) _thread.Join(2_000);
        lock (_cacheGate) { _cache.Clear(); _recent.Clear(); }
        Volatile.Write(ref _state, "disposed");
        if (_thread?.IsAlive != true) _shutdown.Dispose();
    }

    [DllImport("avrt.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr AvSetMmThreadCharacteristics(string taskName, ref uint taskIndex);
    [DllImport("avrt.dll", SetLastError = true)] private static extern bool AvRevertMmThreadCharacteristics(IntPtr handle);
}
