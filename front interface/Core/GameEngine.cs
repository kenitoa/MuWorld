namespace RhythmGame;

public enum NoteJudgmentPhase : byte
{
    Tap,
    Start,
    Hold,
    End,
}

public enum NoteFailureReason : byte
{
    None,
    TapMiss,
    LongStartMiss,
    LongHoldBreak,
    LongEndMiss,
    SlideStartMiss,
    SlidePathBreak,
    SlideEndMiss,
}

public readonly record struct NoteJudgmentEvent(
    float ChartTime,
    float TargetTime,
    int Lane,
    int EndLane,
    NoteType NoteType,
    NoteJudgmentPhase Phase,
    Judgment? Judgment,
    NoteFailureReason FailureReason,
    float OffsetSeconds)
{
    public bool IsMiss => FailureReason != NoteFailureReason.None;
}

public class GameEngine
{
    public const float GameplayDesignHeight = 1080f;
    public const float HitZoneOffset = 130f;
    public const float PerfectWindow = 0.030f;
    public const float GreatWindow   = 0.060f;
    public const float BetterWindow  = 0.090f;
    public const float GoodWindow    = 0.120f;
    public const float BadWindow     = 0.150f;
    private const float MissThreshold = 0.180f;
    private const float HoldTickInterval = 0.25f;
    private const float ChordTimeWindow = 0.035f;

    public List<Note>   Notes     { get; } = [];
    public ScoreManager Score     { get; } = new();
    public bool         IsRunning { get; private set; }
    public float        NoteSpeedMultiplier { get; set; } = 1f;
    public float        AudioOffsetSeconds { get; set; }
    public int          LaneCount { get; private set; } = 4;
    public float        CurrentChartTime => IsRunning ? GetSyncedChartTime() : 0f;
    public float        VisualChartTime => IsRunning ? _visualChartTime : 0f;
    public IReadOnlyList<NoteJudgmentEvent> JudgmentHistory => _judgmentHistory;

    private float _noteSpeed = 280f;
    private float _spawnTimer;
    private float _spawnInterval = 0.85f;
    private float _elapsed;
    private readonly Random _rng = new();
    private int _gameHeight;
    private IReadOnlyList<LaneNote> _chartNotes = [];
    private int _nextChartNoteIndex;
    private float _chartTime;
    private float _visualChartTime;
    private bool _visualClockInitialized;
    private float _spawnLeadTime;
    private readonly bool[] _laneHeld = new bool[7];
    private readonly int[] _laneShuffle = new int[7];
    private readonly List<Judgment> _pendingAutoJudgments = [];
    private readonly List<NoteJudgmentEvent> _judgmentHistory = [];
    private readonly HashSet<Note> _holdResumeGraceNotes = [];
    private int _pendingMisses;
    private float _holdResumeGraceUntilChartTime;

    public void Start(int gameHeight, IReadOnlyList<LaneNote>? chartNotes = null, int laneCount = 4)
    {
        _gameHeight = gameHeight;
        LaneCount = Math.Clamp(laneCount, 4, 7);
        _noteSpeed = 280f;
        _spawnInterval = 0.85f;
        _spawnTimer = 0f;
        _elapsed = 0f;
        _chartTime = 0f;
        _visualChartTime = 0f;
        _visualClockInitialized = false;
        _nextChartNoteIndex = 0;
        _chartNotes = chartNotes ?? [];
        _spawnLeadTime = CalculateSpawnLeadTime();
        IsRunning = true;
        Array.Clear(_laneHeld);
        _pendingAutoJudgments.Clear();
        _judgmentHistory.Clear();
        _holdResumeGraceNotes.Clear();
        _pendingMisses = 0;
        _holdResumeGraceUntilChartTime = float.NegativeInfinity;
        Notes.Clear();
        Score.Reset();
    }

    public void Stop()
    {
        IsRunning = false;
        Array.Clear(_laneHeld);
    }

