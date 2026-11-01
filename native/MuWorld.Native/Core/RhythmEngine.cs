using System;
using System.Collections.Generic;
using System.Linq;

namespace MuWorld.Native.Core;

/// <summary>Integer song timestamps; exact decimal rate/offset comparisons, independent of drawing FPS.</summary>
public sealed class RhythmEngine
{
    public const long MaxWindowUs = 120_000;
    public static readonly IReadOnlyDictionary<string, long> Windows = new Dictionary<string, long>
        { ["PERFECT"] = 20_000, ["GREAT"] = 45_000, ["GOOD"] = 80_000, ["BAD"] = MaxWindowUs };
    public static readonly IReadOnlyDictionary<string, decimal> Weights = new Dictionary<string, decimal>
        { ["PERFECT"] = 1m, ["GREAT"] = .9m, ["GOOD"] = .6m, ["BAD"] = .2m, ["MISS"] = 0m };
    private static readonly string[] Grades = ["PERFECT", "GREAT", "GOOD", "BAD", "MISS"];
    private readonly List<Note> _source;
    private readonly Dictionary<string, int> _noteOrder;
    private readonly List<(InputEvent Input, long Serial)> _events = [];
    private readonly HashSet<(string, string)> _units = [];
    private readonly bool[] _pressed = new bool[4];
    private readonly NoteState?[] _activeHolds = new NoteState?[4];
    private readonly List<Deadline> _deadlines = [];
    private long _serial, _cursor = long.MinValue;
    private int _deadlineIndex;
    private bool _finished;
    private readonly record struct Deadline(long TimeUs, int Index, bool Miss);

    public long InputOffsetUs { get; }
    public decimal Rate { get; }
    public decimal WindowScale { get; }
    public long FromUs { get; }
    public long ToUs { get; }
    public int TotalUnits { get; }
    public int ExtraInputs { get; private set; }
    public int Combo { get; private set; }
    public int MaxCombo { get; private set; }
    public List<NoteState> Notes { get; private set; } = [];
    public List<Judgment> Judgments { get; private set; } = [];
    public List<InputEvent> Inputs { get; private set; } = [];

    public RhythmEngine(Chart chart, Settings settings, decimal rate = 1m, long fromUs = 0, long? toUs = null)
    {
        ArgumentNullException.ThrowIfNull(chart);
        ArgumentNullException.ThrowIfNull(settings);
        if (rate <= 0m || rate > 8m) throw new ArgumentOutOfRangeException(nameof(rate));
        if (settings.WindowScale <= 0m || settings.WindowScale > 8m) throw new ArgumentOutOfRangeException(nameof(settings.WindowScale));
        FromUs = fromUs; ToUs = toUs ?? chart.DurationUs;
        if (FromUs < 0 || ToUs < FromUs || ToUs > chart.DurationUs) throw new ArgumentOutOfRangeException(nameof(fromUs), "Invalid practice interval.");
        InputOffsetUs = settings.InputOffsetUs; Rate = rate; WindowScale = settings.WindowScale;
        var ids = new HashSet<string>();
        foreach (var n in chart.Notes)
        {
            if (string.IsNullOrWhiteSpace(n.Id) || !ids.Add(n.Id) || n.Lane is < 0 or > 3 || n.EndUs is not null && n.EndUs <= n.TimeUs)
                throw new ArgumentException("Invalid or duplicated chart note.", nameof(chart));
        }
        _source = chart.Notes.Where(n => n.TimeUs >= FromUs && n.TimeUs < ToUs && (!n.EndUs.HasValue || n.EndUs <= ToUs))
            .OrderBy(n => n.TimeUs).ThenBy(n => n.Lane)
            .Select(n => new Note { Id = n.Id, Lane = n.Lane, TimeUs = n.TimeUs, EndUs = n.EndUs, Pattern = n.Pattern }).ToList();
        _noteOrder = _source.Select((n, i) => (n.Id, i)).ToDictionary(pair => pair.Id, pair => pair.i);
        TotalUnits = _source.Sum(n => n.EndUs.HasValue ? 2 : 1);
        ResetState();
    }

