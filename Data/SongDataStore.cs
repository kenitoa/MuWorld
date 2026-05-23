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
}

internal sealed class SongScoreRecord
{
    public string SongId { get; set; } = string.Empty;
    public int HighestScore { get; set; }
    public int BestCombo { get; set; }
    public float BestAccuracy { get; set; }
    public int PlayCount { get; set; }
    public string LastPlayedUtc { get; set; } = string.Empty;
    public Dictionary<string, int> DifficultyHighScores { get; set; } = [];
}

internal sealed class SongDataFile
{
    public Dictionary<string, SongMetadata> Metadata { get; set; } = [];
    public Dictionary<string, SongScoreRecord> Scores { get; set; } = [];
}

internal sealed class SongDataStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string _saveFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RhythmGame",
        "song_data.json");

    private SongDataFile? _cache;

    public SongDataFile Load()
    {
        if (_cache is not null)
            return _cache;

        try
        {
            if (!File.Exists(_saveFilePath))
                return _cache = new SongDataFile();

            string json = File.ReadAllText(_saveFilePath);
            _cache = JsonSerializer.Deserialize<SongDataFile>(json, JsonOptions) ?? new SongDataFile();
            return _cache;
        }
        catch
        {
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

    public SongScoreRecord? TryGetScore(string songId)
    {
        SongDataFile data = Load();
        return data.Scores.TryGetValue(songId, out SongScoreRecord? score) ? score : null;
    }

    public SongScoreRecord RecordScore(SongMetadata metadata, int difficultyIndex, int score, int combo, float accuracy)
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

        record.PlayCount++;
        record.HighestScore = Math.Max(record.HighestScore, score);
        record.BestCombo = Math.Max(record.BestCombo, combo);
        record.BestAccuracy = Math.Max(record.BestAccuracy, accuracy);
        record.LastPlayedUtc = DateTime.UtcNow.ToString("O");
        record.DifficultyHighScores[difficulty] = Math.Max(
            record.DifficultyHighScores.GetValueOrDefault(difficulty),
            score);

        Save(data);
        return record;
    }

    private void Save(SongDataFile data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_saveFilePath)!);
        string json = JsonSerializer.Serialize(data, JsonOptions);
        File.WriteAllText(_saveFilePath, json);
        _cache = data;
    }
}
