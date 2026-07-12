using System.Buffers;
using System.Security.Cryptography;

namespace RhythmGame;

internal sealed class ReplayRecord
{
    public string ReplayVersion { get; set; } = ReplayCompatibility.CurrentReplayVersion;
    public string GameVersion { get; set; } = string.Empty;
    public string ChartVersion { get; set; } = string.Empty;
    public string AudioFingerprint { get; set; } = string.Empty;
    public string SongId { get; set; } = string.Empty;
    public string SongTitle { get; set; } = string.Empty;
    public string Artist { get; set; } = string.Empty;
    public int DifficultyIndex { get; set; }
    public string Difficulty { get; set; } = string.Empty;
    public int LaneCount { get; set; }
    public int AudioOffsetMs { get; set; }
    public float SpeedMultiplier { get; set; }
    public string PlayedUtc { get; set; } = string.Empty;
    public int Score { get; set; }
    public float Accuracy { get; set; }
    public string Grade { get; set; } = string.Empty;
    public string ClearType { get; set; } = string.Empty;
    public ReplaySettingsSnapshot Settings { get; set; } = new();
    public ReplayResultSnapshot Result { get; set; } = new();
    public List<LaneNote> Chart { get; set; } = [];
    public List<InputLogEvent> Events { get; set; } = [];
    public List<NoteJudgmentEvent> Judgments { get; set; } = [];
}

internal sealed class ReplaySettingsSnapshot
{
    public int AudioOffsetMs { get; set; }
    public int NoteSpeedPercent { get; set; } = 100;
    public int PlayModeIndex { get; set; }
    public int GameModeIndex { get; set; }
    public int LaneCount { get; set; }
}

internal sealed class ReplayResultSnapshot
{
    public int Score { get; set; }
    public float Accuracy { get; set; }
    public string Grade { get; set; } = string.Empty;
    public string ClearType { get; set; } = string.Empty;
    public int PerfectCount { get; set; }
    public int GreatCount { get; set; }
    public int BetterCount { get; set; }
    public int GoodCount { get; set; }
    public int BadCount { get; set; }
    public int MissCount { get; set; }
    public int MaxCombo { get; set; }
    public int MaxMissStreak { get; set; }
}

internal readonly record struct ReplayValidationResult(bool CanPlay, string UserMessage);

internal static class ReplayCompatibility
{
    public const string CurrentReplayVersion = "3";
    // Use the released assembly version, not InformationalVersion: SourceLink can
    // append a commit hash to the latter, which would invalidate every replay on
    // harmless rebuilds while still being ambiguous for dirty working trees.
    public static string CurrentGameVersion { get; } =
        typeof(ReplayCompatibility).Assembly.GetName().Version?.ToString(3) ?? "unknown";