    public void SwitchLaneMode(int laneCount, IReadOnlyList<LaneNote>? chartNotes = null)
    {
        int previousLaneCount = LaneCount;
        int nextLaneCount = Math.Clamp(laneCount, 4, 7);
        if (previousLaneCount == nextLaneCount && chartNotes is null)
            return;

        LaneCount = nextLaneCount;
        for (int i = 0; i < Notes.Count; i++)
        {
            Note note = Notes[i];
            note.Lane = MapLaneIndex(note.Lane, previousLaneCount, nextLaneCount);
            note.EndLane = MapLaneIndex(note.EndLane, previousLaneCount, nextLaneCount);
        }

        if (chartNotes is not null)
        {
            _chartNotes = chartNotes;
            float syncedChartTime = GetSyncedChartTime();
            _visualChartTime = syncedChartTime;
            _visualClockInitialized = true;
            _nextChartNoteIndex = 0;
            while (_nextChartNoteIndex < _chartNotes.Count &&
                   _chartNotes[_nextChartNoteIndex].Time <= syncedChartTime + 0.05f)
            {
                _nextChartNoteIndex++;
            }
        }

        for (int i = nextLaneCount; i < _laneHeld.Length; i++)
            _laneHeld[i] = false;

        _spawnLeadTime = CalculateSpawnLeadTime();
    }

    private static int MapLaneIndex(int lane, int oldLaneCount, int newLaneCount)
    {
        oldLaneCount = Math.Clamp(oldLaneCount, 1, 7);
        newLaneCount = Math.Clamp(newLaneCount, 1, 7);
        if (oldLaneCount == newLaneCount)
            return Math.Clamp(lane, 0, newLaneCount - 1);

        if (oldLaneCount == 1 || newLaneCount == 1)
            return 0;

        float normalized = Math.Clamp(lane, 0, oldLaneCount - 1) / (float)(oldLaneCount - 1);
        return Math.Clamp((int)MathF.Round(normalized * (newLaneCount - 1)), 0, newLaneCount - 1);
    }

    public void SetLaneHeld(int lane, bool isHeld)
    {
        if (lane < 0 || lane >= _laneHeld.Length)
            return;

        _laneHeld[lane] = isHeld;
        if (!isHeld || _holdResumeGraceNotes.Count == 0 || GetSyncedChartTime() > _holdResumeGraceUntilChartTime)
            return;

        // A timely re-press fulfils the pause grace contract. Remove only notes
        // whose currently-required lane is actually held; other Long/Slide notes
        // must still reacquire their own lane before the deadline.
        float chartTime = GetSyncedChartTime();
        foreach (Note note in _holdResumeGraceNotes.ToArray())
        {
            if (!IsRequiredLaneHeld(note, chartTime))
                continue;

            // Ticks that elapsed while the lane was intentionally released under
            // pause grace are skipped, not backfilled as if it had been held.
            note.HoldTicksAwarded = Math.Max(note.HoldTicksAwarded, CalculateExpectedHoldTicks(note, chartTime));
            _holdResumeGraceNotes.Remove(note);
        }
    }

    public void GrantHoldResumeGrace(float durationSeconds = 0.35f)
    {
        if (!IsRunning)
            return;

        float graceUntil = GetSyncedChartTime() + Math.Clamp(durationSeconds, 0f, 1f);
        _holdResumeGraceNotes.Clear();
        foreach (Note note in Notes.Where(note => note.State == NoteState.Holding))
            _holdResumeGraceNotes.Add(note);

        if (_holdResumeGraceNotes.Count == 0)
        {
            _holdResumeGraceUntilChartTime = float.NegativeInfinity;
            return;
        }

        float nearestHeldEnd = _holdResumeGraceNotes
            .Select(note => note.EndTargetTime + BadWindow)
            .Min();
        _holdResumeGraceUntilChartTime = Math.Min(graceUntil, nearestHeldEnd);
    }

