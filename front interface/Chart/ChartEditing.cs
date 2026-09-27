namespace RhythmGame;

internal sealed record ChartEditorSnapshot(List<LaneNote> Notes, float Bpm, int SelectedIndex, float Cursor);

internal static class ChartEditing
{
    // Divisions per quarter note; zero disables snapping.
    internal static readonly int[] Subdivisions = [0, 1, 2, 3, 4, 6, 8];

    internal static float Snap(float time, float bpm, int subdivision)
    {
        if (!float.IsFinite(time) || !float.IsFinite(bpm) || bpm <= 0f)
            throw new ArgumentOutOfRangeException(nameof(time));
        if (subdivision == 0) return Math.Max(0f, time);
        if (!Subdivisions.Contains(subdivision)) throw new ArgumentOutOfRangeException(nameof(subdivision));
        float step = 60f / bpm / subdivision;
        return Math.Max(0f, MathF.Round(time / step) * step);
    }

    internal static float Snap(float time, IReadOnlyList<ChartTempoPoint> tempoMap, int subdivision)
    {
        ChartTempoPoint point = tempoMap.LastOrDefault(p => p.Time <= time);
        if (point.Bpm <= 0) throw new InvalidDataException("Tempo map has no starting BPM.");
        float snapped = point.Time + Snap(time - point.Time, point.Bpm, subdivision);
        ChartTempoPoint? next = tempoMap.Where(p => p.Time > time).Select(p => (ChartTempoPoint?)p).FirstOrDefault();
        return next.HasValue ? Math.Min(snapped, next.Value.Time) : snapped;
    }

    internal static LaneNote Mirror(LaneNote note, int lanes) => note with
    {
        Lane = lanes - 1 - note.Lane,
        EndLane = note.EndLane < 0 ? -1 : lanes - 1 - note.EndLane,
    };

    internal static float[] Density(IReadOnlyList<LaneNote> notes, float duration, int bins = 48)
    {
        var result = new float[bins];
        float width = Math.Max(1f, duration) / bins;
        foreach (LaneNote note in notes)
            if (float.IsFinite(note.Time) && note.Time >= 0f)
                result[Math.Clamp((int)(note.Time / width), 0, bins - 1)] += 1f / width;
        return result;
    }
}
