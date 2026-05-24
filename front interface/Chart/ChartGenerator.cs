namespace RhythmGame;

internal static class ChartGenerator
{
    private const int LaneCount = 4;
    private const string ChartFolderName = "NoteLane";
    private static readonly object StatusGate = new();
    private static int _isGenerating;
    private static ChartGenerationSnapshot _status = ChartGenerationSnapshot.Idle;
    internal static string? UserChartDirectoryOverride { get; set; }

    private readonly record struct TempoSegment(float Time, float Bpm, float Confidence = 1f);
    private readonly record struct ChartPoint(int Measure, float Offset, float Time, float Energy, PatternKind Pattern);
    private readonly record struct DensitySection(float Start, float End, float Multiplier, PatternKind Pattern);
    public readonly record struct ChartGenerationSnapshot(
        bool IsRunning,
        int TotalSongs,
        int ProcessedSongs,
        int GeneratedCharts,
        int SkippedSongs,
        string CurrentSong,
        string LastMessage)
    {
        public static ChartGenerationSnapshot Idle => new(false, 0, 0, 0, 0, string.Empty, "Chart generation idle.");
    }

    private enum PatternKind
    {
        Stream,
        Stair,
        Trill,
        Jack,
        Chord,
        Roll,
        LongHold,
        Slide,
        Rest,
    }

    public static void GenerateAllCharts()
    {
        GenerateAllChartsCore();
    }

    public static void BeginGenerateAllChartsAsync()
    {
        if (Interlocked.Exchange(ref _isGenerating, 1) == 1)
            return;

        UpdateStatus(new ChartGenerationSnapshot(true, 0, 0, 0, 0, string.Empty, "Chart generation queued."));
        Task.Run(() =>
        {
            try
            {
                GenerateAllChartsCore();
            }
            catch (Exception ex)
            {
                AppLogger.Error("Chart generation worker failed.", ex);
                UpdateStatus(new ChartGenerationSnapshot(false, 0, 0, 0, 0, string.Empty, "Chart generation failed."));
            }
            finally
            {
                Interlocked.Exchange(ref _isGenerating, 0);
            }
        });
    }

    public static ChartGenerationSnapshot GetStatus()
    {
        lock (StatusGate)
            return _status;
    }

    private static void GenerateAllChartsCore()
    {
        string bgmDir = Path.Combine(AppContext.BaseDirectory, "Songs", "InGameBGM", "Original");
        if (!Directory.Exists(bgmDir))
        {
            UpdateStatus(new ChartGenerationSnapshot(false, 0, 0, 0, 0, string.Empty, "Song folder missing."));
            return;
        }

        string chartDir = Path.Combine(AppContext.BaseDirectory, ChartFolderName);
        Directory.CreateDirectory(chartDir);

        string[] audioFiles = AudioFileCatalog.DiscoverSongFiles(bgmDir);
        int processed = 0;
        int generated = 0;
        int skipped = 0;
        UpdateStatus(new ChartGenerationSnapshot(true, audioFiles.Length, 0, 0, 0, string.Empty, "Chart generation started."));

        foreach (string audioPath in audioFiles)
        {
            string songName = Path.GetFileNameWithoutExtension(audioPath);
            try
            {
                UpdateStatus(new ChartGenerationSnapshot(true, audioFiles.Length, processed, generated, skipped, songName, "Analyzing song."));
                AudioAnalysisResult analysis = AudioAnalysisPipeline.Analyze(audioPath);
                if (!analysis.IsSupported || analysis.Beats.Count == 0)
                {
                    skipped++;
                    processed++;
                    UpdateStatus(new ChartGenerationSnapshot(true, audioFiles.Length, processed, generated, skipped, songName, analysis.Message));
                    continue;
                }

                for (int difficulty = 0; difficulty < 3; difficulty++)
                {
                    string chartFile = Path.Combine(chartDir, GetChartFileName(songName, difficulty));
                    if (File.Exists(chartFile))
                        continue;

                    string bmsContent = GenerateBms(songName, analysis.Beats, difficulty, analysis.DurationSeconds);
                    File.WriteAllText(chartFile, bmsContent);
                    generated++;
                }

                processed++;
                UpdateStatus(new ChartGenerationSnapshot(true, audioFiles.Length, processed, generated, skipped, songName, "Chart generation running."));
            }
            catch (Exception ex)
            {
                skipped++;
                processed++;
                AppLogger.Error($"Chart generation failed for {songName}.", ex);
                UpdateStatus(new ChartGenerationSnapshot(true, audioFiles.Length, processed, generated, skipped, songName, "Song skipped after error."));
            }
        }

        UpdateStatus(new ChartGenerationSnapshot(false, audioFiles.Length, processed, generated, skipped, string.Empty, $"Chart generation complete. New charts: {generated}, skipped: {skipped}."));
    }

