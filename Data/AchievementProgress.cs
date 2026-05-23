using System.Text.Json;

namespace RhythmGame;

internal sealed record GameSessionSummary(
    int Score,
    int MaxCombo,
    int PerfectCount,
    int GreatCount,
    int BetterCount,
    int GoodCount,
    int BadCount,
    int MissCount,
    int MaxMissStreak,
    float Accuracy,
    ResultGrade Grade,
    ClearType ClearType,
    string SongId,
    int DifficultyIndex,
    int LaneCount,
    float Bpm)
{
    public int TotalJudgedNotes => PerfectCount + GreatCount + BetterCount + GoodCount + BadCount + MissCount;
    public int TotalHitCount => PerfectCount + GreatCount + BetterCount + GoodCount + BadCount;
    public bool HasPlayableData => TotalJudgedNotes > 0 || Score > 0 || MaxCombo > 0;
}

internal sealed class PlayerProgress
{
    public int TotalGamesPlayed { get; set; }
    public int TotalScore { get; set; }
    public int HighestScore { get; set; }
    public int BestCombo { get; set; }
    public int TotalPerfectCount { get; set; }
    public int TotalGoodCount { get; set; }
    public int TotalMissCount { get; set; }
    public int MisslessRunsCount { get; set; }
    public int ControlledMissRunsCount { get; set; }
    public int FullComboRunsCount { get; set; }
    public int HardClearCount { get; set; }
    public int SevenKeyClearCount { get; set; }
    public int HighBpmClearCount { get; set; }
    public int? LowestMaxMissStreak { get; set; }
    public int TotalNotesProcessed { get; set; }
    public List<string> UnlockedAchievementIds { get; set; } = [];
    public HashSet<string> ClearedSongDifficultyModes { get; set; } = [];
    public HashSet<string> FullComboSongDifficultyModes { get; set; } = [];

    public bool IsUnlocked(string achievementId)
    {
        return UnlockedAchievementIds.Contains(achievementId, StringComparer.Ordinal);
    }

    public void Unlock(string achievementId)
    {
        if (!IsUnlocked(achievementId))
            UnlockedAchievementIds.Add(achievementId);
    }
}

internal sealed record AchievementDefinition(string Id, string Title, string Description, string ConditionText, int Tier);

internal static class AchievementCatalog
{
    public static readonly IReadOnlyList<AchievementDefinition> Definitions =
    [
        new("first_stage", "FIRST STAGE", "첫 유효 플레이 기록을 남겼습니다.", "플레이 1회 완료", 0),
        new("combo_apprentice", "COMBO APPRENTICE", "안정적인 콤보를 만들기 시작했습니다.", "최고 콤보 10 달성", 0),
        new("b_play_3", "WARM UP", "리듬에 몸을 풀었습니다.", "플레이 3회 완료", 0),
        new("b_score_10000", "SCORE ROOKIE", "기본 점수 감각을 익혔습니다.", "최고 점수 10,000 달성", 0),
        new("b_perfect_20", "PRECISION PLAYER", "정확한 타격이 늘고 있습니다.", "누적 PERFECT 20 달성", 0),
        new("b_total_notes_50", "RHYTHM LEARNER", "노트 처리 경험을 쌓았습니다.", "누적 노트 처리 50개", 0),
        new("b_first_clear", "FIRST CLEAR", "게이지 조건을 통과해 곡을 클리어했습니다.", "Clear 이상 1회", 0),
        new("b_four_key_clear", "4K CLEAR", "4K 모드 클리어를 기록했습니다.", "4K에서 Clear 이상 1회", 0),

        new("score_chaser", "SCORE CHASER", "더 높은 점수를 노릴 준비가 됐습니다.", "최고 점수 30,000 달성", 1),
        new("perfect_collector", "PERFECT COLLECTOR", "PERFECT 판정을 꾸준히 모았습니다.", "누적 PERFECT 75 달성", 1),
        new("s_combo_50", "COMBO KEEPER", "긴 콤보를 안정적으로 이어갑니다.", "최고 콤보 50 달성", 1),
        new("s_missless_3", "CLEAN STREAK", "미스 없는 플레이를 반복했습니다.", "Miss 0 플레이 3회", 1),
        new("s_hard_clear", "HARD CLEAR", "Hard 난이도 클리어에 성공했습니다.", "Hard에서 Clear 이상 1회", 1),
        new("s_7k_clear", "7K CLEAR", "7K 입력 체계에 적응했습니다.", "7K에서 Clear 이상 1회", 1),
        new("s_full_combo", "FULL COMBO", "한 곡을 끊김 없이 마무리했습니다.", "Full Combo 이상 1회", 1),
        new("s_unique_modes_3", "MODE EXPLORER", "여러 곡/난이도/레인 조합을 클리어했습니다.", "서로 다른 모드 클리어 3개", 1),

        new("clean_finish", "CLEAN FINISH", "미스 흐름을 짧게 끊으며 안정적으로 클리어했습니다.", "Max Miss Streak 1 이하로 Clear", 2),
        new("st_score_100000", "HUNDRED K", "고득점 영역에 진입했습니다.", "최고 점수 100,000 달성", 2),
        new("st_combo_100", "COMBO CENTURION", "100 콤보를 달성했습니다.", "최고 콤보 100 달성", 2),
        new("st_perfect_300", "PERFECT HUNTER", "PERFECT 누적 기록이 크게 늘었습니다.", "누적 PERFECT 300 달성", 2),
        new("st_hard_clear_5", "HARD RUNNER", "Hard 클리어를 반복했습니다.", "Hard Clear 5회", 2),
        new("st_7k_clear_5", "7K SPECIALIST", "7K 모드를 꾸준히 클리어했습니다.", "7K Clear 5회", 2),
        new("st_high_bpm", "SPEED READER", "빠른 BPM 곡을 클리어했습니다.", "BPM 180 이상 곡 Clear", 2),
        new("st_fc_modes_3", "FULL COMBO ROUTE", "서로 다른 조합에서 Full Combo를 기록했습니다.", "서로 다른 모드 Full Combo 3개", 2),
    ];

