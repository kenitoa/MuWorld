namespace RhythmGame;

internal sealed record ResultFeedbackSummary(
    string TimingLabel,
    string NextGoal,
    string FailureLabel,
    string FailureCompactLabel,
    float[] MissPositions,
    int RecordedMissCount)
{
    public static ResultFeedbackSummary Create(
        ScoreManager score,
        IReadOnlyList<NoteJudgmentEvent> events,
        float chartStartTime,
        float chartEndTime)
    {
        ArgumentNullException.ThrowIfNull(score);
        events ??= [];

        string timingLabel = BuildTimingLabel(score);
        NoteJudgmentEvent[] misses = events.Where(item => item.IsMiss).ToArray();
        (string failureLabel, string failureCompactLabel) = BuildFailureLabels(misses);
        float[] missPositions = BuildMissPositions(misses, chartStartTime, chartEndTime);
        string nextGoal = BuildNextGoal(score, misses, timingLabel, chartStartTime, chartEndTime);
        return new ResultFeedbackSummary(timingLabel, nextGoal, failureLabel, failureCompactLabel, missPositions, misses.Length);
    }

    private static string BuildTimingLabel(ScoreManager score)
    {
        int hits = score.PerfectCount + score.GreatCount + score.BetterCount + score.GoodCount + score.BadCount;
        if (hits == 0)
            return "NO TIMING DATA";

        int averageMs = (int)MathF.Round(score.AverageTimingOffsetSeconds * 1000f);
        int imbalance = score.LateCount - score.EarlyCount;
        int balanceTolerance = Math.Max(2, (int)MathF.Ceiling(hits * 0.12f));

        if (averageMs < -5)
            return $"EARLY {Math.Abs(averageMs)}ms";

        if (averageMs > 5)
            return $"LATE {Math.Abs(averageMs)}ms";

        if (imbalance < -balanceTolerance)
            return "EARLY BIAS";

        if (imbalance > balanceTolerance)
            return "LATE BIAS";

        return $"STABLE {FormatSignedMilliseconds(averageMs)}";
    }

    private static (string Full, string Compact) BuildFailureLabels(IReadOnlyList<NoteJudgmentEvent> misses)
    {
        if (misses.Count == 0)
            return ("NO MISS BREAKS", "NO MISS");

        int tap = misses.Count(item => item.Phase == NoteJudgmentPhase.Tap);
        int start = misses.Count(item => item.Phase == NoteJudgmentPhase.Start);
        int hold = misses.Count(item => item.Phase == NoteJudgmentPhase.Hold);
        int end = misses.Count(item => item.Phase == NoteJudgmentPhase.End);
        return (
            $"TAP {tap}  START {start}  HOLD {hold}  END {end}",
            $"T{tap}  S{start}  H{hold}  E{end}");
    }

    private static float[] BuildMissPositions(
        IReadOnlyList<NoteJudgmentEvent> misses,
        float chartStartTime,
        float chartEndTime)
    {
        float safeStart = float.IsFinite(chartStartTime) ? chartStartTime : 0f;
        float safeEnd = float.IsFinite(chartEndTime) ? chartEndTime : safeStart + 1f;
        float duration = Math.Max(0.001f, safeEnd - safeStart);

        NoteJudgmentEvent[] ordered = misses.OrderBy(item => item.ChartTime).ToArray();
        if (ordered.Length <= 256)
        {
            return ordered
                .Select(item => Math.Clamp((item.ChartTime - safeStart) / duration, 0f, 1f))
                .ToArray();
        }

        // Preserve the entire song range for extreme failure cases. Taking only
        // the first 256 misses made the rail falsely look like every miss happened
        // near the beginning of a dense chart.
        var sampled = new float[256];
        for (int i = 0; i < sampled.Length; i++)
        {
            int sourceIndex = (int)MathF.Round(i * (ordered.Length - 1f) / (sampled.Length - 1f));
            sampled[i] = Math.Clamp((ordered[sourceIndex].ChartTime - safeStart) / duration, 0f, 1f);
        }

        return sampled;
    }

    private static string BuildNextGoal(
        ScoreManager score,
        IReadOnlyList<NoteJudgmentEvent> misses,
        string timingLabel,
        float chartStartTime,
        float chartEndTime)
    {
        int holdBreaks = misses.Count(item => item.FailureReason is NoteFailureReason.LongHoldBreak or NoteFailureReason.SlidePathBreak);
        if (holdBreaks > 0)
            return "NEXT: KEEP HOLD / SLIDE LANES PRESSED";

        int endMisses = misses.Count(item => item.FailureReason is NoteFailureReason.LongEndMiss or NoteFailureReason.SlideEndMiss);
        if (endMisses > 0)
            return "NEXT: RE-PRESS LANES BEFORE NOTE ENDS";

        string cluster = BuildMissClusterLabel(misses, chartStartTime, chartEndTime);
        if (cluster.Length > 0)
            return $"NEXT: REVIEW MISS CLUSTER {cluster}";

        if (timingLabel.StartsWith("EARLY", StringComparison.Ordinal) || timingLabel.StartsWith("LATE", StringComparison.Ordinal))
            return "NEXT: CALIBRATE OR ADJUST INPUT TIMING";

        if (score.BadCount + score.GoodCount + score.BetterCount > 0)
            return "NEXT: TURN GOOD / BETTER NOTES INTO GREAT+";

        if (score.Accuracy >= 99.5f && score.MissCount == 0)
            return "NEXT: TRY A HARDER CHART";

        if (score.MissCount > 0)
            return "NEXT: REDUCE THE LONGEST MISS STREAK";

        return "NEXT: KEEP THE COMBO STABLE";
    }

    private static string BuildMissClusterLabel(
        IReadOnlyList<NoteJudgmentEvent> misses,
        float chartStartTime,
        float chartEndTime)
    {
        if (misses.Count < 2 || !float.IsFinite(chartStartTime) || !float.IsFinite(chartEndTime) || chartEndTime <= chartStartTime)
            return string.Empty;

        const int binCount = 12;
        int[] bins = new int[binCount];
        float duration = chartEndTime - chartStartTime;
        foreach (NoteJudgmentEvent miss in misses)
        {
            int bin = Math.Clamp((int)((miss.ChartTime - chartStartTime) / duration * binCount), 0, binCount - 1);
            bins[bin]++;
        }

        int peak = Array.IndexOf(bins, bins.Max());
        if (peak < 0 || bins[peak] < 2)
            return string.Empty;

        float centerSeconds = Math.Max(0f, chartStartTime + (peak + 0.5f) / binCount * duration - chartStartTime);
        int totalSeconds = (int)MathF.Round(centerSeconds);
        return $"{totalSeconds / 60:00}:{totalSeconds % 60:00}";
    }

    private static string FormatSignedMilliseconds(int value)
    {
        return value switch
        {
            > 0 => $"+{value}ms",
            < 0 => $"-{Math.Abs(value)}ms",
            _ => "0ms",
        };
    }
}