    public int ConsumePendingMisses()
    {
        int misses = _pendingMisses;
        _pendingMisses = 0;
        return misses;
    }

    public Judgment[] ConsumePendingAutoJudgments()
    {
        if (_pendingAutoJudgments.Count == 0)
            return [];

        Judgment[] judgments = _pendingAutoJudgments.ToArray();
        _pendingAutoJudgments.Clear();
        return judgments;
    }

    public bool IsChartComplete
    {
        get
        {
            if (_chartNotes.Count == 0 || _nextChartNoteIndex < _chartNotes.Count)
                return false;

            for (int i = 0; i < Notes.Count; i++)
            {
                if (Notes[i].State is NoteState.Active or NoteState.Holding)
                    return false;
            }

            return true;
        }
    }

    public void Update(float deltaTime, float? playbackPositionSeconds = null)
    {
        if (!IsRunning) return;

        if (!float.IsFinite(deltaTime) || deltaTime < 0f)
            deltaTime = 0f;

        _elapsed += deltaTime;
        bool hasPlaybackPosition = playbackPositionSeconds.HasValue && float.IsFinite(playbackPositionSeconds.GetValueOrDefault());
        if (hasPlaybackPosition)
        {
            // MCI can occasionally report a stale or slightly older position.
            // Keep advancing by monotonic frame time between coarse audio samples;
            // a forward audio sample may catch the clock up, but a stale sample
            // must never freeze or rewind already-resolved gameplay.
            float reportedPosition = MathF.Max(0f, playbackPositionSeconds.GetValueOrDefault());
            _chartTime = reportedPosition > _chartTime
                ? reportedPosition
                : _chartTime + deltaTime;
        }
        else
            _chartTime += deltaTime;

        _noteSpeed = 450f * Math.Clamp(NoteSpeedMultiplier, 0.1f, 20.0f);
        _spawnLeadTime = CalculateSpawnLeadTime();

        if (_chartNotes.Count > 0)
        {
            SpawnChartNotes();
        }
        else
        {
            _spawnInterval = MathF.Max(0.38f, 0.85f - _elapsed * 0.008f);
            _spawnTimer += deltaTime;
            if (_spawnTimer >= _spawnInterval)
            {
                _spawnTimer = 0f;
                SpawnNotes();
            }
        }

        float syncedChartTime = GetSyncedChartTime();
        UpdateVisualChartTime(deltaTime, syncedChartTime, hasPlaybackPosition);
        float visualChartTime = VisualChartTime;
        float hitCenterY = GetHitZoneY(_gameHeight);
        for (int i = Notes.Count - 1; i >= 0; i--)
        {
            Note note = Notes[i];
            if (note.State is NoteState.Active or NoteState.Holding)
            {
                UpdateNotePosition(note, hitCenterY, visualChartTime);

                if (note.State == NoteState.Holding)
                {
                    bool isResumeGraceActive = _holdResumeGraceNotes.Contains(note) && syncedChartTime <= _holdResumeGraceUntilChartTime;
                    if (!IsRequiredLaneHeld(note, syncedChartTime) && isResumeGraceActive)
                    {
                        continue;
                    }

                    UpdateHeldNote(note, syncedChartTime);

                    if (!IsRequiredLaneHeld(note, syncedChartTime) && syncedChartTime < note.EndTargetTime - BadWindow)
                    {
                        ResolveMiss(note, syncedChartTime, GetHoldBreakReason(note));
                    }
                    else if (syncedChartTime >= note.EndTargetTime)
                    {
                        CompleteHeldNote(note, syncedChartTime);
                    }
                    continue;
                }

                if (syncedChartTime - note.TargetTime > MissThreshold)
                {
                    ResolveMiss(note, syncedChartTime, GetStartMissReason(note));
                }
            }
            else if (syncedChartTime - note.ResolvedTime > 1.0f)
            {
                Notes.RemoveAt(i);
            }
        }
    }

