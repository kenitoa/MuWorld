using MuWorld.Native.Core;

namespace MuWorld.Native.Audio;

public sealed class AudioCue
{
    public long TimeUs { get; init; }
    public int Lane { get; init; }
    public long DurationUs { get; init; } = 45_000;
    public bool Accent { get; init; }
}

public sealed class SynthBuffer
{
    public required float[] Samples { get; init; }
    public int SampleRate { get; init; } = 48_000;
    public int Channels => 2;
    public int FrameCount => Samples.Length / 2;
    public long DurationUs => (long)Math.Round(FrameCount * 1_000_000m / SampleRate);
}

/// <summary>WASAPI's correlated QPC is in 100 ns, unlike raw Stopwatch ticks.</summary>
public static class AudioClockMath
{
    public static long SongTimeUs(ulong position, ulong deviceFrequency, ulong anchorQpc100Ns,
        long eventQpcTicks, long qpcFrequency, long startStreamFrame, long fromUs, decimal rate, int sampleRate = 48_000)
    {
        if (deviceFrequency == 0 || qpcFrequency <= 0 || sampleRate <= 0 || rate <= 0) throw new ArgumentOutOfRangeException(nameof(deviceFrequency));
        decimal streamSeconds = (decimal)position / deviceFrequency
            + (decimal)eventQpcTicks / qpcFrequency - (decimal)anchorQpc100Ns / 10_000_000m;
        return checked(fromUs + (long)Math.Round((streamSeconds - (decimal)startStreamFrame / sampleRate) * rate * 1_000_000m, MidpointRounding.AwayFromZero));
    }

    public static double StreamFrame(ulong position, ulong frequency, ulong anchorQpc100Ns, long qpcTicks, int sampleRate = 48_000)
        => ((double)position / frequency + (double)qpcTicks / System.Diagnostics.Stopwatch.Frequency - anchorQpc100Ns / 10_000_000d) * sampleRate;
}

/// <summary>Original music, rendered on a worker before playback. Tempo changes preserve pitch.</summary>
public static class Synthesizer
{
    private static double Midi(int note) => 440 * Math.Pow(2, (note - 69) / 12d);

