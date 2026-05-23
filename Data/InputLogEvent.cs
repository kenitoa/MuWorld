namespace RhythmGame;

internal sealed class ReplayRecord
{
    public string ReplayVersion { get; set; } = "1";
    public string ChartVersion { get; set; } = string.Empty;
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
    public List<InputLogEvent> Events { get; set; } = [];
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
    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string _replayDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RhythmGame",
        "replays");

    public string Save(ReplayRecord replay)
    {
        if (replay.Events.Count == 0)
            return string.Empty;

        Directory.CreateDirectory(_replayDirectory);
        string safeSongId = string.Join("_", replay.SongId.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
        string fileName = $"replay_{safeSongId}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json";
        string path = Path.Combine(_replayDirectory, fileName);
        string json = System.Text.Json.JsonSerializer.Serialize(replay, JsonOptions);
        File.WriteAllText(path, json);
        return path;
    }

    public ReplayRecord? LoadLatest(string songId, int difficultyIndex, int laneCount)
    {
        try
        {
            if (!Directory.Exists(_replayDirectory))
                return null;

            return Directory.EnumerateFiles(_replayDirectory, "replay_*.json")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .Select(Load)
                .FirstOrDefault(replay =>
                    replay is not null &&
                    string.Equals(replay.SongId, songId, StringComparison.Ordinal) &&
                    replay.DifficultyIndex == difficultyIndex &&
                    replay.LaneCount == laneCount);
        }
        catch
        {
            return null;
        }
    }

    private static ReplayRecord? Load(string path)
    {
        try
        {
            string json = File.ReadAllText(path);
            return System.Text.Json.JsonSerializer.Deserialize<ReplayRecord>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }
}
