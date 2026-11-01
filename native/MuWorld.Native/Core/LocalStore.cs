using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace MuWorld.Native.Core;

/// <summary>One validated JSON envelope; same-directory atomic replacement with a previous valid backup.</summary>
public sealed class LocalStore
{
    private const int Version = 1;
    private const long MaximumBytes = 60_000_000;
    private readonly object _sync = new();
    private static readonly JsonSerializerOptions Json = new()
        { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true, WriteIndented = true, MaxDepth = 32 };
    public string DirectoryPath { get; }
    public string LibraryPath => Path.Combine(DirectoryPath, "library.json");
    public string BackupPath => Path.Combine(DirectoryPath, "library.backup.json");
    public string? LoadWarning { get; private set; }
    public Settings Settings { get; set; } = new();
    public List<PlayRecord> Records { get; set; } = [];
    public HashSet<string> Favorites { get; set; } = [];
    public List<CalibrationProfile> Profiles { get; set; } = [];

    public sealed class Envelope
    {
        public string Schema { get; set; } = "muworld.native.backup";
        public int Version { get; set; } = 1;
        public Settings Settings { get; set; } = new();
        public List<PlayRecord> Records { get; set; } = [];
        public HashSet<string> Favorites { get; set; } = [];
        public List<CalibrationProfile> Profiles { get; set; } = [];
    }

