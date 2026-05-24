using System.Text.Json;

namespace RhythmGame;

internal sealed class SongMetadata
{
    public string SongId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Artist { get; set; } = "Unknown Artist";
    public string Format { get; set; } = "AUDIO";
    public float DurationSeconds { get; set; }
    public float Bpm { get; set; }
    public float PreviewStart { get; set; }
    public float PreviewEnd { get; set; }
    public string Genre { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string BgaPath { get; set; } = string.Empty;
    public string CoverPath { get; set; } = string.Empty;
}

internal sealed class SongScoreRecord
{
    public string SongId { get; set; } = string.Empty;
    public int HighestScore { get; set; }
    public int BestCombo { get; set; }
    public float BestAccuracy { get; set; }
    public string BestGrade { get; set; } = string.Empty;
    public string BestClearType { get; set; } = string.Empty;
    public string LastGrade { get; set; } = string.Empty;
    public string LastClearType { get; set; } = string.Empty;
    public int? LowestMaxMissStreak { get; set; }
    public int PlayCount { get; set; }
    public bool IsFavorite { get; set; }
    public string LastPlayedUtc { get; set; } = string.Empty;
    public Dictionary<string, int> DifficultyHighScores { get; set; } = [];
    public Dictionary<string, float> DifficultyBestAccuracy { get; set; } = [];
    public Dictionary<string, string> DifficultyBestGrade { get; set; } = [];
    public Dictionary<string, string> DifficultyBestClearType { get; set; } = [];
    public Dictionary<string, int> DifficultyBestCombo { get; set; } = [];
    public Dictionary<string, int> DifficultyLowestMaxMissStreak { get; set; } = [];
    public Dictionary<string, int> DifficultyPlayCount { get; set; } = [];
    public Dictionary<string, float> AdaptiveDensityByMode { get; set; } = [];
    public List<SongPlayHistoryEntry> History { get; set; } = [];
}

internal sealed class SongPlayHistoryEntry
{
    public string PlayedUtc { get; set; } = string.Empty;
    public int DifficultyIndex { get; set; }
    public string Difficulty { get; set; } = string.Empty;
    public int LaneCount { get; set; }
    public string ModeKey { get; set; } = string.Empty;
    public int Score { get; set; }
    public int MaxCombo { get; set; }
    public float Accuracy { get; set; }
    public string Grade { get; set; } = string.Empty;
    public string ClearType { get; set; } = string.Empty;
    public int MaxMissStreak { get; set; }
    public int PerfectCount { get; set; }
    public int GreatCount { get; set; }
    public int BetterCount { get; set; }
    public int GoodCount { get; set; }
    public int BadCount { get; set; }
    public int MissCount { get; set; }
    public string ReplayPath { get; set; } = string.Empty;
}

internal sealed class SongDataFile
{
    public Dictionary<string, SongMetadata> Metadata { get; set; } = [];
    public Dictionary<string, SongScoreRecord> Scores { get; set; } = [];
}

internal sealed class SongDataStore
{
    internal static string? DefaultSaveFilePathOverride { get; set; }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string _saveFilePath;

    private SongDataFile? _cache;

    public SongDataStore()
        : this(DefaultSaveFilePathOverride ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RhythmGame",
            "song_data.json"))
    {
    }

    internal SongDataStore(string saveFilePath)
    {
        _saveFilePath = saveFilePath;
    }

    public SongDataFile Load()
    {
        if (_cache is not null)
            return _cache;

        try
        {
            if (!SafeJsonFile.TryReadWithBackup(_saveFilePath, out string json))
                return _cache = new SongDataFile();

            _cache = JsonSerializer.Deserialize<SongDataFile>(json, JsonOptions) ?? new SongDataFile();
            return _cache;
        }
        catch (Exception ex)
        {
            AppLogger.Error("Failed to parse song_data.json; trying backup.", ex);
            if (SafeJsonFile.TryReadBackup(_saveFilePath, restore: true, out string backupJson))
            {
                try
                {
                    _cache = JsonSerializer.Deserialize<SongDataFile>(backupJson, JsonOptions) ?? new SongDataFile();
                    return _cache;
                }
                catch (Exception backupEx)
                {
                    AppLogger.Error("Failed to parse song_data.json backup.", backupEx);
                }
            }

            return _cache = new SongDataFile();
        }
    }

