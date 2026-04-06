using System.Text.Json;

namespace RhythmGame;

internal sealed record GameSessionSummary(int Score, int MaxCombo, int PerfectCount, int GreatCount, int BetterCount, int GoodCount, int BadCount, int MissCount)
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

    public int TotalNotesProcessed { get; set; }

    public List<string> UnlockedAchievementIds { get; set; } = [];

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
        // Bronze Achiever (Tier 0) - 20 achievements
        new("first_stage", "FIRST STAGE", "처음으로 유효한 플레이 기록을 남겼습니다.", "플레이 1회 완료", 0),
        new("combo_apprentice", "COMBO APPRENTICE", "한 판에서 안정적인 콤보를 이어갔습니다.", "최고 콤보 10 달성", 0),
        new("b_play_3", "WARM UP", "꾸준히 플레이하기 시작했습니다.", "플레이 3회 완료", 0),
        new("b_play_5", "GETTING STARTED", "리듬 게임에 익숙해지고 있습니다.", "플레이 5회 완료", 0),
        new("b_score_5000", "FIRST POINTS", "처음으로 의미 있는 점수를 기록했습니다.", "최고 점수 5,000 달성", 0),
        new("b_score_10000", "SCORE ROOKIE", "점수가 서서히 올라가고 있습니다.", "최고 점수 10,000 달성", 0),
        new("b_perfect_5", "NICE HIT", "정확한 타격에 눈을 뜨기 시작했습니다.", "누적 PERFECT 5회 달성", 0),
        new("b_perfect_10", "SHARP EYE", "정확도가 점점 좋아지고 있습니다.", "누적 PERFECT 10회 달성", 0),
        new("b_combo_5", "FIRST COMBO", "처음으로 콤보를 이어갔습니다.", "최고 콤보 5 달성", 0),
        new("b_good_5", "FAIR PLAYER", "GOOD 판정을 꾸준히 기록하고 있습니다.", "누적 GOOD 5회 달성", 0),
        new("b_good_10", "CONSISTENT", "안정적인 플레이를 보여주고 있습니다.", "누적 GOOD 10회 달성", 0),
        new("b_total_score_5000", "SCORE SAVER", "누적 점수가 쌓이기 시작했습니다.", "누적 점수 5,000 달성", 0),
        new("b_total_score_10000", "PIGGY BANK", "착실하게 점수를 모으고 있습니다.", "누적 점수 10,000 달성", 0),
        new("b_total_notes_20", "NOTE TAPPER", "노트 처리에 익숙해지고 있습니다.", "누적 노트 처리 20회", 0),
        new("b_total_notes_50", "RHYTHM LEARNER", "리듬에 맞춰 노트를 처리하고 있습니다.", "누적 노트 처리 50회", 0),
        new("b_play_10", "REGULAR PLAYER", "어느새 단골 플레이어가 되었습니다.", "플레이 10회 완료", 0),
        new("b_score_15000", "RISING STAR", "점수가 꾸준히 오르고 있습니다.", "최고 점수 15,000 달성", 0),
        new("b_combo_15", "COMBO BUILDER", "콤보를 이어가는 감각이 생겼습니다.", "최고 콤보 15 달성", 0),
        new("b_perfect_20", "PRECISION PLAYER", "정확한 타격이 습관이 되고 있습니다.", "누적 PERFECT 20회 달성", 0),
        new("b_good_20", "STEADY HAND", "꾸준한 판정을 유지하고 있습니다.", "누적 GOOD 20회 달성", 0),

        // Silver Achiever (Tier 1) - 25 achievements
        new("score_chaser", "SCORE CHASER", "고득점 플레이의 감을 잡기 시작했습니다.", "최고 점수 12,000 달성", 1),
        new("perfect_collector", "PERFECT COLLECTOR", "정확한 타격을 여러 번 성공했습니다.", "누적 PERFECT 15회 달성", 1),
        new("s_play_15", "DEDICATED", "꾸준한 플레이를 이어가고 있습니다.", "플레이 15회 완료", 1),
        new("s_play_20", "ENTHUSIAST", "리듬 게임에 열정을 쏟고 있습니다.", "플레이 20회 완료", 1),
        new("s_score_20000", "HIGH SCORER", "높은 점수대에 진입했습니다.", "최고 점수 20,000 달성", 1),
        new("s_score_30000", "SCORE HUNTER", "점수 사냥의 재미를 알게 되었습니다.", "최고 점수 30,000 달성", 1),
        new("s_combo_20", "COMBO KEEPER", "콤보를 안정적으로 유지합니다.", "최고 콤보 20 달성", 1),
        new("s_combo_30", "CHAIN MASTER", "긴 콤보를 연결할 수 있습니다.", "최고 콤보 30 달성", 1),
        new("s_perfect_30", "SHARP SHOOTER", "정확도가 높아지고 있습니다.", "누적 PERFECT 30회 달성", 1),
        new("s_perfect_50", "ACCURACY ACE", "뛰어난 정확도를 보여주고 있습니다.", "누적 PERFECT 50회 달성", 1),
        new("s_total_score_30000", "SCORE HOARDER", "점수를 착실히 모으고 있습니다.", "누적 점수 30,000 달성", 1),
        new("s_total_score_50000", "WEALTHY PLAYER", "누적 점수가 상당합니다.", "누적 점수 50,000 달성", 1),
        new("s_total_notes_100", "NOTE HANDLER", "노트 처리 경험이 풍부합니다.", "누적 노트 처리 100회", 1),
        new("s_total_notes_200", "RHYTHM WORKER", "수많은 노트를 처리했습니다.", "누적 노트 처리 200회", 1),
        new("s_good_30", "RELIABLE", "안정적인 판정이 많습니다.", "누적 GOOD 30회 달성", 1),
        new("s_good_50", "ROCK SOLID", "흔들림 없는 플레이입니다.", "누적 GOOD 50회 달성", 1),
        new("s_missless_2", "CAREFUL PLAYER", "실수 없는 플레이를 반복했습니다.", "미스 없이 클리어 2회", 1),
        new("s_missless_3", "CLEAN STREAK", "꾸준히 깔끔하게 마무리합니다.", "미스 없이 클리어 3회", 1),
        new("s_play_30", "VETERAN", "이미 베테랑입니다.", "플레이 30회 완료", 1),
        new("s_score_40000", "TOP SCORER", "최상위 점수를 기록했습니다.", "최고 점수 40,000 달성", 1),
        new("s_combo_40", "COMBO EXPERT", "콤보의 달인이 되어가고 있습니다.", "최고 콤보 40 달성", 1),
        new("s_perfect_75", "SNIPER", "높은 정확도로 타격합니다.", "누적 PERFECT 75회 달성", 1),
        new("s_total_score_80000", "TREASURE CHEST", "막대한 점수를 축적했습니다.", "누적 점수 80,000 달성", 1),
        new("s_total_notes_300", "NOTE CRUSHER", "노트 처리의 달인입니다.", "누적 노트 처리 300회", 1),
        new("s_play_40", "HARDCORE FAN", "열정이 넘치는 플레이어입니다.", "플레이 40회 완료", 1),

        // Star Player (Tier 2) - 25 achievements
        new("clean_finish", "CLEAN FINISH", "실수 없이 한 판을 마무리했습니다.", "미스 없이 8노트 이상 처리", 2),
        new("st_play_50", "HALF CENTURY", "50회 플레이를 달성했습니다.", "플레이 50회 완료", 2),
        new("st_score_50000", "ELITE SCORER", "엘리트 점수에 도달했습니다.", "최고 점수 50,000 달성", 2),
        new("st_score_80000", "SCORE KING", "놀라운 점수를 기록했습니다.", "최고 점수 80,000 달성", 2),
        new("st_score_100000", "HUNDRED K", "10만 점의 벽을 넘었습니다.", "최고 점수 100,000 달성", 2),
        new("st_combo_50", "COMBO WARRIOR", "50 콤보를 달성했습니다.", "최고 콤보 50 달성", 2),
        new("st_combo_75", "CHAIN LEGEND", "긴 체인을 만들어냅니다.", "최고 콤보 75 달성", 2),
        new("st_combo_100", "COMBO CENTURION", "100 콤보의 전설입니다.", "최고 콤보 100 달성", 2),
        new("st_perfect_100", "PERFECT HUNTER", "누적 PERFECT 100회를 넘었습니다.", "누적 PERFECT 100회 달성", 2),
        new("st_perfect_150", "PRECISION MASTER", "정밀 타격의 마스터입니다.", "누적 PERFECT 150회 달성", 2),
        new("st_perfect_200", "PERFECT LEGEND", "PERFECT의 전설입니다.", "누적 PERFECT 200회 달성", 2),
        new("st_total_score_100000", "GOLD VAULT", "누적 점수가 10만을 넘었습니다.", "누적 점수 100,000 달성", 2),
        new("st_total_score_200000", "DIAMOND VAULT", "누적 점수가 20만을 넘었습니다.", "누적 점수 200,000 달성", 2),
        new("st_total_notes_500", "NOTE MASTER", "500개의 노트를 처리했습니다.", "누적 노트 처리 500회", 2),
        new("st_total_notes_1000", "RHYTHM MACHINE", "1,000개의 노트를 처리했습니다.", "누적 노트 처리 1,000회", 2),
        new("st_missless_5", "FLAWLESS FIVE", "미스 없이 5회 클리어했습니다.", "미스 없이 클리어 5회", 2),
        new("st_missless_10", "PERFECT RUN", "미스 없이 10회 클리어했습니다.", "미스 없이 클리어 10회", 2),
        new("st_play_75", "MARATHON", "75회 플레이를 달성했습니다.", "플레이 75회 완료", 2),
        new("st_play_100", "CENTURION", "100회 플레이의 전설입니다.", "플레이 100회 완료", 2),
        new("st_score_150000", "SCORE EMPEROR", "15만 점을 돌파했습니다.", "최고 점수 150,000 달성", 2),
        new("st_combo_150", "UNSTOPPABLE", "150 콤보를 달성했습니다.", "최고 콤보 150 달성", 2),
        new("st_perfect_300", "GODLIKE", "누적 PERFECT 300회를 넘었습니다.", "누적 PERFECT 300회 달성", 2),
        new("st_total_score_500000", "MILLIONAIRE", "누적 점수가 50만을 넘었습니다.", "누적 점수 500,000 달성", 2),
        new("st_total_notes_2000", "RHYTHM GOD", "2,000개의 노트를 처리했습니다.", "누적 노트 처리 2,000회", 2),
        new("st_missless_20", "ABSOLUTE ZERO", "미스 없이 20회 클리어했습니다.", "미스 없이 클리어 20회", 2),
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

        if (session.MissCount == 0 && session.TotalHitCount >= 8)
            progress.MisslessRunsCount++;

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
            "first_stage" => $"{Math.Min(progress.TotalGamesPlayed, 1)}/1",
            "combo_apprentice" => $"{Math.Min(progress.BestCombo, 10)}/10",
            "b_play_3" => $"{Math.Min(progress.TotalGamesPlayed, 3)}/3",
            "b_play_5" => $"{Math.Min(progress.TotalGamesPlayed, 5)}/5",
            "b_score_5000" => $"{Math.Min(progress.HighestScore, 5000):N0}/5,000",
            "b_score_10000" => $"{Math.Min(progress.HighestScore, 10000):N0}/10,000",
            "b_perfect_5" => $"{Math.Min(progress.TotalPerfectCount, 5)}/5",
            "b_perfect_10" => $"{Math.Min(progress.TotalPerfectCount, 10)}/10",
            "b_combo_5" => $"{Math.Min(progress.BestCombo, 5)}/5",
            "b_good_5" => $"{Math.Min(progress.TotalGoodCount, 5)}/5",
            "b_good_10" => $"{Math.Min(progress.TotalGoodCount, 10)}/10",
            "b_total_score_5000" => $"{Math.Min(progress.TotalScore, 5000):N0}/5,000",
            "b_total_score_10000" => $"{Math.Min(progress.TotalScore, 10000):N0}/10,000",
            "b_total_notes_20" => $"{Math.Min(progress.TotalNotesProcessed, 20)}/20",
            "b_total_notes_50" => $"{Math.Min(progress.TotalNotesProcessed, 50)}/50",
            "b_play_10" => $"{Math.Min(progress.TotalGamesPlayed, 10)}/10",
            "b_score_15000" => $"{Math.Min(progress.HighestScore, 15000):N0}/15,000",
            "b_combo_15" => $"{Math.Min(progress.BestCombo, 15)}/15",
            "b_perfect_20" => $"{Math.Min(progress.TotalPerfectCount, 20)}/20",
            "b_good_20" => $"{Math.Min(progress.TotalGoodCount, 20)}/20",

            "score_chaser" => $"{Math.Min(progress.HighestScore, 12000):N0}/12,000",
            "perfect_collector" => $"{Math.Min(progress.TotalPerfectCount, 15)}/15",
            "s_play_15" => $"{Math.Min(progress.TotalGamesPlayed, 15)}/15",
            "s_play_20" => $"{Math.Min(progress.TotalGamesPlayed, 20)}/20",
            "s_score_20000" => $"{Math.Min(progress.HighestScore, 20000):N0}/20,000",
            "s_score_30000" => $"{Math.Min(progress.HighestScore, 30000):N0}/30,000",
            "s_combo_20" => $"{Math.Min(progress.BestCombo, 20)}/20",
            "s_combo_30" => $"{Math.Min(progress.BestCombo, 30)}/30",
            "s_perfect_30" => $"{Math.Min(progress.TotalPerfectCount, 30)}/30",
            "s_perfect_50" => $"{Math.Min(progress.TotalPerfectCount, 50)}/50",
            "s_total_score_30000" => $"{Math.Min(progress.TotalScore, 30000):N0}/30,000",
            "s_total_score_50000" => $"{Math.Min(progress.TotalScore, 50000):N0}/50,000",
            "s_total_notes_100" => $"{Math.Min(progress.TotalNotesProcessed, 100)}/100",
            "s_total_notes_200" => $"{Math.Min(progress.TotalNotesProcessed, 200)}/200",
            "s_good_30" => $"{Math.Min(progress.TotalGoodCount, 30)}/30",
            "s_good_50" => $"{Math.Min(progress.TotalGoodCount, 50)}/50",
            "s_missless_2" => $"{Math.Min(progress.MisslessRunsCount, 2)}/2",
            "s_missless_3" => $"{Math.Min(progress.MisslessRunsCount, 3)}/3",
            "s_play_30" => $"{Math.Min(progress.TotalGamesPlayed, 30)}/30",
            "s_score_40000" => $"{Math.Min(progress.HighestScore, 40000):N0}/40,000",
            "s_combo_40" => $"{Math.Min(progress.BestCombo, 40)}/40",
            "s_perfect_75" => $"{Math.Min(progress.TotalPerfectCount, 75)}/75",
            "s_total_score_80000" => $"{Math.Min(progress.TotalScore, 80000):N0}/80,000",
            "s_total_notes_300" => $"{Math.Min(progress.TotalNotesProcessed, 300)}/300",
            "s_play_40" => $"{Math.Min(progress.TotalGamesPlayed, 40)}/40",

            "clean_finish" => $"{Math.Min(progress.MisslessRunsCount, 1)}/1",
            "st_play_50" => $"{Math.Min(progress.TotalGamesPlayed, 50)}/50",
            "st_score_50000" => $"{Math.Min(progress.HighestScore, 50000):N0}/50,000",
            "st_score_80000" => $"{Math.Min(progress.HighestScore, 80000):N0}/80,000",
            "st_score_100000" => $"{Math.Min(progress.HighestScore, 100000):N0}/100,000",
            "st_combo_50" => $"{Math.Min(progress.BestCombo, 50)}/50",
            "st_combo_75" => $"{Math.Min(progress.BestCombo, 75)}/75",
            "st_combo_100" => $"{Math.Min(progress.BestCombo, 100)}/100",
            "st_perfect_100" => $"{Math.Min(progress.TotalPerfectCount, 100)}/100",
            "st_perfect_150" => $"{Math.Min(progress.TotalPerfectCount, 150)}/150",
            "st_perfect_200" => $"{Math.Min(progress.TotalPerfectCount, 200)}/200",
            "st_total_score_100000" => $"{Math.Min(progress.TotalScore, 100000):N0}/100,000",
            "st_total_score_200000" => $"{Math.Min(progress.TotalScore, 200000):N0}/200,000",
            "st_total_notes_500" => $"{Math.Min(progress.TotalNotesProcessed, 500)}/500",
            "st_total_notes_1000" => $"{Math.Min(progress.TotalNotesProcessed, 1000):N0}/1,000",
            "st_missless_5" => $"{Math.Min(progress.MisslessRunsCount, 5)}/5",
            "st_missless_10" => $"{Math.Min(progress.MisslessRunsCount, 10)}/10",
            "st_play_75" => $"{Math.Min(progress.TotalGamesPlayed, 75)}/75",
            "st_play_100" => $"{Math.Min(progress.TotalGamesPlayed, 100)}/100",
            "st_score_150000" => $"{Math.Min(progress.HighestScore, 150000):N0}/150,000",
            "st_combo_150" => $"{Math.Min(progress.BestCombo, 150)}/150",
            "st_perfect_300" => $"{Math.Min(progress.TotalPerfectCount, 300)}/300",
            "st_total_score_500000" => $"{Math.Min(progress.TotalScore, 500000):N0}/500,000",
            "st_total_notes_2000" => $"{Math.Min(progress.TotalNotesProcessed, 2000):N0}/2,000",
            "st_missless_20" => $"{Math.Min(progress.MisslessRunsCount, 20)}/20",
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
            "b_play_5" => progress.TotalGamesPlayed >= 5,
            "b_score_5000" => progress.HighestScore >= 5000,
            "b_score_10000" => progress.HighestScore >= 10000,
            "b_perfect_5" => progress.TotalPerfectCount >= 5,
            "b_perfect_10" => progress.TotalPerfectCount >= 10,
            "b_combo_5" => progress.BestCombo >= 5,
            "b_good_5" => progress.TotalGoodCount >= 5,
            "b_good_10" => progress.TotalGoodCount >= 10,
            "b_total_score_5000" => progress.TotalScore >= 5000,
            "b_total_score_10000" => progress.TotalScore >= 10000,
            "b_total_notes_20" => progress.TotalNotesProcessed >= 20,
            "b_total_notes_50" => progress.TotalNotesProcessed >= 50,
            "b_play_10" => progress.TotalGamesPlayed >= 10,
            "b_score_15000" => progress.HighestScore >= 15000,
            "b_combo_15" => progress.BestCombo >= 15,
            "b_perfect_20" => progress.TotalPerfectCount >= 20,
            "b_good_20" => progress.TotalGoodCount >= 20,

            "score_chaser" => progress.HighestScore >= 12000,
            "perfect_collector" => progress.TotalPerfectCount >= 15,
            "s_play_15" => progress.TotalGamesPlayed >= 15,
            "s_play_20" => progress.TotalGamesPlayed >= 20,
            "s_score_20000" => progress.HighestScore >= 20000,
            "s_score_30000" => progress.HighestScore >= 30000,
            "s_combo_20" => progress.BestCombo >= 20,
            "s_combo_30" => progress.BestCombo >= 30,
            "s_perfect_30" => progress.TotalPerfectCount >= 30,
            "s_perfect_50" => progress.TotalPerfectCount >= 50,
            "s_total_score_30000" => progress.TotalScore >= 30000,
            "s_total_score_50000" => progress.TotalScore >= 50000,
            "s_total_notes_100" => progress.TotalNotesProcessed >= 100,
            "s_total_notes_200" => progress.TotalNotesProcessed >= 200,
            "s_good_30" => progress.TotalGoodCount >= 30,
            "s_good_50" => progress.TotalGoodCount >= 50,
            "s_missless_2" => progress.MisslessRunsCount >= 2,
            "s_missless_3" => progress.MisslessRunsCount >= 3,
            "s_play_30" => progress.TotalGamesPlayed >= 30,
            "s_score_40000" => progress.HighestScore >= 40000,
            "s_combo_40" => progress.BestCombo >= 40,
            "s_perfect_75" => progress.TotalPerfectCount >= 75,
            "s_total_score_80000" => progress.TotalScore >= 80000,
            "s_total_notes_300" => progress.TotalNotesProcessed >= 300,
            "s_play_40" => progress.TotalGamesPlayed >= 40,

            "clean_finish" => progress.MisslessRunsCount >= 1,
            "st_play_50" => progress.TotalGamesPlayed >= 50,
            "st_score_50000" => progress.HighestScore >= 50000,
            "st_score_80000" => progress.HighestScore >= 80000,
            "st_score_100000" => progress.HighestScore >= 100000,
            "st_combo_50" => progress.BestCombo >= 50,
            "st_combo_75" => progress.BestCombo >= 75,
            "st_combo_100" => progress.BestCombo >= 100,
            "st_perfect_100" => progress.TotalPerfectCount >= 100,
            "st_perfect_150" => progress.TotalPerfectCount >= 150,
            "st_perfect_200" => progress.TotalPerfectCount >= 200,
            "st_total_score_100000" => progress.TotalScore >= 100000,
            "st_total_score_200000" => progress.TotalScore >= 200000,
            "st_total_notes_500" => progress.TotalNotesProcessed >= 500,
            "st_total_notes_1000" => progress.TotalNotesProcessed >= 1000,
            "st_missless_5" => progress.MisslessRunsCount >= 5,
            "st_missless_10" => progress.MisslessRunsCount >= 10,
            "st_play_75" => progress.TotalGamesPlayed >= 75,
            "st_play_100" => progress.TotalGamesPlayed >= 100,
            "st_score_150000" => progress.HighestScore >= 150000,
            "st_combo_150" => progress.BestCombo >= 150,
            "st_perfect_300" => progress.TotalPerfectCount >= 300,
            "st_total_score_500000" => progress.TotalScore >= 500000,
            "st_total_notes_2000" => progress.TotalNotesProcessed >= 2000,
            "st_missless_20" => progress.MisslessRunsCount >= 20,
            _ => false,
        };
    }
}

internal sealed class AchievementProgressStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string _saveFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RhythmGame",
        "player_progress.json");

    public PlayerProgress Load()
    {
        try
        {
            if (!File.Exists(_saveFilePath))
                return new PlayerProgress();

            string json = File.ReadAllText(_saveFilePath);
            return JsonSerializer.Deserialize<PlayerProgress>(json, JsonOptions) ?? new PlayerProgress();
        }
        catch
        {
            return new PlayerProgress();
        }
    }

    public void Save(PlayerProgress progress)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_saveFilePath)!);
        string json = JsonSerializer.Serialize(progress, JsonOptions);
        File.WriteAllText(_saveFilePath, json);
    }
}