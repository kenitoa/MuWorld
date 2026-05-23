using System.Diagnostics;
using System.Reflection;
using RhythmGame;

namespace MuWorld.SelfTests;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        var runner = new SelfTestRunner();
        return runner.RunAll();
    }
}

internal sealed class SelfTestRunner
{
    private readonly List<string> _failures = [];
    private int _passed;

    public int RunAll()
    {
        Run("ScoreManager unit calculations", TestScoreManager);
        Run("NoteLane BMS parse and validation", TestNoteLaneBmsParse);
        Run("ChartGenerator filename and tempo map", TestChartGenerator);
        Run("UserSettingsStore backup recovery", TestUserSettingsStore);
        Run("SongDataStore lane-mode records/history", TestSongDataStore);
        Run("Judgment timing simulation", TestJudgmentTimingSimulation);
        Run("Long and slide note behavior", TestLongAndSlideNotes);
        Run("UI smoke and resolution draw", TestUiSmokeAndResolutionDraw);
        Run("10-minute engine simulation", TestLongPlaySimulation);

        Console.WriteLine($"Self-test result: {_passed} passed, {_failures.Count} failed.");
        foreach (string failure in _failures)
            Console.WriteLine(failure);

        return _failures.Count == 0 ? 0 : 1;
    }

    private void Run(string name, Action test)
    {
        try
        {
            test();
            _passed++;
            Console.WriteLine($"PASS {name}");
        }
        catch (Exception ex)
        {
            _failures.Add($"FAIL {name}: {ex.Message}");
            Console.WriteLine($"FAIL {name}: {ex}");
        }
    }

    private static void TestScoreManager()
    {
        var score = new ScoreManager();
        score.AddHit(Judgment.Perfect);
        score.AddHit(Judgment.Great);
        score.AddHit(Judgment.Better);
        score.AddHit(Judgment.Good);
        score.AddHit(Judgment.Bad);
        score.AddMiss();

        ExpectNear(score.Accuracy, (1f + 0.9f + 0.75f + 0.5f + 0.25f) * 100f / 6f, 0.001f, "weighted accuracy");
        Expect(score.MaxCombo == 5, "max combo");
        Expect(score.MissCount == 1 && score.MaxMissStreak == 1, "miss streak");
        Expect(ScoreManager.CalculateClearType(5, 0, 0, 0, 0, 0) == ClearType.Perfect, "perfect clear type");
        Expect(ScoreManager.CalculateClearType(2, 3, 0, 0, 0, 0) == ClearType.AllGreatPlus, "all great clear type");
        Expect(ScoreManager.CalculateGrade(99.8f, 0, 120, ClearType.AllGreatPlus) == ResultGrade.SPlus, "S+ grade");
        Expect(ScoreManager.CalculateNormalizedScore(1, 0, 0, 0, 0, 1) == 500_000, "normalized score");
    }

    private static void TestNoteLaneBmsParse()
    {
        string title = "self_test_chart_" + Guid.NewGuid().ToString("N");
        ChartGenerator.UserChartDirectoryOverride = Path.Combine(AppContext.BaseDirectory, "SelfTestCharts");
        string path = ChartGenerator.GetUserChartPath(title, difficultyIndex: 1, laneCount: 7);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, string.Join(Environment.NewLine,
        [
            "#BPM 120",
            "#BPM01 240",
            "#00008:0001",
            "#00011:0100",
            "#00012:0002",
            "#00113:0034",
            "#00211:ZZ",
            "#100011:0100"
        ]));