    public static ReplayValidationResult Validate(
        ReplayRecord replay,
        string expectedSongId,
        int expectedDifficultyIndex,
        int expectedLaneCount)
    {
        if (!string.Equals(replay.ReplayVersion, CurrentReplayVersion, StringComparison.Ordinal))
            return new ReplayValidationResult(false, "REPLAY FORMAT OUTDATED - RECORD AGAIN");

        if (string.IsNullOrWhiteSpace(replay.GameVersion) ||
            !string.Equals(replay.GameVersion, CurrentGameVersion, StringComparison.Ordinal))
        {
            return new ReplayValidationResult(false, "REPLAY GAME VERSION MISMATCH");
        }

        if (string.IsNullOrWhiteSpace(replay.AudioFingerprint))
            return new ReplayValidationResult(false, "REPLAY AUDIO FINGERPRINT IS MISSING");

        if (!string.Equals(replay.SongId, expectedSongId, StringComparison.Ordinal) ||
            replay.DifficultyIndex != expectedDifficultyIndex ||
            replay.LaneCount != expectedLaneCount)
        {
            return new ReplayValidationResult(false, "REPLAY SONG OR MODE MISMATCH");
        }

        if (replay.Chart is not { Count: > 0 } || replay.Chart.Count > 200_000)
            return new ReplayValidationResult(false, "REPLAY CHART DATA IS INVALID");

        float previousChartTime = float.NegativeInfinity;
        foreach (LaneNote note in replay.Chart)
        {
            int endLane = note.EndLane >= 0 ? note.EndLane : note.Lane;
            if (!float.IsFinite(note.Time) || note.Time < 0f || note.Time < previousChartTime ||
                note.Lane < 0 || note.Lane >= expectedLaneCount || endLane < 0 || endLane >= expectedLaneCount ||
                !Enum.IsDefined(note.Type) || !float.IsFinite(note.Duration) || note.Duration < 0f)
            {
                return new ReplayValidationResult(false, "REPLAY CHART DATA IS INVALID");
            }

            previousChartTime = note.Time;
        }

        string embeddedChartVersion = BuildChartVersion(
            replay.SongId,
            replay.DifficultyIndex,
            replay.LaneCount,
            replay.Chart);
        if (string.IsNullOrWhiteSpace(replay.ChartVersion) ||
            !string.Equals(replay.ChartVersion, embeddedChartVersion, StringComparison.Ordinal))
        {
            return new ReplayValidationResult(false, "REPLAY CHART SNAPSHOT IS INVALID");
        }

        if (replay.Settings is null || replay.Settings.LaneCount != expectedLaneCount ||
            replay.Settings.AudioOffsetMs is < -150 or > 150 ||
            replay.Settings.NoteSpeedPercent is < 10 or > 500 ||
            replay.Settings.PlayModeIndex is < 0 or > 1 ||
            replay.Settings.GameModeIndex is < 0 or > 2)
        {
            return new ReplayValidationResult(false, "REPLAY SETTINGS ARE INVALID");
        }

        if (replay.Result is null || replay.Result.Score is < 0 or > 1_000_000 ||
            !float.IsFinite(replay.Result.Accuracy) || replay.Result.Accuracy is < 0f or > 100f ||
            replay.Result.PerfectCount < 0 || replay.Result.GreatCount < 0 || replay.Result.BetterCount < 0 ||
            replay.Result.GoodCount < 0 || replay.Result.BadCount < 0 || replay.Result.MissCount < 0 ||
            replay.Result.MaxCombo < 0 || replay.Result.MaxMissStreak < 0 ||
            replay.Result.MaxCombo > replay.Result.PerfectCount + replay.Result.GreatCount + replay.Result.BetterCount + replay.Result.GoodCount + replay.Result.BadCount ||
            replay.Result.MaxMissStreak > replay.Result.MissCount)
        {
            return new ReplayValidationResult(false, "REPLAY RESULT IS INVALID");
        }

        if (replay.Events is null || replay.Events.Count == 0)
            return new ReplayValidationResult(false, "REPLAY HAS NO INPUT EVENTS");

        float previousTime = float.NegativeInfinity;
        foreach (InputLogEvent input in replay.Events)
        {
            if (!float.IsFinite(input.Time) || input.Time < 0f || input.Time < previousTime ||
                input.Lane < 0 || input.Lane >= expectedLaneCount)
            {
                return new ReplayValidationResult(false, "REPLAY INPUT DATA IS INVALID");
            }

            previousTime = input.Time;
        }

        if (replay.Judgments is null)
            return new ReplayValidationResult(false, "REPLAY JUDGMENT DATA IS INVALID");

        float previousJudgmentTime = float.NegativeInfinity;
        foreach (NoteJudgmentEvent judgment in replay.Judgments)
        {
            bool hasValidHitJudgment = judgment.Judgment is Judgment hitJudgment && Enum.IsDefined(hitJudgment);
            bool semanticOutcomeIsValid = judgment.FailureReason == NoteFailureReason.None
                ? hasValidHitJudgment
                : judgment.Judgment is null;
            if (!float.IsFinite(judgment.ChartTime) || !float.IsFinite(judgment.TargetTime) || !float.IsFinite(judgment.OffsetSeconds) ||
                judgment.ChartTime < 0f || judgment.TargetTime < 0f || judgment.ChartTime < previousJudgmentTime ||
                judgment.Lane < 0 || judgment.Lane >= expectedLaneCount ||
                judgment.EndLane < 0 || judgment.EndLane >= expectedLaneCount || !Enum.IsDefined(judgment.NoteType) ||
                !Enum.IsDefined(judgment.Phase) || !Enum.IsDefined(judgment.FailureReason) || !semanticOutcomeIsValid)
            {
                return new ReplayValidationResult(false, "REPLAY JUDGMENT DATA IS INVALID");
            }

            previousJudgmentTime = judgment.ChartTime;
        }

        if (replay.Judgments.Count(item => item.Judgment == Judgment.Perfect) != replay.Result.PerfectCount ||
            replay.Judgments.Count(item => item.Judgment == Judgment.Great) != replay.Result.GreatCount ||
            replay.Judgments.Count(item => item.Judgment == Judgment.Better) != replay.Result.BetterCount ||
            replay.Judgments.Count(item => item.Judgment == Judgment.Good) != replay.Result.GoodCount ||
            replay.Judgments.Count(item => item.Judgment == Judgment.Bad) != replay.Result.BadCount ||
            replay.Judgments.Count(item => item.IsMiss) != replay.Result.MissCount)
        {
            return new ReplayValidationResult(false, "REPLAY JUDGMENT SUMMARY IS INVALID");
        }

        return new ReplayValidationResult(true, "REPLAY READY");
    }

