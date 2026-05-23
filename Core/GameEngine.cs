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

    public List<Note>   Notes     { get; } = [];
    public ScoreManager Score     { get; } = new();
    public bool         IsRunning { get; private set; }
    public float        NoteSpeedMultiplier { get; set; } = 1f;
    public float        AudioOffsetSeconds { get; set; }
    public int          LaneCount { get; private set; } = 4;

    private float         _noteSpeed     = 280f;
    private float         _spawnTimer    = 0f;
    private float         _spawnInterval = 0.85f;
    private float         _elapsed       = 0f;
    private readonly Random _rng         = new();
    private int           _gameHeight;
    private IReadOnlyList<LaneNote> _chartNotes = [];
    private int _nextChartNoteIndex;
    private float _chartTime;
    private float _spawnLeadTime;
    private readonly bool[] _laneHeld = new bool[7];
    private readonly int[] _laneShuffle = new int[7];

    public void Start(int gameHeight, IReadOnlyList<LaneNote>? chartNotes = null, int laneCount = 4)
    {
        _gameHeight    = gameHeight;
        LaneCount      = Math.Clamp(laneCount, 4, 7);
        _noteSpeed     = 280f;
        _spawnInterval = 0.85f;
        _spawnTimer    = 0f;
        _elapsed       = 0f;
        _chartTime = 0f;
        _nextChartNoteIndex = 0;
        _chartNotes = chartNotes ?? [];
        _spawnLeadTime = CalculateSpawnLeadTime();
        IsRunning      = true;
        Array.Clear(_laneHeld);
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

    public void Update(float deltaTime)
    {
        if (!IsRunning) return;

        _elapsed += deltaTime;
        _chartTime += deltaTime;

        _noteSpeed = 450f * Math.Clamp(NoteSpeedMultiplier, 0.5f, 5.0f);
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
                    if (!_laneHeld[note.Lane] && syncedChartTime < note.EndTargetTime - BadWindow)
                    {
                        ResolveNote(note, NoteState.Miss, syncedChartTime);
                        Score.AddMiss();
                    }
                    else if (syncedChartTime >= note.EndTargetTime)
                    {
                        ResolveNote(note, NoteState.Hit, syncedChartTime);
                    }
                    continue;
                }

                if (syncedChartTime - note.TargetTime > MissThreshold)
                {
                    ResolveNote(note, NoteState.Miss, syncedChartTime);
                    Score.AddMiss();
                }
            }
            else if (syncedChartTime - note.ResolvedTime > 1.0f)
            {
                Notes.RemoveAt(i);
            }
        }
    }

    private static readonly string[] JudgmentLabels = ["PERFECT!", "GREAT!", "BETTER", "GOOD", "BAD"];

    public readonly record struct HitResult(Judgment Judgment, string Label);

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

        Judgment j;
        if      (bestTimeDiff <= PerfectWindow) j = Judgment.Perfect;
        else if (bestTimeDiff <= GreatWindow)   j = Judgment.Great;
        else if (bestTimeDiff <= BetterWindow)  j = Judgment.Better;
        else if (bestTimeDiff <= GoodWindow)    j = Judgment.Good;
        else                                    j = Judgment.Bad;

        Score.AddHit(j);
        ResolveNote(best, best.Type == NoteType.Long ? NoteState.Holding : NoteState.Hit, syncedChartTime);
        return new HitResult(j, JudgmentLabels[(int)j]);
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
}
