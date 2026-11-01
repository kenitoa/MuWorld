using MuWorld.Native.Core;
using MuWorld.Native.Audio;
using System.Text.Json;
using System.Text.Json.Nodes;

var failures = new List<string>(); var passed = 0;
void Run(string name, Action action)
{
    try { action(); passed++; Console.WriteLine($"PASS {name}"); }
    catch (Exception ex) { failures.Add(name + ": " + ex); Console.WriteLine($"FAIL {name}: {ex.Message}"); }
}
void Equal<T>(T expected, T actual, string? message = null)
{ if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception(message ?? $"Expected {expected}, got {actual}"); }
void True(bool value, string message = "Expected true") { if (!value) throw new Exception(message); }
void Near(double expected, double actual, double tolerance = 0.000001) { if (Math.Abs(expected - actual) > tolerance) throw new Exception($"Expected {expected}, got {actual}"); }
void Throws(Action action) { try { action(); } catch { return; } throw new Exception("Expected validation failure"); }
Note N(string id, int lane, long time, long? end = null) => new() { Id = id, Lane = lane, TimeUs = time, EndUs = end, Pattern = end.HasValue ? "hold" : "single" };
Chart C(params Note[] notes) => new() { Id = "test-v1", SongId = "glasshouse", Difficulty = "normal", Version = "1", DurationUs = 5_000_000, Bpm = 100, Notes = notes.ToList() };
Settings S(long offset = 0, decimal scale = 1m) => new() { InputOffsetUs = offset, WindowScale = scale };
string Json<T>(T value) => JsonSerializer.Serialize(value);
PlayRecord Record(string id = "run-1", Settings? settings = null)
{
    var s = settings ?? S(5000); var chart = C(N("a", 0, 1_000_000));
    var engine = new RhythmEngine(chart, s); engine.Input(0, true, 1_000_000 + s.InputOffsetUs);
    var summary = engine.Finish();
    return new PlayRecord { Id = id, SongId = chart.SongId, ChartId = chart.Id, ChartVersion = "1", Difficulty = "normal", Mode = "standard", Rate = 1,
        InputOffsetUs = s.InputOffsetUs, Settings = s.Clone(), Summary = summary, Analysis = Analytics.Analyze(summary, chart), Completed = true, ToUs = chart.DurationUs,
        Environment = new() { ["deviceId"] = "test-device", ["sampleRate"] = "48000" } };
}
string TempDirectory()
{ var path = Path.Combine(Path.GetTempPath(), "MuWorldNative-CoreTest-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(path); return path; }

Run("5ms setting subtracts exactly 5000us once", () => {
    var e = new RhythmEngine(C(N("a", 0, 1_000_000)), S(5000));
    var hit = e.Input(0, true, 1_007_000).Single(); Equal(2000d, hit.DeltaUs!.Value); Equal(5000L, e.InputOffsetUs);
});
Run("every symmetric judgment boundary is inclusive without epsilon", () => {
    var cases = new[] { (20_000L, "PERFECT", "GREAT"), (45_000L, "GREAT", "GOOD"), (80_000L, "GOOD", "BAD"), (120_000L, "BAD", "MISS") };
    foreach (var (boundary, grade, next) in cases) foreach (var sign in new[] { -1, 1 }) {
        var e = new RhythmEngine(C(N("a", 0, 1_000_000)), S()); Equal(grade, e.Input(0, true, 1_000_000 + sign * boundary).Single().Grade);
        var other = new RhythmEngine(C(N("a", 0, 1_000_000)), S()); var result = other.Input(0, true, 1_000_000 + sign * (boundary + 1));
        Equal(next == "MISS" && sign < 0 ? 0 : 1, result.Count);
        if (result.Count > 0) Equal(next, result.Single().Grade);
    }
});
Run("rate uses exact real-time windows and offset remains real microseconds", () => {
    foreach (var rate in new[] { .5m, .8m, 1m, 1.1m, 1.25m, 2m }) {
        var e = new RhythmEngine(C(N("a", 0, 1_000_000)), S(5000), rate);
        var raw = 1_000_000 + (long)(25_000m * rate); var hit = e.Input(0, true, raw).Single();
        Equal("PERFECT", hit.Grade); Equal(20_000d, hit.DeltaUs!.Value);
        var outside = new RhythmEngine(C(N("a", 0, 1_000_000)), S(5000), rate);
        Equal("GREAT", outside.Input(0, true, raw + 1).Single().Grade);
    }
});
Run("fractional decimal rate never rounds a comparison into a different grade", () => {
    const decimal rate = .3333333333333333333333333333m;
    var boundary = 20_000m * rate;
    var raw = 1_000_000 + (long)decimal.Floor(boundary);
    Equal("PERFECT", new RhythmEngine(C(N("a", 0, 1_000_000)), S(), rate).Input(0, true, raw).Single().Grade);
    Equal("GREAT", new RhythmEngine(C(N("a", 0, 1_000_000)), S(), rate).Input(0, true, raw + 1).Single().Grade);
});
Run("precision practice 0.25 gives an exact plus/minus 5ms PERFECT window", () => {
    foreach (var sign in new[] { -1, 1 }) {
        Equal("PERFECT", new RhythmEngine(C(N("a", 0, 1_000_000)), S(0, .25m)).Input(0, true, 1_000_000 + sign * 5000).Single().Grade);
        Equal("GREAT", new RhythmEngine(C(N("a", 0, 1_000_000)), S(0, .25m)).Input(0, true, 1_000_000 + sign * 5001).Single().Grade);
    }
});
Run("negative offsets and fractional negative deadlines use floor correctly", () => {
    var e = new RhythmEngine(C(N("a", 0, 0)), S(-200_001), .8m);
    Equal(0, e.Advance(-64_001).Count); Equal("MISS", e.Advance(-64_000).Single().Grade);
    var valid = new RhythmEngine(C(N("a", 0, 0)), S(-200_001), .8m);
    Equal("BAD", valid.Input(0, true, -64_001).Single().Grade);
});
Run("earliest pending lane note wins and repeated downs never hit another note", () => {
    var e = new RhythmEngine(C(N("a", 0, 1_000_000), N("b", 0, 1_030_000)), S());
    Equal("a", e.Input(0, true, 1_025_000).Single().NoteId); Equal(0, e.Input(0, true, 1_030_000).Count);
    e.Input(0, false, 1_031_000); Equal("b", e.Input(0, true, 1_032_000).Single().NoteId);
    Equal(0, e.ExtraInputs);
    var extra = new RhythmEngine(C(N("a", 0, 1_000_000)), S()); extra.Input(0, true, 0); extra.Input(0, true, 1_000_000); Equal(1, extra.ExtraInputs);
});
Run("same-frame up/down and simultaneous lanes keep distinct timestamps", () => {
    var e = new RhythmEngine(C(N("a", 0, 1_000_000), N("b", 0, 1_010_000), N("c", 1, 1_000_000)), S());
    e.Input(0, true, 1_000_000); e.Input(0, false, 1_001_000); e.Input(0, true, 1_010_000); e.Input(1, true, 1_003_000);
    Equal("a,c,b", string.Join(",", e.Judgments.Select(j => j.NoteId))); Equal(3000d, e.Judgments[1].DeltaUs!.Value);
});
Run("hold completes at corrected end and late release adds no penalty", () => {
    foreach (var offset in new long[] { -5000, 0, 5000 }) {
        var e = new RhythmEngine(C(N("h", 0, 1_000_000, 2_000_000)), S(offset)); e.Input(0, true, 1_000_000 + offset);
        Equal(0, e.Advance(1_999_999 + offset).Count); var tail = e.Input(0, false, 2_000_000 + offset).Single();
        Equal("hold", tail.Kind); Equal("PERFECT", tail.Grade); True(tail.DeltaUs is null); Equal(1_000_000, e.Finish().Score);
    }
});
Run("one microsecond early release breaks sustain permanently", () => {
    var e = new RhythmEngine(C(N("h", 0, 1_000_000, 2_000_000)), S()); e.Input(0, true, 1_000_000);
    Equal("MISS", e.Input(0, false, 1_999_999).Single().Grade); e.Input(0, true, 2_000_000); e.Advance(2_100_000);
    Equal("broken", e.Notes[0].HoldState); Equal(2, e.Judgments.Count);
});
Run("missed hold head fails both units and short late head earns no sustain", () => {
    var e = new RhythmEngine(C(N("h", 0, 1_000_000, 2_000_000)), S()); Equal(2, e.Advance(1_120_001).Count); Equal(2, e.Finish().Counts["MISS"]);
    var shortHold = new RhythmEngine(C(N("h", 0, 1_000_000, 1_050_000)), S()); var judgments = shortHold.Input(0, true, 1_060_000);
    Equal("GOOD", judgments[0].Grade); Equal("MISS", judgments[1].Grade);
});
Run("late input timestamps reconcile previously displayed MISS and sustain success", () => {
    var e = new RhythmEngine(C(N("h", 0, 1_000_000, 2_000_000)), S()); e.Advance(2_100_000);
    Equal(2, e.Snapshot().Counts["MISS"]); e.Input(0, true, 1_000_000); Equal(2, e.Snapshot().Counts["PERFECT"]);
    Equal("MISS", e.Input(0, false, 1_999_999).Single().Grade); Equal(1, e.Snapshot().Counts["MISS"]); Equal(1, e.MaxCombo);
});
Run("replay is identical at 30/60/120/240fps including stalled-frame delivery", () => {
    var chart = C(N("a", 0, 1_000_000), N("h", 1, 1_100_000, 1_500_000), N("m", 2, 1_180_000), N("b", 0, 1_600_000));
    InputEvent[] inputs = [new(0, true, 1_000_000), new(0, false, 1_010_000), new(1, true, 1_100_000), new(1, false, 1_450_000), new(0, true, 1_605_000)];
    var normal = new RhythmEngine(chart, S()); foreach (var e in inputs) normal.Input(e.Lane, e.Pressed, e.TimeUs); var expected = Json(normal.Finish());
    foreach (var fps in new[] { 30, 60, 120, 240 }) {
        var replay = new RhythmEngine(chart, S()); var index = 0;
        for (long frame = 0; frame <= 2_000_000; frame += 1_000_000 / fps) {
            var now = frame > 1_050_000 && frame < 1_400_000 ? 1_400_000 : frame; replay.Advance(now);
            while (index < inputs.Length && inputs[index].TimeUs <= now) { var e = inputs[index++]; replay.Input(e.Lane, e.Pressed, e.TimeUs); }
        }
        Equal(expected, Json(replay.Finish()), $"Replay differed at {fps}fps");
    }
});
Run("practice boundaries exclude incomplete holds and clone the source chart", () => {
    var original = C(N("before", 0, 100_000, 600_000), N("start", 1, 500_000), N("inside", 2, 700_000, 1_500_000), N("cross", 3, 900_000, 1_600_000), N("end", 0, 1_500_000));
    var e = new RhythmEngine(original, S(), 1, 500_000, 1_500_000); Equal("start,inside", string.Join(",", e.Notes.Select(n => n.Id))); Equal(3, e.TotalUnits);
    e.Notes[0].TimeUs = 123; Equal(500_000L, original.Notes[1].TimeUs);
});
Run("weighted score, authoritative snapshot copies and finish immutability", () => {
    var e = new RhythmEngine(C(N("p", 0, 500_000), N("g", 1, 1_000_000), N("o", 2, 1_500_000), N("b", 3, 2_000_000), N("m", 0, 2_500_000)), S());
    e.Input(0, true, 500_000); e.Input(1, true, 1_030_000); e.Input(2, true, 1_560_000); e.Input(3, true, 2_100_000);
    var summary = e.Finish(); Equal(54d, summary.Accuracy); Equal(540_000, summary.Score); Equal(4, summary.MaxCombo); Equal(0, summary.Combo);
    summary.Judgments[0].Grade = "MISS"; summary.Inputs.Clear(); Equal(1, e.Snapshot().Counts["PERFECT"]); Equal(4, e.Inputs.Count); Equal(0, e.Input(0, false, 3_000_000).Count);
});
Run("empty charts and invalid configurations are explicit", () => {
    Equal(0d, new RhythmEngine(C(), S()).Finish().Accuracy);
    Throws(() => new RhythmEngine(C(), S(), 0)); Throws(() => new RhythmEngine(C(), S(), 1, 3, 2));
    Throws(() => new RhythmEngine(C(N("a", 0, 1), N("a", 1, 2)), S())); Throws(() => new RhythmEngine(C(), S()).Input(4, true, 0));
});

Run("statistics exclude MISS/sustain and calculate population spread plus nearest-rank p95", () => {
    var chart = C(N("a", 0, 1_000_000), N("b", 1, 1_000_000), N("c", 2, 1_000_000), N("miss", 3, 1_000_000), N("hold", 0, 2_000_000, 3_000_000));
    var e = new RhythmEngine(chart, S()); e.Input(0, true, 990_000); e.Input(1, true, 1_000_000); e.Input(2, true, 1_010_000); e.Input(0, false, 1_100_000);
    e.Input(0, true, 2_000_000); e.Advance(3_000_000); var a = Analytics.Analyze(e.Finish(), chart);
    Equal(4, a.Timing.Count); Equal(1, a.MissCount); Equal(5, a.HeadCount); Equal(0d, a.Timing.MeanMs!.Value); Equal(5d, a.Timing.MaeMs!.Value);
    Near(Math.Sqrt(50), a.Timing.StdMs!.Value); Equal(5d, a.Timing.MadMs!.Value); Equal(10d, a.Timing.P95Ms!.Value); Equal(1, a.Holds.Evaluated); Equal(100d, a.Holds.SuccessRate!.Value);
    Equal(1, a.Chords.Count); Equal(0, a.Chords.MeasuredCount); True(a.Chords.MedianSpreadMs is null); Equal(4, a.Histogram.Sum(b => b.Count));
});
Run("fully hit chords measure corrected real-time lane spread", () => {
    var chart = C(N("a", 0, 1_000_000), N("b", 1, 1_000_000)); var e = new RhythmEngine(chart, S(), .5m);
    e.Input(0, true, 995_000); e.Input(1, true, 1_005_000); var a = Analytics.Analyze(e.Finish(), chart);
    Equal(1, a.Chords.Count); Equal(1, a.Chords.MeasuredCount); Equal(20d, a.Chords.MedianSpreadMs!.Value);
});
Run("empty timing data stays unknown rather than fabricated zero", () => {
    var a = Analytics.Analyze(new RunSummary(), C()); Equal(0, a.Timing.Count); True(a.Timing.MeanMs is null); True(a.Timing.P95Ms is null);
    Equal(4, a.Lanes.Count); True(a.Lanes.All(l => l.MissRate is null)); True(a.Holds.SuccessRate is null);
});
Run("segment statistics use weighted units and recommendations cite actual misses", () => {
    var chart = C(N("a", 0, 1_000_000), N("b", 1, 2_000_000), N("c", 2, 3_000_000), N("d", 3, 4_000_000));
    var a = Analytics.Analyze(new RhythmEngine(chart, S()).Finish(), chart); Equal(4, a.MissCount); Equal(0d, a.Segments[0].Accuracy!.Value);
    True(a.Recommendations[0].Body.Contains("4개")); Equal(0L, a.Recommendations[0].FromUs); Equal(5_000_000L, a.Recommendations[0].ToUs);
});
Run("growth comparison excludes interruptions and separates rate, window, offset and devices", () => {
    PlayRecord a = Record("a"), b = Record("b"), interrupted = Record("i"), device = Record("d"), practice = Record("p"), window = Record("w"), rate = Record("r");
    interrupted.Interrupted = true; device.Environment["deviceId"] = "other"; practice.Mode = "practice"; window.Settings.WindowScale = .25m; rate.Rate = .8m;
    var groups = Analytics.CompareRuns([a, b, interrupted, device, practice, window, rate]); Equal(4, groups.Count); Equal(5, groups.Sum(g => g.Count)); True(groups.Any(g => g.Count == 2));
});
Run("CSV exports raw judgments with empty missing deltas and formula-safe text", () => {
    var r = Record(); r.Summary.Judgments[0].NoteId = "=HYPERLINK(1)"; r.Summary.Judgments[0].DeltaUs = -5000;
    var csv = Analytics.ToCsv(r); True(csv.StartsWith('\uFEFF')); True(csv.Contains("\"'=HYPERLINK(1)\"")); True(csv.Contains("\"-5000\""));
});

Run("atomic JSON roundtrip preserves settings, precision, records, favorites and profiles", () => {
    var directory = TempDirectory(); var store = new LocalStore(directory); store.Settings.InputOffsetUs = 5000; store.Settings.WindowScale = .25m; store.Favorites.Add("glasshouse");
    store.Profiles.Add(new CalibrationProfile { Name = "유선 헤드폰", InputOffsetUs = 5000, VisualOffsetUs = -7001, DeviceId = "wasapi-id", DeviceName = "USB DAC" }); store.Save();
    store.AddRecord(Record(settings: store.Settings)); var loaded = new LocalStore(directory);
    Equal(5000L, loaded.Settings.InputOffsetUs); Equal(.25m, loaded.Settings.WindowScale); Equal(1, loaded.Records.Count); Equal(.25m, loaded.Records[0].Settings.WindowScale);
    True(loaded.Favorites.Contains("glasshouse")); Equal(-7001L, loaded.Profiles[0].VisualOffsetUs); Equal("wasapi-id", loaded.Profiles[0].DeviceId);
    var export = Path.Combine(directory, "export.json"); loaded.ExportBackup(export); var imported = new LocalStore(Path.Combine(directory, "imported")); imported.ImportBackup(export);
    Equal(Json(loaded.Settings), Json(imported.Settings)); Equal(Json(loaded.Records), Json(imported.Records)); Equal(1, imported.Profiles.Count);
});
Run("invalid imports cannot partially change settings or existing records", () => {
    var directory = TempDirectory(); var store = new LocalStore(directory); store.Settings.InputOffsetUs = 5000; store.AddRecord(Record());
    var before = File.ReadAllText(store.LibraryPath); var export = Path.Combine(directory, "bad.json"); store.ExportBackup(export);
    var node = JsonNode.Parse(File.ReadAllText(export))!; node["settings"]!["inputOffsetUs"] = 5000.5; File.WriteAllText(export, node.ToJsonString());
    Throws(() => store.ImportBackup(export)); Equal(before, File.ReadAllText(store.LibraryPath)); Equal(5000L, store.Settings.InputOffsetUs); Equal(1, store.Records.Count);
    node["settings"]!["inputOffsetUs"] = 7000; node["version"] = 999; File.WriteAllText(export, node.ToJsonString()); Throws(() => store.ImportBackup(export)); Equal(before, File.ReadAllText(store.LibraryPath));
    File.WriteAllText(export, "{}"); Throws(() => store.ImportBackup(export)); Equal(before, File.ReadAllText(store.LibraryPath)); Equal(1, store.Records.Count);
});
Run("invalid precision, duplicate keys and contradictory judgment totals are rejected", () => {
    var store = new LocalStore(TempDirectory()); store.Save(); var before = File.ReadAllText(store.LibraryPath);
    store.Settings.WindowScale = .249m; Throws(store.Save); Equal(before, File.ReadAllText(store.LibraryPath)); store.Settings.WindowScale = 1;
    store.Settings.Keys = [0x44, 0x44, 0x4A, 0x4B]; Throws(store.Save); Equal(before, File.ReadAllText(store.LibraryPath)); store.Settings.Keys = [0x44, 0x46, 0x4A, 0x4B];
    var bad = Record(); bad.Summary.Judgments[0].Grade = "MISS"; bad.Summary.Judgments[0].DeltaUs = null; Throws(() => store.AddRecord(bad)); Equal(0, store.Records.Count);
});
Run("native contrast preset saves with the UI's exact preset identifier", () => {
    var store = new LocalStore(TempDirectory()); store.Settings.Preset = "contrast"; store.Settings.HighContrast = true; store.Save();
    var reloaded = new LocalStore(store.DirectoryPath); Equal("contrast", reloaded.Settings.Preset); True(reloaded.Settings.HighContrast);
});
Run("corrupt primary recovers previous valid backup without destroying it", () => {
    var directory = TempDirectory(); var store = new LocalStore(directory); store.Settings.InputOffsetUs = 5000; store.Save(); store.Settings.InputOffsetUs = 7000; store.Save();
    File.WriteAllText(store.LibraryPath, "{broken"); var recovered = new LocalStore(directory); Equal(5000L, recovered.Settings.InputOffsetUs); True(recovered.LoadWarning is not null);
    recovered.Settings.VisualOffsetUs = -3000; recovered.Save(); Equal(5000L, new LocalStore(directory).Settings.InputOffsetUs); True(File.ReadAllText(recovered.BackupPath).Contains("5000"));
});
Run("locked-file save failure keeps prior JSON and does not append an in-memory record", () => {
    var store = new LocalStore(TempDirectory()); store.Save(); var before = File.ReadAllText(store.LibraryPath);
    using (var locked = new FileStream(store.LibraryPath, FileMode.Open, FileAccess.Read, FileShare.None)) Throws(() => store.AddRecord(Record()));
    Equal(before, File.ReadAllText(store.LibraryPath)); Equal(0, store.Records.Count);
});
Run("record deletion and backup duplication checks are transactional", () => {
    var store = new LocalStore(TempDirectory()); store.AddRecord(Record()); var export = Path.Combine(store.DirectoryPath, "duplicate.json"); store.ExportBackup(export);
    var node = JsonNode.Parse(File.ReadAllText(export))!; node["records"]!.AsArray().Add(node["records"]![0]!.DeepClone()); File.WriteAllText(export, node.ToJsonString());
    Throws(() => store.ImportBackup(export)); Equal(1, store.Records.Count); True(store.RemoveRecord("run-1")); True(!store.RemoveRecord("run-1")); Equal(0, new LocalStore(store.DirectoryPath).Records.Count);
});
Run("saved real input log reconstructs the same native judgment and analysis", () => {
    var chart = C(N("h", 0, 0, 1_000_000), N("a", 1, 500_000)); var settings = S(-5000); var e = new RhythmEngine(chart, settings, .8m);
    e.Input(0, true, -4000); e.Input(1, true, 496_000); e.Input(0, false, 996_000); var expected = e.Finish();
    var r = Record(); r.Settings = settings; r.InputOffsetUs = -5000; r.Rate = .8m; r.Mode = "practice"; r.Summary = expected; r.Analysis = Analytics.Analyze(expected, chart);
    var store = new LocalStore(TempDirectory()); store.AddRecord(r); var loaded = new LocalStore(store.DirectoryPath).Records.Single(); var replay = new RhythmEngine(chart, loaded.Settings, loaded.Rate);
    replay.Advance(2_000_000); foreach (var input in loaded.Summary.Inputs) replay.Input(input.Lane, input.Pressed, input.TimeUs);
    Equal(Json(expected), Json(replay.Finish())); Equal(100d, loaded.Analysis.Holds.SuccessRate!.Value); Equal(-4000L, loaded.Summary.Inputs[0].TimeUs);
});

Run("WASAPI frequency and QPC tick units map into the same original-song microseconds", () => {
    var time = AudioClockMath.SongTimeUs(192_000, 96_000, 100_000_000, 10_005_000_000, 1_000_000_000, 48_000, 4_000_000, .8m);
    Equal(4_804_000L, time);
    var chart = C(N("clock-tap", 0, 4_800_000)); var engine = new RhythmEngine(chart, S(5000), .8m);
    Equal(0d, engine.Input(0, true, time).Single().DeltaUs!.Value);
});
Run("output clock mapping preserves historical queued input rather than processing time", () => {
    var historical = AudioClockMath.SongTimeUs(192_000, 96_000, 100_000_000, 9_750_000_000, 1_000_000_000, 48_000, 4_000_000, .8m);
    Equal(4_600_000L, historical);
    var now = AudioClockMath.SongTimeUs(192_000, 96_000, 100_000_000, 10_250_000_000, 1_000_000_000, 48_000, 4_000_000, .8m);
    Equal(400_000L, now - historical);
});
Run("replacing a correlated device anchor does not jump the song clock", () => {
    var oldAnchor = AudioClockMath.SongTimeUs(480_000, 48_000, 100_000_000, 10_250_000_000, 1_000_000_000, 480_000, 2_000_000, .5m);
    var newAnchor = AudioClockMath.SongTimeUs(492_000, 48_000, 102_500_000, 10_250_000_000, 1_000_000_000, 480_000, 2_000_000, .5m);
    Equal(2_125_000L, oldAnchor); Equal(oldAnchor, newAnchor);
});
Run("native audio lead-in and trailing time remain visible to judgment", () => {
    var lead = AudioClockMath.SongTimeUs(48_000, 48_000, 100_000_000, 10_000_000_000, 1_000_000_000, 144_000, 0, 1m);
    Equal(-2_000_000L, lead);
    var trailing = AudioClockMath.SongTimeUs(384_000, 48_000, 100_000_000, 10_240_000_000, 1_000_000_000, 144_000, 0, 1m);
    Equal(5_240_000L, trailing);
    Throws(() => AudioClockMath.SongTimeUs(0, 0, 0, 0, 1, 0, 0, 1));
});
Run("native synthesis is deterministic PCM and tempo changes preserve duration scaling", () => {
    var song = new Song { Id = "synth-test", Bpm = 120, Bars = 1, DurationSec = 3, Chords = [[60, 64, 67, 71]], Melody = [0, 1, 2, 3] };
    var normal = Synthesizer.Render(song, 1m, 8000); var repeat = Synthesizer.Render(song, 1m, 8000); var slow = Synthesizer.Render(song, .5m, 8000);
    Equal(24_000, normal.FrameCount); Equal(48_000, slow.FrameCount); True(normal.Samples.SequenceEqual(repeat.Samples));
    True(normal.Samples.Any(v => Math.Abs(v) > .001)); True(normal.Samples.All(float.IsFinite));
    using var cancel = new CancellationTokenSource(); cancel.Cancel(); Throws(() => Synthesizer.Render(song, 1, 8000, cancel.Token));
});
Run("slower native music preserves the measured musical pitch rather than resampling it", () => {
    var song = new Song { Id = "pitch-test", Bpm = 120, Bars = 1, DurationSec = 3, Chords = [[69]], Melody = [0] };
    var normal = Synthesizer.Render(song, 1m, 8000); var slow = Synthesizer.Render(song, .8m, 8000);
    double Energy(SynthBuffer buffer, double frequency)
    {
        var start = (int)(.03 * buffer.SampleRate); var count = (int)(.2 * buffer.SampleRate); double real = 0, imaginary = 0;
        for (var i = 0; i < count; i++) {
            var sample = buffer.Samples[(start + i) * 2] * (.5 - .5 * Math.Cos(2 * Math.PI * i / (count - 1)));
            var phase = 2 * Math.PI * frequency * (start + i) / buffer.SampleRate;
            real += sample * Math.Cos(phase); imaginary += sample * Math.Sin(phase);
        }
        return real * real + imaginary * imaginary;
    }
    double BassPitch(SynthBuffer buffer) => Enumerable.Range(0, 161).Select(i => 180 + i * .5).MaxBy(frequency => Energy(buffer, frequency));
    Near(220, BassPitch(normal), 1); Near(220, BassPitch(slow), 1);
    True(Energy(slow, 220) > Energy(slow, 176) * 20, "A resampled 0.8x A3 bass would shift to 176Hz; native synthesis must remain at 220Hz.");
});

Console.WriteLine($"\nNative core self-tests: {passed} passed, {failures.Count} failed");
if (failures.Count > 0) { foreach (var failure in failures) Console.Error.WriteLine(failure); Environment.ExitCode = 1; }