    private void UpdateVisualChartTime(float deltaTime, float syncedChartTime, bool hasPlaybackPosition)
    {
        syncedChartTime = MathF.Max(0f, syncedChartTime);
        if (!_visualClockInitialized || deltaTime <= 0f || MathF.Abs(syncedChartTime - _visualChartTime) > 0.18f)
        {
            _visualChartTime = syncedChartTime;
            _visualClockInitialized = true;
            return;
        }

        float predicted = _visualChartTime + deltaTime;
        float error = syncedChartTime - predicted;
        float correctionLimit = MathF.Max(0.002f, deltaTime * (hasPlaybackPosition ? 0.55f : 1.0f));
        float correction = Math.Clamp(error * 0.22f, -correctionLimit, correctionLimit);
        float corrected = predicted + correction;

        if (hasPlaybackPosition)
            corrected = Math.Clamp(corrected, syncedChartTime - 0.035f, syncedChartTime + 0.065f);

        _visualChartTime = MathF.Max(0f, corrected);
    }

    private static readonly string[] JudgmentLabels = ["PERFECT!", "GREAT!", "BETTER", "GOOD", "BAD"];

    public readonly record struct HitResult(Judgment Judgment, string Label, float OffsetSeconds, string TimingLabel, int ChordSize = 1, string Detail = "");

    public HitResult? TryHit(int lane, float? chartTimeOverride = null)
    {
        if (!IsRunning) return null;

        Note? best = null;
        float bestTimeDiff = BadWindow + 0.001f;
        float syncedChartTime = ResolveInputChartTime(chartTimeOverride);

        for (int i = 0; i < Notes.Count; i++)
        {
            Note note = Notes[i];
            if (note.State != NoteState.Active) continue;
            if (note.Lane != lane) continue;

            float timeDiff = MathF.Abs(syncedChartTime - note.TargetTime);
            if (timeDiff > BadWindow) continue;

            if (timeDiff < bestTimeDiff)
            {
                bestTimeDiff = timeDiff;
                best = note;
            }
        }

        if (best is null) return null;

        Judgment judgment = Judge(bestTimeDiff);
        float signedOffset = syncedChartTime - best.TargetTime;
        Score.AddHit(judgment, signedOffset);
        best.StartJudgment = judgment;
        RecordJudgment(best, best.Type == NoteType.Tap ? NoteJudgmentPhase.Tap : NoteJudgmentPhase.Start, judgment, NoteFailureReason.None, syncedChartTime, signedOffset);

        if (best.Type is NoteType.Long or NoteType.Slide)
        {
            best.State = NoteState.Holding;
            best.HoldStartTime = syncedChartTime;
            best.ResolvedTime = syncedChartTime;
            UpdateHeldNote(best, syncedChartTime);
        }
        else
        {
            ResolveNote(best, NoteState.Hit, syncedChartTime);
        }

        return new HitResult(judgment, JudgmentLabels[(int)judgment], signedOffset, FormatTimingLabel(signedOffset), best.ChordSize, GetHitDetail(best));
    }

