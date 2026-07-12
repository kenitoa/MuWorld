namespace RhythmGame;

public enum ChartDiagnosticSeverity
{
    Info,
    Warning,
    Error
}

public sealed record ChartDiagnostic(ChartDiagnosticSeverity Severity, string Message, int LineNumber = 0);

public sealed record ChartValidationResult(
    IReadOnlyList<LaneNote> Notes,
    IReadOnlyList<ChartDiagnostic> Diagnostics,
    ChartDifficultyInfo Difficulty);

public sealed record ChartDifficultyInfo(
    int Level,
    float NotesPerSecond,
    float ChordRatio,
    float JackRatio,
    float LongRatio,
    float SlideRatio,
    float HandMovement);

internal static class ChartValidator
{
    public const float MinTapGapSeconds = 0.085f;
    private const float MinHoldGapSeconds = 0.045f;
    private const float ChordWindowSeconds = 0.035f;

    public static ChartValidationResult ValidateAndFilter(
        IReadOnlyList<LaneNote> source,
        int laneCount,
        IEnumerable<ChartDiagnostic>? parserDiagnostics = null)
    {
        laneCount = Math.Clamp(laneCount, 4, 7);
        var diagnostics = new List<ChartDiagnostic>();
        if (parserDiagnostics is not null)
            diagnostics.AddRange(parserDiagnostics);

        var accepted = new List<LaneNote>(source.Count);
        float[] laneBlockedUntil = Enumerable.Repeat(float.NegativeInfinity, laneCount).ToArray();
        int chordRunCount = 0;
        float currentChordTime = float.NegativeInfinity;

        foreach (LaneNote note in source.OrderBy(n => n.Time).ThenBy(n => n.Lane))
        {
            if (!float.IsFinite(note.Time) || note.Time < 0f)
            {
                diagnostics.Add(new ChartDiagnostic(ChartDiagnosticSeverity.Warning, $"Skipped note with invalid time {note.Time}."));
                continue;
            }

            if (!Enum.IsDefined(note.Type))
            {
                diagnostics.Add(new ChartDiagnostic(ChartDiagnosticSeverity.Warning, $"Skipped note with invalid type value {(int)note.Type}."));
                continue;
            }

            if (!float.IsFinite(note.Duration) || note.Duration < 0f)
            {
                diagnostics.Add(new ChartDiagnostic(ChartDiagnosticSeverity.Warning, $"Skipped note with invalid duration {note.Duration}."));
                continue;
            }

            if (note.Lane < 0 || note.Lane >= laneCount)
            {
                diagnostics.Add(new ChartDiagnostic(ChartDiagnosticSeverity.Warning, $"Skipped note on invalid lane {note.Lane + 1}."));
                continue;
            }

            int lane = note.Lane;
            int endLane = note.EndLane >= 0 ? note.EndLane : lane;
            if (endLane < 0 || endLane >= laneCount)
            {
                diagnostics.Add(new ChartDiagnostic(ChartDiagnosticSeverity.Warning, $"Slide end lane {endLane + 1} is outside {laneCount}K."));
                continue;
            }

            if (MathF.Abs(note.Time - currentChordTime) <= ChordWindowSeconds)
                chordRunCount++;
            else
            {
                currentChordTime = note.Time;
                chordRunCount = 1;
            }

            if (chordRunCount > laneCount)
            {
                diagnostics.Add(new ChartDiagnostic(ChartDiagnosticSeverity.Warning, $"Skipped chord larger than {laneCount} notes at {note.Time:F2}s."));
                continue;
            }

            float duration = Math.Max(0f, note.Duration);
            float clearTime = note.Type == NoteType.Tap
                ? note.Time + MinTapGapSeconds
                : note.Time + duration + MinHoldGapSeconds;

            if (note.Time < laneBlockedUntil[lane])
            {
                diagnostics.Add(new ChartDiagnostic(ChartDiagnosticSeverity.Warning, $"Skipped overlapping note on lane {lane + 1} at {note.Time:F2}s."));
                continue;
            }

            if (note.Type == NoteType.Slide && endLane != lane && note.Time < laneBlockedUntil[endLane])
            {
                diagnostics.Add(new ChartDiagnostic(ChartDiagnosticSeverity.Warning, $"Skipped slide ending into occupied lane {endLane + 1} at {note.Time:F2}s."));
                continue;
            }

            accepted.Add(new LaneNote(note.Time, lane, note.Type, duration, endLane));
            laneBlockedUntil[lane] = clearTime;
            if (note.Type == NoteType.Slide && endLane != lane)
                laneBlockedUntil[endLane] = Math.Max(laneBlockedUntil[endLane], note.Time + duration + MinHoldGapSeconds);
        }

        return new ChartValidationResult(accepted, diagnostics, Analyze(accepted, laneCount));
    }

    public static ChartDifficultyInfo Analyze(IReadOnlyList<LaneNote> notes, int laneCount)
    {
        if (notes.Count == 0)
            return new ChartDifficultyInfo(1, 0f, 0f, 0f, 0f, 0f, 0f);

        float duration = Math.Max(1f, notes.Max(n => n.Time + Math.Max(0f, n.Duration)) - notes.Min(n => n.Time));
        float nps = notes.Count / duration;
        int chordNotes = 0;
        int jacks = 0;
        float movement = 0f;
        int longCount = 0;
        int slideCount = 0;

        List<LaneNote> ordered = notes.OrderBy(n => n.Time).ThenBy(n => n.Lane).ToList();
        int chordLeft = 0;
        int chordRight = 0;
        for (int i = 0; i < ordered.Count; i++)
        {
            while (ordered[i].Time - ordered[chordLeft].Time > ChordWindowSeconds)
                chordLeft++;
            chordRight = Math.Max(chordRight, i + 1);
            while (chordRight < ordered.Count && ordered[chordRight].Time - ordered[i].Time <= ChordWindowSeconds)
                chordRight++;
            if (chordRight - chordLeft > 1)
                chordNotes++;
        }

        LaneNote? previous = null;
        foreach (LaneNote note in ordered)
        {
            if (previous is LaneNote prev)
            {
                if (prev.Lane == note.Lane && note.Time - prev.Time <= 0.22f)
                    jacks++;
                movement += MathF.Abs(note.Lane - prev.Lane) / Math.Max(1f, laneCount - 1);
            }

            if (note.Type == NoteType.Long)
                longCount++;
            else if (note.Type == NoteType.Slide)
                slideCount++;

            previous = note;
        }

        float chordRatio = chordNotes / (float)notes.Count;
        float jackRatio = jacks / (float)Math.Max(1, notes.Count - 1);
        float longRatio = longCount / (float)notes.Count;
        float slideRatio = slideCount / (float)notes.Count;
        float handMovement = movement / Math.Max(1, notes.Count - 1);
        float raw =
            nps * 1.15f +
            chordRatio * 5.0f +
            jackRatio * 4.0f +
            longRatio * 1.8f +
            slideRatio * 2.2f +
            handMovement * 2.0f;
        int level = Math.Clamp((int)MathF.Round(raw), 1, 15);

        return new ChartDifficultyInfo(level, nps, chordRatio, jackRatio, longRatio, slideRatio, handMovement);
    }
}