        try
        {
            ChartValidationResult chart = NoteLane.LoadValidatedChart(title, "self", 1, 7);
            Expect(chart.Notes.Count >= 3, "parsed note count");
            Expect(chart.Notes.Any(n => n.Type == NoteType.Tap && n.Lane == 0), "tap parsed");
            Expect(chart.Notes.Any(n => n.Type == NoteType.Long), "long parsed");
            Expect(chart.Notes.Any(n => n.Type == NoteType.Slide && n.EndLane == 3), "slide parsed");
            Expect(chart.Diagnostics.Count > 0, "diagnostics collected");
            Expect(chart.Difficulty.Level is >= 1 and <= 15, "difficulty level range");
        }
        finally
        {
            TryDelete(path);
            ChartGenerator.UserChartDirectoryOverride = null;
        }
    }

    private static void TestChartGenerator()
    {
        Expect(ChartGenerator.GetChartFileName("A Song: Test", 0) == "easy_a_song_test.bms", "easy filename normalization");
        Expect(ChartGenerator.GetChartFileName("A Song: Test", 2, 9).EndsWith("_7k.bms", StringComparison.Ordinal), "lane filename clamp");

        MethodInfo method = typeof(ChartGenerator).GetMethod("DetectTempoMap", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("DetectTempoMap not found.");
        var beats = new List<WavAnalyzer.BeatInfo>
        {
            new(0f, 1f, Confidence: 0.9f),
            new(0.5f, 1f, Confidence: 0.9f),
            new(1.0f, 1f, Confidence: 0.9f),
            new(1.5f, 1f, Confidence: 0.9f),
            new(2.0f, 1f, Confidence: 0.9f),
            new(2.5f, 1f, Confidence: 0.9f),
        };
        object? segments = method.Invoke(null, [beats]);
        Expect(segments is not null, "tempo map returned");
        int count = (int)(segments!.GetType().GetProperty("Count")?.GetValue(segments) ?? 0);
        Expect(count > 0, "tempo segment count");
    }

    private static void TestUserSettingsStore()
    {
        string path = TempFile("user_settings.json");
        var store = new UserSettingsStore(path);
        store.Save(new UserSettings { BgmVolume = 12, PreviewVolume = 34 });
        store.Save(new UserSettings { BgmVolume = 56, PreviewVolume = 78 });
        File.WriteAllText(path, "{ broken json");

        UserSettings recovered = store.Load();
        Expect(recovered.BgmVolume == 12, "user settings recovered from backup");
        Expect(File.Exists(path + ".bak"), "user settings backup exists");
    }

    private static void TestSongDataStore()
    {
        string path = TempFile("song_data.json");
        var store = new SongDataStore(path);
        var metadata = new SongMetadata { SongId = "song-a", Title = "Song A", Artist = "Tester", Bpm = 180 };

        store.RecordScore(metadata, 1, 1000, 20, 80f, ResultGrade.B, ClearType.Clear, 2, 4);
        store.RecordScore(metadata, 1, 5000, 80, 96f, ResultGrade.S, ClearType.FullCombo, 0, 7);
        SongScoreRecord record = store.RecordScore(metadata, 1, 500, 10, 70f, ResultGrade.C, ClearType.Clear, 3, 4);

        Expect(record.DifficultyHighScores["Normal:4K"] == 1000, "4K high score retained");
        Expect(record.DifficultyHighScores["Normal:7K"] == 5000, "7K high score retained");
        Expect(record.History.Count == 3, "history count");
        Expect(record.DifficultyPlayCount["Normal:4K"] == 2, "4K play count");
    }

    private static void TestJudgmentTimingSimulation()
    {
        (float offset, Judgment expected)[] cases =
        [
            (0.000f, Judgment.Perfect),
            (0.045f, Judgment.Great),
            (0.075f, Judgment.Better),
            (0.105f, Judgment.Good),
            (0.140f, Judgment.Bad),
        ];

        foreach ((float offset, Judgment expected) in cases)
        {
            GameEngine engine = StartEngineWith([new LaneNote(2.0f, 0)]);
            engine.Update(0.016f, 0.55f);
            engine.Update(0.016f, 2.0f + offset);
            GameEngine.HitResult? hit = engine.TryHit(0);
            Expect(hit.HasValue, $"hit exists for {expected}");
            Expect(hit!.Value.Judgment == expected, $"judgment {expected}");
        }

        GameEngine missEngine = StartEngineWith([new LaneNote(2.0f, 0)]);
        missEngine.Update(0.016f, 0.55f);
        missEngine.Update(0.25f, 2.19f);
        Expect(missEngine.ConsumePendingMisses() == 1, "miss threshold");
    }

    private static void TestLongAndSlideNotes()
    {
        GameEngine earlyRelease = StartEngineWith([new LaneNote(2.0f, 0, NoteType.Long, 1.0f, 0)]);
        earlyRelease.Update(0.016f, 0.55f);
        earlyRelease.SetLaneHeld(0, true);
        earlyRelease.Update(0.016f, 2.0f);
        Expect(earlyRelease.TryHit(0).HasValue, "long start hit");
        earlyRelease.SetLaneHeld(0, false);
        earlyRelease.Update(0.2f, 2.55f);
        Expect(earlyRelease.ConsumePendingMisses() == 1, "long early release miss");

        GameEngine longEnd = StartEngineWith([new LaneNote(2.0f, 0, NoteType.Long, 1.0f, 0)]);
        longEnd.Update(0.016f, 0.55f);
        longEnd.SetLaneHeld(0, true);
        longEnd.Update(0.016f, 2.0f);
        Expect(longEnd.TryHit(0).HasValue, "long end start");
        longEnd.Update(0.25f, 2.90f);
        GameEngine.HitResult? release = longEnd.TryRelease(0);
        Expect(release.HasValue && longEnd.Score.MissCount == 0, "long end release");

        GameEngine slideWrong = StartEngineWith([new LaneNote(2.0f, 0, NoteType.Slide, 1.0f, 2)]);
        slideWrong.Update(0.016f, 0.55f);
        slideWrong.SetLaneHeld(0, true);
        slideWrong.Update(0.016f, 2.0f);
        Expect(slideWrong.TryHit(0).HasValue, "slide start hit");
        slideWrong.Update(0.25f, 2.65f);
        Expect(slideWrong.ConsumePendingMisses() == 1, "slide wrong lane miss");

        GameEngine slideEnd = StartEngineWith([new LaneNote(2.0f, 0, NoteType.Slide, 1.0f, 2)]);
        slideEnd.Update(0.016f, 0.55f);
        slideEnd.SetLaneHeld(0, true);
        slideEnd.Update(0.016f, 2.0f);
        Expect(slideEnd.TryHit(0).HasValue, "slide end start");
        slideEnd.SetLaneHeld(0, false);
        slideEnd.SetLaneHeld(2, true);
        slideEnd.Update(0.25f, 2.90f);
        GameEngine.HitResult? slideRelease = slideEnd.TryRelease(2);
        Expect(slideRelease.HasValue && slideEnd.Score.MissCount == 0, "slide end release");
    }

    private static void TestUiSmokeAndResolutionDraw()
    {
        SongDataStore.DefaultSaveFilePathOverride = Path.Combine(AppContext.BaseDirectory, "SelfTestData", "song_data.json");
        Size[] sizes =
        [
            new(960, 640),
            new(1152, 768),
            new(1366, 768),
            new(1920, 1080),
            new(2560, 1080),
        ];

        foreach (Size size in sizes)
        {
            using var form = new GameForm(selfTestMode: true) { ClientSize = size, ShowInTaskbar = false };
            DrawScreen(form, "Splash", null);
            DrawScreen(form, "MainMenu", null);
            DrawScreen(form, "SongSelect", null);
            DrawGameScreen(form, size);
            DrawScreen(form, "Analyze", null);
        }

        SongDataStore.DefaultSaveFilePathOverride = null;
    }

    private static void TestLongPlaySimulation()
    {
        List<LaneNote> notes = [];
        for (int i = 0; i < 1200; i++)
            notes.Add(new LaneNote(2f + i * 0.5f, i % 4));

        GameEngine engine = StartEngineWith(notes, height: 768);
        var stopwatch = Stopwatch.StartNew();
        for (int frame = 0; frame < 36_600; frame++)
        {
            float position = frame / 60f;
            engine.Update(1f / 60f, position);
            if (frame % 300 == 0)
                _ = engine.ConsumePendingMisses();
        }

        stopwatch.Stop();
        Expect(engine.Score.TotalJudgedNotes >= 1000, "long simulation judged notes");
        Expect(engine.Notes.Count < 20, "long simulation note cleanup");
        Expect(stopwatch.Elapsed.TotalSeconds < 15, "long simulation runtime budget");
    }

    private static GameEngine StartEngineWith(IReadOnlyList<LaneNote> notes, int height = 768, int lanes = 4)
    {
        var engine = new GameEngine();
        engine.Start(height, notes, lanes);
        return engine;
    }

    private static void DrawGameScreen(GameForm form, Size size)
    {
        FieldInfo engineField = typeof(GameForm).GetField("_engine", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("_engine field not found.");
        var engine = (GameEngine)engineField.GetValue(form)!;
        engine.Start(size.Height, [new LaneNote(2f, 0), new LaneNote(2.5f, 1, NoteType.Long, 0.5f, 1)], 4);
        engine.Update(0.016f, 1.0f);
        try
        {
            DrawToBitmapAndAssert(form, size, "Game");
        }
        finally
        {
            engine.Stop();
        }
    }

    private static void DrawScreen(GameForm form, string screenName, Action? setup)
    {
        FieldInfo screenField = typeof(GameForm).GetField("_screen", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("_screen field not found.");
        object screenValue = Enum.Parse(screenField.FieldType, screenName);
        screenField.SetValue(form, screenValue);
        setup?.Invoke();
        DrawToBitmapAndAssert(form, form.ClientSize, screenName);
    }

    private static void DrawToBitmapAndAssert(GameForm form, Size size, string screenName)
    {
        using var bitmap = new Bitmap(size.Width, size.Height);
        using Graphics graphics = Graphics.FromImage(bitmap);
        using var args = new PaintEventArgs(graphics, new Rectangle(Point.Empty, size));
        typeof(GameForm)
            .GetMethod("OnPaint", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(form, [args]);
        Expect(HasNonBlankPixels(bitmap), $"{screenName} bitmap nonblank at {size.Width}x{size.Height}");
    }

    private static bool HasNonBlankPixels(Bitmap bitmap)
    {
        Color first = bitmap.GetPixel(0, 0);
        int stepX = Math.Max(1, bitmap.Width / 16);
        int stepY = Math.Max(1, bitmap.Height / 16);
        for (int y = 0; y < bitmap.Height; y += stepY)
        {
            for (int x = 0; x < bitmap.Width; x += stepX)
            {
                if (bitmap.GetPixel(x, y).ToArgb() != first.ToArgb())
                    return true;
            }
        }

        return false;
    }

    private static string TempFile(string fileName)
    {
        string directory = Path.Combine(Path.GetTempPath(), "MuWorld.SelfTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, fileName);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Test cleanup must not hide assertion failures.
        }
    }

    private static void Expect(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static void ExpectNear(float actual, float expected, float tolerance, string message)
    {
        if (MathF.Abs(actual - expected) > tolerance)
            throw new InvalidOperationException($"{message}: expected {expected}, actual {actual}");
    }
}