    public static List<AchievementDefinition> ApplySession(PlayerProgress progress, GameSessionSummary session)
    {
        progress.TotalGamesPlayed++;
        progress.TotalScore += session.Score;
        progress.HighestScore = Math.Max(progress.HighestScore, session.Score);
        progress.BestCombo = Math.Max(progress.BestCombo, session.MaxCombo);
        progress.TotalPerfectCount += session.PerfectCount;
        progress.TotalGoodCount += session.GreatCount + session.BetterCount + session.GoodCount;
        progress.TotalMissCount += session.MissCount;
        progress.TotalNotesProcessed += session.TotalJudgedNotes;
        progress.LowestMaxMissStreak = progress.LowestMaxMissStreak is null
            ? session.MaxMissStreak
            : Math.Min(progress.LowestMaxMissStreak.Value, session.MaxMissStreak);

        bool cleared = session.ClearType >= ClearType.Clear;
        string modeKey = BuildModeAchievementKey(session);
        if (session.MissCount == 0 && session.TotalHitCount >= 8)
            progress.MisslessRunsCount++;
        if (session.MaxMissStreak <= 1 && session.TotalHitCount >= 8 && cleared)
            progress.ControlledMissRunsCount++;
        if (session.ClearType >= ClearType.FullCombo)
        {
            progress.FullComboRunsCount++;
            progress.FullComboSongDifficultyModes.Add(modeKey);
        }
        if (cleared)
        {
            progress.ClearedSongDifficultyModes.Add(modeKey);
            if (session.DifficultyIndex >= 2)
                progress.HardClearCount++;
            if (session.LaneCount >= 7)
                progress.SevenKeyClearCount++;
            if (session.Bpm >= 180f)
                progress.HighBpmClearCount++;
        }

        List<AchievementDefinition> unlocked = [];
        foreach (AchievementDefinition definition in Definitions)
        {
            if (progress.IsUnlocked(definition.Id) || !IsSatisfied(definition.Id, progress))
                continue;

            progress.Unlock(definition.Id);
            unlocked.Add(definition);
        }

        return unlocked;
    }

    public static string GetProgressText(AchievementDefinition definition, PlayerProgress progress)
    {
        return definition.Id switch
        {
            "first_stage" => FormatProgress(progress.TotalGamesPlayed, 1),
            "combo_apprentice" => FormatProgress(progress.BestCombo, 10),
            "b_play_3" => FormatProgress(progress.TotalGamesPlayed, 3),
            "b_score_10000" => FormatProgress(progress.HighestScore, 10000),
            "b_perfect_20" => FormatProgress(progress.TotalPerfectCount, 20),
            "b_total_notes_50" => FormatProgress(progress.TotalNotesProcessed, 50),
            "b_first_clear" => FormatProgress(progress.ClearedSongDifficultyModes.Count, 1),
            "b_four_key_clear" => progress.ClearedSongDifficultyModes.Any(k => k.EndsWith(":4K", StringComparison.Ordinal)) ? "1/1" : "0/1",
            "score_chaser" => FormatProgress(progress.HighestScore, 30000),
            "perfect_collector" => FormatProgress(progress.TotalPerfectCount, 75),
            "s_combo_50" => FormatProgress(progress.BestCombo, 50),
            "s_missless_3" => FormatProgress(progress.MisslessRunsCount, 3),
            "s_hard_clear" => FormatProgress(progress.HardClearCount, 1),
            "s_7k_clear" => FormatProgress(progress.SevenKeyClearCount, 1),
            "s_full_combo" => FormatProgress(progress.FullComboRunsCount, 1),
            "s_unique_modes_3" => FormatProgress(progress.ClearedSongDifficultyModes.Count, 3),
            "clean_finish" => FormatProgress(progress.ControlledMissRunsCount, 1),
            "st_score_100000" => FormatProgress(progress.HighestScore, 100000),
            "st_combo_100" => FormatProgress(progress.BestCombo, 100),
            "st_perfect_300" => FormatProgress(progress.TotalPerfectCount, 300),
            "st_hard_clear_5" => FormatProgress(progress.HardClearCount, 5),
            "st_7k_clear_5" => FormatProgress(progress.SevenKeyClearCount, 5),
            "st_high_bpm" => FormatProgress(progress.HighBpmClearCount, 1),
            "st_fc_modes_3" => FormatProgress(progress.FullComboSongDifficultyModes.Count, 3),
            _ => string.Empty,
        };
    }

