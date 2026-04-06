namespace RhythmGame;

/// <summary>
/// WAV 파일의 오디오 데이터를 분석하여 에너지 피크(비트)를 감지하는 클래스.
/// </summary>
internal static class WavAnalyzer
{
    public readonly record struct BeatInfo(float Time, float Energy);

    /// <summary>
    /// WAV 파일을 읽어 에너지 기반 비트 위치를 반환한다.
    /// </summary>
    public static List<BeatInfo> Analyze(string wavPath)
    {
        using var fs = new FileStream(wavPath, FileMode.Open, FileAccess.Read);
        using var reader = new BinaryReader(fs);

        // RIFF 헤더 파싱
        string riff = new(reader.ReadChars(4));
        if (riff != "RIFF")
            return [];

        reader.ReadInt32(); // file size
        string wave = new(reader.ReadChars(4));
        if (wave != "WAVE")
            return [];

        int sampleRate = 0;
        short channels = 0;
        short bitsPerSample = 0;
        byte[]? audioData = null;

        // 청크 탐색
        while (fs.Position < fs.Length - 8)
        {
            string chunkId = new(reader.ReadChars(4));
            int chunkSize = reader.ReadInt32();

            if (chunkId == "fmt ")
            {
                short audioFormat = reader.ReadInt16();
                channels = reader.ReadInt16();
                sampleRate = reader.ReadInt32();
                reader.ReadInt32(); // byte rate
                reader.ReadInt16(); // block align
                bitsPerSample = reader.ReadInt16();
                int remaining = chunkSize - 16;
                if (remaining > 0)
                    reader.ReadBytes(remaining);
            }
            else if (chunkId == "data")
            {
                audioData = reader.ReadBytes(chunkSize);
            }
            else
            {
                // 알 수 없는 청크 스킵
                if (chunkSize > 0 && fs.Position + chunkSize <= fs.Length)
                    fs.Seek(chunkSize, SeekOrigin.Current);
                else
                    break;
            }
        }

        if (audioData == null || sampleRate == 0 || channels == 0 || bitsPerSample == 0)
            return [];

        // PCM 샘플을 float 배열로 변환 (모노 다운믹스)
        float[] samples = ConvertToMonoFloat(audioData, channels, bitsPerSample);
        if (samples.Length == 0)
            return [];

        return DetectBeats(samples, sampleRate);
    }

    /// <summary>
    /// WAV 파일의 총 재생 시간(초)을 반환한다.
    /// </summary>
    public static float GetDuration(string wavPath)
    {
        try
        {
            using var fs = new FileStream(wavPath, FileMode.Open, FileAccess.Read);
            using var reader = new BinaryReader(fs);

            reader.ReadChars(4); // RIFF
            reader.ReadInt32();
            reader.ReadChars(4); // WAVE

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
                else
                {
                    if (chunkSize > 0 && fs.Position + chunkSize <= fs.Length)
                        fs.Seek(chunkSize, SeekOrigin.Current);
                    else
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
        // 윈도우 크기: ~23ms (1024 samples at 44100Hz)
        int windowSize = sampleRate / 43;
        int hopSize = windowSize / 2;
        int totalWindows = (samples.Length - windowSize) / hopSize;

        if (totalWindows <= 0)
            return [];

        // 각 윈도우의 RMS 에너지 계산
        float[] energies = new float[totalWindows];
        for (int w = 0; w < totalWindows; w++)
        {
            int start = w * hopSize;
            double sum = 0;
            for (int i = 0; i < windowSize; i++)
            {
                float s = samples[start + i];
                sum += s * s;
            }
            energies[w] = (float)Math.Sqrt(sum / windowSize);
        }

        // 로컬 평균 에너지 대비 피크 감지
        int avgWindowHalf = 22; // ~0.5초 범위
        var beats = new List<BeatInfo>();
        float minInterval = 0.08f; // 최소 비트 간격 (초)
        float lastBeatTime = -1f;

        for (int w = 1; w < totalWindows - 1; w++)
        {
            // 로컬 평균 계산
            int lo = Math.Max(0, w - avgWindowHalf);
            int hi = Math.Min(totalWindows - 1, w + avgWindowHalf);
            float localSum = 0;
            for (int j = lo; j <= hi; j++)
                localSum += energies[j];
            float localAvg = localSum / (hi - lo + 1);

            float threshold = localAvg * 1.4f;
            float energy = energies[w];

            // 피크: 현재가 양쪽 이웃보다 크고 임계값 초과
            if (energy > threshold && energy >= energies[w - 1] && energy >= energies[w + 1])
            {
                float time = w * hopSize / (float)sampleRate;
                if (time - lastBeatTime >= minInterval)
                {
                    beats.Add(new BeatInfo(time, energy));
                    lastBeatTime = time;
                }
            }
        }

        return beats;
    }
}