    public HitResult? TryRelease(int lane, float? chartTimeOverride = null)
    {
        if (!IsRunning)
            return null;

        float syncedChartTime = ResolveInputChartTime(chartTimeOverride);
        Note? best = null;
        float bestTimeDiff = BadWindow + 0.001f;

        for (int i = 0; i < Notes.Count; i++)
        {
            Note note = Notes[i];
            if (note.State != NoteState.Holding) continue;
            if (note.Type == NoteType.Long && note.Lane != lane) continue;
            if (note.Type == NoteType.Slide && note.EndLane != lane) continue;

            float signedDiff = syncedChartTime - note.EndTargetTime;
            float timeDiff = MathF.Abs(signedDiff);
            if (timeDiff > BadWindow)
            {
                if (signedDiff < -BadWindow)
                {
                    ResolveMiss(note, syncedChartTime, GetHoldBreakReason(note));
                    return new HitResult(Judgment.Bad, "MISS", signedDiff, "EARLY RELEASE", note.ChordSize, GetHitDetail(note));
                }

                continue;
            }

            if (timeDiff < bestTimeDiff)
            {
                bestTimeDiff = timeDiff;
                best = note;
            }
        }

        if (best is null)
            return null;

        float signedOffset = syncedChartTime - best.EndTargetTime;
        Judgment judgment = Judge(bestTimeDiff);
        best.EndJudgment = judgment;
        Score.AddHit(judgment, signedOffset);
        RecordJudgment(best, NoteJudgmentPhase.End, judgment, NoteFailureReason.None, syncedChartTime, signedOffset);
        ResolveNote(best, NoteState.Hit, syncedChartTime);
        return new HitResult(judgment, $"{JudgmentLabels[(int)judgment]} END", signedOffset, FormatTimingLabel(signedOffset), best.ChordSize, GetHitDetail(best));
    }

    private float CalculateSpawnLeadTime()
    {
        float hitCenterY = GetHitZoneY(_gameHeight);
        float travelDistance = Math.Max(1f, hitCenterY + Note.Height / 2f);
        return travelDistance / Math.Max(1f, _noteSpeed);
    }

    private float GetSyncedChartTime()
    {
        return _chartTime - AudioOffsetSeconds;
    }

    private float ResolveInputChartTime(float? chartTimeOverride)
    {
        return chartTimeOverride.HasValue && float.IsFinite(chartTimeOverride.GetValueOrDefault())
            ? MathF.Max(0f, chartTimeOverride.GetValueOrDefault())
            : GetSyncedChartTime();
    }

    private static string FormatTimingLabel(float signedOffsetSeconds)
    {
        int ms = (int)MathF.Round(signedOffsetSeconds * 1000f);
        if (Math.Abs(ms) <= 3)
            return "SYNC";

        return ms < 0 ? $"EARLY {Math.Abs(ms)}ms" : $"LATE {ms}ms";
    }

    private void SpawnChartNotes()
    {
        while (_nextChartNoteIndex < _chartNotes.Count)
        {
            LaneNote chartNote = _chartNotes[_nextChartNoteIndex];
            if (chartNote.Time > GetSyncedChartTime() + _spawnLeadTime)
                break;

            if (chartNote.Lane >= 0 && chartNote.Lane < LaneCount)
            {
                int endLane = chartNote.EndLane >= 0 ? chartNote.EndLane : chartNote.Lane;
                var note = new Note(chartNote.Lane)
                {
                    TargetTime = chartNote.Time,
                    Type = chartNote.Type,
                    Duration = Math.Max(0f, chartNote.Duration),
                    EndLane = Math.Clamp(endLane, 0, LaneCount - 1),
                };
                ApplyChordInfo(note);
                Notes.Add(note);
            }

            _nextChartNoteIndex++;
        }
    }

    private void SpawnNotes()
    {
        int count = _rng.Next(1, 3);

        for (int i = 0; i < LaneCount; i++) _laneShuffle[i] = i;
        for (int i = LaneCount - 1; i > 0; i--)
        {
            int j = _rng.Next(i + 1);
            (_laneShuffle[i], _laneShuffle[j]) = (_laneShuffle[j], _laneShuffle[i]);
        }

        float targetTime = GetSyncedChartTime() + _spawnLeadTime;
        for (int i = 0; i < count; i++)
        {
            NoteType type = (_elapsed > 8f && i == 0 && _rng.Next(8) == 0) ? NoteType.Long : NoteType.Tap;
            var note = new Note(_laneShuffle[i])
            {
                TargetTime = targetTime,
                Type = type,
                Duration = type == NoteType.Long ? 0.5f : 0f,
            };
            if (count > 1)
            {
                note.ChordSize = count;
                note.ChordHint = $"CHORD x{count}";
            }
            Notes.Add(note);
        }
    }

