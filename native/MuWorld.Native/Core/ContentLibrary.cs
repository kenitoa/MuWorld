using System.IO;
using System.Text.Json;
using MuWorld.Native.Audio;

namespace MuWorld.Native.Core;

/// <summary>Statically bundled compositions and charts. No script interpreter is used at runtime.</summary>
public sealed class ContentLibrary
{
    public const long BeatUs = 600_000;
    private readonly Dictionary<string, Song> _songs;
    private readonly Dictionary<string, Chart> _charts;
    public IReadOnlyList<Song> Songs { get; }
    public IReadOnlyList<Chart> Drills { get; }

    public ContentLibrary(string? assetPath = null)
    {
        assetPath ??= Path.Combine(AppContext.BaseDirectory, "Assets", "content.json");
        using var stream = File.OpenRead(assetPath);
        var document = JsonSerializer.Deserialize<ContentDocument>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("음악 콘텐츠를 읽을 수 없습니다.");
        if (document.Version != 1 || document.Songs.Count != 5 || document.Charts.Count != 15 || document.Drills.Count != 12)
            throw new InvalidDataException("음악 콘텐츠의 버전 또는 구성이 올바르지 않습니다.");
        if (document.Songs.Select(song => song.Id).Distinct(StringComparer.Ordinal).Count() != document.Songs.Count)
            throw new InvalidDataException("중복된 곡 ID가 있습니다.");
        _songs = document.Songs.ToDictionary(song => song.Id, StringComparer.Ordinal);
        foreach (var song in document.Songs)
        {
            if (string.IsNullOrWhiteSpace(song.Id) || string.IsNullOrWhiteSpace(song.Title) || song.Bpm <= 0 || song.DurationSec <= 0
                || song.Chords.Length == 0 || song.Chords.Any(chord => chord.Length == 0) || song.Melody.Length == 0)
                throw new InvalidDataException("음악 악보에 필요한 정보가 없습니다.");
        }
        _charts = new Dictionary<string, Chart>(StringComparer.Ordinal);
        foreach (var chart in document.Charts)
        {
            ValidateChart(chart);
            if (!_songs.ContainsKey(chart.SongId) || !new[] { "easy", "normal", "hard" }.Contains(chart.Difficulty)
                || !_charts.TryAdd($"{chart.SongId}:{chart.Difficulty}", chart))
                throw new InvalidDataException("채보의 곡 또는 난이도가 올바르지 않습니다.");
        }
        foreach (var drill in document.Drills)
        {
            ValidateChart(drill);
            if (!_songs.ContainsKey(drill.SongId) || drill.FromUs < 0 || drill.ToUs <= drill.FromUs || drill.ToUs > drill.DurationUs)
                throw new InvalidDataException("연습 채보의 구간이 올바르지 않습니다.");
        }
        Songs = document.Songs.AsReadOnly();
        Drills = document.Drills.AsReadOnly();
    }

    public Song GetSong(string id) => _songs.TryGetValue(id, out var song) ? song : throw new KeyNotFoundException($"곡을 찾을 수 없습니다: {id}");

    public Chart GetChart(string songId, string difficulty = "normal") => _charts.TryGetValue($"{songId}:{difficulty}", out var chart)
        ? CloneChart(chart) : throw new KeyNotFoundException($"채보를 찾을 수 없습니다: {songId}/{difficulty}");

    private static Chart CloneChart(Chart chart) => new()
    {
        Id = chart.Id, SongId = chart.SongId, Difficulty = chart.Difficulty, Version = chart.Version,
        DurationUs = chart.DurationUs, Bpm = chart.Bpm, Title = chart.Title, Description = chart.Description, Focus = chart.Focus,
        FromUs = chart.FromUs, ToUs = chart.ToUs,
        Notes = chart.Notes.Select(note => new Note { Id = note.Id, Lane = note.Lane, TimeUs = note.TimeUs, EndUs = note.EndUs, Pattern = note.Pattern }).ToList()
    };

