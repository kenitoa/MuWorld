namespace RhythmGame;

public class GameEngine
{
    // ── 판정 범위 (초 단위 — 시간 기반 판정) ──────────────────────────────────────
    public const float HitZoneOffset = 130f;   // 화면 아래쪽에서의 거리 (렌더링용)
    public const float PerfectWindow = 0.030f; // ±30ms
    public const float GreatWindow   = 0.060f; // ±60ms
    public const float BetterWindow  = 0.090f; // ±90ms
    public const float GoodWindow    = 0.120f; // ±120ms
    public const float BadWindow     = 0.150f; // ±150ms
    private const float MissThreshold = 0.180f; // 이 시간을 넘기면 Miss

    public List<Note>   Notes     { get; } = [];
    public ScoreManager Score     { get; } = new();
    public bool         IsRunning { get; private set; }
    public float        NoteSpeedMultiplier { get; set; } = 1f;

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

    // ── 시작 / 종료 ────────────────────────────────────────────────────────────
    public void Start(int gameHeight, IReadOnlyList<LaneNote>? chartNotes = null)
    {
        _gameHeight    = gameHeight;
        _noteSpeed     = 280f;
        _spawnInterval = 0.85f;
        _spawnTimer    = 0f;
        _elapsed       = 0f;
        _chartTime = 0f;
        _nextChartNoteIndex = 0;
        _chartNotes = chartNotes ?? [];
        _spawnLeadTime = CalculateSpawnLeadTime();
        IsRunning      = true;
        Notes.Clear();
        Score.Reset();
    }

    public void Stop() => IsRunning = false;

    public bool IsChartComplete
    {
        get
        {
            if (_chartNotes.Count == 0 || _nextChartNoteIndex < _chartNotes.Count)
                return false;
            for (int i = 0; i < Notes.Count; i++)
            {
                if (Notes[i].State == NoteState.Active)
                    return false;
            }
            return true;
        }
    }

    // ── 매 프레임 업데이트 ─────────────────────────────────────────────────────
    public void Update(float deltaTime)
    {
        if (!IsRunning) return;

        _elapsed += deltaTime;
        _chartTime += deltaTime;

        // 배속에 비례하여 노트 간격(속도) 조절
        _noteSpeed = 450f * Math.Clamp(NoteSpeedMultiplier, 0.5f, 5.0f);
        _spawnLeadTime = CalculateSpawnLeadTime();

        if (_chartNotes.Count > 0)
        {
            SpawnChartNotes();
        }
        else
        {
            // 기존 랜덤 스폰 fallback
            _spawnInterval = MathF.Max(0.38f, 0.85f - _elapsed * 0.008f);
            _spawnTimer += deltaTime;
            if (_spawnTimer >= _spawnInterval)
            {
                _spawnTimer = 0f;
                SpawnNotes();
            }
        }

        // 노트 Y좌표 계산 + Miss 처리 + 오래된 노트 제거 (단일 루프)
        float hitCenterY = _gameHeight - HitZoneOffset;
        for (int i = Notes.Count - 1; i >= 0; i--)
        {
            var note = Notes[i];
            if (note.State == NoteState.Active)
            {
                float remainingTime = note.TargetTime - _chartTime;
                note.Y = hitCenterY - (Note.Height / 2f) - remainingTime * _noteSpeed;

                // 시간 기반 Miss 판정: 노트 TargetTime을 MissThreshold 이상 지나면 Miss
                if (remainingTime < -MissThreshold)
                {
                    note.State = NoteState.Miss;
                    Score.AddMiss();
                }
            }
            else if (note.State != NoteState.Active)
            {
                // Hit/Miss 처리된 노트가 화면 밖으로 나가면 제거
                if (note.Y > _gameHeight + 100f)
                    Notes.RemoveAt(i);
            }
        }
    }

    // ── 판정 결과 (문자열 할당 방지용 상수) ──────────────────────────────────────
    private static readonly string[] JudgmentLabels = ["PERFECT!", "GREAT!", "BETTER", "GOOD", "BAD"];

    // ── 키 입력 판정 (시간 기반) ──────────────────────────────────────────────
    /// <returns>판정 문자열("PERFECT!" / "GOOD"), 범위 밖이면 null</returns>
    public string? TryHit(int lane)
    {
        if (!IsRunning) return null;

        Note? best = null;
        float bestTimeDiff = BadWindow + 0.001f; // BadWindow 이내만 탐색

        for (int i = 0; i < Notes.Count; i++)
        {
            var note = Notes[i];
            if (note.State != NoteState.Active) continue;
            if (note.Lane != lane) continue;

            float timeDiff = MathF.Abs(_chartTime - note.TargetTime);

            // BadWindow 밖이면 스킵
            if (timeDiff > BadWindow) continue;

            if (timeDiff < bestTimeDiff)
            {
                bestTimeDiff = timeDiff;
                best = note;
            }
        }

        if (best is null) return null;

        best.State = NoteState.Hit;

        // 판정 결정 — 시간 차이 기반 (좁은 범위부터)
        Judgment j;
        if      (bestTimeDiff <= PerfectWindow) j = Judgment.Perfect;
        else if (bestTimeDiff <= GreatWindow)   j = Judgment.Great;
        else if (bestTimeDiff <= BetterWindow)  j = Judgment.Better;
        else if (bestTimeDiff <= GoodWindow)    j = Judgment.Good;
        else                                    j = Judgment.Bad;

        Score.AddHit(j);
        return JudgmentLabels[(int)j];
    }

    // ── 노트 생성 ─────────────────────────────────────────────────────────────
    private float CalculateSpawnLeadTime()
    {
        float hitCenterY = _gameHeight - HitZoneOffset;
        float travelDistance = Math.Max(1f, hitCenterY + Note.Height / 2f);
        return travelDistance / Math.Max(1f, _noteSpeed);
    }

    private void SpawnChartNotes()
    {
        while (_nextChartNoteIndex < _chartNotes.Count)
        {
            LaneNote chartNote = _chartNotes[_nextChartNoteIndex];
            if (chartNote.Time > _chartTime + _spawnLeadTime)
                break;

            if (chartNote.Lane is >= 0 and < 4)
            {
                var note = new Note(chartNote.Lane) { TargetTime = chartNote.Time };
                Notes.Add(note);
            }

            _nextChartNoteIndex++;
        }
    }

    private readonly int[] _laneShuffle = new int[4];

    private void SpawnNotes()
    {
        int count = _rng.Next(1, 3);

        // Fisher-Yates shuffle (no LINQ allocation)
        for (int i = 0; i < 4; i++) _laneShuffle[i] = i;
        for (int i = 3; i > 0; i--)
        {
            int j = _rng.Next(i + 1);
            (_laneShuffle[i], _laneShuffle[j]) = (_laneShuffle[j], _laneShuffle[i]);
        }

        float targetTime = _chartTime + _spawnLeadTime;
        for (int i = 0; i < count; i++)
        {
            var note = new Note(_laneShuffle[i]) { TargetTime = targetTime };
            Notes.Add(note);
        }
    }
}
