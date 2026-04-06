namespace RhythmGame;

public readonly record struct LaneNote(float Time, int Lane);

public static class NoteLane
{
    private const string ChartFolderName = "NoteLane";
    private const string DefaultChartFile = "default.bms";

    public static IReadOnlyList<LaneNote> LoadNotes(string? title, string? artist, int difficultyIndex)
    {
        string chartDir = Path.Combine(AppContext.BaseDirectory, ChartFolderName);

        // WAV 기반 곡: ChartGenerator가 생성한 파일 탐색
        if (!string.IsNullOrWhiteSpace(title))
        {
            string generatedFileName = ChartGenerator.GetChartFileName(title, difficultyIndex);
            string generatedPath = Path.Combine(chartDir, generatedFileName);
            if (File.Exists(generatedPath))
            {
                List<LaneNote> notes = ParseSimpleBms(generatedPath);
                if (notes.Count > 0)
                    return notes;
            }
        }

        // 기본 채보 fallback
        string defaultPath = Path.Combine(chartDir, DefaultChartFile);
        if (File.Exists(defaultPath))
        {
            List<LaneNote> notes = ParseSimpleBms(defaultPath);
            if (notes.Count > 0)
                return notes;
        }

        return CreateFallbackPattern(difficultyIndex);
    }

    private static List<LaneNote> ParseSimpleBms(string filePath)
    {
        float bpm = 128f;
        var notes = new List<LaneNote>();

        foreach (string rawLine in File.ReadLines(filePath))
        {
            string line = rawLine.Trim();
            if (line.Length == 0)
                continue;

            if (line.StartsWith("#BPM", StringComparison.OrdinalIgnoreCase))
            {
                string value = line[4..].Trim();
                if (float.TryParse(value, out float parsedBpm) && parsedBpm > 0f)
                    bpm = parsedBpm;
                continue;
            }

            if (!line.StartsWith('#'))
                continue;

            int colonIndex = line.IndexOf(':');
            if (colonIndex < 0)
                continue;

            string head = line[1..colonIndex];
            if (head.Length != 5)
                continue;

            if (!int.TryParse(head[..3], out int measure))
                continue;

            if (!int.TryParse(head[3..], out int channel))
                continue;

            int lane = channel switch
            {
                11 => 0,
                12 => 1,
                13 => 2,
                14 => 3,
                _ => -1,
            };

            if (lane < 0)
                continue;

            string data = line[(colonIndex + 1)..].Trim();
            if (data.Length < 2 || data.Length % 2 != 0)
                continue;

            int cells = data.Length / 2;
            float secondsPerMeasure = 240f / bpm;

            for (int i = 0; i < cells; i++)
            {
                string token = data.Substring(i * 2, 2);
                if (token == "00")
                    continue;

                float offset = i / (float)cells;
                float time = (measure + offset) * secondsPerMeasure;
                notes.Add(new LaneNote(time, lane));
            }
        }

        return notes.OrderBy(n => n.Time).ThenBy(n => n.Lane).ToList();
    }

    private static IReadOnlyList<LaneNote> CreateFallbackPattern(int difficultyIndex)
    {
        int notesPerMeasure = difficultyIndex switch { 0 => 4, 1 => 8, _ => 14 };

        int totalMeasures = 10;
        float bpm = difficultyIndex switch { 0 => 120f, 1 => 136f, _ => 156f };

        float secondsPerMeasure = 240f / bpm;
        var notes = new List<LaneNote>(totalMeasures * notesPerMeasure);
        for (int measure = 0; measure < totalMeasures; measure++)
        {
            for (int i = 0; i < notesPerMeasure; i++)
            {
                float time = (measure + i / (float)notesPerMeasure) * secondsPerMeasure;
                int lane = (measure + i) % 4;
                notes.Add(new LaneNote(time, lane));
            }
        }

        return notes;
    }

}