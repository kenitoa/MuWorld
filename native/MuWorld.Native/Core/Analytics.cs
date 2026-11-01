using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace MuWorld.Native.Core;

public static class Analytics
{
    private const long SegmentUs = 10_000_000;
    private static double? Mean(IEnumerable<double> values)
    { var a = values.ToArray(); return a.Length == 0 ? null : a.Average(); }
    private static double? Median(IEnumerable<double> values)
    { var a = values.Order().ToArray(); return a.Length == 0 ? null : a.Length % 2 == 1 ? a[a.Length / 2] : (a[a.Length / 2 - 1] + a[a.Length / 2]) / 2; }
    private static double? Percent(int part, int total) => total == 0 ? null : part * 100d / total;
    private static bool Timed(Judgment j) => j.Kind == "head" && j.Grade != "MISS" && j.DeltaUs.HasValue && double.IsFinite(j.DeltaUs.Value);
    private static double DeltaMs(Judgment j) => j.DeltaUs!.Value / 1000d;

    public static AnalysisResult Analyze(RunSummary summary, Chart chart)
    {
        var all = summary.Judgments.Where(j => j.Lane is >= 0 and <= 3 && RhythmEngine.Weights.ContainsKey(j.Grade)).ToList();
        var heads = all.Where(j => j.Kind == "head").ToList();
        var times = heads.Where(Timed).Select(DeltaMs).ToArray();
        var center = Mean(times); var midpoint = Median(times);
        var absolute = times.Select(Math.Abs).Order().ToArray();
        var timing = new TimingStats
        {
            Count = times.Length, MeanMs = center, MedianMs = midpoint, MaeMs = Mean(absolute),
            StdMs = times.Length == 0 ? null : Math.Sqrt(times.Average(v => (v - center!.Value) * (v - center.Value))),
            MadMs = times.Length == 0 ? null : Median(times.Select(v => Math.Abs(v - midpoint!.Value))),
            P95Ms = absolute.Length == 0 ? null : absolute[(int)Math.Ceiling(absolute.Length * .95) - 1],
            Early = times.Count(v => v < 0), Late = times.Count(v => v > 0), Exact = times.Count(v => v == 0)
        };
        var result = new AnalysisResult { Timing = timing, HeadCount = heads.Count, MissCount = heads.Count(j => j.Grade == "MISS") };
        result.MissRate = Percent(result.MissCount, heads.Count);
        for (var lane = 0; lane < 4; lane++)
        {
            var events = heads.Where(j => j.Lane == lane).ToList(); var deltas = events.Where(Timed).Select(DeltaMs).ToList();
            var misses = events.Count(j => j.Grade == "MISS");
            result.Lanes.Add(new LaneStats { Lane = lane, Count = events.Count, Misses = misses, MissRate = Percent(misses, events.Count), MeanMs = Mean(deltas), MaeMs = Mean(deltas.Select(Math.Abs)) });
        }
        result.Patterns = heads.GroupBy(j => string.IsNullOrWhiteSpace(j.Pattern) ? "unknown" : j.Pattern).Select(group =>
        {
            var events = group.ToList(); var misses = events.Count(j => j.Grade == "MISS");
            return new PatternStats { Pattern = group.Key, Count = events.Count, Misses = misses, MissRate = Percent(misses, events.Count), MeanMs = Mean(events.Where(Timed).Select(DeltaMs)) };
        }).OrderByDescending(p => p.Count).ThenBy(p => p.Pattern, StringComparer.Ordinal).ToList();

        var final = all.Count == 0 ? 0 : all.Max(j => j.TargetUs) + 1;
        var duration = Math.Max(chart.DurationUs, final);
        for (long start = 0; start < duration; start += SegmentUs)
        {
            var end = Math.Min(duration, start + SegmentUs);
            var events = all.Where(j => j.TargetUs >= start && j.TargetUs < end).ToList();
            result.Segments.Add(new SegmentStats { StartUs = start, EndUs = end, Count = events.Count,
                Misses = events.Count(j => j.Grade == "MISS"),
                Accuracy = events.Count == 0 ? null : (double)(events.Sum(j => RhythmEngine.Weights[j.Grade]) * 100m / events.Count) });
        }
        var extent = Math.Min(1000, Math.Max(120, Math.Ceiling((absolute.LastOrDefault()) / 5) * 5));
        for (var from = -extent; from < extent; from += 5) result.Histogram.Add(new HistogramBin { FromMs = from, ToMs = from + 5 });
        foreach (var value in times)
        {
            var index = Math.Clamp((int)Math.Floor((value + extent) / 5), 0, result.Histogram.Count - 1);
            result.Histogram[index].Count++;
        }
        var chords = heads.GroupBy(j => j.TargetUs).Where(g => g.Select(j => j.Lane).Distinct().Count() >= 2).Select(g => g.ToList()).ToList();
        var spreads = chords.Where(group => group.All(Timed)).Select(group => group.Max(DeltaMs) - group.Min(DeltaMs)).ToList();
        result.Chords = new ChordStats { Count = chords.Count, MeasuredCount = spreads.Count, MedianSpreadMs = Median(spreads) };
        var holdIds = chart.Notes.Where(n => n.EndUs.HasValue).Select(n => n.Id).ToHashSet();
        var sustains = all.Where(j => j.Kind == "hold").ToList();
        var broken = sustains.Count(j => j.Grade == "MISS");
        result.Holds = new HoldStats { Total = Math.Max(heads.Count(j => holdIds.Contains(j.NoteId)), sustains.Count), Evaluated = sustains.Count,
            Broken = broken, SuccessRate = Percent(sustains.Count - broken, sustains.Count) };
        var weakest = result.Segments.Where(s => s.Count >= 4).OrderBy(s => s.Accuracy).ThenByDescending(s => s.Misses).ThenBy(s => s.StartUs).FirstOrDefault();
        if (weakest?.Accuracy < 95)
            result.Recommendations.Add(new Recommendation { Title = "실수가 모인 구간 반복", Body = $"{weakest.StartUs / 1_000_000}–{Math.Ceiling(weakest.EndUs / 1_000_000d):0}초 구간 {weakest.Count}개 판정 중 {weakest.Misses}개가 MISS입니다. 낮은 속도로 확인한 뒤 원래 속도로 돌아오세요.", FromUs = Math.Max(0, weakest.StartUs - 2_000_000), ToUs = Math.Min(duration, weakest.EndUs + 2_000_000) });
        if (timing.Count >= 12 && Math.Abs(timing.MedianMs ?? 0) >= 8 && timing.MadMs <= 15)
            result.Recommendations.Add(new Recommendation { Title = "한 방향으로 치우친 타이밍 확인", Body = $"타격 중앙값이 {timing.MedianMs:F1} ms입니다. 연주 습관과 장치·보정 설정을 함께 확인하세요. 이 수치만으로 물리 장치 지연을 확정할 수는 없습니다.", FromUs = 0, ToUs = Math.Min(duration, 20_000_000) });
        if (result.Recommendations.Count < 2 && broken > 0)
        {
            var position = Math.Max(0, sustains.First(j => j.Grade == "MISS").TargetUs);
            result.Recommendations.Add(new Recommendation { Title = "홀드 유지 연습", Body = $"홀드 유지 {sustains.Count}개 중 {broken}개가 끊겼습니다. 누른 상태로 다른 레인을 치는 구간을 확인하세요.", FromUs = Math.Max(0, position - 5_000_000), ToUs = Math.Min(duration, position + 5_000_000) });
        }
        if (result.Recommendations.Count == 0 && heads.Count > 0)
            result.Recommendations.Add(new Recommendation { Title = "같은 조건으로 한 번 더 확인", Body = "같은 채보·속도·보정·장치에서 정확도와 타이밍 산포가 얼마나 일정한지 확인하세요.", FromUs = 0, ToUs = Math.Min(duration, 20_000_000) });
        result.Recommendations = result.Recommendations.Take(2).ToList();
        return result;
    }