    public static ReplayValidationResult Validate(
        ReplayRecord replay,
        string expectedSongId,
        int expectedDifficultyIndex,
        int expectedLaneCount,
        string expectedChartVersion)
    {
        ReplayValidationResult embeddedValidation = Validate(
            replay,
            expectedSongId,
            expectedDifficultyIndex,
            expectedLaneCount);
        if (!embeddedValidation.CanPlay)
            return embeddedValidation;

        return string.Equals(replay.ChartVersion, expectedChartVersion, StringComparison.Ordinal)
            ? embeddedValidation
            : new ReplayValidationResult(false, "REPLAY CHART CHANGED - RECORD AGAIN");
    }

    public static ReplayValidationResult ValidateForPlayback(
        ReplayRecord replay,
        string expectedSongId,
        int expectedDifficultyIndex,
        int expectedLaneCount,
        string expectedAudioFingerprint)
    {
        ReplayValidationResult validation = Validate(
            replay,
            expectedSongId,
            expectedDifficultyIndex,
            expectedLaneCount);
        if (!validation.CanPlay)
            return validation;

        return string.Equals(replay.AudioFingerprint, expectedAudioFingerprint, StringComparison.Ordinal)
            ? validation
            : new ReplayValidationResult(false, "REPLAY AUDIO CHANGED - RECORD AGAIN");
    }

    public static string BuildChartVersion(
        string songId,
        int difficultyIndex,
        int laneCount,
        IReadOnlyList<LaneNote> chartNotes)
    {
        unchecked
        {
            int hash = 17;
            hash = AddStableHash(hash, songId);
            hash = hash * 31 + difficultyIndex;
            hash = hash * 31 + laneCount;
            foreach (LaneNote note in chartNotes)
            {
                hash = hash * 31 + note.Lane;
                hash = hash * 31 + note.EndLane;
                hash = hash * 31 + (int)note.Type;
                hash = AddStableHash(hash, note.Time);
                hash = AddStableHash(hash, note.Duration);
            }

            return $"{songId}:{difficultyIndex}:{laneCount}K:{hash:x8}";
        }
    }

    private static int AddStableHash(int hash, string value)
    {
        foreach (char c in value ?? string.Empty)
            hash = hash * 31 + c;
        return hash;
    }

    private static int AddStableHash(int hash, float value)
    {
        return hash * 31 + BitConverter.SingleToInt32Bits(value);
    }

    public static string BuildAudioFingerprint(string audioPath)
    {
        return BuildAudioFingerprintAsync(audioPath).GetAwaiter().GetResult();
    }

    public static Task<string> BuildAudioFingerprintAsync(string audioPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(audioPath))
            throw new ArgumentException("An audio path is required.", nameof(audioPath));

        string fullPath = Path.GetFullPath(audioPath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("The replay audio file was not found.", fullPath);

        return ComputeAudioFingerprintAsync(fullPath, cancellationToken);
    }

    private static async Task<string> ComputeAudioFingerprintAsync(string fullPath, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = ArrayPool<byte>.Shared.Rent(1024 * 1024);
        try
        {
            int bytesRead;
            while ((bytesRead = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false)) > 0)
                hash.AppendData(buffer, 0, bytesRead);

            cancellationToken.ThrowIfCancellationRequested();
            return $"sha256:{Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant()}";
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public static string CompareResult(
        ReplayRecord replay,
        ScoreManager score,
        ResultGrade grade,
        ClearType clearType,
        IReadOnlyList<NoteJudgmentEvent>? actualJudgments = null)
    {
        ReplayResultSnapshot expected = replay.Result;
        IReadOnlyList<NoteJudgmentEvent> expectedJudgments = replay.Judgments ?? [];
        bool judgmentEventsMatch = actualJudgments is null
            ? expectedJudgments.Count == 0
            : AreJudgmentEventsEquivalent(expectedJudgments, actualJudgments);
        bool matches = expected is not null &&
            expected.Score == score.Score &&
            MathF.Abs(expected.Accuracy - score.Accuracy) <= 0.01f &&
            expected.PerfectCount == score.PerfectCount &&
            expected.GreatCount == score.GreatCount &&
            expected.BetterCount == score.BetterCount &&
            expected.GoodCount == score.GoodCount &&
            expected.BadCount == score.BadCount &&
            expected.MissCount == score.MissCount &&
            expected.MaxCombo == score.MaxCombo &&
            expected.MaxMissStreak == score.MaxMissStreak &&
            string.Equals(expected.Grade, ScoreManager.FormatGrade(grade), StringComparison.Ordinal) &&
            string.Equals(expected.ClearType, ScoreManager.FormatClearType(clearType), StringComparison.Ordinal) &&
            judgmentEventsMatch;

        if (matches)
        {
            AppLogger.Info($"Replay verified for {replay.SongId}: score={score.Score}, accuracy={score.Accuracy:F2}.");
            return "REPLAY VERIFIED";
        }

        AppLogger.Info(
            $"Replay result mismatch for {replay.SongId}: expected score={expected?.Score}, actual score={score.Score}, " +
            $"expected accuracy={expected?.Accuracy:F2}, actual accuracy={score.Accuracy:F2}, " +
            $"expected judgments={expected?.PerfectCount}/{expected?.GreatCount}/{expected?.BetterCount}/{expected?.GoodCount}/{expected?.BadCount}/{expected?.MissCount}, " +
            $"actual judgments={score.PerfectCount}/{score.GreatCount}/{score.BetterCount}/{score.GoodCount}/{score.BadCount}/{score.MissCount}");
        return "REPLAY MISMATCH";
    }

    private static bool AreJudgmentEventsEquivalent(
        IReadOnlyList<NoteJudgmentEvent> expected,
        IReadOnlyList<NoteJudgmentEvent> actual)
    {
        if (expected.Count != actual.Count)
            return false;

        for (int i = 0; i < expected.Count; i++)
        {
            NoteJudgmentEvent left = expected[i];
            NoteJudgmentEvent right = actual[i];
            // ChartTime and OffsetSeconds describe the sampling instant. They can move by a
            // frame while the semantic result for the same chart target remains identical.
            // Replay verification therefore compares the stable note identity and outcome.
            if (left.Lane != right.Lane || left.EndLane != right.EndLane || left.NoteType != right.NoteType ||
                left.Phase != right.Phase || left.Judgment != right.Judgment || left.FailureReason != right.FailureReason ||
                MathF.Abs(left.TargetTime - right.TargetTime) > 0.001f)
            {
                return false;
            }
        }

        return true;
    }
}

internal sealed record InputLogEvent(
    float Time,
    int Lane,
    string Input,
    bool KeyDown,
    string Judgment,
    string Source);

internal sealed class InputLogStore
{
    private readonly string _logDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RhythmGame",
        "input_logs");

