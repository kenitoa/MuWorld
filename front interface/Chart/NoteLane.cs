using System.Globalization;

namespace RhythmGame;

public readonly record struct LaneNote(
    float Time,
    int Lane,
    NoteType Type = NoteType.Tap,
    float Duration = 0f,
    int EndLane = -1);

public sealed record EditableChart(
    string Path,
    float Bpm,
    IReadOnlyList<LaneNote> Notes,
    IReadOnlyList<ChartDiagnostic> Diagnostics,
    ChartDifficultyInfo Difficulty);

public static class NoteLane
{
    private const string ChartFolderName = "NoteLane";
    private const string DefaultChartFile = "default.bms";
    private const int MaxMeasure = 999;
    private const int MaxResolution = 192;

    private readonly record struct RawNote(int Measure, float Offset, int Lane, string Token);
    private readonly record struct TempoEvent(int Measure, float Offset, float Bpm);
    private sealed record BmsParseResult(List<LaneNote> Notes, float BaseBpm, List<ChartDiagnostic> Diagnostics);

    public static IReadOnlyList<LaneNote> LoadNotes(string? title, string? artist, int difficultyIndex, int laneCount = 4)
    {
        return LoadValidatedChart(title, artist, difficultyIndex, laneCount).Notes;
    }

    public static ChartValidationResult LoadValidatedChart(string? title, string? artist, int difficultyIndex, int laneCount = 4)
    {
        laneCount = Math.Clamp(laneCount, 4, 7);
        string chartDir = Path.Combine(AppContext.BaseDirectory, ChartFolderName);

        if (!string.IsNullOrWhiteSpace(title))
        {
            string userChartPath = ChartGenerator.GetUserChartPath(title, difficultyIndex, laneCount);
            if (File.Exists(userChartPath))
            {
                BmsParseResult result = ParseSimpleBms(userChartPath, laneCount);
                if (result.Notes.Count > 0)
                    return ChartValidator.ValidateAndFilter(NormalizeForLaneMode(result.Notes, difficultyIndex, laneCount), laneCount, result.Diagnostics);
            }

            string legacyUserChartPath = ChartGenerator.GetUserChartPath(title, difficultyIndex);
            if (File.Exists(legacyUserChartPath))
            {
                BmsParseResult result = ParseSimpleBms(legacyUserChartPath, laneCount);
                if (result.Notes.Count > 0)
                    return ChartValidator.ValidateAndFilter(NormalizeForLaneMode(result.Notes, difficultyIndex, laneCount), laneCount, result.Diagnostics);
            }

            string laneSpecificGeneratedPath = ChartGenerator.GetGeneratedChartPath(title, difficultyIndex, laneCount);
            if (File.Exists(laneSpecificGeneratedPath))
            {
                BmsParseResult result = ParseSimpleBms(laneSpecificGeneratedPath, laneCount);
                if (result.Notes.Count > 0)
                    return ChartValidator.ValidateAndFilter(NormalizeForLaneMode(result.Notes, difficultyIndex, laneCount), laneCount, result.Diagnostics);
            }

            string generatedPath = Path.Combine(chartDir, ChartGenerator.GetChartFileName(title, difficultyIndex));
            if (File.Exists(generatedPath))
            {
                BmsParseResult result = ParseSimpleBms(generatedPath, laneCount);
                if (result.Notes.Count > 0)
                    return ChartValidator.ValidateAndFilter(ApplyDynamicDifficulty(NormalizeForLaneMode(result.Notes, difficultyIndex, laneCount), title, difficultyIndex, laneCount), laneCount, result.Diagnostics);
            }
        }

        string defaultPath = Path.Combine(chartDir, DefaultChartFile);
        if (File.Exists(defaultPath))
        {
            BmsParseResult result = ParseSimpleBms(defaultPath, laneCount);
            if (result.Notes.Count > 0)
                return ChartValidator.ValidateAndFilter(ApplyDynamicDifficulty(NormalizeForLaneMode(result.Notes, difficultyIndex, laneCount), title, difficultyIndex, laneCount), laneCount, result.Diagnostics);
        }

        return ChartValidator.ValidateAndFilter(CreateFallbackPattern(difficultyIndex, laneCount), laneCount);
    }

    public static EditableChart LoadEditableChart(string title, int difficultyIndex, int laneCount)
    {
        string path = ChartGenerator.EnsureUserEditableChart(title, difficultyIndex, laneCount);
        BmsParseResult result = ParseSimpleBms(path, laneCount);
        ChartValidationResult validated = ChartValidator.ValidateAndFilter(result.Notes, laneCount, result.Diagnostics);
        return new EditableChart(path, result.BaseBpm, validated.Notes, validated.Diagnostics, validated.Difficulty);
    }