    private static void UpdateStatus(ChartGenerationSnapshot status)
    {
        lock (StatusGate)
            _status = status;
    }

    public static string GetChartFileName(string songName, int difficultyIndex)
    {
        string prefix = difficultyIndex switch { 0 => "easy", 1 => "normal", _ => "hard" };
        return $"{prefix}_{NormalizeSongFileName(songName)}.bms";
    }

    public static string GetChartFileName(string songName, int difficultyIndex, int laneCount)
    {
        string prefix = difficultyIndex switch { 0 => "easy", 1 => "normal", _ => "hard" };
        return $"{prefix}_{NormalizeSongFileName(songName)}_{Math.Clamp(laneCount, 4, 7)}k.bms";
    }

    public static string GetUserChartPath(string songName, int difficultyIndex)
    {
        string chartDir = UserChartDirectoryOverride ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RhythmGame",
            "Charts");

        return Path.Combine(chartDir, GetChartFileName(songName, difficultyIndex));
    }

    public static string GetUserChartPath(string songName, int difficultyIndex, int laneCount)
    {
        string chartDir = UserChartDirectoryOverride ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RhythmGame",
            "Charts");

        return Path.Combine(chartDir, GetChartFileName(songName, difficultyIndex, laneCount));
    }

    public static string EnsureUserEditableChart(string songName, int difficultyIndex)
    {
        return EnsureUserEditableChart(songName, difficultyIndex, 4);
    }

    public static string EnsureUserEditableChart(string songName, int difficultyIndex, int laneCount)
    {
        laneCount = Math.Clamp(laneCount, 4, 7);
        string userChartPath = GetUserChartPath(songName, difficultyIndex, laneCount);
        Directory.CreateDirectory(Path.GetDirectoryName(userChartPath)!);

        if (File.Exists(userChartPath))
            return userChartPath;

        string laneGeneratedPath = Path.Combine(AppContext.BaseDirectory, ChartFolderName, GetChartFileName(songName, difficultyIndex, laneCount));
        string generatedPath = Path.Combine(AppContext.BaseDirectory, ChartFolderName, GetChartFileName(songName, difficultyIndex));
        string legacyUserPath = GetUserChartPath(songName, difficultyIndex);
        string defaultPath = Path.Combine(AppContext.BaseDirectory, ChartFolderName, "default.bms");
        string? sourcePath = File.Exists(laneGeneratedPath)
            ? laneGeneratedPath
            : File.Exists(legacyUserPath)
                ? legacyUserPath
                : File.Exists(generatedPath)
                    ? generatedPath
                    : File.Exists(defaultPath)
                        ? defaultPath
                        : null;

        if (sourcePath is null)
            File.WriteAllText(userChartPath, BuildBmsString(songName, 120f, [], [new TempoSegment(0f, 120f)]));
        else
            File.Copy(sourcePath, userChartPath, overwrite: false);

        return userChartPath;
    }

    public static void SaveUserChart(string songName, int difficultyIndex, int laneCount, float bpm, IReadOnlyList<LaneNote> notes)
    {
        string path = GetUserChartPath(songName, difficultyIndex, laneCount);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        ChartValidationResult validated = ChartValidator.ValidateAndFilter(notes, laneCount);
        string content = BuildBmsStringFromLaneNotes(songName, bpm, validated.Notes, [new TempoSegment(0f, Math.Clamp(bpm, 40f, 300f))]);
        File.WriteAllText(path, content);
    }

    private static string GenerateBms(string songName, List<WavAnalyzer.BeatInfo> beats, int difficulty, float analyzedDurationSeconds)
    {
        List<TempoSegment> tempoMap = DetectTempoMap(beats);
        float bpm = tempoMap[0].Bpm;
        float duration = Math.Max(analyzedDurationSeconds, beats.Count > 0 ? beats[^1].Time + 2f : 60f);
        float downbeatOffset = EstimateDownbeatOffset(beats, bpm);
        List<DensitySection> densityCurve = BuildDensityCurve(beats, duration, difficulty);
        int targetNotes = Math.Max(10, (int)MathF.Round(GetNotesPerSecond(difficulty) * duration));

        List<ChartPoint> points = SelectChartPoints(beats, targetNotes, difficulty, bpm, downbeatOffset, densityCurve);
        IReadOnlyList<LaneNote> notes = AssignGeneratedNotes(points, difficulty, LaneCount, bpm, densityCurve);
        ChartValidationResult validated = ChartValidator.ValidateAndFilter(notes, LaneCount);
        return BuildBmsStringFromLaneNotes(songName, bpm, validated.Notes, tempoMap);
    }

    private static float GetNotesPerSecond(int difficulty)
    {
        return difficulty switch
        {
            0 => 1.0f,
            1 => 5.0f,
            _ => 7.0f,
        };
    }

    private static int GetSubdivision(int difficulty)
    {
        return difficulty switch
        {
            0 => 8,
            1 => 16,
            _ => 24,
        };
    }

    private static List<ChartPoint> SelectChartPoints(
        List<WavAnalyzer.BeatInfo> beats,
        int targetNotes,
        int difficulty,
        float bpm,
        float downbeatOffset,
        List<DensitySection> densityCurve)
    {
        float secondsPerMeasure = 240f / bpm;
        int subdivision = GetSubdivision(difficulty);

        var selected = beats
            .OrderByDescending(b => (b.Confidence * 2f + b.Energy + b.Flux + b.LowEnergy * 0.6f + b.HighEnergy * 0.4f) * GetDensityMultiplierAt(densityCurve, b.Time))
            .Take(Math.Min(targetNotes, beats.Count))
            .Select(b => ToChartPoint(b.Time, b.Energy + b.Flux, secondsPerMeasure, subdivision, downbeatOffset, GetPatternAt(densityCurve, b.Time)))
            .ToList();

        if (selected.Count < targetNotes)
            FillEvenGrid(selected, targetNotes, beats[^1].Time + 2f, secondsPerMeasure, subdivision, downbeatOffset, densityCurve);

        return selected
            .GroupBy(p => (p.Measure, Cell: (int)MathF.Round(p.Offset * subdivision)))
            .Select(g => g.OrderByDescending(p => p.Energy).First())
            .OrderBy(p => p.Time)
            .Take(targetNotes)
            .ToList();
    }

    private static ChartPoint ToChartPoint(float time, float energy, float secondsPerMeasure, int subdivision, float downbeatOffset, PatternKind pattern)
    {
        float position = Math.Max(0f, (time - downbeatOffset) / secondsPerMeasure);
        int measure = (int)MathF.Floor(position);
        float offset = position - measure;
        offset = Math.Clamp(MathF.Round(offset * subdivision) / subdivision, 0f, 0.999f);
        return new ChartPoint(measure, offset, time, energy, pattern);
    }

    private static void FillEvenGrid(
        List<ChartPoint> points,
        int targetNotes,
        float duration,
        float secondsPerMeasure,
        int subdivision,
        float downbeatOffset,
        List<DensitySection> densityCurve)
    {
        float step = duration / Math.Max(1, targetNotes);
        for (float time = 0f; points.Count < targetNotes && time <= duration; time += step)
            points.Add(ToChartPoint(time, 0f, secondsPerMeasure, subdivision, downbeatOffset, GetPatternAt(densityCurve, time)));
    }

    private static IReadOnlyList<LaneNote> AssignGeneratedNotes(
        List<ChartPoint> points,
        int difficulty,
        int laneCount,
        float bpm,
        List<DensitySection> densityCurve)
    {
        float secondsPerMeasure = 240f / bpm;
        var notes = new List<LaneNote>(points.Count + points.Count / 5);
        int previousLane = -1;
        int sameLaneRun = 0;
        int sameHandRun = 0;
        int previousHand = -1;
        float previousTime = -10f;
        float centerLaneCooldownUntil = -10f;

        for (int i = 0; i < points.Count; i++)
        {
            ChartPoint point = points[i];
            float noteTime = (point.Measure + point.Offset) * secondsPerMeasure;
            int lane = PickPatternLane(point, i, difficulty, laneCount, previousLane, previousHand, sameHandRun, noteTime, previousTime, centerLaneCooldownUntil);

            if (lane == previousLane)
            {
                sameLaneRun++;
                bool allowJack = point.Pattern == PatternKind.Jack && difficulty > 0;
                if (!allowJack && (sameLaneRun > 1 || noteTime - previousTime < 0.22f))
                    lane = (lane + 1 + difficulty) % laneCount;
            }
            else
            {
                sameLaneRun = 1;
            }

            int hand = GetHand(lane, laneCount);
            if (hand == previousHand && hand >= 0)
                sameHandRun++;
            else
                sameHandRun = 1;

            NoteType type = PickPatternNoteType(point.Pattern, difficulty, laneCount, i);
            float duration = type switch
            {
                NoteType.Long => difficulty == 0 ? 0.45f : 0.62f + difficulty * 0.12f,
                NoteType.Slide => difficulty == 0 ? 0.38f : 0.48f + difficulty * 0.08f,
                _ => 0f,
            };
            int endLane = type == NoteType.Slide
                ? PickSlideEndLane(lane, point.Pattern, laneCount, i)
                : lane;

            notes.Add(new LaneNote(noteTime, lane, type, duration, endLane));
            if (point.Pattern == PatternKind.Chord && difficulty > 0 && notes.Count < points.Count * 2)
            {
                int chordLane = PickChordLane(lane, laneCount, hand);
                notes.Add(new LaneNote(noteTime, chordLane));
            }

            if (laneCount is 5 or 7 && lane == laneCount / 2)
                centerLaneCooldownUntil = noteTime + 0.65f;

            previousLane = lane;
            previousHand = GetHand(lane, laneCount);
            previousTime = noteTime;
        }

        return notes
            .OrderBy(n => n.Time)
            .ThenBy(n => n.Lane)
            .ToList();
    }

    private static int PickPatternLane(
        ChartPoint point,
        int index,
        int difficulty,
        int laneCount,
        int previousLane,
        int previousHand,
        int sameHandRun,
        float noteTime,
        float previousTime,
        float centerLaneCooldownUntil)
    {
        int lane = point.Pattern switch
        {
            PatternKind.Stair => (point.Measure + index) % laneCount,
            PatternKind.Trill => index % 2 == 0 ? Math.Max(0, previousLane) : (Math.Max(0, previousLane) + 2) % laneCount,
            PatternKind.Jack => previousLane >= 0 ? previousLane : index % laneCount,
            PatternKind.Roll => (index * 2 + point.Measure) % laneCount,
            PatternKind.Slide => (point.Measure + index + difficulty) % laneCount,
            PatternKind.LongHold => (point.Measure * 2 + index) % laneCount,
            PatternKind.Chord => (index + point.Measure + difficulty) % laneCount,
            _ => difficulty switch
            {
                0 => (point.Measure + index) % laneCount,
                1 => (point.Measure * 2 + index) % laneCount,
                _ => (index * 2 + point.Measure + (int)(point.Offset * 8)) % laneCount,
            },
        };

        if (laneCount is 5 or 7 && lane == laneCount / 2 && noteTime < centerLaneCooldownUntil)
            lane = (lane + 1 + difficulty) % laneCount;

        int hand = GetHand(lane, laneCount);
        if (sameHandRun >= 3 && hand == previousHand && point.Pattern != PatternKind.Trill)
        {
            int mirrored = laneCount - 1 - lane;
            if (GetHand(mirrored, laneCount) != previousHand)
                lane = mirrored;
        }

        if (previousLane == lane && noteTime - previousTime < 0.18f && point.Pattern != PatternKind.Jack)
            lane = (lane + 1) % laneCount;

        return Math.Clamp(lane, 0, laneCount - 1);
    }

    private static NoteType PickPatternNoteType(PatternKind pattern, int difficulty, int laneCount, int index)
    {
        if (pattern == PatternKind.LongHold)
            return NoteType.Long;
        if (pattern == PatternKind.Slide && laneCount > 4)
            return NoteType.Slide;
        if (difficulty > 0 && pattern == PatternKind.Roll && index % 12 == 5)
            return NoteType.Long;
        if (difficulty > 1 && laneCount > 4 && pattern == PatternKind.Stair && index % 16 == 9)
            return NoteType.Slide;
        return NoteType.Tap;
    }

    private static int PickSlideEndLane(int lane, PatternKind pattern, int laneCount, int index)
    {
        int direction = (pattern == PatternKind.Stair || index % 2 == 0) ? 1 : -1;
        int endLane = lane + direction * (index % 3 == 0 ? 2 : 1);
        if (endLane < 0 || endLane >= laneCount)
            endLane = lane - direction;
        return Math.Clamp(endLane, 0, laneCount - 1);
    }

    private static int PickChordLane(int lane, int laneCount, int hand)
    {
        int candidate = laneCount - 1 - lane;
        if (candidate != lane)
            return candidate;

        return hand <= 0 ? Math.Min(laneCount - 1, lane + 1) : Math.Max(0, lane - 1);
    }

    private static int GetHand(int lane, int laneCount)
    {
        int center = laneCount / 2;
        if (laneCount is 5 or 7 && lane == center)
            return -1;

        return lane < center ? 0 : 1;
    }

    private static float EstimateDownbeatOffset(List<WavAnalyzer.BeatInfo> beats, float bpm)
    {
        if (beats.Count < 4)
            return 0f;

        float beatSeconds = 60f / Math.Clamp(bpm, 40f, 240f);
        float bestOffset = 0f;
        float bestScore = float.NegativeInfinity;

        for (int candidate = 0; candidate < 16; candidate++)
        {
            float offset = candidate * beatSeconds / 16f;
            float score = 0f;
            int count = 0;
            foreach (WavAnalyzer.BeatInfo beat in beats)
            {
                if (beat.Time < offset)
                    continue;

                float position = (beat.Time - offset) / beatSeconds;
                float distanceToMeasure = MathF.Abs(position % 4f);
                distanceToMeasure = MathF.Min(distanceToMeasure, 4f - distanceToMeasure);
                if (distanceToMeasure <= 0.22f)
                {
                    score += beat.LowEnergy * 1.35f + beat.Confidence * 0.75f + beat.Energy * 0.4f;
                    count++;
                }
            }

            if (count > 0)
                score /= count;

            if (score > bestScore)
            {
                bestScore = score;
                bestOffset = offset;
            }
        }

        return Math.Clamp(bestOffset, 0f, beatSeconds);
    }

    private static List<DensitySection> BuildDensityCurve(List<WavAnalyzer.BeatInfo> beats, float duration, int difficulty)
    {
        float sectionLength = difficulty == 0 ? 12f : 8f;
        var sections = new List<DensitySection>();
        if (duration <= 0f)
            return [new DensitySection(0f, 60f, 1f, PatternKind.Stream)];

        float globalAverage = beats.Count == 0 ? 0f : beats.Average(b => b.Energy + b.Flux + b.HighEnergy);
        for (float start = 0f; start < duration; start += sectionLength)
        {
            float end = Math.Min(duration, start + sectionLength);
            var local = beats.Where(b => b.Time >= start && b.Time < end).ToList();
            float localAverage = local.Count == 0 ? 0f : local.Average(b => b.Energy + b.Flux + b.HighEnergy);
            float intensity = globalAverage <= 0f ? 1f : localAverage / globalAverage;
            PatternKind pattern = PickSectionPattern(local, intensity, difficulty);
            float multiplier = pattern == PatternKind.Rest
                ? 0.35f
                : intensity switch
                {
                    < 0.72f => 0.72f,
                    > 1.45f => 1.28f,
                    > 1.12f => 1.12f,
                    _ => 0.95f,
                };

            if (start < 2f || duration - end < 2f)
            {
                multiplier = Math.Min(multiplier, 0.55f);
                pattern = PatternKind.Rest;
            }

            sections.Add(new DensitySection(start, end, multiplier, pattern));
        }

        return sections;
    }

    private static PatternKind PickSectionPattern(List<WavAnalyzer.BeatInfo> beats, float intensity, int difficulty)
    {
        if (beats.Count < 2 || intensity < 0.45f)
            return PatternKind.Rest;

        float lowShare = beats.Sum(b => b.LowEnergy) / Math.Max(0.0001f, beats.Sum(b => b.LowEnergy + b.MidEnergy + b.HighEnergy));
        float highShare = beats.Sum(b => b.HighEnergy) / Math.Max(0.0001f, beats.Sum(b => b.LowEnergy + b.MidEnergy + b.HighEnergy));
        float averageGap = beats.Zip(beats.Skip(1), (a, b) => b.Time - a.Time).Where(g => g > 0.05f && g < 1.5f).DefaultIfEmpty(0.5f).Average();

        if (difficulty > 0 && intensity > 1.45f && highShare > 0.34f)
            return PatternKind.Chord;
        if (difficulty > 1 && averageGap < 0.18f)
            return PatternKind.Roll;
        if (difficulty > 1 && averageGap is >= 0.22f and <= 0.36f && intensity > 1.05f)
            return PatternKind.Jack;
        if (difficulty > 0 && lowShare > 0.48f && intensity > 1.05f)
            return PatternKind.LongHold;
        if (difficulty > 0 && highShare > 0.36f)
            return PatternKind.Stair;
        if (difficulty > 1 && intensity > 1.1f)
            return PatternKind.Trill;

        return PatternKind.Stream;
    }

    private static PatternKind GetPatternAt(List<DensitySection> densityCurve, float time)
    {
        return densityCurve.FirstOrDefault(s => time >= s.Start && time < s.End).Pattern;
    }

    private static float GetDensityMultiplierAt(List<DensitySection> densityCurve, float time)
    {
        foreach (DensitySection section in densityCurve)
        {
            if (time >= section.Start && time < section.End)
                return section.Multiplier;
        }

        return 1f;
    }

    private static List<TempoSegment> DetectTempoMap(List<WavAnalyzer.BeatInfo> beats)
    {
        float baseBpm = EstimateBpm(beats);
        var segments = new List<TempoSegment> { new(0f, baseBpm) };

        if (beats.Count < 16)
            return segments;

        const int window = 12;
        const float minSegmentSeconds = 8f;
        const float minConfidence = 0.42f;
        float previousBpm = baseBpm;
        float previousSegmentTime = 0f;
        for (int i = window; i < beats.Count - window; i += window)
        {
            var intervals = new List<float>(window);
            for (int j = i - window + 1; j <= i; j++)
            {
                float gap = beats[j].Time - beats[j - 1].Time;
                if (gap > 0.15f && gap < 1.5f)
                    intervals.Add(gap);
            }

            if (intervals.Count < 4)
                continue;

            intervals.Sort();
            float median = intervals[intervals.Count / 2];
            float averageDeviation = intervals.Average(g => MathF.Abs(g - median));
            float confidence = Math.Clamp(1f - averageDeviation / Math.Max(0.001f, median * 0.38f), 0f, 1f);
            float bpm = NormalizeBpm(60f / median);
            float segmentTime = beats[i].Time;

            if (confidence < minConfidence ||
                segmentTime - previousSegmentTime < minSegmentSeconds ||
                MathF.Abs(bpm - previousBpm) < 5f)
                continue;

            segments.Add(new TempoSegment(segmentTime, bpm, confidence));
            previousBpm = bpm;
            previousSegmentTime = segmentTime;
        }

        return MergeTempoSegments(segments);
    }

    private static List<TempoSegment> MergeTempoSegments(List<TempoSegment> segments)
    {
        if (segments.Count <= 1)
            return segments;

        var merged = new List<TempoSegment> { segments[0] };
        foreach (TempoSegment segment in segments.Skip(1))
        {
            TempoSegment previous = merged[^1];
            if (MathF.Abs(previous.Bpm - segment.Bpm) < 4f)
            {
                float bpm = (previous.Bpm * previous.Confidence + segment.Bpm * segment.Confidence) /
                    Math.Max(0.001f, previous.Confidence + segment.Confidence);
                merged[^1] = previous with { Bpm = MathF.Round(bpm), Confidence = Math.Max(previous.Confidence, segment.Confidence) };
            }
            else
            {
                merged.Add(segment);
            }
        }

        return merged;
    }

    private static float EstimateBpm(List<WavAnalyzer.BeatInfo> beats)
    {
        if (beats.Count < 2)
            return 120f;

        var intervals = new List<float>();
        for (int i = 1; i < beats.Count; i++)
        {
            float gap = beats[i].Time - beats[i - 1].Time;
            if (gap > 0.15f && gap < 1.5f)
                intervals.Add(gap);
        }

        if (intervals.Count == 0)
            return 120f;

        intervals.Sort();
        return NormalizeBpm(60f / intervals[intervals.Count / 2]);
    }

    private static float NormalizeBpm(float bpm)
    {
        while (bpm < 80f) bpm *= 2f;
        while (bpm > 200f) bpm /= 2f;
        return MathF.Round(bpm);
    }

    private static string BuildBmsString(string songName, float bpm, List<(int measure, int lane, float offset)> notes, List<TempoSegment> tempoMap)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"#TITLE {songName}");
        sb.AppendLine("#ARTIST AutoGenerated");
        sb.AppendLine($"#BPM {bpm:F0}");
        AppendTempoDefinitions(sb, tempoMap);
        AppendTempoEvents(sb, bpm, tempoMap);

        foreach (var laneGroup in notes.GroupBy(n => (n.measure, n.lane)).OrderBy(g => g.Key.measure).ThenBy(g => g.Key.lane))
        {
            List<float> offsets = laneGroup.Select(n => n.offset).Distinct().OrderBy(o => o).ToList();
            int resolution = DetermineResolution(offsets);
            char[] cells = CreateCells(resolution);

            foreach (float offset in offsets)
            {
                int cell = Math.Clamp((int)MathF.Round(offset * resolution), 0, resolution - 1);
                cells[cell * 2 + 1] = '1';
            }

            sb.AppendLine($"#{laneGroup.Key.measure:D3}{11 + laneGroup.Key.lane:D2}:{new string(cells)}");
        }

        return sb.ToString();
    }

    private static string BuildBmsStringFromLaneNotes(string songName, float bpm, IReadOnlyList<LaneNote> notes, List<TempoSegment> tempoMap)
    {
        bpm = Math.Clamp(bpm, 40f, 300f);
        float secondsPerMeasure = 240f / bpm;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"#TITLE {songName}");
        sb.AppendLine("#ARTIST UserChart");
        sb.AppendLine($"#BPM {bpm:F0}");
        AppendTempoDefinitions(sb, tempoMap);
        AppendTempoEvents(sb, bpm, tempoMap);

        var cellsByLane = new Dictionary<(int measure, int lane), Dictionary<int, string>>();
        foreach (LaneNote note in notes.OrderBy(n => n.Time).ThenBy(n => n.Lane))
        {
            float position = Math.Max(0f, note.Time / secondsPerMeasure);
            int measure = Math.Clamp((int)MathF.Floor(position), 0, 999);
            float offset = Math.Clamp(position - measure, 0f, 0.999f);
            int resolution = 48;
            int cell = Math.Clamp((int)MathF.Round(offset * resolution), 0, resolution - 1);
            int lane = Math.Clamp(note.Lane, 0, 6);
            string token = note.Type switch
            {
                NoteType.Long => "02",
                NoteType.Slide => $"3{Math.Clamp(note.EndLane + 1, 1, 7):X1}",
                _ => "01",
            };

            var key = (measure, lane);
            if (!cellsByLane.TryGetValue(key, out Dictionary<int, string>? cellMap))
            {
                cellMap = [];
                cellsByLane[key] = cellMap;
            }

            cellMap[cell] = token;
        }

        foreach (var laneGroup in cellsByLane.OrderBy(kv => kv.Key.measure).ThenBy(kv => kv.Key.lane))
        {
            int resolution = 48;
            char[] cells = CreateCells(resolution);
            foreach (var (cell, token) in laneGroup.Value)
            {
                cells[cell * 2] = token[0];
                cells[cell * 2 + 1] = token[1];
            }

            sb.AppendLine($"#{laneGroup.Key.measure:D3}{11 + laneGroup.Key.lane:D2}:{new string(cells)}");
        }

        return sb.ToString();
    }

    private static void AppendTempoDefinitions(System.Text.StringBuilder sb, List<TempoSegment> tempoMap)
    {
        for (int i = 1; i < tempoMap.Count && i <= 255; i++)
            sb.AppendLine($"#BPM{i:X2} {tempoMap[i].Bpm:F0}");
    }

    private static void AppendTempoEvents(System.Text.StringBuilder sb, float baseBpm, List<TempoSegment> tempoMap)
    {
        if (tempoMap.Count <= 1)
            return;

        float secondsPerMeasure = 240f / baseBpm;
        var events = tempoMap
            .Skip(1)
            .Take(255)
            .Select((t, i) =>
            {
                float position = t.Time / secondsPerMeasure;
                return (Measure: Math.Max(0, (int)MathF.Floor(position)), Offset: Math.Clamp(position % 1f, 0f, 0.999f), Token: (i + 1).ToString("X2"));
            })
            .GroupBy(e => e.Measure);

        foreach (var group in events.OrderBy(g => g.Key))
        {
            List<float> offsets = group.Select(e => e.Offset).Distinct().OrderBy(o => o).ToList();
            int resolution = DetermineResolution(offsets);
            char[] cells = CreateCells(resolution);

            foreach (var tempoEvent in group)
            {
                int cell = Math.Clamp((int)MathF.Round(tempoEvent.Offset * resolution), 0, resolution - 1);
                cells[cell * 2] = tempoEvent.Token[0];
                cells[cell * 2 + 1] = tempoEvent.Token[1];
            }

            sb.AppendLine($"#{group.Key:D3}08:{new string(cells)}");
        }
    }

    private static char[] CreateCells(int resolution)
    {
        char[] cells = new char[resolution * 2];
        Array.Fill(cells, '0');
        return cells;
    }

    private static int DetermineResolution(List<float> offsets)
    {
        foreach (int resolution in new[] { 4, 8, 12, 16, 24 })
        {
            if (offsets.All(o => MathF.Abs(MathF.Round(o * resolution) / resolution - o) <= 0.03f))
                return resolution;
        }

        return 24;
    }

    private static string NormalizeSongFileName(string name)
    {
        var sb = new System.Text.StringBuilder(name.Length);
        foreach (char ch in name)
        {
            if (char.IsLetterOrDigit(ch))
                sb.Append(char.ToLowerInvariant(ch));
            else if (ch is ' ' or '-' or '_')
                sb.Append('_');
        }

        return sb.ToString();
    }
}