    private static void ValidateChart(Chart chart)
    {
        if (string.IsNullOrWhiteSpace(chart.Id) || chart.DurationUs <= 0 || chart.Bpm <= 0 || chart.Notes.Count == 0)
            throw new InvalidDataException("채보 정보가 올바르지 않습니다.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var laneEnds = new long[] { -1, -1, -1, -1 };
        long previousTime = -1;
        foreach (var note in chart.Notes)
        {
            if (string.IsNullOrEmpty(note.Id) || !ids.Add(note.Id) || note.Lane is < 0 or > 3 || note.TimeUs < 0
                || note.TimeUs > chart.DurationUs || note.TimeUs < previousTime || note.TimeUs <= laneEnds[note.Lane]
                || note.EndUs is long end && (end <= note.TimeUs || end > chart.DurationUs))
                throw new InvalidDataException($"채보의 노트가 올바르지 않습니다: {chart.Id}/{note.Id}");
            laneEnds[note.Lane] = note.EndUs ?? note.TimeUs;
            previousTime = note.TimeUs;
        }
    }

    public static ArcadeSession CreateArcade(string kind)
    {
        if (kind is not ("echo" or "silence" or "workshop" or "guard" or "route")) throw new ArgumentOutOfRangeException(nameof(kind));
        var session = new ArcadeSession
        {
            Kind = kind, ActiveLanes = kind == "route" ? [0, 1, 2, 3] : kind == "silence" ? [0] : [0, 2],
            Chart = new Chart { Id = $"arcade-{kind}", SongId = $"arcade-{kind}", Difficulty = "arcade", Version = "1", Bpm = 100, DurationUs = 48 * BeatUs },
            Title = kind switch { "echo" => "에코 스튜디오", "silence" => "빈 박자 챌린지", "workshop" => "비트 공방", "guard" => "리듬 가드", _ => "비트 루트" },
            Description = kind switch
            {
                "echo" => "4박을 듣고, 다음 4박에 같은 리듬을 재현하세요.",
                "silence" => "클릭음이 사라지는 8박에도 같은 간격을 유지하세요. 처음 네 박은 듣고 왼쪽 키로 시작합니다.",
                "workshop" => "작업선에 부품이 닿을 때 탭하고, 긴 부품은 끝까지 유지하세요.",
                "guard" => "좌우 공격의 예고를 읽고 방어선에 닿는 순간 입력하세요. 긴 공격은 계속 누릅니다.",
                _ => "네 방향 명령을 확인한 뒤, 다음 네 박에 순서대로 실행해 목적지에 도착하세요."
            }
        };
        void Add(int lane, double beat, double holdBeats = 0, string? pattern = null)
        {
            session.Chart.Notes.Add(new Note { Id = $"arcade-{kind}-{session.Chart.Notes.Count}", Lane = lane,
                TimeUs = (long)Math.Round(beat * BeatUs), EndUs = holdBeats > 0 ? (long)Math.Round((beat + holdBeats) * BeatUs) : null, Pattern = pattern ?? kind });
        }
        if (kind == "echo")
        {
            double[][] rhythms = [[0, 1, 2, 3], [0, .5, 2, 3], [0, 1, 1.5, 3], [0, .5, 1.5, 2.5], [0, 1, 2, 2.5, 3.5], [0, .5, 1, 2, 3, 3.5]];
            for (var round = 0; round < rhythms.Length; round++)
                for (var index = 0; index < rhythms[round].Length; index++)
                {
                    var lane = (index + round) % 2 == 0 ? 0 : 2;
                    var cue = new AudioCue { TimeUs = (long)Math.Round((round * 8 + rhythms[round][index]) * BeatUs), Lane = lane, DurationUs = 130_000 };
                    session.CallCues.Add(cue); session.Cues.Add(cue);
                    Add(lane, round * 8 + 4 + rhythms[round][index]);
                }
        }
        else if (kind == "silence")
        {
            for (var beat = 4; beat < 48; beat++) Add(0, beat, pattern: "pulse");
        }
        else if (kind == "route")
        {
            int[][] commands = [[3, 3, 1, 0], [0, 2, 3, 3], [1, 1, 3, 2], [2, 0, 0, 1], [3, 2, 2, 0], [0, 1, 1, 3]];
            int[][] directions = [[-1, 0], [0, 1], [0, -1], [1, 0]];
            for (var round = 0; round < commands.Length; round++)
            {
                var x = 2; var y = 2;
                foreach (var lane in commands[round]) { x += directions[lane][0]; y += directions[lane][1]; }
                session.Routes.Add(new ArcadeRoute { Commands = commands[round].ToArray(), Goal = [x, y] });
                for (var index = 0; index < 4; index++) Add(commands[round][index], round * 8 + 4 + index);
            }
        }
        else
        {
            for (int beat = 4, index = 0; beat < 47; beat += 2, index++)
            {
                var hold = index % (kind == "guard" ? 4 : 3) == 2 ? 1.25 : 0;
                Add(index % 2 == 0 ? 0 : 2, beat, hold, hold > 0 ? "hold" : "alternating");
                if (kind == "workshop" && index % 5 == 4) Add(index % 2 == 0 ? 2 : 0, beat + 1, pattern: "alternating");
            }
            session.Cues.AddRange(session.Chart.Notes.Select(note => new AudioCue { TimeUs = note.TimeUs, Lane = note.Lane, DurationUs = 100_000 }));
        }
        for (var beat = 0; beat < 48; beat++)
            if (!session.IsSilentBeat(beat)) session.Cues.Add(new AudioCue { TimeUs = beat * BeatUs, Lane = beat % 4 == 0 ? 0 : 1, DurationUs = 35_000, Accent = true });
        session.Chart.Notes = session.Chart.Notes.OrderBy(note => note.TimeUs).ThenBy(note => note.Lane).ToList();
        session.Cues = session.Cues.OrderBy(cue => cue.TimeUs).ToList();
        ValidateChart(session.Chart);
        return session;
    }

    public static ArcadeSession GetArcade(string kind) => CreateArcade(kind);

    public static ArcadeSession CreateTutorial(int step)
    {
        if (step is < 0 or > 3) throw new ArgumentOutOfRangeException(nameof(step));
        string[] titles = ["한 키 탭", "좌우 교대", "동시치기", "길게 누르기"];
        string[] descriptions = ["노트가 선에 닿을 때 왼쪽 키를 눌렀다 놓으세요.", "왼쪽과 오른쪽 키를 번갈아 눌러보세요.", "같은 높이에 있는 두 노트는 두 키를 함께 누릅니다.", "긴 노트의 머리가 선에 닿을 때 누르고 막대 끝까지 유지하세요."];
        var session = new ArcadeSession { Kind = $"tutorial-{step}", Title = titles[step], Description = descriptions[step], ActiveLanes = step == 0 ? [0] : [0, 2], RoundCount = 1,
            Chart = new Chart { Id = $"tutorial-{step}", SongId = "tutorial", Difficulty = "tutorial", Version = "1", Bpm = 100, DurationUs = 8 * BeatUs } };
        void Add(int lane, int beat, int? end = null) => session.Chart.Notes.Add(new Note { Id = $"tutorial-{step}-{session.Chart.Notes.Count}", Lane = lane, TimeUs = beat * BeatUs, EndUs = end * BeatUs, Pattern = titles[step] });
        if (step < 2) for (var beat = 2; beat <= 7; beat++) Add(step == 0 || beat % 2 == 0 ? 0 : 2, beat);
        else if (step == 2) foreach (var beat in new[] { 2, 4, 6 }) { Add(0, beat); Add(2, beat); }
        else { Add(0, 2, 4); Add(2, 5, 7); }
        for (var beat = 0; beat < 8; beat++) session.Cues.Add(new AudioCue { TimeUs = beat * BeatUs, Lane = beat % 4 == 0 ? 0 : 1, Accent = true });
        ValidateChart(session.Chart);
        return session;
    }

    private sealed class ContentDocument
    {
        public ContentDocument() { }
        public int Version { get; set; }
        public List<Song> Songs { get; set; } = [];
        public List<Chart> Charts { get; set; } = [];
        public List<Chart> Drills { get; set; } = [];
    }
}

public sealed class ArcadeRoute
{
    public int[] Commands { get; init; } = [];
    public int[] Goal { get; init; } = [2, 2];
}

public sealed class ArcadeSession
{
    public string Kind { get; init; } = "";
    public Chart Chart { get; set; } = new();
    public List<AudioCue> Cues { get; set; } = [];
    public string Title { get; init; } = "";
    public string Description { get; init; } = "";
    public int[] ActiveLanes { get; init; } = [0, 1, 2, 3];
    public List<AudioCue> CallCues { get; } = [];
    public List<ArcadeRoute> Routes { get; } = [];
    public int RoundCount { get; init; } = 6;
    public long BeatUs => ContentLibrary.BeatUs;
    public int RoundBeats => 8;
    public int RoundAt(long songTimeUs) => Math.Clamp((int)(Math.Max(0, songTimeUs) / (8 * BeatUs)), 0, Math.Max(0, RoundCount - 1));
    public bool IsSilentBeat(int beat) => Kind == "silence" && (beat is >= 16 and < 24 || beat is >= 32 and < 40);
    public string PhaseAt(long songTimeUs)
    {
        if (songTimeUs < 0) return "prepare";
        if (songTimeUs >= Chart.DurationUs) return "complete";
        var beat = (int)(songTimeUs / BeatUs);
        return Kind switch
        {
            "echo" => beat % 8 < 4 ? "listen" : "respond",
            "route" => beat % 8 < 4 ? "plan" : "execute",
            "silence" => beat < 4 ? "listen" : IsSilentBeat(beat) ? "silent" : "play",
            _ => beat < 2 && Kind.StartsWith("tutorial", StringComparison.Ordinal) ? "listen" : "play"
        };
    }
}