    public static bool IsSatisfied(string achievementId, PlayerProgress progress)
    {
        return achievementId switch
        {
            "first_stage" => progress.TotalGamesPlayed >= 1,
            "combo_apprentice" => progress.BestCombo >= 10,
            "b_play_3" => progress.TotalGamesPlayed >= 3,
            "b_score_10000" => progress.HighestScore >= 10000,
            "b_perfect_20" => progress.TotalPerfectCount >= 20,
            "b_total_notes_50" => progress.TotalNotesProcessed >= 50,
            "b_first_clear" => progress.ClearedSongDifficultyModes.Count >= 1,
            "b_four_key_clear" => progress.ClearedSongDifficultyModes.Any(k => k.EndsWith(":4K", StringComparison.Ordinal)),
            "score_chaser" => progress.HighestScore >= 30000,
            "perfect_collector" => progress.TotalPerfectCount >= 75,
            "s_combo_50" => progress.BestCombo >= 50,
            "s_missless_3" => progress.MisslessRunsCount >= 3,
            "s_hard_clear" => progress.HardClearCount >= 1,
            "s_7k_clear" => progress.SevenKeyClearCount >= 1,
            "s_full_combo" => progress.FullComboRunsCount >= 1,
            "s_unique_modes_3" => progress.ClearedSongDifficultyModes.Count >= 3,
            "clean_finish" => progress.ControlledMissRunsCount >= 1,
            "st_score_100000" => progress.HighestScore >= 100000,
            "st_combo_100" => progress.BestCombo >= 100,
            "st_perfect_300" => progress.TotalPerfectCount >= 300,
            "st_hard_clear_5" => progress.HardClearCount >= 5,
            "st_7k_clear_5" => progress.SevenKeyClearCount >= 5,
            "st_high_bpm" => progress.HighBpmClearCount >= 1,
            "st_fc_modes_3" => progress.FullComboSongDifficultyModes.Count >= 3,
            _ => false,
        };
    }

    private static string BuildModeAchievementKey(GameSessionSummary session)
    {
        string difficulty = session.DifficultyIndex switch
        {
            0 => "Easy",
            1 => "Normal",
            _ => "Hard",
        };
        return $"{session.SongId}:{difficulty}:{Math.Clamp(session.LaneCount, 4, 7)}K";
    }

    private static string FormatProgress(int value, int target)
    {
        return $"{Math.Min(value, target):N0}/{target:N0}";
    }
}

internal sealed class AchievementProgressStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string _saveFilePath;

    public AchievementProgressStore()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RhythmGame",
            "player_progress.json"))
    {
    }

    internal AchievementProgressStore(string saveFilePath)
    {
        _saveFilePath = saveFilePath;
    }

    public PlayerProgress Load()
    {
        try
        {
            if (!SafeJsonFile.TryReadWithBackup(_saveFilePath, out string json))
                return new PlayerProgress();

            return JsonSerializer.Deserialize<PlayerProgress>(json, JsonOptions) ?? new PlayerProgress();
        }
        catch (Exception ex)
        {
            AppLogger.Error("Failed to parse player_progress.json; trying backup.", ex);
            if (SafeJsonFile.TryReadBackup(_saveFilePath, restore: true, out string backupJson))
            {
                try
                {
                    return JsonSerializer.Deserialize<PlayerProgress>(backupJson, JsonOptions) ?? new PlayerProgress();
                }
                catch (Exception backupEx)
                {
                    AppLogger.Error("Failed to parse player_progress.json backup.", backupEx);
                }
            }

            return new PlayerProgress();
        }
    }

    public void Save(PlayerProgress progress)
    {
        string json = JsonSerializer.Serialize(progress, JsonOptions);
        SafeJsonFile.WriteWithBackup(_saveFilePath, json);
    }
}
