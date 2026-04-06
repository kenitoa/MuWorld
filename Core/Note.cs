namespace RhythmGame;

public enum NoteState { Active, Hit, Miss }

public class Note
{
    public const float Height = 22f;

    public int       Lane       { get; set; }
    public float     Y          { get; set; }
    public NoteState State      { get; set; } = NoteState.Active;
    /// <summary>판정선에 도달해야 하는 차트 시각 (초).</summary>
    public float     TargetTime { get; set; }

    public Note(int lane)
    {
        Lane = lane;
        Y    = -Height;
    }
}
