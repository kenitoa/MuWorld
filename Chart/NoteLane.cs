using System.Globalization;

namespace RhythmGame;

public readonly record struct LaneNote(
    float Time,
    int Lane,
    NoteType Type = NoteType.Tap,
    float Duration = 0f,
    int EndLane = -1);

public static class NoteLane
{
    private const string ChartFolderName = "NoteLane";
    private const string DefaultChartFile = "default.bms";

    private readonly record struct RawNote(int Measure, float Offset, int Lane, string Token);
    private readonly record struct TempoEvent(int Measure, float Offset, float Bpm);

    public static IReadOnlyList<LaneNote> LoadNotes(string? title, string? artist, int difficultyIndex, int laneCount = 4)
    {
        laneCount = Math.Clamp(laneCount, 4, 7);
        string chartDir = Path.Combine(AppContext.BaseDirectory, ChartFolderName);

        if (!string.IsNullOrWhiteSpace(title))
        {
            string userChartPath = ChartGenerator.GetUserChartPath(title, difficultyIndex);
            if (File.Exists(userChartPath))
            {
                List<LaneNote> notes = ParseSimpleBms(userChartPath, laneCount);
                if (notes.Count > 0)
                    return NormalizeForLaneMode(notes, difficultyIndex, laneCount);
            }

            string generatedPath = Path.Combine(chartDir, ChartGenerator.GetChartFileName(title, difficultyIndex));
            if (File.Exists(generatedPath))
            {
                List<LaneNote> notes = ParseSimpleBms(generatedPath, laneCount);
                if (notes.Count > 0)
                    return ApplyDynamicDifficulty(NormalizeForLaneMode(notes, difficultyIndex, laneCount), title, difficultyIndex);
            }
        }

        string defaultPath = Path.Combine(chartDir, DefaultChartFile);
        if (File.Exists(defaultPath))
        {
            List<LaneNote> notes = ParseSimpleBms(defaultPath, laneCount);
            if (notes.Count > 0)
                return ApplyDynamicDifficulty(NormalizeForLaneMode(notes, difficultyIndex, laneCount), title, difficultyIndex);
        }

        return CreateFallbackPattern(difficultyIndex, laneCount);
    }

    private static List<LaneNote> ParseSimpleBms(string filePath, int laneCount)
    {
        float baseBpm = 128f;
        var bpmTable = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        var rawNotes = new List<RawNote>();
        var tempoEvents = new List<TempoEvent>();

        foreach (string rawLine in File.ReadLines(filePath))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || !line.StartsWith('#'))
                continue;

            if (TryParseBpmDefinition(line, bpmTable, ref baseBpm))
                continue;

            int colonIndex = line.IndexOf(':');
            if (colonIndex < 0)
                continue;

            string head = line[1..colonIndex];
            if (head.Length != 5 ||
                !int.TryParse(head[..3], out int measure) ||
                !int.TryParse(head[3..], out int channel))
            {
                continue;
            }

            string data = line[(colonIndex + 1)..].Trim();
            if (data.Length < 2 || data.Length % 2 != 0)
                continue;

            int cells = data.Length / 2;
            if (channel == 8)
            {
                for (int i = 0; i < cells; i++)
                {
                    string token = data.Substring(i * 2, 2);
                    if (token == "00" || !bpmTable.TryGetValue(token, out float bpm))
                        continue;

                    tempoEvents.Add(new TempoEvent(measure, i / (float)cells, bpm));
                }
                continue;
            }

            int lane = channel is >= 11 and <= 17 ? channel - 11 : -1;
            if (lane < 0 || lane >= laneCount)
                continue;

