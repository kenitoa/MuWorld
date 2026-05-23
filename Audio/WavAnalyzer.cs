namespace RhythmGame;

internal static class WavAnalyzer
{
    public readonly record struct BeatInfo(float Time, float Energy, float Flux = 0f, float Confidence = 0f);

    public static List<BeatInfo> Analyze(string wavPath)
    {
        using var fs = new FileStream(wavPath, FileMode.Open, FileAccess.Read);
        using var reader = new BinaryReader(fs);

        if (new string(reader.ReadChars(4)) != "RIFF")
            return [];

        reader.ReadInt32();
        if (new string(reader.ReadChars(4)) != "WAVE")
            return [];

        int sampleRate = 0;
        short channels = 0;
        short bitsPerSample = 0;
        byte[]? audioData = null;

        while (fs.Position < fs.Length - 8)
        {
            string chunkId = new(reader.ReadChars(4));
            int chunkSize = reader.ReadInt32();

            if (chunkId == "fmt ")
            {
                reader.ReadInt16();
                channels = reader.ReadInt16();
                sampleRate = reader.ReadInt32();
                reader.ReadInt32();
                reader.ReadInt16();
                bitsPerSample = reader.ReadInt16();
                int remaining = chunkSize - 16;
                if (remaining > 0)
                    reader.ReadBytes(remaining);
            }
            else if (chunkId == "data")
            {
                audioData = reader.ReadBytes(chunkSize);
            }
            else if (chunkSize > 0 && fs.Position + chunkSize <= fs.Length)
            {
                fs.Seek(chunkSize, SeekOrigin.Current);
            }
            else
            {
                break;
            }
        }

        if (audioData is null || sampleRate == 0 || channels == 0 || bitsPerSample == 0)
            return [];

        float[] samples = ConvertToMonoFloat(audioData, channels, bitsPerSample);
        return samples.Length == 0 ? [] : DetectBeats(samples, sampleRate);
    }

    public static float GetDuration(string wavPath)
    {
        try
        {
            using var fs = new FileStream(wavPath, FileMode.Open, FileAccess.Read);
            using var reader = new BinaryReader(fs);

            reader.ReadChars(4);
            reader.ReadInt32();
            reader.ReadChars(4);

            int sampleRate = 0;
            short channels = 0;
            short bitsPerSample = 0;
            int dataSize = 0;

            while (fs.Position < fs.Length - 8)
            {
                string chunkId = new(reader.ReadChars(4));
                int chunkSize = reader.ReadInt32();

                if (chunkId == "fmt ")
                {
                    reader.ReadInt16();
                    channels = reader.ReadInt16();
                    sampleRate = reader.ReadInt32();
                    reader.ReadInt32();
                    reader.ReadInt16();
                    bitsPerSample = reader.ReadInt16();
                    int remaining = chunkSize - 16;
                    if (remaining > 0)
                        reader.ReadBytes(remaining);
                }
                else if (chunkId == "data")
                {
                    dataSize = chunkSize;
                    break;
                }
                else if (chunkSize > 0 && fs.Position + chunkSize <= fs.Length)
                {
                    fs.Seek(chunkSize, SeekOrigin.Current);
                }
                else
                {
                    break;
                }
            }

            if (sampleRate == 0 || channels == 0 || bitsPerSample == 0)
                return 0f;

            int bytesPerSample = bitsPerSample / 8;
            int totalSamples = dataSize / (bytesPerSample * channels);
            return totalSamples / (float)sampleRate;
        }
        catch
        {
            return 0f;
        }
    }

    private static float[] ConvertToMonoFloat(byte[] data, int channels, int bitsPerSample)
    {
        int bytesPerSample = bitsPerSample / 8;
        int blockSize = bytesPerSample * channels;
        int totalFrames = data.Length / blockSize;
        float[] mono = new float[totalFrames];

        for (int i = 0; i < totalFrames; i++)
        {
            float sum = 0f;
            int baseOffset = i * blockSize;

            for (int ch = 0; ch < channels; ch++)
            {
                int offset = baseOffset + ch * bytesPerSample;
                float sample = bitsPerSample switch
                {
                    16 => BitConverter.ToInt16(data, offset) / 32768f,
                    24 => (data[offset] | (data[offset + 1] << 8) | ((sbyte)data[offset + 2] << 16)) / 8388608f,
                    32 => BitConverter.ToInt32(data, offset) / 2147483648f,
                    8 => (data[offset] - 128) / 128f,
                    _ => 0f,
                };
                sum += sample;
            }

            mono[i] = sum / channels;
        }

        return mono;
    }

    private static List<BeatInfo> DetectBeats(float[] samples, int sampleRate)
    {
        int windowSize = Math.Max(512, sampleRate / 43);
        int hopSize = Math.Max(128, windowSize / 2);
        int totalWindows = (samples.Length - windowSize) / hopSize;

        if (totalWindows <= 0)
            return [];

        float[] rms = new float[totalWindows];
        float[] highFrequencyContent = new float[totalWindows];
        for (int w = 0; w < totalWindows; w++)
        {
            int start = w * hopSize;
            double energySum = 0;
            double diffSum = 0;
            float previous = samples[start];

            for (int i = 0; i < windowSize; i++)
            {
                float sample = samples[start + i];
                energySum += sample * sample;
                diffSum += Math.Abs(sample - previous);
                previous = sample;
            }

            rms[w] = (float)Math.Sqrt(energySum / windowSize);
            highFrequencyContent[w] = (float)(diffSum / windowSize);
        }

        float[] onset = new float[totalWindows];
        for (int w = 1; w < totalWindows; w++)
        {
            float energyRise = Math.Max(0f, rms[w] - rms[w - 1]);
            float transientRise = Math.Max(0f, highFrequencyContent[w] - highFrequencyContent[w - 1]);
            onset[w] = energyRise * 0.65f + transientRise * 1.35f + rms[w] * 0.18f;
        }

        int avgWindowHalf = Math.Max(8, (int)Math.Round(0.45f * sampleRate / hopSize));
        var beats = new List<BeatInfo>();
        float minInterval = 0.08f;
        float lastBeatTime = -1f;

        for (int w = 1; w < totalWindows - 1; w++)
        {
            int lo = Math.Max(0, w - avgWindowHalf);
            int hi = Math.Min(totalWindows - 1, w + avgWindowHalf);
            float localSum = 0f;
            float localMax = 0f;
            for (int j = lo; j <= hi; j++)
            {
                localSum += onset[j];
                localMax = Math.Max(localMax, onset[j]);
            }

            float localAvg = localSum / (hi - lo + 1);
            float threshold = localAvg + (localMax - localAvg) * 0.32f;
            float strength = onset[w];

            if (strength > threshold && strength >= onset[w - 1] && strength >= onset[w + 1])
            {
                float time = w * hopSize / (float)sampleRate;
                if (time - lastBeatTime >= minInterval)
                {
                    float confidence = localMax <= 0f ? 0f : Math.Clamp(strength / localMax, 0f, 1f);
                    beats.Add(new BeatInfo(time, rms[w], highFrequencyContent[w], confidence));
                    lastBeatTime = time;
                }
            }
        }

        return beats;
    }
}