    private void UpdateNotePosition(Note note, float hitCenterY, float syncedChartTime)
    {
        float remainingTime = note.TargetTime - syncedChartTime;
        note.Y = hitCenterY - (Note.Height / 2f) - remainingTime * _noteSpeed;

        float endRemainingTime = note.EndTargetTime - syncedChartTime;
        note.EndY = hitCenterY - (Note.Height / 2f) - endRemainingTime * _noteSpeed;
    }

    private static void ResolveNote(Note note, NoteState state, float syncedChartTime)
    {
        note.State = state;
        note.ResolvedTime = syncedChartTime;
    }

    private void UpdateHeldNote(Note note, float syncedChartTime)
    {
        if (note.Duration <= 0f)
        {
            note.HoldProgress = 1f;
            return;
        }

        note.HoldProgress = Math.Clamp((syncedChartTime - note.TargetTime) / note.Duration, 0f, 1f);
        // Catch up ticks through the last interval strictly before the note end.
        // Using the sampled frame time directly used to award zero catch-up ticks
        // whenever one audio-position jump crossed the end target.
        int expectedTicks = CalculateExpectedHoldTicks(note, syncedChartTime);
        while (note.HoldTicksAwarded < expectedTicks)
        {
            note.HoldTicksAwarded++;
            Score.AddHoldTick();
        }
    }

    private static int CalculateExpectedHoldTicks(Note note, float chartTime)
    {
        float tickSampleTime = Math.Min(chartTime, note.EndTargetTime - 0.0001f);
        return (int)MathF.Floor(Math.Max(0f, tickSampleTime - note.TargetTime) / HoldTickInterval);
    }

    private bool IsRequiredLaneHeld(Note note, float syncedChartTime)
    {
        if (note.Type != NoteType.Slide)
            return _laneHeld[note.Lane];

        return _laneHeld[GetSlideRequiredLane(note, syncedChartTime)];
    }

    private static int GetSlideRequiredLane(Note note, float syncedChartTime)
    {
        if (note.Duration <= 0f)
            return note.EndLane;

        float progress = Math.Clamp((syncedChartTime - note.TargetTime) / note.Duration, 0f, 1f);
        return progress < 0.5f ? note.Lane : note.EndLane;
    }

    private void CompleteHeldNote(Note note, float syncedChartTime)
    {
        if (!IsRequiredLaneHeld(note, syncedChartTime))
        {
            ResolveMiss(note, syncedChartTime, GetEndMissReason(note));
            return;
        }

        float diff = MathF.Abs(syncedChartTime - note.EndTargetTime);
        if (_holdResumeGraceNotes.Contains(note) && diff > BadWindow)
        {
            ResolveMiss(note, syncedChartTime, GetEndMissReason(note));
            return;
        }

        // Holding through the end is an automatic completion, so its quality must
        // not depend on whether this WinForms timer frame arrived 5 ms or 200 ms
        // after the chart target. An explicit release still uses TryRelease timing.
        Judgment judgment = _holdResumeGraceNotes.Contains(note) ? Judge(diff) : Judgment.Perfect;
        float signedOffset = _holdResumeGraceNotes.Contains(note) ? syncedChartTime - note.EndTargetTime : 0f;
        note.EndJudgment = judgment;
        Score.AddHit(judgment, signedOffset);
        RecordJudgment(note, NoteJudgmentPhase.End, judgment, NoteFailureReason.None, syncedChartTime, signedOffset);
        _pendingAutoJudgments.Add(judgment);
        _holdResumeGraceNotes.Remove(note);
        ResolveNote(note, NoteState.Hit, syncedChartTime);
    }

