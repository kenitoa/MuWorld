namespace RhythmGame;

public enum Judgment : byte
{
    Perfect,
    Great,
    Better,
    Good,
    Bad
}

public enum ResultGrade : byte
{
    SPlus,
    S,
    A,
    B,
    C,
    D,
    F
}

public enum ClearType : byte
{
    Failed,
    Clear,
    FullCombo,
    AllGreatPlus,
    Perfect
}

public class ScoreManager
{
    private const int MaxNormalizedScore = 1_000_000;
    private const int MaxHoldTickBonus = 25_000;

    private int _holdTickBonus;

    public int Score
    {
        get
        {
            int baseScore = CalculateNormalizedScore(
                PerfectCount,
                GreatCount,
                BetterCount,
                GoodCount,
                BadCount,
                MissCount);

            if (baseScore >= MaxNormalizedScore)
                return MaxNormalizedScore;

            return Math.Min(MaxNormalizedScore - 1, baseScore + _holdTickBonus);
        }
    }

    public int Combo { get; private set; }
    public int MaxCombo { get; private set; }
    public int PerfectCount { get; private set; }
    public int GreatCount { get; private set; }
    public int BetterCount { get; private set; }
    public int GoodCount { get; private set; }
    public int BadCount { get; private set; }
    public int MissCount { get; private set; }
    public int CurrentMissStreak { get; private set; }
    public int MaxMissStreak { get; private set; }
    public int EarlyCount { get; private set; }
    public int LateCount { get; private set; }
    public float TimingOffsetSumSeconds { get; private set; }
    public int TotalJudgedNotes => HitCount + MissCount;
    public float Accuracy => CalculateWeightedAccuracy(
        PerfectCount,
        GreatCount,
        BetterCount,
        GoodCount,
        BadCount,
        MissCount);
    public ClearType ClearType => CalculateClearType(
        PerfectCount,
        GreatCount,
        BetterCount,
        GoodCount,
        BadCount,
        MissCount);
    public ResultGrade Grade => CalculateGrade(Accuracy, MissCount, MaxCombo, ClearType);
    public float AverageTimingOffsetSeconds => HitCount > 0
        ? TimingOffsetSumSeconds / HitCount
        : 0f;

    private int HitCount => PerfectCount + GreatCount + BetterCount + GoodCount + BadCount;

    public void AddHit(Judgment judgment, float signedOffsetSeconds = 0f)
    {
        Combo++;
        CurrentMissStreak = 0;
        if (Combo > MaxCombo) MaxCombo = Combo;

        TimingOffsetSumSeconds += signedOffsetSeconds;
        if (signedOffsetSeconds < -0.003f)
            EarlyCount++;
        else if (signedOffsetSeconds > 0.003f)
            LateCount++;

        switch (judgment)
        {
            case Judgment.Perfect: PerfectCount++; break;
            case Judgment.Great: GreatCount++; break;
            case Judgment.Better: BetterCount++; break;
            case Judgment.Good: GoodCount++; break;
            case Judgment.Bad: BadCount++; break;
        }
    }

    public void AddMiss()
    {
        MissCount++;
        CurrentMissStreak++;
        if (CurrentMissStreak > MaxMissStreak) MaxMissStreak = CurrentMissStreak;
        Combo = 0;
    }

    public void AddHoldTick()
    {
        _holdTickBonus = Math.Min(MaxHoldTickBonus, _holdTickBonus + Math.Max(1, Combo) * 4);
    }

    public void Reset()
    {
        _holdTickBonus = 0;
        Combo = 0;
        MaxCombo = 0;
        PerfectCount = 0;
        GreatCount = 0;
        BetterCount = 0;
        GoodCount = 0;
        BadCount = 0;
        MissCount = 0;
        CurrentMissStreak = 0;
        MaxMissStreak = 0;
        EarlyCount = 0;
        LateCount = 0;
        TimingOffsetSumSeconds = 0f;
    }

    public static float GetJudgmentWeight(Judgment judgment)
    {
        return judgment switch
        {
            Judgment.Perfect => 1f,
            Judgment.Great => 0.9f,
            Judgment.Better => 0.75f,
            Judgment.Good => 0.5f,
            Judgment.Bad => 0.25f,
            _ => 0f,
        };
    }

    public static float CalculateWeightedAccuracy(
        int perfect,
        int great,
        int better,
        int good,
        int bad,
        int miss)
    {
        int total = perfect + great + better + good + bad + miss;
        if (total <= 0)
            return 100f;

        float weighted =
            perfect * GetJudgmentWeight(Judgment.Perfect) +
            great * GetJudgmentWeight(Judgment.Great) +
            better * GetJudgmentWeight(Judgment.Better) +
            good * GetJudgmentWeight(Judgment.Good) +
            bad * GetJudgmentWeight(Judgment.Bad);

        return weighted * 100f / total;
    }

    public static int CalculateNormalizedScore(
        int perfect,
        int great,
        int better,
        int good,
        int bad,
        int miss)
    {
        int total = perfect + great + better + good + bad + miss;
        if (total <= 0)
            return 0;

        float accuracy = CalculateWeightedAccuracy(perfect, great, better, good, bad, miss) / 100f;
        return Math.Clamp((int)MathF.Round(accuracy * MaxNormalizedScore), 0, MaxNormalizedScore);
    }

    public static ClearType CalculateClearType(
        int perfect,
        int great,
        int better,
        int good,
        int bad,
        int miss)
    {
        int total = perfect + great + better + good + bad + miss;
        if (total <= 0)
            return ClearType.Failed;

        float accuracy = CalculateWeightedAccuracy(perfect, great, better, good, bad, miss);
        if (accuracy < 60f)
            return ClearType.Failed;

        if (miss == 0 && great == 0 && better == 0 && good == 0 && bad == 0)
            return ClearType.Perfect;

        if (miss == 0 && better == 0 && good == 0 && bad == 0)
            return ClearType.AllGreatPlus;

        if (miss == 0)
            return ClearType.FullCombo;

        return ClearType.Clear;
    }

    public static ResultGrade CalculateGrade(float accuracy, int missCount, int maxCombo, ClearType clearType)
    {
        if (maxCombo <= 0 || clearType == ClearType.Failed || accuracy < 60f)
            return ResultGrade.F;

        if (accuracy >= 99.5f && missCount == 0 && clearType >= ClearType.AllGreatPlus)
            return ResultGrade.SPlus;

        if (accuracy >= 95f && missCount <= 3)
            return ResultGrade.S;

        if (accuracy >= 90f)
            return ResultGrade.A;

        if (accuracy >= 80f)
            return ResultGrade.B;

        if (accuracy >= 70f)
            return ResultGrade.C;

        return ResultGrade.D;
    }

    public static string FormatGrade(ResultGrade grade)
    {
        return grade == ResultGrade.SPlus ? "S+" : grade.ToString();
    }

    public static string FormatClearType(ClearType clearType)
    {
        return clearType switch
        {
            ClearType.AllGreatPlus => "All Great+",
            ClearType.FullCombo => "Full Combo",
            _ => clearType.ToString(),
        };
    }
}