    public SongMetadata UpsertMetadata(SongMetadata metadata)
    {
        SongDataFile data = Load();
        data.Metadata[metadata.SongId] = metadata;
        Save(data);
        return metadata;
    }

    public void UpsertMetadataBatch(IEnumerable<SongMetadata> metadataItems)
    {
        SongDataFile data = Load();
        bool changed = false;
        foreach (SongMetadata metadata in metadataItems)
        {
            data.Metadata[metadata.SongId] = metadata;
            changed = true;
        }

        if (changed)
            Save(data);
    }

    public SongScoreRecord? TryGetScore(string songId)
    {
        SongDataFile data = Load();
        return data.Scores.TryGetValue(songId, out SongScoreRecord? score) ? score : null;
    }

    public SongScoreRecord? TryFindScoreBySongKey(string songKey)
    {
        SongDataFile data = Load();
        if (data.Scores.TryGetValue(songKey, out SongScoreRecord? direct))
            return direct;

        foreach (var (songId, metadata) in data.Metadata)
        {
            if (!string.Equals(metadata.Title, songKey, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(metadata.SongId, songKey, StringComparison.OrdinalIgnoreCase))
                continue;

            if (data.Scores.TryGetValue(songId, out SongScoreRecord? score))
                return score;
        }

        return null;
    }

    public void SetFavorite(string songId, bool isFavorite)
    {
        SongDataFile data = Load();
        if (!data.Scores.TryGetValue(songId, out SongScoreRecord? record))
        {
            record = new SongScoreRecord { SongId = songId };
            data.Scores[songId] = record;
        }

        record.IsFavorite = isFavorite;
        Save(data);
    }

    public SongScoreRecord RecordScore(
        SongMetadata metadata,
        int difficultyIndex,
        int score,
        int combo,
        float accuracy,
        ResultGrade grade,
        ClearType clearType,
        int maxMissStreak,
        int laneCount,
        int perfectCount = 0,
        int greatCount = 0,
        int betterCount = 0,
        int goodCount = 0,
        int badCount = 0,
        int missCount = 0,
        string replayPath = "")
    {
        SongDataFile data = Load();
        data.Metadata[metadata.SongId] = metadata;

        if (!data.Scores.TryGetValue(metadata.SongId, out SongScoreRecord? record))
        {
            record = new SongScoreRecord { SongId = metadata.SongId };
            data.Scores[metadata.SongId] = record;
        }

        string difficulty = difficultyIndex switch
        {
            0 => "Easy",
            1 => "Normal",
            _ => "Hard",
        };
        string modeKey = $"{difficulty}:{Math.Clamp(laneCount, 4, 7)}K";

        string playedUtc = DateTime.UtcNow.ToString("O");
        record.PlayCount++;
        record.HighestScore = Math.Max(record.HighestScore, score);
        record.BestCombo = Math.Max(record.BestCombo, combo);
        record.BestAccuracy = Math.Max(record.BestAccuracy, accuracy);
        if (string.IsNullOrWhiteSpace(record.BestGrade) || grade < ParseGrade(record.BestGrade))
            record.BestGrade = ScoreManager.FormatGrade(grade);
        if (string.IsNullOrWhiteSpace(record.BestClearType) || clearType > ParseClearType(record.BestClearType))
            record.BestClearType = ScoreManager.FormatClearType(clearType);
        record.LastGrade = ScoreManager.FormatGrade(grade);
        record.LastClearType = ScoreManager.FormatClearType(clearType);
        record.LowestMaxMissStreak = record.LowestMaxMissStreak is null
            ? maxMissStreak
            : Math.Min(record.LowestMaxMissStreak.Value, maxMissStreak);
        record.LastPlayedUtc = playedUtc;
        record.DifficultyHighScores[difficulty] = Math.Max(
            record.DifficultyHighScores.GetValueOrDefault(difficulty),
            score);
        record.DifficultyHighScores[modeKey] = Math.Max(
            record.DifficultyHighScores.GetValueOrDefault(modeKey),
            score);
        record.DifficultyBestAccuracy[modeKey] = Math.Max(
            record.DifficultyBestAccuracy.GetValueOrDefault(modeKey),
            accuracy);
        if (!record.DifficultyBestAccuracy.ContainsKey(difficulty))
            record.DifficultyBestAccuracy[difficulty] = accuracy;
        else
            record.DifficultyBestAccuracy[difficulty] = Math.Max(record.DifficultyBestAccuracy[difficulty], accuracy);

        if (!record.DifficultyBestGrade.TryGetValue(modeKey, out string? modeGrade) || grade < ParseGrade(modeGrade))
            record.DifficultyBestGrade[modeKey] = ScoreManager.FormatGrade(grade);
        if (!record.DifficultyBestClearType.TryGetValue(modeKey, out string? modeClear) || clearType > ParseClearType(modeClear))
            record.DifficultyBestClearType[modeKey] = ScoreManager.FormatClearType(clearType);
        record.DifficultyBestCombo[modeKey] = Math.Max(record.DifficultyBestCombo.GetValueOrDefault(modeKey), combo);
        record.DifficultyLowestMaxMissStreak[modeKey] = record.DifficultyLowestMaxMissStreak.TryGetValue(modeKey, out int oldStreak)
            ? Math.Min(oldStreak, maxMissStreak)
            : maxMissStreak;
        record.DifficultyPlayCount[modeKey] = record.DifficultyPlayCount.GetValueOrDefault(modeKey) + 1;

        record.History.Insert(0, new SongPlayHistoryEntry
        {
            PlayedUtc = playedUtc,
            DifficultyIndex = difficultyIndex,
            Difficulty = difficulty,
            LaneCount = Math.Clamp(laneCount, 4, 7),
            ModeKey = modeKey,
            Score = score,
            MaxCombo = combo,
            Accuracy = accuracy,
            Grade = ScoreManager.FormatGrade(grade),
            ClearType = ScoreManager.FormatClearType(clearType),
            MaxMissStreak = maxMissStreak,
            PerfectCount = perfectCount,
            GreatCount = greatCount,
            BetterCount = betterCount,
            GoodCount = goodCount,
            BadCount = badCount,
            MissCount = missCount,
            ReplayPath = replayPath,
        });
        if (record.History.Count > 50)
            record.History.RemoveRange(50, record.History.Count - 50);

        record.AdaptiveDensityByMode[modeKey] = CalculateAdaptiveDensity(record.DifficultyBestAccuracy[modeKey], clearType, record.PlayCount);

        Save(data);
        return record;
    }

    public static string GetDifficultyModeKey(int difficultyIndex, int laneCount)
    {
        string difficulty = difficultyIndex switch
        {
            0 => "Easy",
            1 => "Normal",
            _ => "Hard",
        };

        return $"{difficulty}:{Math.Clamp(laneCount, 4, 7)}K";
    }

    private static float CalculateAdaptiveDensity(float accuracy, ClearType clearType, int playCount)
    {
        float density = accuracy switch
        {
            < 60f => 0.65f,
            < 75f => 0.80f,
            < 88f => 0.92f,
            > 97f when clearType >= ClearType.FullCombo && playCount >= 3 => 1.03f,
            _ => 1f,
        };

        return Math.Clamp(density, 0.6f, 1.05f);
    }

    private void Save(SongDataFile data)
    {
        string json = JsonSerializer.Serialize(data, JsonOptions);
        SafeJsonFile.WriteWithBackup(_saveFilePath, json);
        _cache = data;
    }

    private static ResultGrade ParseGrade(string value)
    {
        return value switch
        {
            "S+" => ResultGrade.SPlus,
            "S" => ResultGrade.S,
            "A" => ResultGrade.A,
            "B" => ResultGrade.B,
            "C" => ResultGrade.C,
            "D" => ResultGrade.D,
            _ => ResultGrade.F,
        };
    }

    private static ClearType ParseClearType(string value)
    {
        return value switch
        {
            "Perfect" => ClearType.Perfect,
            "All Great+" => ClearType.AllGreatPlus,
            "Full Combo" => ClearType.FullCombo,
            "Clear" => ClearType.Clear,
            _ => ClearType.Failed,
        };
    }
}