    public void Save(IReadOnlyList<InputLogEvent> events)
    {
        if (events.Count == 0)
            return;

        Directory.CreateDirectory(_logDirectory);
        string fileName = $"input_log_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json";
        string path = Path.Combine(_logDirectory, fileName);
        string json = System.Text.Json.JsonSerializer.Serialize(events, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true,
        });
        File.WriteAllText(path, json);
    }
}

internal sealed class ReplayStore
{
    internal static string? DefaultReplayDirectoryOverride { get; set; }

    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string _replayDirectory;

    public ReplayStore()
        : this(DefaultReplayDirectoryOverride ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RhythmGame",
            "replays"))
    {
    }

    internal ReplayStore(string replayDirectory)
    {
        _replayDirectory = replayDirectory;
    }

    public string Save(ReplayRecord replay)
    {
        ArgumentNullException.ThrowIfNull(replay);
        if (replay.Events is not { Count: > 0 })
            return string.Empty;

        Directory.CreateDirectory(_replayDirectory);
        string safeSongId = string.Join("_", replay.SongId.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
        string fileName = $"replay_{safeSongId}_{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid():N}.json";
        string path = Path.Combine(_replayDirectory, fileName);
        string json = System.Text.Json.JsonSerializer.Serialize(replay, JsonOptions);
        SafeJsonFile.WriteWithBackup(path, json);
        return path;
    }

    public ReplayRecord? LoadLatest(string songId, int difficultyIndex, int laneCount)
    {
        return LoadCandidates(songId, difficultyIndex, laneCount).FirstOrDefault();
    }

    public IReadOnlyList<ReplayRecord> LoadCandidates(
        string songId,
        int difficultyIndex,
        int laneCount,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!Directory.Exists(_replayDirectory))
                return [];

            string[] paths = Directory.EnumerateFiles(_replayDirectory, "replay_*.json")
                .Where(path => Path.GetFileName(path).StartsWith($"replay_{BuildSafeSongId(songId)}_", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .ToArray();
            var candidates = new List<ReplayRecord>();
            foreach (string path in paths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ReplayRecord? replay = Load(path);
                if (replay is not null && string.Equals(replay.SongId, songId, StringComparison.Ordinal) &&
                    replay.DifficultyIndex == difficultyIndex && replay.LaneCount == laneCount)
                {
                    candidates.Add(replay);
                }
            }

            return candidates;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            AppLogger.Error("Failed to enumerate replay files.", ex);
            return [];
        }
    }

    private static ReplayRecord? Load(string path)
    {
        try
        {
            string json = File.ReadAllText(path);
            return System.Text.Json.JsonSerializer.Deserialize<ReplayRecord>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            AppLogger.Error($"Failed to load replay {Path.GetFileName(path)}.", ex);
            return null;
        }
    }

    private static string BuildSafeSongId(string songId)
    {
        return string.Join("_", (songId ?? string.Empty).Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
    }
}
