namespace RhythmGame;

public enum NoteState { Active, Holding, Hit, Miss }

public enum NoteType { Tap, Long, Slide }

public class Note
{
    public const float Height = 22f;

    public int       Lane       { get; set; }
    public int       EndLane    { get; set; }
    public float     Y          { get; set; }
    public float     EndY       { get; set; }
    public NoteState State      { get; set; } = NoteState.Active;
    public NoteType  Type       { get; set; } = NoteType.Tap;
    /// <summary>판정선에 도달해야 하는 차트 시각 (초).</summary>
    public float     TargetTime { get; set; }
    public float     Duration   { get; set; }
    public float     ResolvedTime { get; set; }
    public float     HoldStartTime { get; set; }
    public float     HoldProgress { get; set; }
    public int       HoldTicksAwarded { get; set; }
    public Judgment? StartJudgment { get; set; }
    public Judgment? EndJudgment { get; set; }
    public int       ChordSize { get; set; } = 1;
    public string    ChordHint { get; set; } = string.Empty;

    public float EndTargetTime => TargetTime + Math.Max(0f, Duration);

    public Note(int lane)
    {
        Lane = lane;
        EndLane = lane;
        Y    = -Height;
        EndY = -Height;
    }
}