            for (int i = 0; i < cells; i++)
            {
                string token = data.Substring(i * 2, 2);
                if (token == "00")
                    continue;

                rawNotes.Add(new RawNote(measure, i / (float)cells, lane, token));
            }
        }

        var timing = new BmsTiming(baseBpm, tempoEvents);
        return rawNotes
            .Select(n => CreateLaneNoteFromToken(timing.ToSeconds(n.Measure, n.Offset), n.Lane, n.Token, laneCount))
            .OrderBy(n => n.Time)
            .ThenBy(n => n.Lane)
            .ToList();
    }

    private static bool TryParseBpmDefinition(string line, Dictionary<string, float> bpmTable, ref float baseBpm)
    {
        if (line.StartsWith("#BPM ", StringComparison.OrdinalIgnoreCase))
        {
            string value = line[4..].Trim();
            if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsedBpm) && parsedBpm > 0f)
                baseBpm = parsedBpm;
            return true;
        }

        if (line.Length > 6 && line.StartsWith("#BPM", StringComparison.OrdinalIgnoreCase))
        {
            string id = line.Substring(4, 2);
            string value = line[6..].Trim();
            if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsedBpm) && parsedBpm > 0f)
                bpmTable[id] = parsedBpm;
            return true;
        }

        return false;
    }

    private static LaneNote CreateLaneNoteFromToken(float time, int lane, string token, int laneCount)
    {
        int value = Convert.ToInt32(token, 16);
        NoteType type = value switch
        {
            2 => NoteType.Long,
            3 => NoteType.Slide,
            _ => NoteType.Tap,
        };

        float duration = type == NoteType.Long ? 0.65f : 0f;
        int endLane = type == NoteType.Slide ? Math.Min(laneCount - 1, lane + 1) : lane;
        return new LaneNote(time, lane, type, duration, endLane);
    }

    private static IReadOnlyList<LaneNote> NormalizeForLaneMode(List<LaneNote> source, int difficultyIndex, int laneCount)
    {
        if (source.Count == 0)
            return source;

        bool needsLaneSpread = laneCount > 4 && source.Max(n => n.Lane) < laneCount - 1;
        var result = new List<LaneNote>(source.Count);

        for (int i = 0; i < source.Count; i++)
        {
            LaneNote note = source[i];
            int lane = needsLaneSpread ? PickSpreadLane(i, laneCount) : Math.Clamp(note.Lane, 0, laneCount - 1);
            NoteType type = note.Type;
            float duration = note.Duration;
            int endLane = note.EndLane >= 0 ? Math.Clamp(note.EndLane, 0, laneCount - 1) : lane;

            if (type == NoteType.Tap && difficultyIndex > 0)
            {
                if (i % 18 == 8 && laneCount > 4)
                {
                    type = NoteType.Slide;
                    endLane = Math.Clamp(lane + (i % 36 == 8 ? 1 : -1), 0, laneCount - 1);
                }
                else if (i % 14 == 6)
                {
                    type = NoteType.Long;
                    duration = difficultyIndex == 1 ? 0.48f : 0.62f;
                    endLane = lane;
                }
            }

            result.Add(new LaneNote(note.Time, lane, type, duration, endLane));
        }

        return result.OrderBy(n => n.Time).ThenBy(n => n.Lane).ToList();
    }

    private static IReadOnlyList<LaneNote> ApplyDynamicDifficulty(IReadOnlyList<LaneNote> source, string? title, int difficultyIndex)
    {
        if (source.Count < 16 || string.IsNullOrWhiteSpace(title))
            return source;

        SongScoreRecord? score = new SongDataStore().TryGetScore(AudioFileCatalog.GetSongId(title));
        if (score is null || score.PlayCount < 2)
            return source;

        float density = score.BestAccuracy switch
        {
            < 60f => 0.70f,
            < 75f => 0.82f,
            < 88f => 0.92f,
            _ => 1f,
        };

        if (density >= 0.99f)
            return source;

        var adjusted = new List<LaneNote>(source.Count);
        float accumulator = 0f;
        for (int i = 0; i < source.Count; i++)
        {
            accumulator += density;
            if (i == 0 || accumulator >= 1f)
            {
                adjusted.Add(source[i]);
                accumulator -= 1f;
            }
        }

        int minimumNotes = difficultyIndex switch { 0 => 10, 1 => 18, _ => 26 };
        return adjusted.Count >= minimumNotes ? adjusted : source.Take(minimumNotes).ToList();
    }

    private static int PickSpreadLane(int index, int laneCount)
    {
        if (laneCount == 5)
            return index % 5;

        int[] sevenKeyOrder = [0, 3, 6, 2, 4, 1, 5];
        return sevenKeyOrder[index % sevenKeyOrder.Length];
    }

    private static IReadOnlyList<LaneNote> CreateFallbackPattern(int difficultyIndex, int laneCount)
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
                int lane = (measure + i) % laneCount;
                NoteType type = NoteType.Tap;
                float duration = 0f;
                int endLane = lane;

                if (difficultyIndex > 0 && i % 7 == 3)
                {
                    type = NoteType.Long;
                    duration = difficultyIndex == 1 ? 0.48f : 0.62f;
                }
                else if (difficultyIndex > 0 && laneCount > 4 && i % 11 == 5)
                {
                    type = NoteType.Slide;
                    endLane = Math.Clamp(lane + 1, 0, laneCount - 1);
                }

                notes.Add(new LaneNote(time, lane, type, duration, endLane));
            }
        }

        return notes;
    }

    private sealed class BmsTiming
    {
        private readonly float _baseBpm;
        private readonly List<TempoEvent> _events;

        public BmsTiming(float baseBpm, List<TempoEvent> events)
        {
            _baseBpm = baseBpm;
            _events = events
                .OrderBy(e => e.Measure)
                .ThenBy(e => e.Offset)
                .ToList();
        }

        public float ToSeconds(int measure, float offset)
        {
            float targetPosition = measure + offset;
            float currentPosition = 0f;
            float currentBpm = _baseBpm;
            float seconds = 0f;

            foreach (TempoEvent tempoEvent in _events)
            {
                float eventPosition = tempoEvent.Measure + tempoEvent.Offset;
                if (eventPosition > targetPosition)
                    break;

                if (eventPosition > currentPosition)
                {
                    seconds += (eventPosition - currentPosition) * 240f / currentBpm;
                    currentPosition = eventPosition;
                }

                currentBpm = tempoEvent.Bpm;
            }

            if (targetPosition > currentPosition)
                seconds += (targetPosition - currentPosition) * 240f / currentBpm;

            return seconds;
        }
    }
}
