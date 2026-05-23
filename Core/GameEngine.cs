namespace RhythmGame;

public class GameEngine
{
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

    private float _noteSpeed = 280f;
    private float _spawnTimer;
    private float _spawnInterval = 0.85f;
    private float _elapsed;
    private readonly Random _rng = new();
    private int _gameHeight;
    private IReadOnlyList<LaneNote> _chartNotes = [];
    private int _nextChartNoteIndex;
    private float _chartTime;
    private float _spawnLeadTime;
    private readonly bool[] _laneHeld = new bool[7];
    private readonly int[] _laneShuffle = new int[7];
    private readonly List<Judgment> _pendingAutoJudgments = [];
    private int _pendingMisses;

    public void Start(int gameHeight, IReadOnlyList<LaneNote>? chartNotes = null, int laneCount = 4)
    {
        _gameHeight = gameHeight;
        LaneCount = Math.Clamp(laneCount, 4, 7);
        _noteSpeed = 280f;
        _spawnInterval = 0.85f;
        _spawnTimer = 0f;
        _elapsed = 0f;
        _chartTime = 0f;
        _nextChartNoteIndex = 0;
        _chartNotes = chartNotes ?? [];
        _spawnLeadTime = CalculateSpawnLeadTime();
        IsRunning = true;
        Array.Clear(_laneHeld);
        _pendingAutoJudgments.Clear();
        _pendingMisses = 0;
        Notes.Clear();
        Score.Reset();
    }

    public void Stop()
    {
        IsRunning = false;
        Array.Clear(_laneHeld);
    }

    public void SetLaneHeld(int lane, bool isHeld)
    {
        if (lane >= 0 && lane < _laneHeld.Length)
            _laneHeld[lane] = isHeld;
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

        _elapsed += deltaTime;
        if (playbackPositionSeconds.HasValue)
            _chartTime = MathF.Max(0f, playbackPositionSeconds.Value);
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

        float hitCenterY = _gameHeight - HitZoneOffset;
        float syncedChartTime = GetSyncedChartTime();
        for (int i = Notes.Count - 1; i >= 0; i--)
        {
            Note note = Notes[i];
            if (note.State is NoteState.Active or NoteState.Holding)
            {
                UpdateNotePosition(note, hitCenterY, syncedChartTime);

                if (note.State == NoteState.Holding)
                {
                    UpdateHeldNote(note, syncedChartTime);
                    if (!IsRequiredLaneHeld(note, syncedChartTime) && syncedChartTime < note.EndTargetTime - BadWindow)
                    {
                        ResolveMiss(note, syncedChartTime);
                    }
                    else if (syncedChartTime >= note.EndTargetTime)
                    {
                        CompleteHeldNote(note, syncedChartTime);
                    }
                    continue;
                }

                if (syncedChartTime - note.TargetTime > MissThreshold)
                {
                    ResolveMiss(note, syncedChartTime);
                }
            }
            else if (syncedChartTime - note.ResolvedTime > 1.0f)
            {
                Notes.RemoveAt(i);
            }
        }
    }

    private static readonly string[] JudgmentLabels = ["PERFECT!", "GREAT!", "BETTER", "GOOD", "BAD"];

    public readonly record struct HitResult(Judgment Judgment, string Label, float OffsetSeconds, string TimingLabel, int ChordSize = 1, string Detail = "");

    public HitResult? TryHit(int lane)
    {
        if (!IsRunning) return null;

        Note? best = null;
        float bestTimeDiff = BadWindow + 0.001f;
        float syncedChartTime = GetSyncedChartTime();

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

    public HitResult? TryRelease(int lane)
    {
        if (!IsRunning)
            return null;

        float syncedChartTime = GetSyncedChartTime();
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
                    ResolveMiss(note, syncedChartTime);
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
        ResolveNote(best, NoteState.Hit, syncedChartTime);
        return new HitResult(judgment, $"{JudgmentLabels[(int)judgment]} END", signedOffset, FormatTimingLabel(signedOffset), best.ChordSize, GetHitDetail(best));
    }

    private float CalculateSpawnLeadTime()
    {
        float hitCenterY = _gameHeight - HitZoneOffset;
        float travelDistance = Math.Max(1f, hitCenterY + Note.Height / 2f);
        return travelDistance / Math.Max(1f, _noteSpeed);
    }

    private float GetSyncedChartTime()
    {
        return _chartTime - AudioOffsetSeconds;
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
        int expectedTicks = (int)MathF.Floor(Math.Max(0f, syncedChartTime - note.TargetTime) / HoldTickInterval);
        while (note.HoldTicksAwarded < expectedTicks && syncedChartTime < note.EndTargetTime)
        {
            note.HoldTicksAwarded++;
            Score.AddHoldTick();
        }
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
            ResolveMiss(note, syncedChartTime);
            return;
        }

        float diff = MathF.Abs(syncedChartTime - note.EndTargetTime);
        Judgment judgment = Judge(diff);
        note.EndJudgment = judgment;
        Score.AddHit(judgment, syncedChartTime - note.EndTargetTime);
        _pendingAutoJudgments.Add(judgment);
        ResolveNote(note, NoteState.Hit, syncedChartTime);
    }

    private void ResolveMiss(Note note, float syncedChartTime)
    {
        ResolveNote(note, NoteState.Miss, syncedChartTime);
        Score.AddMiss();
        _pendingMisses++;
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
}