    public LocalStore(string? directory = null)
    {
        DirectoryPath = Path.GetFullPath(directory ?? Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "MuWorldNative"));
        if (TryRead(LibraryPath, out var primary)) Apply(primary!);
        else if (TryRead(BackupPath, out var backup))
        { Apply(backup!); LoadWarning = "기본 기록을 읽지 못해 직전의 유효한 백업을 불러왔습니다."; }
        else if (File.Exists(LibraryPath) || File.Exists(BackupPath))
            LoadWarning = "기록 파일을 읽지 못했습니다. 원본 파일은 그대로 보존되어 있습니다. 백업을 확인하세요.";
    }

    public void Save()
    {
        lock (_sync) { var envelope = Current(); Validate(envelope); WriteLibrary(envelope); }
    }
    public void AddRecord(PlayRecord record)
    {
        lock (_sync)
        {
            ValidateRecord(record);
            var next = Current();
            next.Records = Records.ToList();
            next.Records.RemoveAll(r => r.Id == record.Id); next.Records.Add(Clone(record));
            Validate(next); WriteLibrary(next); Apply(next);
        }
    }
    public bool RemoveRecord(string id)
    {
        lock (_sync)
        {
            var next = Current();
            next.Records = Records.ToList();
            if (next.Records.RemoveAll(r => r.Id == id) == 0) return false;
            Validate(next); WriteLibrary(next); Apply(next); return true;
        }
    }
    public void ExportBackup(string path)
    {
        lock (_sync)
        {
            var current = Current(); Validate(current);
            AtomicWrite(Path.GetFullPath(path), Serialize(current), null);
        }
    }
    public void ImportBackup(string path)
    {
        lock (_sync)
        {
            var incoming = Read(path); Validate(incoming);
            WriteLibrary(incoming); Apply(incoming);
        }
    }

    private Envelope Current() => new() { Settings = Settings, Records = Records, Favorites = Favorites, Profiles = Profiles };
    private void Apply(Envelope e)
    {
        var copy = Clone(e);
        Settings = copy.Settings; Records = copy.Records; Favorites = copy.Favorites; Profiles = copy.Profiles;
    }
    private static T Clone<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Json), Json)!;
    private static string Serialize(Envelope value)
    {
        var text = JsonSerializer.Serialize(value, Json);
        if (Encoding.UTF8.GetByteCount(text) > MaximumBytes) throw new InvalidDataException("저장할 기록이 너무 큽니다. 백업 후 오래된 기록을 정리하세요.");
        return text;
    }
    private static Envelope Read(string path)
    {
        if (new FileInfo(path).Length > MaximumBytes) throw new InvalidDataException("백업은 60 MB 이하여야 합니다.");
        var text = File.ReadAllText(path, Encoding.UTF8);
        using var document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 32 });
        Require(document.RootElement.ValueKind == JsonValueKind.Object, "백업은 JSON 객체여야 합니다.");
        var fields = document.RootElement.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Require(new[] { "schema", "version", "settings", "records", "favorites", "profiles" }.All(fields.Contains), "백업의 필수 항목이 누락되었습니다.");
        var envelope = JsonSerializer.Deserialize<Envelope>(text, Json) ?? throw new InvalidDataException("비어 있는 백업입니다.");
        Validate(envelope); return envelope;
    }
    private static bool TryRead(string path, out Envelope? envelope)
    {
        try { envelope = Read(path); return true; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or ArgumentException or OverflowException)
        { envelope = null; return false; }
    }
    private void WriteLibrary(Envelope envelope)
    {
        var text = Serialize(envelope);
        AtomicWrite(LibraryPath, text, TryRead(LibraryPath, out _) ? BackupPath : null);
    }
    private static void AtomicWrite(string destination, string text, string? previousBackup)
    {
        var directory = Path.GetDirectoryName(destination)!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 16_384, FileOptions.WriteThrough))
            {
                var data = Encoding.UTF8.GetBytes(text); stream.Write(data); stream.Flush(true);
            }
            if (File.Exists(destination) && previousBackup is not null) File.Replace(temporary, destination, previousBackup, true);
            else File.Move(temporary, destination, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidDataException(message); }
    private static bool Label(string? value, int limit = 300) => !string.IsNullOrWhiteSpace(value) && value.Length <= limit;
    private static bool Finite(double value, double low, double high) => double.IsFinite(value) && value >= low && value <= high;
    private static void ValidateKeys(int[]? keys) => Require(keys is { Length: 4 } && keys.Distinct().Count() == 4 && keys.All(k => k is >= 1 and <= 255 && k is not (9 or 27 or 91 or 92)), "서로 다른 네 개의 유효한 키가 필요합니다.");
    public static void ValidateSettings(Settings? s)
    {
        Require(s is not null, "설정이 없습니다.");
        ValidateKeys(s!.Keys);
        Require(s.InputOffsetUs is >= -500_000 and <= 500_000 && s.VisualOffsetUs is >= -500_000 and <= 500_000, "보정값의 허용 범위를 벗어났습니다.");
        Require(s.ApproachMs is >= 300 and <= 3000 && Finite(s.Volume, 0, 1) && Finite(s.EffectVolume, 0, 1), "화면 속도 또는 볼륨이 유효하지 않습니다.");
        Require(Finite(s.LaneWidth, 60, 180) && Finite(s.NoteBrightness, 10, 150) && Finite(s.BackgroundBrightness, 0, 150) && Finite(s.Effects, 0, 100) && Finite(s.UiScale, 75, 150), "화면 설정이 유효하지 않습니다.");
        Require(s.PracticeRate is >= .5m and <= 1.5m && s.WindowScale is >= .25m and <= 2m, "연습 속도 또는 판정 폭이 유효하지 않습니다.");
        Require(Finite(s.PracticeFromSec, 0, 3599) && Finite(s.PracticeToSec, .001, 3600) && s.PracticeToSec > s.PracticeFromSec, "연습 종료는 시작보다 뒤여야 합니다.");
        Require(new[] { "comfort", "focus", "custom", "highContrast", "contrast" }.Contains(s.Preset), "알 수 없는 화면 프리셋입니다.");
    }
    private static void Validate(Envelope e)
    {
        Require(e.Version == Version && e.Schema == "muworld.native.backup", "지원하지 않는 MuWorld 네이티브 백업입니다.");
        ValidateSettings(e.Settings);
        var records = e.Records ?? throw new InvalidDataException("기록 목록이 없습니다.");
        Require(records.Count <= 500, "기록은 500개 이하여야 합니다.");
        Require(records.All(r => r is not null) && records.Select(r => r.Id).Distinct().Count() == records.Count, "중복되거나 비어 있는 기록이 있습니다.");
        foreach (var record in records) ValidateRecord(record);
        Require(e.Favorites is not null && e.Favorites.Count <= 500 && e.Favorites.All(id => Label(id)), "즐겨찾기가 유효하지 않습니다.");
        Require(e.Profiles is not null && e.Profiles.Count <= 30 && e.Profiles.All(p => p is not null) && e.Profiles.Select(p => p.Id).Distinct().Count() == e.Profiles.Count, "보정 프로필이 유효하지 않습니다.");
        foreach (var p in e.Profiles!)
        {
            Require(Label(p.Id) && Label(p.Name, 80) && p.InputOffsetUs is >= -500_000 and <= 500_000 && p.VisualOffsetUs is >= -500_000 and <= 500_000 && p.UpdatedAt != default, "보정 프로필 값이 유효하지 않습니다.");
            Require(p.DeviceId is not null && p.DeviceId.Length <= 2000 && p.DeviceName is not null && p.DeviceName.Length <= 1000, "장치 프로필이 유효하지 않습니다.");
            ValidateKeys(p.Keys);
        }
    }
    public static void ValidateRecord(PlayRecord r)
    {
        Require(r is not null && Label(r.Id) && Label(r.SongId) && Label(r.ChartId) && Label(r.ChartVersion) && Label(r.Difficulty) && r.CreatedAt != default, "기록 식별자 또는 날짜가 유효하지 않습니다.");
        Require(new[] { "standard", "practice", "arcade" }.Contains(r!.Mode) && r.Rate is >= .25m and <= 2m, "기록 모드 또는 속도가 유효하지 않습니다.");
        ValidateSettings(r.Settings);
        Require(r.InputOffsetUs == r.Settings.InputOffsetUs && r.FromUs >= 0 && r.ToUs >= r.FromUs, "기록과 설정의 보정값 또는 구간이 다릅니다.");
        var s = r.Summary;
        Require(s is not null && Finite(s.Accuracy, 0, 100) && s.Score is >= 0 and <= 1_000_000 && s.TotalUnits is >= 0 and <= 100_000 && s.JudgedUnits >= 0 && s.JudgedUnits <= s.TotalUnits
            && s.Combo >= 0 && s.Combo <= s.MaxCombo && s.MaxCombo <= s.JudgedUnits && s.ExtraInputs >= 0, "기록 점수 또는 판정 수가 유효하지 않습니다.");
        Require(s!.Judgments is not null && s.Inputs is not null && s.Judgments.Count == s.JudgedUnits && s.Inputs.Count <= 100_000 && s.Counts is not null, "입력 또는 판정 목록이 유효하지 않습니다.");
        var actual = RhythmEngine.Weights.Keys.ToDictionary(g => g, _ => 0);
        var units = new HashSet<(string, string)>();
        decimal weight = 0;
        foreach (var j in s.Judgments!)
        {
            Require(j is not null && Label(j.NoteId) && j.Lane is >= 0 and <= 3 && j.Kind is "head" or "hold" && actual.ContainsKey(j.Grade) && j.TargetUs >= 0 && Label(j.Pattern), "개별 판정이 유효하지 않습니다.");
            Require(units.Add((j!.NoteId, j.Kind)), "중복 판정 단위가 있습니다.");
            Require(j.DeltaUs is null || Finite(j.DeltaUs.Value, -2_000_000, 2_000_000), "판정 편차가 유효하지 않습니다.");
            Require(j.Grade != "MISS" || j.DeltaUs is null, "MISS에는 타이밍 편차가 없어야 합니다.");
            Require(j.Kind != "head" || j.Grade == "MISS" || j.DeltaUs.HasValue, "타격 편차가 없습니다.");
            actual[j.Grade]++; weight += RhythmEngine.Weights[j.Grade];
        }
        Require(actual.All(pair => s.Counts!.TryGetValue(pair.Key, out var count) && count == pair.Value), "개별 판정과 집계가 다릅니다.");
        var expectedAccuracy = s.JudgedUnits == 0 ? 0 : (double)(weight * 100m / s.JudgedUnits);
        var expectedScore = s.TotalUnits == 0 ? 0 : (int)decimal.Round(weight * 1_000_000m / s.TotalUnits, 0, MidpointRounding.AwayFromZero);
        Require(Math.Abs(s.Accuracy - expectedAccuracy) <= .000001 && s.Score == expectedScore, "판정과 점수가 일치하지 않습니다.");
        foreach (var e in s.Inputs!) Require(e is not null && e.Lane is >= 0 and <= 3, "개별 입력이 유효하지 않습니다.");
        Require(r.Analysis is not null && r.Environment is not null && r.Environment.Count <= 50 && r.Environment.All(p => Label(p.Key, 100) && p.Value is not null && p.Value.Length <= 2000), "분석 또는 장치 정보가 유효하지 않습니다.");
    }
}