    private void ResetState()
    {
        Notes = _source.Select(n => new NoteState(n)).ToList();
        Judgments = []; Inputs = []; Combo = MaxCombo = ExtraInputs = 0;
        Array.Clear(_pressed); Array.Clear(_activeHolds);
        _units.Clear(); _deadlines.Clear(); _deadlineIndex = 0;
        for (var i = 0; i < Notes.Count; i++)
        {
            var n = Notes[i];
            var miss = checked((long)decimal.Floor(n.TimeUs + (InputOffsetUs + MaxWindowUs * WindowScale) * Rate) + 1);
            _deadlines.Add(new Deadline(miss, i, true));
            if (n.EndUs.HasValue)
                _deadlines.Add(new Deadline(checked((long)decimal.Ceiling(n.EndUs.Value + InputOffsetUs * Rate)), i, false));
        }
        _deadlines.Sort((a, b) =>
        {
            var time = a.TimeUs.CompareTo(b.TimeUs);
            if (time != 0) return time;
            var index = a.Index.CompareTo(b.Index);
            return index != 0 ? index : b.Miss.CompareTo(a.Miss);
        });
    }

    private decimal SongDelta(long time, long target) => (decimal)time - target - InputOffsetUs * Rate;
    private string? Grade(long time, long target)
    {
        var distance = decimal.Abs(SongDelta(time, target));
        foreach (var grade in Grades.AsSpan(0, 4))
            if (distance <= Windows[grade] * WindowScale * Rate) return grade;
        return null;
    }
    private int Compare(Judgment a, Judgment b)
    {
        var t = a.TimeUs.CompareTo(b.TimeUs);
        if (t != 0) return t;
        var n = _noteOrder[a.NoteId].CompareTo(_noteOrder[b.NoteId]);
        return n != 0 ? n : a.Kind == b.Kind ? 0 : a.Kind == "head" ? -1 : 1;
    }
    private void Emit(NoteState note, string grade, string kind, long timeUs, double? deltaUs = null)
    {
        if (!_units.Add((note.Id, kind))) return;
        var value = new Judgment { NoteId = note.Id, Lane = note.Lane, TimeUs = timeUs,
            TargetUs = kind == "hold" ? note.EndUs!.Value : note.TimeUs, DeltaUs = deltaUs,
            Grade = grade, Kind = kind, Pattern = note.Pattern };
        var previous = Judgments.LastOrDefault();
        Judgments.Add(value);
        if (previous is not null && Compare(previous, value) > 0)
        {
            Judgments.Sort(Compare); Combo = MaxCombo = 0;
            foreach (var judgment in Judgments)
            { Combo = judgment.Grade == "MISS" ? 0 : Combo + 1; MaxCombo = Math.Max(MaxCombo, Combo); }
        }
        else { Combo = grade == "MISS" ? 0 : Combo + 1; MaxCombo = Math.Max(MaxCombo, Combo); }
    }
    private void ResolveHold(NoteState note, bool success, long time)
    {
        note.HoldState = success ? "complete" : "broken";
        if (ReferenceEquals(_activeHolds[note.Lane], note)) _activeHolds[note.Lane] = null;
        Emit(note, success ? "PERFECT" : "MISS", "hold", time);
    }
    private void RunDeadlines(long time, bool inclusive = true)
    {
        while (_deadlineIndex < _deadlines.Count)
        {
            var d = _deadlines[_deadlineIndex];
            if (d.TimeUs > time || !inclusive && d.TimeUs == time) break;
            _deadlineIndex++;
            var note = Notes[d.Index];
            if (d.Miss && note.State == "pending")
            {
                note.State = "miss"; Emit(note, "MISS", "head", d.TimeUs);
                if (note.EndUs.HasValue) ResolveHold(note, false, d.TimeUs);
            }
            else if (!d.Miss && note.HoldState == "holding") ResolveHold(note, _pressed[note.Lane], d.TimeUs);
        }
    }
    private void Apply(InputEvent e)
    {
        RunDeadlines(e.TimeUs, false);
        Inputs.Add(Clone(e));
        if (_pressed[e.Lane] == e.Pressed) { RunDeadlines(e.TimeUs); return; }
        _pressed[e.Lane] = e.Pressed;
        if (!e.Pressed)
        {
            if (_activeHolds[e.Lane] is { } hold) ResolveHold(hold, SongDelta(e.TimeUs, hold.EndUs!.Value) >= 0m, e.TimeUs);
        }
        else
        {
            var note = Notes.FirstOrDefault(n => n.Lane == e.Lane && n.State == "pending");
            var grade = note is null ? null : Grade(e.TimeUs, note.TimeUs);
            if (grade is null) ExtraInputs++;
            else
            {
                note!.State = "hit";
                Emit(note, grade, "head", e.TimeUs, (double)(((decimal)e.TimeUs - note.TimeUs) / Rate - InputOffsetUs));
                if (note.EndUs.HasValue)
                {
                    var delta = SongDelta(e.TimeUs, note.EndUs.Value);
                    if (delta > 0m) ResolveHold(note, false, e.TimeUs);
                    else if (delta == 0m) ResolveHold(note, true, e.TimeUs);
                    else { note.HoldState = "holding"; _activeHolds[e.Lane] = note; }
                }
            }
        }
        RunDeadlines(e.TimeUs);
    }
    public IReadOnlyList<Judgment> Input(int lane, bool pressed, long songTimeUs)
    {
        if (lane is < 0 or > 3) throw new ArgumentOutOfRangeException(nameof(lane));
        if (_finished) return [];
        var before = Judgments.ToList();
        var e = new InputEvent(lane, pressed, songTimeUs);
        _events.Add((e, _serial++));
        if (songTimeUs < _cursor)
        {
            ResetState();
            foreach (var entry in _events.OrderBy(entry => entry.Input.TimeUs).ThenBy(entry => entry.Serial)) Apply(entry.Input);
            RunDeadlines(_cursor);
        }
        else { Apply(e); _cursor = songTimeUs; }
        return Changed(before);
    }
    public IReadOnlyList<Judgment> Advance(long songTimeUs)
    {
        if (_finished || songTimeUs < _cursor) return [];
        _cursor = songTimeUs;
        if (_deadlineIndex >= _deadlines.Count || _deadlines[_deadlineIndex].TimeUs > songTimeUs) return [];
        var before = Judgments.ToList(); RunDeadlines(songTimeUs); return Changed(before);
    }
    private List<Judgment> Changed(List<Judgment> before)
    {
        var old = before.ToDictionary(j => (j.NoteId, j.Kind));
        return Judgments.Where(j => !old.TryGetValue((j.NoteId, j.Kind), out var p) || p.Grade != j.Grade || p.TimeUs != j.TimeUs || p.DeltaUs != j.DeltaUs).Select(Clone).ToList();
    }
    public RunSummary Snapshot()
    {
        var counts = Grades.ToDictionary(g => g, _ => 0);
        decimal weight = 0;
        foreach (var j in Judgments) { counts[j.Grade]++; weight += Weights[j.Grade]; }
        return new RunSummary { Accuracy = Judgments.Count > 0 ? (double)(weight * 100m / Judgments.Count) : 0,
            Score = TotalUnits > 0 ? (int)decimal.Round(weight * 1_000_000m / TotalUnits, 0, MidpointRounding.AwayFromZero) : 0,
            Combo = Combo, MaxCombo = MaxCombo, TotalUnits = TotalUnits, JudgedUnits = Judgments.Count,
            ExtraInputs = ExtraInputs, Counts = counts, Judgments = Judgments.Select(Clone).ToList(), Inputs = Inputs.Select(Clone).ToList() };
    }
    public RunSummary Finish()
    {
        if (!_finished)
        {
            RunDeadlines(Math.Max(ToUs, Math.Max(_cursor, _deadlines.Count > 0 ? _deadlines[^1].TimeUs : ToUs)));
            _finished = true;
        }
        return Snapshot();
    }
    private static InputEvent Clone(InputEvent e) => new(e.Lane, e.Pressed, e.TimeUs);
    private static Judgment Clone(Judgment j) => new() { NoteId = j.NoteId, Lane = j.Lane, TimeUs = j.TimeUs, TargetUs = j.TargetUs, DeltaUs = j.DeltaUs, Grade = j.Grade, Kind = j.Kind, Pattern = j.Pattern };
}