    private static BmsParseResult ParseSimpleBms(string filePath, int laneCount)
    {
        float baseBpm = 128f;
        var bpmTable = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        var rawNotes = new List<RawNote>();
        var tempoEvents = new List<TempoEvent>();
        var diagnostics = new List<ChartDiagnostic>();
        int lineNumber = 0;

        foreach (string rawLine in File.ReadLines(filePath))
        {
            lineNumber++;
            string line = rawLine.Trim();
            if (line.Length == 0 || !line.StartsWith('#'))
                continue;

            if (TryParseBpmDefinition(line, bpmTable, ref baseBpm, diagnostics, lineNumber))
                continue;

            int colonIndex = line.IndexOf(':');
            if (colonIndex < 0)
                continue;

            string head = line[1..colonIndex];
            if (head.Length != 5 ||
                !int.TryParse(head[..3], out int measure) ||
                !int.TryParse(head[3..], out int channel))
            {
                diagnostics.Add(new ChartDiagnostic(ChartDiagnosticSeverity.Warning, $"Invalid BMS channel header '{head}'.", lineNumber));
                continue;
            }

            if (measure < 0 || measure > MaxMeasure)
            {
                diagnostics.Add(new ChartDiagnostic(ChartDiagnosticSeverity.Warning, $"Skipped measure {measure}; supported range is 000-{MaxMeasure:D3}.", lineNumber));
                continue;
            }

            string data = line[(colonIndex + 1)..].Trim();
            if (data.Length < 2 || data.Length % 2 != 0)
            {
                diagnostics.Add(new ChartDiagnostic(ChartDiagnosticSeverity.Warning, $"Channel data must be even-length token pairs.", lineNumber));
                continue;
            }

            int cells = data.Length / 2;
            if (cells > MaxResolution)
            {
                diagnostics.Add(new ChartDiagnostic(ChartDiagnosticSeverity.Warning, $"Skipped measure {measure:D3} channel {channel:D2}; resolution {cells} exceeds {MaxResolution}.", lineNumber));
                continue;
            }

            if (channel == 8)
            {
                for (int i = 0; i < cells; i++)
                {
                    string token = data.Substring(i * 2, 2);
                    if (token == "00")
                        continue;

                    if (!IsHexToken(token))
                    {
                        diagnostics.Add(new ChartDiagnostic(ChartDiagnosticSeverity.Warning, $"Invalid tempo token '{token}'.", lineNumber));
                        continue;
                    }

                    if (!bpmTable.TryGetValue(token, out float bpm))
                    {
                        diagnostics.Add(new ChartDiagnostic(ChartDiagnosticSeverity.Warning, $"Undefined tempo token '{token}'.", lineNumber));
                        continue;
                    }

                    tempoEvents.Add(new TempoEvent(measure, i / (float)cells, bpm));
                }
                continue;
            }

            int lane = channel is >= 11 and <= 17 ? channel - 11 : -1;
            if (lane < 0 || lane >= laneCount)
            {
                if (channel is >= 11 and <= 17)
                    diagnostics.Add(new ChartDiagnostic(ChartDiagnosticSeverity.Warning, $"Channel {channel:D2} is outside current {laneCount}K lane range.", lineNumber));
                continue;
            }

            for (int i = 0; i < cells; i++)
            {
                string token = data.Substring(i * 2, 2);
                if (token == "00")
                    continue;

                if (!IsHexToken(token))
                {
                    diagnostics.Add(new ChartDiagnostic(ChartDiagnosticSeverity.Warning, $"Invalid note token '{token}'.", lineNumber));
                    continue;
                }

                rawNotes.Add(new RawNote(measure, i / (float)cells, lane, token));
            }
        }

        var timing = new BmsTiming(baseBpm, tempoEvents);
        List<LaneNote> notes = rawNotes
            .Select(n => CreateLaneNoteFromToken(timing.ToSeconds(n.Measure, n.Offset), n.Lane, n.Token, laneCount))
            .OrderBy(n => n.Time)
            .ThenBy(n => n.Lane)
            .ToList();
        return new BmsParseResult(notes, baseBpm, diagnostics);
    }