    public static SynthBuffer Render(Song song, decimal rate = 1m, int sampleRate = 48_000, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(song);
        if (rate is < .5m or > 1.5m || sampleRate < 8_000 || sampleRate > 192_000) throw new ArgumentOutOfRangeException(nameof(rate));
        if (song.Bpm <= 0 || song.Bars <= 0 || song.DurationSec <= 0 || song.DurationSec > 600 || song.Chords.Length == 0 || song.Melody.Length == 0) throw new ArgumentException("곡 합성 정보가 올바르지 않습니다.");
        int length = checked((int)Math.Ceiling(song.DurationSec / (double)rate * sampleRate));
        var data = new float[checked(length * 2)];
        double beat = 60d / song.Bpm / (double)rate;
        int totalBeats = song.Bars * 4;
        uint randomState = (uint)(9317 + song.Art * 77);
        double Random() { randomState = unchecked(randomState * 1664525 + 1013904223); return randomState / 4294967296d; }

        void Tone(double frequency, double at, double span, double level, double pan = 0, bool pad = false, double harmonic = 0)
        {
            int start = (int)Math.Round(at * sampleRate), count = Math.Min((int)Math.Round(span * sampleRate), length - start);
            if (start < 0 || count <= 0) return;
            double omega = 2 * Math.PI * frequency / sampleRate, coefficient = 2 * Math.Cos(omega), harmonicCoefficient = 2 * Math.Cos(omega * 2);
            double previous = 0, current = Math.Sin(omega), previousH = 0, currentH = Math.Sin(omega * 2);
            double gainL = level * Math.Sqrt((1 - pan) / 2), gainR = level * Math.Sqrt((1 + pan) / 2);
            double attack = Math.Min(count * .2, sampleRate * (pad ? .2 : .006)), releaseStart = count * .65;
            double decay = Math.Exp(-6d / count), envelope = 1;
            for (int i = 0; i < count; i++)
            {
                double next = coefficient * current - previous; previous = current; current = next;
                double nextH = harmonicCoefficient * currentH - previousH; previousH = currentH; currentH = nextH;
                double shape = i < attack ? i / attack : pad ? i > releaseStart ? (count - i) / (count - releaseStart) : 1 : envelope;
                double value = (current + currentH * harmonic) * shape;
                data[(start + i) * 2] += (float)(value * gainL); data[(start + i) * 2 + 1] += (float)(value * gainR);
                envelope *= decay;
            }
        }
        void Kick(double at, double level)
        {
            int start = (int)Math.Round(at * sampleRate), count = Math.Min((int)(.32 * sampleRate), length - start);
            double frequencyDelta = 90, amplitude = level, phase = 0, pitchDecay = Math.Exp(-25d / sampleRate), gainDecay = Math.Exp(-19d / sampleRate);
            for (int i = 0; i < count; i++)
            {
                phase += 2 * Math.PI * (45 + frequencyDelta) / sampleRate;
                float value = (float)(Math.Sin(phase) * amplitude * Math.Min(1, i / (sampleRate * .002)));
                data[(start + i) * 2] += value; data[(start + i) * 2 + 1] += value;
                frequencyDelta *= pitchDecay; amplitude *= gainDecay;
            }
        }
        void Noise(double at, double span, double level, bool snare = false, double pan = 0)
        {
            int start = (int)Math.Round(at * sampleRate), count = Math.Min((int)Math.Round(span * sampleRate), length - start);
            double decay = Math.Exp(-6d / Math.Max(1, count)), previous = 0, filtered = 0, amplitude = level;
            for (int i = 0; i < count; i++)
            {
                double white = Random() * 2 - 1, high = white - previous; previous = white;
                filtered = filtered * .65 + white * .35;
                double value = (snare ? filtered : high * .5) * amplitude * Math.Min(1, i / (sampleRate * .0005));
                data[(start + i) * 2] += (float)(value * (1 - pan)); data[(start + i) * 2 + 1] += (float)(value * (1 + pan));
                amplitude *= decay;
            }
            if (snare) Tone(178, at, .09, level * .3);
        }
        for (int b = 0; b < totalBeats; b += 8)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var chord = song.Chords[b / 8 % song.Chords.Length];
            for (int voice = 0; voice < chord.Length; voice++) Tone(Midi(chord[voice]), b * beat, 8.5 * beat, .018, Math.Clamp((voice - 1.5) * .25, -.9, .9), true, .07);
        }
        for (int halfBeat = 0; halfBeat < totalBeats * 2; halfBeat++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            double b = halfBeat / 2d, local = b % 4; int bar = (int)(b / 4);
            var chord = song.Chords[(int)(b / 8) % song.Chords.Length];
            bool intro = b < 8, outro = b >= totalBeats - 4, breakdown = bar % 16 is 12 or 13;
            double level = intro ? .65 : outro ? .6 : breakdown ? .75 : 1;
            int octave = halfBeat % 16 == 11 || song.Art == 4 && halfBeat % 8 == 6 ? 24 : 12;
            Tone(Midi(chord[song.Melody[halfBeat % song.Melody.Length] % chord.Length] + octave), b * beat, beat * 1.7, .085 * level, Math.Sin(halfBeat * .61) * .3, false, song.Art is 0 or 4 ? .22 : .12);
            if (b % 2 == 0) Tone(Midi(chord[0] - 12), b * beat, beat * 1.8, .14 * level, 0, false, .14);
            if (intro) continue;
            bool kick = song.Groove == "velvet" ? b % 1 == 0 : song.Groove == "dawn" ? local is 0 or 1.5 or 2.5 : local is 0 or 2;
            if (kick) Kick(b * beat, (breakdown ? .17 : .28) * (outro ? .7 : 1));
            if (local is 1 or 3) Noise(b * beat, .16, breakdown ? .1 : .17, true, -.05);
            Noise(b * beat, local % 1 == .5 ? .075 : .045, local % 1 == .5 ? .024 : .016, false, .2);
            Noise((b + .25) * beat, .025, .01, false, -.25);
        }
        foreach (int note in song.Chords[0]) Tone(Midi(note + 12), totalBeats * beat, 3.8 * beat, .022, 0, true);
        int delay = (int)Math.Round(beat * .75 * sampleRate);
        for (int frame = delay; frame < length; frame++)
        {
            float echoL = data[(frame - delay) * 2] * .1f, echoR = data[(frame - delay) * 2 + 1] * .1f;
            data[frame * 2] += echoR; data[frame * 2 + 1] += echoL;
        }
        float peak = .001f;
        foreach (float value in data) peak = Math.Max(peak, Math.Abs(value));
        double normalization = Math.Min(2.1, .84 / peak), fadeIn = sampleRate * .015, fadeOut = sampleRate * .8;
        for (int frame = 0; frame < length; frame++)
        {
            double gain = Math.Min(1, Math.Min(frame / fadeIn, (length - 1 - frame) / fadeOut)) * normalization;
            data[frame * 2] *= (float)gain; data[frame * 2 + 1] *= (float)gain;
        }
        return new SynthBuffer { Samples = data, SampleRate = sampleRate };
    }

    public static float[] ToneBuffer(int lane, long durationUs = 45_000, bool accent = false, int sampleRate = 48_000)
    {
        if (lane is < 0 or > 3 || durationUs <= 0 || durationUs > 10_000_000) throw new ArgumentOutOfRangeException(nameof(lane));
        int count = Math.Max(1, (int)Math.Round(durationUs * sampleRate / 1_000_000d));
        var result = new float[count];
        double frequency = accent ? lane == 0 ? 1050 : 800 : new[] { 440d, 550d, 660d, 880d }[lane];
        for (int i = 0; i < count; i++)
        {
            double t = (double)i / sampleRate, envelope = Math.Exp(-6d * i / count) * Math.Min(1, t * 1000);
            result[i] = (float)(Math.Sin(2 * Math.PI * frequency * t) * envelope * (accent ? .22 : .24));
        }
        return result;
    }

    public static SynthBuffer RenderCues(IReadOnlyList<AudioCue> cues, long durationUs, int sampleRate = 48_000)
    {
        if (durationUs <= 0 || durationUs > 600_000_000) throw new ArgumentOutOfRangeException(nameof(durationUs));
        int frames = checked((int)Math.Ceiling(durationUs * sampleRate / 1_000_000d));
        var samples = new float[checked(frames * 2)];
        foreach (var cue in cues)
        {
            if (cue.TimeUs < 0 || cue.TimeUs >= durationUs) continue;
            var tone = ToneBuffer(cue.Lane, cue.DurationUs, cue.Accent, sampleRate);
            int start = (int)Math.Round(cue.TimeUs * sampleRate / 1_000_000d);
            for (int i = 0; i < tone.Length && start + i < frames; i++)
            {
                samples[(start + i) * 2] += tone[i]; samples[(start + i) * 2 + 1] += tone[i];
            }
        }
        for (int i = 0; i < samples.Length; i++) samples[i] = Math.Clamp(samples[i], -.9f, .9f);
        return new SynthBuffer { Samples = samples, SampleRate = sampleRate };
    }
}
