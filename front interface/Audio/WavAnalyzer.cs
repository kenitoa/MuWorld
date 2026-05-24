namespace RhythmGame;

internal static class WavAnalyzer
{
    public readonly record struct BeatInfo(
        float Time,
        float Energy,
        float Flux = 0f,
        float Confidence = 0f,
        float LowEnergy = 0f,
        float MidEnergy = 0f,
        float HighEnergy = 0f);

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
        catch (Exception ex)
        {
            AppLogger.Error($"Failed to read WAV duration for {Path.GetFileName(wavPath)}.", ex);
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
        int windowSize = Math.Max(512, sampleRate / 86);
        int hopSize = Math.Max(128, windowSize / 2);
        int totalWindows = (samples.Length - windowSize) / hopSize;

        if (totalWindows <= 0)
            return [];

        float[] rms = new float[totalWindows];
        float[] lowBand = new float[totalWindows];
        float[] midBand = new float[totalWindows];
        float[] highBand = new float[totalWindows];
        float[] spectralFlux = new float[totalWindows];
        float slowLow = 0f;
        float fastLow = 0f;
        for (int w = 0; w < totalWindows; w++)
        {
            int start = w * hopSize;
            double energySum = 0;
            double lowSum = 0;
            double midSum = 0;
            double highSum = 0;
            double fluxSum = 0;
            float previous = samples[start];

            for (int i = 0; i < windowSize; i++)
            {
                float sample = samples[start + i];
                slowLow = slowLow * 0.992f + sample * 0.008f;
                fastLow = fastLow * 0.92f + sample * 0.08f;
                float low = slowLow;
                float mid = fastLow - slowLow;
                float high = sample - fastLow;
                float diff = Math.Abs(sample - previous);

                energySum += sample * sample;
                lowSum += low * low;
                midSum += mid * mid;
                highSum += high * high;
                fluxSum += Math.Max(0f, diff - Math.Abs(previous) * 0.08f);
                previous = sample;
            }

            rms[w] = (float)Math.Sqrt(energySum / windowSize);
            lowBand[w] = (float)Math.Sqrt(lowSum / windowSize);
            midBand[w] = (float)Math.Sqrt(midSum / windowSize);
            highBand[w] = (float)Math.Sqrt(highSum / windowSize);
            spectralFlux[w] = (float)(fluxSum / windowSize);
        }

        float[] onset = new float[totalWindows];
        for (int w = 1; w < totalWindows; w++)
        {
            float energyRise = Math.Max(0f, rms[w] - rms[w - 1]);
            float lowRise = Math.Max(0f, lowBand[w] - lowBand[w - 1]);
            float midRise = Math.Max(0f, midBand[w] - midBand[w - 1]);
            float highRise = Math.Max(0f, highBand[w] - highBand[w - 1]);
            float fluxRise = Math.Max(0f, spectralFlux[w] - spectralFlux[w - 1]);
            onset[w] = lowRise * 1.45f + midRise * 0.95f + highRise * 1.35f + fluxRise * 1.15f + energyRise * 0.55f + rms[w] * 0.10f;
        }

        int avgWindowHalf = Math.Max(8, (int)Math.Round(0.45f * sampleRate / hopSize));
        var beats = new List<BeatInfo>();
        float minInterval = 0.075f;
        float lastBeatTime = -1f;

        for (int w = 1; w < totalWindows - 1; w++)
        {
            int lo = Math.Max(0, w - avgWindowHalf);
            int hi = Math.Min(totalWindows - 1, w + avgWindowHalf);
            float localSum = 0f;
            float localMax = 0f;
            float variance = 0f;
            for (int j = lo; j <= hi; j++)
            {
                localSum += onset[j];
                localMax = Math.Max(localMax, onset[j]);
            }

            float localAvg = localSum / (hi - lo + 1);
            for (int j = lo; j <= hi; j++)
            {
                float diff = onset[j] - localAvg;
                variance += diff * diff;
            }

            float localStdDev = MathF.Sqrt(variance / (hi - lo + 1));
            float threshold = localAvg + localStdDev * 0.55f + (localMax - localAvg) * 0.22f;
            float strength = onset[w];

            if (strength > threshold && strength >= onset[w - 1] && strength >= onset[w + 1])
            {
                float time = w * hopSize / (float)sampleRate;
                if (time - lastBeatTime >= minInterval)
                {
                    float contrast = localStdDev <= 0f ? 0f : Math.Clamp((strength - localAvg) / (localStdDev * 2.4f), 0f, 1f);
                    float peakRatio = localMax <= 0f ? 0f : Math.Clamp(strength / localMax, 0f, 1f);
                    float confidence = Math.Clamp(contrast * 0.65f + peakRatio * 0.35f, 0f, 1f);
                    beats.Add(new BeatInfo(time, rms[w], spectralFlux[w], confidence, lowBand[w], midBand[w], highBand[w]));
                    lastBeatTime = time;
                }
            }
        }

        return beats;
    }
}
