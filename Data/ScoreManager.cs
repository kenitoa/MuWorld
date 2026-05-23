namespace RhythmGame;

public enum Judgment : byte
{
    Perfect,
    Great,
    Better,
    Good,
    Bad
}

public class ScoreManager
{
    // 판정별 점수 (배열 인덱스 = Judgment enum 값)
    private static readonly int[] JudgmentScores = [300, 250, 150, 100, 50];

    public int Score        { get; private set; }
    public int Combo        { get; private set; }
    public int MaxCombo     { get; private set; }
    public int PerfectCount { get; private set; }
    public int GreatCount   { get; private set; }
    public int BetterCount  { get; private set; }
    public int GoodCount    { get; private set; }
    public int BadCount     { get; private set; }
    public int MissCount    { get; private set; }
    public int CurrentMissStreak { get; private set; }
    public int MaxMissStreak     { get; private set; }

    public void AddHit(Judgment judgment)
    {
        Combo++;
        CurrentMissStreak = 0;
        if (Combo > MaxCombo) MaxCombo = Combo;

        Score += JudgmentScores[(int)judgment] * Combo;

        switch (judgment)
        {
            case Judgment.Perfect: PerfectCount++; break;
            case Judgment.Great:   GreatCount++;   break;
            case Judgment.Better:  BetterCount++;  break;
            case Judgment.Good:    GoodCount++;    break;
            case Judgment.Bad:     BadCount++;     break;
        }
    }

    public void AddMiss()
    {
        MissCount++;
        CurrentMissStreak++;
        if (CurrentMissStreak > MaxMissStreak) MaxMissStreak = CurrentMissStreak;
        Combo = 0;
    }

    public void Reset()
    {
        Score        = 0;
        Combo        = 0;
        MaxCombo     = 0;
        PerfectCount = 0;
        GreatCount   = 0;
        BetterCount  = 0;
        GoodCount    = 0;
        BadCount     = 0;
        MissCount    = 0;
        CurrentMissStreak = 0;
        MaxMissStreak = 0;
    }
}