    private void ResolveMiss(Note note, float syncedChartTime, NoteFailureReason reason)
    {
        _holdResumeGraceNotes.Remove(note);
        ResolveNote(note, NoteState.Miss, syncedChartTime);
        Score.AddMiss();
        _pendingMisses++;
        NoteJudgmentPhase phase = reason switch
        {
            NoteFailureReason.TapMiss => NoteJudgmentPhase.Tap,
            NoteFailureReason.LongStartMiss or NoteFailureReason.SlideStartMiss => NoteJudgmentPhase.Start,
            NoteFailureReason.LongHoldBreak or NoteFailureReason.SlidePathBreak => NoteJudgmentPhase.Hold,
            _ => NoteJudgmentPhase.End,
        };
        float targetTime = phase is NoteJudgmentPhase.Tap or NoteJudgmentPhase.Start
            ? note.TargetTime
            : note.EndTargetTime;
        RecordJudgment(note, phase, null, reason, syncedChartTime, syncedChartTime - targetTime);
    }

    private void RecordJudgment(
        Note note,
        NoteJudgmentPhase phase,
        Judgment? judgment,
        NoteFailureReason failureReason,
        float chartTime,
        float offsetSeconds)
    {
        float targetTime = phase is NoteJudgmentPhase.Tap or NoteJudgmentPhase.Start
            ? note.TargetTime
            : note.EndTargetTime;
        _judgmentHistory.Add(new NoteJudgmentEvent(
            chartTime,
            targetTime,
            note.Lane,
            note.EndLane,
            note.Type,
            phase,
            judgment,
            failureReason,
            offsetSeconds));
    }

    private static NoteFailureReason GetStartMissReason(Note note)
    {
        return note.Type switch
        {
            NoteType.Long => NoteFailureReason.LongStartMiss,
            NoteType.Slide => NoteFailureReason.SlideStartMiss,
            _ => NoteFailureReason.TapMiss,
        };
    }

    private static NoteFailureReason GetHoldBreakReason(Note note)
    {
        return note.Type == NoteType.Slide
            ? NoteFailureReason.SlidePathBreak
            : NoteFailureReason.LongHoldBreak;
    }

    private static NoteFailureReason GetEndMissReason(Note note)
    {
        return note.Type == NoteType.Slide
            ? NoteFailureReason.SlideEndMiss
            : NoteFailureReason.LongEndMiss;
    }

    private static Judgment Judge(float absoluteTimeDiff)
    {
        if      (absoluteTimeDiff <= PerfectWindow) return Judgment.Perfect;
        else if (absoluteTimeDiff <= GreatWindow)   return Judgment.Great;
        else if (absoluteTimeDiff <= BetterWindow)  return Judgment.Better;
        else if (absoluteTimeDiff <= GoodWindow)    return Judgment.Good;
        else                                        return Judgment.Bad;
    }

    private void ApplyChordInfo(Note note)
    {
        int chordSize = 0;
        int left = 0;
        int right = 0;
        for (int i = 0; i < _chartNotes.Count; i++)
        {
            LaneNote other = _chartNotes[i];
            if (MathF.Abs(other.Time - note.TargetTime) > ChordTimeWindow)
                continue;

            chordSize++;
            if (other.Lane < LaneCount / 2) left++;
            else right++;
        }

        note.ChordSize = Math.Max(1, chordSize);
        note.ChordHint = chordSize > 1 ? $"CHORD x{chordSize} L{left}/R{right}" : string.Empty;
    }

    private static string GetHitDetail(Note note)
    {
        if (!string.IsNullOrWhiteSpace(note.ChordHint))
            return note.ChordHint;

        return note.Type switch
        {
            NoteType.Long => "HOLD",
            NoteType.Slide => "SLIDE",
            _ => string.Empty,
        };
    }

    public static float GetHitZoneOffset(int gameHeight)
    {
        float scale = Math.Max(0.52f, gameHeight / GameplayDesignHeight);
        return HitZoneOffset * scale;
    }

    public static float GetHitZoneY(int gameHeight) => gameHeight - GetHitZoneOffset(gameHeight);
}