    /// <summary>Groups only completed uninterrupted standard runs with identical recorded conditions.</summary>
    public static List<List<PlayRecord>> CompareRuns(IEnumerable<PlayRecord> records) => records
        .Where(r => r.Mode == "standard" && r.Completed && !r.Interrupted)
        .GroupBy(r => string.Join("\u001f", r.SongId, r.ChartId, r.ChartVersion, r.Difficulty, r.Rate.ToString(CultureInfo.InvariantCulture),
            r.InputOffsetUs, r.Settings.WindowScale.ToString(CultureInfo.InvariantCulture), string.Join(",", r.Settings.Keys),
            string.Join(";", r.Environment.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => $"{p.Key.Length}:{p.Key}{p.Value.Length}:{p.Value}"))))
        .Select(g => g.OrderBy(r => r.CreatedAt).ThenBy(r => r.Id, StringComparer.Ordinal).ToList())
        .OrderByDescending(g => g[^1].CreatedAt).ToList();

    public static string ToCsv(PlayRecord record)
    {
        List<object?[]> rows = [ ["MuWorld 기록", "값"], ["id", record.Id], ["createdAt", record.CreatedAt.ToString("O")],
            ["songId", record.SongId], ["chartId", record.ChartId], ["chartVersion", record.ChartVersion], ["difficulty", record.Difficulty],
            ["mode", record.Mode], ["rate", record.Rate], ["inputOffsetUs", record.InputOffsetUs], ["completed", record.Completed],
            ["interrupted", record.Interrupted], ["accuracy", record.Summary.Accuracy], [],
            ["noteId", "lane", "kind", "targetUs", "timeUs", "deltaUs", "grade", "pattern"] ];
        rows.AddRange(record.Summary.Judgments.Select(j => new object?[] { j.NoteId, j.Lane, j.Kind, j.TargetUs, j.TimeUs, j.DeltaUs, j.Grade, j.Pattern }));
        return "\uFEFF" + string.Join("\r\n", rows.Select(row => string.Join(",", row.Select(CsvCell)))) + "\r\n";
    }
    private static string CsvCell(object? value)
    {
        var text = value is IFormattable numeric ? numeric.ToString(null, CultureInfo.InvariantCulture) : value?.ToString() ?? "";
        if (value is string && text.TrimStart().Length > 0 && "=+-@".Contains(text.TrimStart()[0])) text = "'" + text;
        return "\"" + text.Replace("\"", "\"\"") + "\"";
    }
}