    private static bool TryParseBpmDefinition(string line, Dictionary<string, float> bpmTable, ref float baseBpm, List<ChartDiagnostic> diagnostics, int lineNumber)
    {
        if (line.StartsWith("#BPM ", StringComparison.OrdinalIgnoreCase))
        {
            string value = line[4..].Trim();
            if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsedBpm) && parsedBpm > 0f)
                baseBpm = parsedBpm;
            else
                diagnostics.Add(new ChartDiagnostic(ChartDiagnosticSeverity.Warning, $"Invalid base BPM '{value}'. BPM must be positive.", lineNumber));
            return true;
        }

        if (line.Length > 6 && line.StartsWith("#BPM", StringComparison.OrdinalIgnoreCase))
        {
            string id = line.Substring(4, 2);
            string value = line[6..].Trim();
            if (!IsHexToken(id))
            {
                diagnostics.Add(new ChartDiagnostic(ChartDiagnosticSeverity.Warning, $"Invalid BPM token id '{id}'.", lineNumber));
                return true;
            }

            if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsedBpm) && parsedBpm > 0f)
                bpmTable[id] = parsedBpm;
            else
                diagnostics.Add(new ChartDiagnostic(ChartDiagnosticSeverity.Warning, $"Invalid BPM value '{value}'. BPM must be positive.", lineNumber));
            return true;
        }

        return false;
    }

    private static bool IsHexToken(string token)
    {
        return token.Length == 2 && token.All(Uri.IsHexDigit);
    }

    private static LaneNote CreateLaneNoteFromToken(float time, int lane, string token, int laneCount)
    {
        int value = Convert.ToInt32(token, 16);
        NoteType type = value switch
        {
            2 => NoteType.Long,
            3 => NoteType.Slide,
            _ when (value >> 4) == 3 => NoteType.Slide,
            _ => NoteType.Tap,
        };

        float duration = type switch
        {
            NoteType.Long => 0.65f,
            NoteType.Slide => 0.48f,
            _ => 0f,
        };
        int endLane = type == NoteType.Slide ? DecodeSlideEndLane(token, lane, laneCount) : lane;
        return new LaneNote(time, lane, type, duration, endLane);
    }

    private static int DecodeSlideEndLane(string token, int lane, int laneCount)
    {
        if (token.Length == 2 && token[0] == '3' && int.TryParse(token[1].ToString(), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int encodedLane))
            return Math.Clamp(encodedLane - 1, 0, laneCount - 1);

        return Math.Min(laneCount - 1, lane + 1);
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
                    duration = difficultyIndex == 1 ? 0.42f : 0.56f;
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

    private static IReadOnlyList<LaneNote> ApplyDynamicDifficulty(IReadOnlyList<LaneNote> source, string? title, int difficultyIndex, int laneCount)
    {
        if (source.Count < 16 || string.IsNullOrWhiteSpace(title))
            return source;

        SongScoreRecord? score = new SongDataStore().TryFindScoreBySongKey(title);
        if (score is null || score.PlayCount < 2)
            return source;

        string modeKey = SongDataStore.GetDifficultyModeKey(difficultyIndex, laneCount);
        float modeAccuracy = score.DifficultyBestAccuracy.GetValueOrDefault(modeKey, score.BestAccuracy);
        float density = score.AdaptiveDensityByMode.GetValueOrDefault(modeKey, modeAccuracy switch
        {
            < 60f => 0.65f,
            < 75f => 0.80f,
            < 88f => 0.92f,
            _ => 1f,
        });

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

        if (laneCount == 6)
        {
            int[] sixKeyOrder = [0, 3, 5, 2, 4, 1];
            return sixKeyOrder[index % sixKeyOrder.Length];
        }

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
                    duration = difficultyIndex == 1 ? 0.42f : 0.56f;
                    endLane = Math.Clamp(lane + 1, 0, laneCount - 1);
                }

                notes.Add(new LaneNote(time, lane, type, duration, endLane));
            }
        }

        return notes;
    }

    private static IReadOnlyList<LaneNote> ValidateAndResolveOverlaps(IReadOnlyList<LaneNote> source, int laneCount)
    {
        const float minTapGap = 0.085f;
        const float minLongGap = 0.045f;
        var accepted = new List<LaneNote>(source.Count);
        float[] laneBlockedUntil = Enumerable.Repeat(float.NegativeInfinity, laneCount).ToArray();

        foreach (LaneNote note in source.OrderBy(n => n.Time).ThenBy(n => n.Lane))
        {
            int lane = Math.Clamp(note.Lane, 0, laneCount - 1);
            int endLane = note.EndLane >= 0 ? Math.Clamp(note.EndLane, 0, laneCount - 1) : lane;
            float duration = Math.Max(0f, note.Duration);
            float clearTime = note.Type == NoteType.Tap ? note.Time + minTapGap : note.Time + duration + minLongGap;

            if (note.Time < laneBlockedUntil[lane])
                continue;

            if (note.Type == NoteType.Slide && endLane != lane && note.Time < laneBlockedUntil[endLane])
                continue;

            accepted.Add(new LaneNote(note.Time, lane, note.Type, duration, endLane));
            laneBlockedUntil[lane] = clearTime;
            if (note.Type == NoteType.Slide && endLane != lane)
                laneBlockedUntil[endLane] = Math.Max(laneBlockedUntil[endLane], note.Time + duration + minLongGap);
        }

        return accepted;
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
