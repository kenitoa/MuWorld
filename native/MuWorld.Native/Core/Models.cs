using System;
using System.Collections.Generic;
using System.Linq;

namespace MuWorld.Native.Core;

public class Song
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Subtitle { get; set; } = "";
    public string Artist { get; set; } = "MuWorld";
    public string Genre { get; set; } = "";
    public string Description { get; set; } = "";
    public string Color { get; set; } = "#a7bc8c";
    public string Accent { get; set; } = "#bbb4db";
    public int Bpm { get; set; }
    public int Bars { get; set; }
    public double DurationSec { get; set; }
    public int Art { get; set; }
    public string[] Tags { get; set; } = [];
    public Dictionary<string, int> Level { get; set; } = new();
    public int[][] Chords { get; set; } = [];
    public int[] Melody { get; set; } = [];
    public string Groove { get; set; } = "";
}

public class Note
{
    public string Id { get; set; } = "";
    public int Lane { get; set; }
    public long TimeUs { get; set; }
    public long? EndUs { get; set; }
    public string Pattern { get; set; } = "single";
}

public class NoteState : Note
{
    public string State { get; set; } = "pending";
    public string? HoldState { get; set; }
    public NoteState() { }
    public NoteState(Note source)
    {
        Id = source.Id; Lane = source.Lane; TimeUs = source.TimeUs;
        EndUs = source.EndUs; Pattern = source.Pattern;
        HoldState = EndUs.HasValue ? "pending" : null;
    }
}

public class Chart
{
    public string Id { get; set; } = "";
    public string SongId { get; set; } = "";
    public string Difficulty { get; set; } = "normal";
    public string Version { get; set; } = "1";
    public long DurationUs { get; set; }
    public int Bpm { get; set; }
    public List<Note> Notes { get; set; } = [];
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Focus { get; set; } = "";
    public long FromUs { get; set; }
    public long ToUs { get; set; }
}

public class Settings
{
    public int[] Keys { get; set; } = [0x44, 0x46, 0x4A, 0x4B];
    public long InputOffsetUs { get; set; }
    public long VisualOffsetUs { get; set; }
    public int ApproachMs { get; set; } = 1200;
    public double Volume { get; set; } = .7;
    public double EffectVolume { get; set; } = .3;
    public double LaneWidth { get; set; } = 100;
    public double NoteBrightness { get; set; } = 90;
    public double BackgroundBrightness { get; set; } = 100;
    public double Effects { get; set; } = 25;
    public bool ShowTiming { get; set; }
    public bool ShowCombo { get; set; } = true;
    public bool HighContrast { get; set; }
    public double UiScale { get; set; } = 100;
    public string Preset { get; set; } = "comfort";
    public decimal PracticeRate { get; set; } = .8m;
    public decimal WindowScale { get; set; } = 1m;
    public double PracticeFromSec { get; set; }
    public double PracticeToSec { get; set; } = 20;
    public bool AutoStep { get; set; }
    public bool Metronome { get; set; }
    public Settings Clone()
    {
        var copy = (Settings)MemberwiseClone();
        copy.Keys = Keys.ToArray();
        return copy;
    }
}

public class InputEvent
{
    public int Lane { get; set; }
    public bool Pressed { get; set; }
    public long TimeUs { get; set; }
    public InputEvent() { }
    public InputEvent(int lane, bool pressed, long timeUs) { Lane = lane; Pressed = pressed; TimeUs = timeUs; }
}

public class Judgment
{
    public string NoteId { get; set; } = "";
    public int Lane { get; set; }
    public long TimeUs { get; set; }
    public long TargetUs { get; set; }
    public double? DeltaUs { get; set; }
    public string Grade { get; set; } = "MISS";
    public string Kind { get; set; } = "head";
    public string Pattern { get; set; } = "single";
}

public class RunSummary
{
    public double Accuracy { get; set; }
    public int Score { get; set; }
    public int Combo { get; set; }
    public int MaxCombo { get; set; }
    public int TotalUnits { get; set; }
    public int JudgedUnits { get; set; }
    public int ExtraInputs { get; set; }
    public Dictionary<string, int> Counts { get; set; } = new() { ["PERFECT"] = 0, ["GREAT"] = 0, ["GOOD"] = 0, ["BAD"] = 0, ["MISS"] = 0 };
    public List<Judgment> Judgments { get; set; } = [];
    public List<InputEvent> Inputs { get; set; } = [];
}

public class TimingStats
{
    public int Count { get; set; }
    public double? MeanMs { get; set; }
    public double? MedianMs { get; set; }
    public double? MaeMs { get; set; }
    public double? StdMs { get; set; }
    public double? MadMs { get; set; }
    public double? P95Ms { get; set; }
    public int Early { get; set; }
    public int Late { get; set; }
    public int Exact { get; set; }
}

public class LaneStats
{
    public int Lane { get; set; }
    public int Count { get; set; }
    public int Misses { get; set; }
    public double? MissRate { get; set; }
    public double? MeanMs { get; set; }
    public double? MaeMs { get; set; }
}

public class PatternStats
{
    public string Pattern { get; set; } = "single";
    public int Count { get; set; }
    public int Misses { get; set; }
    public double? MissRate { get; set; }
    public double? MeanMs { get; set; }
}

public class SegmentStats
{
    public long StartUs { get; set; }
    public long EndUs { get; set; }
    public int Count { get; set; }
    public int Misses { get; set; }
    public double? Accuracy { get; set; }
}

public class HistogramBin
{
    public double FromMs { get; set; }
    public double ToMs { get; set; }
    public int Count { get; set; }
}

public class HoldStats
{
    public int Total { get; set; }
    public int Broken { get; set; }
    public int Evaluated { get; set; }
    public double? SuccessRate { get; set; }
}

public class ChordStats
{
    public int Count { get; set; }
    public int MeasuredCount { get; set; }
    public double? MedianSpreadMs { get; set; }
}

public class Recommendation
{
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public long FromUs { get; set; }
    public long ToUs { get; set; }
}

public class AnalysisResult
{
    public TimingStats Timing { get; set; } = new();
    public List<LaneStats> Lanes { get; set; } = [];
    public List<PatternStats> Patterns { get; set; } = [];
    public List<SegmentStats> Segments { get; set; } = [];
    public List<HistogramBin> Histogram { get; set; } = [];
    public HoldStats Holds { get; set; } = new();
    public ChordStats Chords { get; set; } = new();
    public List<Recommendation> Recommendations { get; set; } = [];
    public int HeadCount { get; set; }
    public int MissCount { get; set; }
    public double? MissRate { get; set; }
}

public class PlayRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string SongId { get; set; } = "";
    public string ChartId { get; set; } = "";
    public string ChartVersion { get; set; } = "1";
    public string Difficulty { get; set; } = "normal";
    public string Mode { get; set; } = "standard";
    public decimal Rate { get; set; } = 1m;
    public long InputOffsetUs { get; set; }
    public Settings Settings { get; set; } = new();
    public RunSummary Summary { get; set; } = new();
    public AnalysisResult Analysis { get; set; } = new();
    public Dictionary<string, string> Environment { get; set; } = new();
    public bool Completed { get; set; }
    public bool Interrupted { get; set; }
    public long FromUs { get; set; }
    public long ToUs { get; set; }
}

public class CalibrationProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "";
    public long InputOffsetUs { get; set; }
    public long VisualOffsetUs { get; set; }
    public int[] Keys { get; set; } = [0x44, 0x46, 0x4A, 0x4B];
    public string DeviceId { get; set; } = "";
    public string DeviceName { get; set; } = "";
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

