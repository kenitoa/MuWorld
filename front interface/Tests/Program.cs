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
        Run("Statistics snapshot uses real play history", TestStatisticsSnapshot);
        Run("Song Select uses actual song files", TestSongSelectUsesActualSongFiles);
        Run("Judgment timing simulation", TestJudgmentTimingSimulation);
        Run("Long and slide note behavior", TestLongAndSlideNotes);
        Run("Combo, speed, and live lane switching", TestComboSpeedAndLiveLaneSwitching);
        Run("Perspective note lane alignment", TestPerspectiveNoteLaneAlignment);
        Run("Analyze layout bounds", TestAnalyzeLayoutBounds);
        Run("UI smoke and resolution draw", TestUiSmokeAndResolutionDraw);
        Run("Settings pages render and interact", TestSettingsPagesRenderAndInteract);
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

    private static void TestStatisticsSnapshot()
    {
        var data = new SongDataFile();
        data.Metadata["song-a"] = new SongMetadata { SongId = "song-a", Title = "Song A", DurationSeconds = 120f };
        data.Scores["song-a"] = new SongScoreRecord
        {
            SongId = "song-a",
            PlayCount = 2,
            BestCombo = 42,
            BestAccuracy = 95f,
            BestGrade = "S",
            History =
            [
                new SongPlayHistoryEntry
                {
                    PlayedUtc = DateTime.Now.ToString("O"),
                    Accuracy = 95f,
                    Grade = "S",
                    MaxCombo = 42,
                    PerfectCount = 9,
                    GreatCount = 1,
                },
                new SongPlayHistoryEntry
                {
                    PlayedUtc = DateTime.Now.AddDays(-1).ToString("O"),
                    Accuracy = 70f,
                    Grade = "C",
                    MaxCombo = 12,
                    PerfectCount = 4,
                    GoodCount = 3,
                    BadCount = 2,
                    MissCount = 1,
                }
            ],
        };

        MethodInfo method = typeof(GameForm).GetMethod("BuildStatisticsSnapshot", BindingFlags.NonPublic | BindingFlags.Static, null, [typeof(PlayerProgress), typeof(SongDataFile)], null)
            ?? throw new InvalidOperationException("BuildStatisticsSnapshot overload not found.");
        object snapshot = method.Invoke(null, [new PlayerProgress(), data])!;
        Type snapshotType = snapshot.GetType();

        Expect((string)snapshotType.GetProperty("TotalPlaysText")!.GetValue(snapshot)! == "2", "statistics total plays from history");
        Expect((string)snapshotType.GetProperty("AverageAccuracyText")!.GetValue(snapshot)! == "79.5%", "statistics weighted accuracy from judged notes");
        Expect((string)snapshotType.GetProperty("BestComboText")!.GetValue(snapshot)! == "42", "statistics best combo from history");
        Expect((string)snapshotType.GetProperty("BestRankText")!.GetValue(snapshot)! == "S", "statistics best rank from grades");
        Expect((int)snapshotType.GetProperty("PlayTimeMinutes")!.GetValue(snapshot)! == 4, "statistics play time from song duration");
        Expect((int)snapshotType.GetProperty("ActiveDays")!.GetValue(snapshot)! == 2, "statistics active days from played dates");

        var recent = (float[])snapshotType.GetProperty("RecentAccuracy")!.GetValue(snapshot)!;
        ExpectNear(recent[6], 95f, 0.001f, "statistics today's accuracy");
        ExpectNear(recent[5], 70f, 0.001f, "statistics yesterday accuracy");

        var distribution = (int[])snapshotType.GetProperty("RankDistribution")!.GetValue(snapshot)!;
        Expect(distribution[0] == 50 && distribution[3] == 50, "statistics rank distribution from grades");
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

    private static void TestSongSelectUsesActualSongFiles()
    {
        SongDataStore.DefaultSaveFilePathOverride = TempFile("song_select_actual_files.json");
        try
        {
            string songDir = Path.Combine(AppContext.BaseDirectory, "Songs", "InGameBGM", "Original");
            string[] audioFiles = AudioFileCatalog.DiscoverSongFiles(songDir);
            Expect(audioFiles.Length >= 10, "actual song files copied to runtime");

            MethodInfo invalidate = typeof(GameForm).GetMethod("InvalidateSongCache", BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new InvalidOperationException("InvalidateSongCache not found.");
            MethodInfo discoverSongs = typeof(GameForm).GetMethod("DiscoverSongs", BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new InvalidOperationException("DiscoverSongs not found.");
            invalidate.Invoke(null, []);
            Array songs = (Array)discoverSongs.Invoke(null, [])!;
            Expect(songs.Length == audioFiles.Length, "song select count matches audio files");

            HashSet<string> titles = [];
            foreach (object song in songs)
            {
                string title = (string)song.GetType().GetProperty("Title")!.GetValue(song)!;
                titles.Add(title);
            }

            Expect(titles.Contains("Akina Drift"), "song select includes Akina Drift");
            Expect(titles.Contains("Rust Hover"), "song select includes Rust Hover");
        }
        finally
        {
            SongDataStore.DefaultSaveFilePathOverride = null;
        }
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

    private static void TestComboSpeedAndLiveLaneSwitching()
    {
        var score = new ScoreManager();
        foreach (Judgment judgment in Enum.GetValues<Judgment>())
            score.AddHit(judgment);
        Expect(score.Combo == 5, "combo increments for every successful judgment");
        Expect(score.MaxCombo == 5, "max combo follows consecutive hits");
        score.AddHoldTick();
        Expect(score.Combo == 5, "hold ticks do not inflate combo");
        score.AddMiss();
        Expect(score.Combo == 0, "miss resets combo");
        score.AddHit(Judgment.Perfect);
        Expect(score.Combo == 1, "combo restarts after miss");

        GameEngine smoothEngine = StartEngineWith([new LaneNote(2.0f, 0)], height: 768, lanes: 4);
        smoothEngine.Update(0.016f, 0f);
        smoothEngine.Update(0.016f, 0f);
        Expect(smoothEngine.VisualChartTime > 0.005f, "visual chart time advances between coarse playback samples");
        ExpectNear(smoothEngine.CurrentChartTime, 0f, 0.001f, "raw chart time stays audio locked while visual clock smooths");
        smoothEngine.Update(0.016f, 1.0f);
        ExpectNear(smoothEngine.VisualChartTime, 1.0f, 0.001f, "visual chart time snaps on large sync jump");

        foreach (int oldLanes in new[] { 4, 5, 6, 7 })
        {
            foreach (int newLanes in new[] { 4, 5, 6, 7 })
            {
                GameEngine engine = StartEngineWith(
                [
                    new LaneNote(2.0f, 0),
                    new LaneNote(2.0f, oldLanes - 1),
                    new LaneNote(4.0f, Math.Min(oldLanes - 1, 1)),
                ], height: 900, lanes: oldLanes);
                engine.Update(0.016f, 1.0f);
                Expect(engine.Notes.Count >= 2, $"spawned notes before {oldLanes}K to {newLanes}K switch");

                engine.SwitchLaneMode(newLanes,
                [
                    new LaneNote(2.0f, 0),
                    new LaneNote(2.0f, newLanes - 1),
                    new LaneNote(4.0f, Math.Min(newLanes - 1, 1)),
                ]);

                Expect(engine.LaneCount == newLanes, $"{oldLanes}K to {newLanes}K engine lane count");
                Expect(engine.Notes.All(n => n.Lane >= 0 && n.Lane < newLanes && n.EndLane >= 0 && n.EndLane < newLanes), $"{oldLanes}K to {newLanes}K active notes remapped into bounds");
                Expect(engine.Notes.Any(n => n.Lane == 0), $"{oldLanes}K to {newLanes}K left edge preserved");
                Expect(engine.Notes.Any(n => n.Lane == newLanes - 1), $"{oldLanes}K to {newLanes}K right edge preserved");
            }
        }

        UserSettingsStore.DefaultSaveFilePathOverride = TempFile("live_lane_settings.json");
        try
        {
            using var form = new GameForm(selfTestMode: true) { ClientSize = new Size(1680, 944), ShowInTaskbar = false };
            FieldInfo engineField = typeof(GameForm).GetField("_engine", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("_engine field not found.");
            FieldInfo speedField = typeof(GameForm).GetField("_speedMultiplier", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("_speedMultiplier field not found.");
            MethodInfo applySpeed = typeof(GameForm).GetMethod("ApplySpeedToEngine", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("ApplySpeedToEngine not found.");
            MethodInfo switchLane = typeof(GameForm).GetMethod("SwitchLaneModeToCount", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("SwitchLaneModeToCount not found.");
            PropertyInfo laneCountProperty = typeof(GameForm).GetProperty("LaneCount", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("LaneCount property not found.");

            var engine = (GameEngine)engineField.GetValue(form)!;
            speedField.SetValue(form, 1.0f);
            applySpeed.Invoke(form, []);
            ExpectNear(engine.NoteSpeedMultiplier, 2.0f, 0.001f, "1.0x applies half-speed engine scale");

            engine.Start(form.ClientSize.Height,
            [
                new LaneNote(2.0f, 0),
                new LaneNote(2.0f, 3),
                new LaneNote(4.0f, 2),
            ], 4);
            engine.Update(0.016f, 1.0f);

            switchLane.Invoke(form, [6]);
            Expect((int)laneCountProperty.GetValue(form)! == 6, "form switches to 6K during play");
            Expect(engine.LaneCount == 6, "engine switches to 6K during play");
            Expect(engine.Notes.All(n => n.Lane >= 0 && n.Lane < 6 && n.EndLane >= 0 && n.EndLane < 6), "6K live switch remaps active notes");

            switchLane.Invoke(form, [7]);
            Expect((int)laneCountProperty.GetValue(form)! == 7, "form switches to 7K during play");
            Expect(engine.LaneCount == 7, "engine switches to 7K during play");
            Expect(engine.Notes.All(n => n.Lane >= 0 && n.Lane < 7 && n.EndLane >= 0 && n.EndLane < 7), "7K live switch remaps active notes");
            engine.Stop();
        }
        finally
        {
            UserSettingsStore.DefaultSaveFilePathOverride = null;
        }
    }

    private static void TestUiSmokeAndResolutionDraw()
    {
        SongDataStore.DefaultSaveFilePathOverride = Path.Combine(AppContext.BaseDirectory, "SelfTestData", "song_data.json");
        AchievementProgressStore.DefaultSaveFilePathOverride = Path.Combine(AppContext.BaseDirectory, "SelfTestData", "player_progress.json");
        try
        {
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
                DrawSettingsTabs(form);
                DrawScreen(form, "SongSelect", null);
                DrawScreen(form, "Achievement", null);
                DrawGameScreen(form, size);
                DrawPausedGameScreen(form, size);
                DrawScreen(form, "Analyze", null);
            }
        }
        finally
        {
            SongDataStore.DefaultSaveFilePathOverride = null;
            AchievementProgressStore.DefaultSaveFilePathOverride = null;
        }
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
        MethodInfo getSpeedPanel = typeof(GameForm).GetMethod("GetSpeedPanelBounds", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("GetSpeedPanelBounds not found.");
        MethodInfo getSpeedValue = typeof(GameForm).GetMethod("GetSpeedValueBounds", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("GetSpeedValueBounds not found.");
        MethodInfo getMinus = typeof(GameForm).GetMethod("GetSpeedMinusButtonBounds", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("GetSpeedMinusButtonBounds not found.");
        MethodInfo getPlus = typeof(GameForm).GetMethod("GetSpeedPlusButtonBounds", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("GetSpeedPlusButtonBounds not found.");
        MethodInfo getRail = typeof(GameForm).GetMethod("GetSpeedRailBounds", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("GetSpeedRailBounds not found.");

        RectangleF speedPanel = (RectangleF)getSpeedPanel.Invoke(form, [])!;
        RectangleF speedValue = (RectangleF)getSpeedValue.Invoke(form, [speedPanel])!;
        RectangleF minus = (RectangleF)getMinus.Invoke(form, [speedPanel])!;
        RectangleF plus = (RectangleF)getPlus.Invoke(form, [speedPanel])!;
        RectangleF rail = (RectangleF)getRail.Invoke(form, [speedPanel])!;
        Expect(speedPanel.Contains(speedValue), $"speed value stays inside panel at {size.Width}x{size.Height}");
        Expect(speedPanel.Contains(minus) && speedPanel.Contains(plus) && speedPanel.Contains(rail), $"speed controls stay inside panel at {size.Width}x{size.Height}");
        Expect(speedValue.Bottom < minus.Top && speedValue.Bottom < plus.Top, $"speed value does not overlap buttons at {size.Width}x{size.Height}");
        Expect(rail.Left > minus.Right && rail.Right < plus.Left, $"speed rail stays between buttons at {size.Width}x{size.Height}");
        Expect(rail.Top < minus.Bottom && rail.Bottom > minus.Top, $"speed rail vertically aligns with buttons at {size.Width}x{size.Height}");

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

    private static void TestPerspectiveNoteLaneAlignment()
    {
        using var form = new GameForm(selfTestMode: true) { ClientSize = new Size(1680, 944), ShowInTaskbar = false };
        MethodInfo getPlayArea = typeof(GameForm).GetMethod("GetPlayAreaBounds", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("GetPlayAreaBounds not found.");
        MethodInfo getNoteRect = typeof(GameForm).GetMethod("GetPerspectiveLaneNoteRect", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("GetPerspectiveLaneNoteRect not found.");
        MethodInfo getLaneX = typeof(GameForm).GetMethod("PerspectiveLaneX", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("PerspectiveLaneX not found.");
        MethodInfo createRoundedRect = typeof(GameForm).GetMethod(
            "CreateRoundedRect",
            BindingFlags.NonPublic | BindingFlags.Static,
            binder: null,
            types: [typeof(RectangleF), typeof(float)],
            modifiers: null)
            ?? throw new InvalidOperationException("CreateRoundedRect(RectangleF, float) not found.");
        FieldInfo laneModeIndex = typeof(GameForm).GetField("_laneModeIndex", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("_laneModeIndex field not found.");

        for (int mode = 0; mode < 4; mode++)
        {
            laneModeIndex.SetValue(form, mode);
            Rectangle playArea = (Rectangle)getPlayArea.Invoke(form, [])!;
            int hitY = form.ClientSize.Height - (int)GameEngine.HitZoneOffset;
            int laneCount = mode switch { 0 => 4, 1 => 5, 2 => 6, _ => 7 };

            for (int lane = 0; lane < laneCount; lane++)
            {
                float topLaneLeft = (float)getLaneX.Invoke(form, [playArea, lane, playArea.Top + 20f, hitY])!;
                float midLaneLeft = (float)getLaneX.Invoke(form, [playArea, lane, (playArea.Top + hitY) / 2f, hitY])!;
                float hitLaneLeft = (float)getLaneX.Invoke(form, [playArea, lane, hitY, hitY])!;
                float topLaneRight = (float)getLaneX.Invoke(form, [playArea, lane + 1, playArea.Top + 20f, hitY])!;
                float midLaneRight = (float)getLaneX.Invoke(form, [playArea, lane + 1, (playArea.Top + hitY) / 2f, hitY])!;
                float hitLaneRight = (float)getLaneX.Invoke(form, [playArea, lane + 1, hitY, hitY])!;
                var topRect = (RectangleF)getNoteRect.Invoke(form, [playArea, lane, playArea.Top + 20f, hitY])!;
                var midRect = (RectangleF)getNoteRect.Invoke(form, [playArea, lane, (playArea.Top + hitY) / 2f, hitY])!;
                var hitRect = (RectangleF)getNoteRect.Invoke(form, [playArea, lane, hitY, hitY])!;

                ExpectNear(topLaneLeft, hitLaneLeft, 0.01f, $"{laneCount}K lane {lane} left boundary stays vertical");
                ExpectNear(midLaneLeft, hitLaneLeft, 0.01f, $"{laneCount}K lane {lane} mid left boundary stays vertical");
                ExpectNear(topLaneRight, hitLaneRight, 0.01f, $"{laneCount}K lane {lane} right boundary stays vertical");
                ExpectNear(midLaneRight, hitLaneRight, 0.01f, $"{laneCount}K lane {lane} mid right boundary stays vertical");
                AssertNoteFitsPerspectiveLane(getLaneX, form, playArea, lane, topRect, hitY, $"{laneCount}K lane {lane} top");
                AssertNoteFitsPerspectiveLane(getLaneX, form, playArea, lane, midRect, hitY, $"{laneCount}K lane {lane} mid");
                AssertNoteFitsPerspectiveLane(getLaneX, form, playArea, lane, hitRect, hitY, $"{laneCount}K lane {lane} hit");
                ExpectNear(topRect.Width, hitRect.Width, 0.01f, $"{laneCount}K lane {lane} note width stays constant on vertical lane");
                ExpectNear(midRect.Width, hitRect.Width, 0.01f, $"{laneCount}K lane {lane} note mid width stays constant on vertical lane");
                ExpectNear(topRect.Height, hitRect.Height, 0.01f, $"{laneCount}K lane {lane} note height stays constant");
                ExpectNear(midRect.Height, hitRect.Height, 0.01f, $"{laneCount}K lane {lane} note mid height stays constant");
            }
        }

        RectangleF subpixelNote = new(10.25f, 20.5f, 120.5f, 17.25f);
        using var subpixelPath = (System.Drawing.Drawing2D.GraphicsPath)createRoundedRect.Invoke(null, [subpixelNote, 5f])!;
        RectangleF subpixelBounds = subpixelPath.GetBounds();
        ExpectNear(subpixelBounds.Left, subpixelNote.Left, 0.01f, "rounded note path preserves subpixel left");
        ExpectNear(subpixelBounds.Top, subpixelNote.Top, 0.01f, "rounded note path preserves subpixel top");
        ExpectNear(subpixelBounds.Width, subpixelNote.Width, 0.01f, "rounded note path preserves subpixel width");
    }

    private static void AssertNoteFitsPerspectiveLane(MethodInfo getLaneX, GameForm form, Rectangle playArea, int lane, RectangleF rect, int hitY, string label)
    {
        float centerY = rect.Top + rect.Height / 2f;
        float left = (float)getLaneX.Invoke(form, [playArea, lane, centerY, hitY])!;
        float right = (float)getLaneX.Invoke(form, [playArea, lane + 1, centerY, hitY])!;
        float center = rect.Left + rect.Width / 2f;

        Expect(rect.Left >= left - 0.01f, $"{label} note left stays inside lane");
        Expect(rect.Right <= right + 0.01f, $"{label} note right stays inside lane");
        ExpectNear(center, (left + right) / 2f, 0.01f, $"{label} note center follows lane center");
        ExpectNear(rect.Width, (right - left) * 0.62f, 0.01f, $"{label} note width follows lane width");
    }

    private static void TestAnalyzeLayoutBounds()
    {
        Size[] sizes = [new(960, 640), new(1680, 944), new(2560, 1080)];
        foreach (Size size in sizes)
        {
            using var form = new GameForm(selfTestMode: true) { ClientSize = size, ShowInTaskbar = false };
            MethodInfo getPanel = typeof(GameForm).GetMethod("GetAnalyzeContentBounds", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("GetAnalyzeContentBounds not found.");
            MethodInfo getAction = typeof(GameForm).GetMethod("GetAnalyzeActionButtonBounds", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("GetAnalyzeActionButtonBounds not found.");

            Rectangle panel = (Rectangle)getPanel.Invoke(form, [])!;
            Rectangle previous = Rectangle.Empty;
            for (int i = 0; i < 3; i++)
            {
                Rectangle button = (Rectangle)getAction.Invoke(form, [i])!;
                Expect(panel.Contains(button), $"analyze button {i} stays inside panel at {size.Width}x{size.Height}");
                if (i > 0)
                    Expect(button.Left > previous.Right, $"analyze button {i} does not overlap previous at {size.Width}x{size.Height}");
                previous = button;
            }

            DrawScreen(form, "Analyze", null);
        }
    }

    private static void DrawPausedGameScreen(GameForm form, Size size)
    {
        FieldInfo engineField = typeof(GameForm).GetField("_engine", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("_engine field not found.");
        FieldInfo pausedField = typeof(GameForm).GetField("_isGamePaused", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("_isGamePaused field not found.");
        MethodInfo getPauseActionBounds = typeof(GameForm).GetMethod("GetPauseActionButtonBounds", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("GetPauseActionButtonBounds not found.");

        var engine = (GameEngine)engineField.GetValue(form)!;
        engine.Start(size.Height, [new LaneNote(2f, 0), new LaneNote(2.5f, 1, NoteType.Long, 0.5f, 1)], 4);
        engine.Update(0.016f, 1.0f);
        pausedField.SetValue(form, true);
        try
        {
            Rectangle first = (Rectangle)getPauseActionBounds.Invoke(form, [0])!;
            Rectangle second = (Rectangle)getPauseActionBounds.Invoke(form, [1])!;
            Expect(second.Top > first.Top && second.Left == first.Left, "pause menu actions stack vertically");
            DrawToBitmapAndAssert(form, size, "Paused game");
        }
        finally
        {
            pausedField.SetValue(form, false);
            engine.Stop();
        }
    }

    private static void TestSettingsPagesRenderAndInteract()
    {
        UserSettingsStore.DefaultSaveFilePathOverride = TempFile("settings_interaction.json");
        AchievementProgressStore.DefaultSaveFilePathOverride = TempFile("achievement_progress.json");
        try
        {
            using var form = new GameForm(selfTestMode: true) { ClientSize = new Size(1680, 944), ShowInTaskbar = false };
            FieldInfo screenField = typeof(GameForm).GetField("_screen", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("_screen field not found.");
            FieldInfo tabField = typeof(GameForm).GetField("_settingsTabIndex", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("_settingsTabIndex field not found.");
            FieldInfo speedField = typeof(GameForm).GetField("_speedMultiplier", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("_speedMultiplier field not found.");
            FieldInfo textScaleField = typeof(GameForm).GetField("_textScalePercent", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("_textScalePercent field not found.");
            FieldInfo hitSoundField = typeof(GameForm).GetField("_hitSoundSkin", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("_hitSoundSkin field not found.");
            FieldInfo hitSoundLabelsField = typeof(GameForm).GetField("HitSoundSkinLabels", BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new InvalidOperationException("HitSoundSkinLabels field not found.");
            FieldInfo playModeLabelsField = typeof(GameForm).GetField("PlayModeLabels", BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new InvalidOperationException("PlayModeLabels field not found.");
            FieldInfo playModeIndexField = typeof(GameForm).GetField("_playModeIndex", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("_playModeIndex field not found.");
            FieldInfo keyBindingModeField = typeof(GameForm).GetField("_keyBindingModeIndex", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("_keyBindingModeIndex field not found.");
            FieldInfo keyBindingCaptureField = typeof(GameForm).GetField("_keyBindingCaptureLane", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("_keyBindingCaptureLane field not found.");
            FieldInfo laneKeyBindingsField = typeof(GameForm).GetField("_laneKeyBindings", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("_laneKeyBindings field not found.");
            FieldInfo keyTestPressedField = typeof(GameForm).GetField("_keyTestPressed", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("_keyTestPressed field not found.");
            FieldInfo calibrationStopwatchField = typeof(GameForm).GetField("_calibrationStopwatch", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("_calibrationStopwatch field not found.");
            FieldInfo calibrationOffsetsField = typeof(GameForm).GetField("_calibrationOffsets", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("_calibrationOffsets field not found.");
            FieldInfo calibrationTargetsField = typeof(GameForm).GetField("_calibrationTargetTimes", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("_calibrationTargetTimes field not found.");
            FieldInfo calibrationSavedField = typeof(GameForm).GetField("_calibrationSaved", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("_calibrationSaved field not found.");
            FieldInfo audioOffsetField = typeof(GameForm).GetField("_audioOffsetMs", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("_audioOffsetMs field not found.");

            MethodInfo mouseDown = typeof(GameForm).GetMethod("HandleSettingsMouseDown", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("HandleSettingsMouseDown not found.");
            MethodInfo getTabBounds = typeof(GameForm).GetMethod("GetSettingsTabBounds", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("GetSettingsTabBounds not found.");
            MethodInfo getTrackBounds = typeof(GameForm).GetMethod("GetSliderTrackBounds", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("GetSliderTrackBounds not found.");
            MethodInfo getSegmentBounds = typeof(GameForm).GetMethod("GetSettingsSegmentBounds", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("GetSettingsSegmentBounds not found.");
            MethodInfo getDropdownItemBounds = typeof(GameForm).GetMethod("GetSettingsDropdownItemBounds", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("GetSettingsDropdownItemBounds not found.");
            MethodInfo doesDropdownValueFit = typeof(GameForm).GetMethod("DoesDropdownValueFit", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("DoesDropdownValueFit not found.");
            MethodInfo getToggleBounds = typeof(GameForm).GetMethod("GetSettingsToggleBounds", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("GetSettingsToggleBounds not found.");
            MethodInfo getRowCenterY = typeof(GameForm).GetMethod("GetSettingsRowCenterY", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("GetSettingsRowCenterY not found.");
            MethodInfo getKeyBindingBounds = typeof(GameForm).GetMethod("GetKeyBindingEntryButtonBounds", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("GetKeyBindingEntryButtonBounds not found.");
            MethodInfo getCalibrationBounds = typeof(GameForm).GetMethod("GetCalibrationEntryButtonBounds", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("GetCalibrationEntryButtonBounds not found.");
            MethodInfo getSystemResetBounds = typeof(GameForm).GetMethod("GetSettingsSystemResetButtonBounds", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("GetSettingsSystemResetButtonBounds not found.");
            MethodInfo processAutoPlayMode = typeof(GameForm).GetMethod("ProcessAutoPlayMode", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("ProcessAutoPlayMode not found.");
            MethodInfo keyBindingMouseDown = typeof(GameForm).GetMethod("HandleKeyBindingsMouseDown", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("HandleKeyBindingsMouseDown not found.");
            MethodInfo keyBindingKeyDown = typeof(GameForm).GetMethod("HandleKeyBindingsKeyDown", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("HandleKeyBindingsKeyDown not found.");
            MethodInfo keyBindingKeyUp = typeof(GameForm).GetMethod("HandleKeyBindingsKeyUp", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("HandleKeyBindingsKeyUp not found.");
            MethodInfo getKeyBindingModeBounds = typeof(GameForm).GetMethod("GetKeyBindingModeTabBounds", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("GetKeyBindingModeTabBounds not found.");
            MethodInfo getKeyBindingLaneBounds = typeof(GameForm).GetMethod("GetKeyBindingLaneBounds", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("GetKeyBindingLaneBounds not found.");
            MethodInfo getKeyBindingResetBounds = typeof(GameForm).GetMethod("GetKeyBindingResetButtonBounds", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("GetKeyBindingResetButtonBounds not found.");
            MethodInfo getKeyBindingDoneBounds = typeof(GameForm).GetMethod("GetKeyBindingDoneButtonBounds", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("GetKeyBindingDoneButtonBounds not found.");
            MethodInfo inputCalibrationMouseDown = typeof(GameForm).GetMethod("HandleInputCalibrationMouseDown", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("HandleInputCalibrationMouseDown not found.");
            MethodInfo getCalibrationBackBounds = typeof(GameForm).GetMethod("GetCalibrationBackButtonBounds", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("GetCalibrationBackButtonBounds not found.");
            MethodInfo getCalibrationStartBounds = typeof(GameForm).GetMethod("GetCalibrationStartButtonBounds", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("GetCalibrationStartButtonBounds not found.");
            MethodInfo saveCalibrationResult = typeof(GameForm).GetMethod("SaveInputCalibrationResult", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("SaveInputCalibrationResult not found.");
            FieldInfo engineField = typeof(GameForm).GetField("_engine", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("_engine field not found.");

            object settingsValue = Enum.Parse(screenField.FieldType, "Settings");
            screenField.SetValue(form, settingsValue);

            for (int tab = 0; tab < 5; tab++)
            {
                Rectangle tabBounds = (Rectangle)getTabBounds.Invoke(form, [tab])!;
                mouseDown.Invoke(form, [tabBounds.Center()]);
                Expect((int)tabField.GetValue(form)! == tab, $"settings tab {tab} activated");
                DrawToBitmapAndAssert(form, form.ClientSize, $"Settings tab {tab}");
            }

            tabField.SetValue(form, 0);
            string[] hitSoundLabels = (string[])hitSoundLabelsField.GetValue(null)!;
            Expect(hitSoundLabels.SequenceEqual(["CLASSIC", "EDM", "LO-FI"]), "fixed hit sound labels");
            Rectangle hitSoundBounds = (Rectangle)getSegmentBounds.Invoke(form, ["hitskin"])!;
            Rectangle hitPitchBounds = (Rectangle)getSegmentBounds.Invoke(form, ["hitpitch"])!;
            Rectangle muteBounds = (Rectangle)getToggleBounds.Invoke(form, ["hitmute"])!;
            ExpectNear(hitSoundBounds.Top + hitSoundBounds.Height / 2f, (float)getRowCenterY.Invoke(form, [3])!, 1f, "hit sound control row aligned");
            ExpectNear(hitPitchBounds.Top + hitPitchBounds.Height / 2f, (float)getRowCenterY.Invoke(form, [4])!, 1f, "hit pitch control row aligned");
            ExpectNear(muteBounds.Top + muteBounds.Height / 2f, (float)getRowCenterY.Invoke(form, [5])!, 1f, "hit mute control row aligned");
            var settingsClickStopwatch = Stopwatch.StartNew();
            for (int i = 0; i < 20; i++)
                mouseDown.Invoke(form, [muteBounds.Center()]);
            settingsClickStopwatch.Stop();
            Expect(settingsClickStopwatch.ElapsedMilliseconds < 500, "settings clicks remain responsive");
            mouseDown.Invoke(form, [hitSoundBounds.Center()]);
            DrawToBitmapAndAssert(form, form.ClientSize, "Settings hit sound dropdown open");
            Rectangle hitSoundEdmItem = (Rectangle)getDropdownItemBounds.Invoke(form, ["hitskin", 1])!;
            mouseDown.Invoke(form, [hitSoundEdmItem.Center()]);
            Expect(string.Equals((string)hitSoundField.GetValue(form)!, "EDM", StringComparison.Ordinal), "hit sound EDM segment selectable");
            mouseDown.Invoke(form, [hitSoundBounds.Center()]);
            Rectangle hitSoundLofiItem = (Rectangle)getDropdownItemBounds.Invoke(form, ["hitskin", 2])!;
            mouseDown.Invoke(form, [hitSoundLofiItem.Center()]);
            Expect(string.Equals((string)hitSoundField.GetValue(form)!, "LO-FI", StringComparison.Ordinal), "hit sound LO-FI segment selectable");

            tabField.SetValue(form, 1);
            string[] playModeLabels = (string[])playModeLabelsField.GetValue(null)!;
            Expect(playModeLabels.SequenceEqual(["NORMAL", "PRACTICE", "AUTO"]), "three play mode labels");
            Rectangle playModeBounds = (Rectangle)getSegmentBounds.Invoke(form, ["playmode"])!;
            mouseDown.Invoke(form, [playModeBounds.Center()]);
            Rectangle autoPlayItem = (Rectangle)getDropdownItemBounds.Invoke(form, ["playmode", 2])!;
            mouseDown.Invoke(form, [autoPlayItem.Center()]);
            Expect((int)playModeIndexField.GetValue(form)! == 2, "auto play mode selectable");
            var engine = (GameEngine)engineField.GetValue(form)!;
            engine.Start(form.ClientSize.Height, [new LaneNote(0f, 0)], 4);
            engine.Update(0.016f, 0f);
            processAutoPlayMode.Invoke(form, []);
            Expect(engine.Score.TotalJudgedNotes > 0, "auto play mode judges notes");
            engine.Stop();
            speedField.SetValue(form, 1.0f);
            float oldSpeed = (float)speedField.GetValue(form)!;
            Rectangle noteSpeedTrack = (Rectangle)getTrackBounds.Invoke(form, [Enum.Parse(typeof(SettingsSlider), "NoteSpeed")])!;
            mouseDown.Invoke(form, [new Point(noteSpeedTrack.Right - 1, noteSpeedTrack.Top + noteSpeedTrack.Height / 2)]);
            Expect((float)speedField.GetValue(form)! > oldSpeed, "note speed slider changes value");

            tabField.SetValue(form, 2);
            Rectangle laneModeBounds = (Rectangle)getSegmentBounds.Invoke(form, ["lanemode"])!;
            using (var fitBitmap = new Bitmap(form.ClientSize.Width, form.ClientSize.Height))
            using (Graphics fitGraphics = Graphics.FromImage(fitBitmap))
            using (var fitFont = new Font("Segoe UI", 12.5f, FontStyle.Regular))
            {
                bool fits = (bool)doesDropdownValueFit.Invoke(form, [fitGraphics, "S  D  F  Space  J  K  L", fitFont, laneModeBounds])!;
                Expect(fits, "7K key layout text fits dropdown bounds");
            }
            Rectangle keyBindingBounds = (Rectangle)getKeyBindingBounds.Invoke(form, [])!;
            mouseDown.Invoke(form, [keyBindingBounds.Center()]);
            Expect(screenField.GetValue(form)!.ToString() == "KeyBindings", "key bindings page opens");
            Rectangle keyModeBounds = (Rectangle)getKeyBindingModeBounds.Invoke(form, [])!;
            keyBindingMouseDown.Invoke(form, [new Point(keyModeBounds.Left + keyModeBounds.Width * 7 / 8, keyModeBounds.Top + keyModeBounds.Height / 2)]);
            Expect((int)keyBindingModeField.GetValue(form)! == 3, "7K key binding tab activates");
            DrawToBitmapAndAssert(form, form.ClientSize, "KeyBindings 7K");
            Rectangle laneOneBounds = (Rectangle)getKeyBindingLaneBounds.Invoke(form, [0])!;
            keyBindingMouseDown.Invoke(form, [laneOneBounds.Center()]);
            Expect((int)keyBindingCaptureField.GetValue(form)! == 0, "lane capture starts");
            keyBindingKeyDown.Invoke(form, [Keys.A]);
            Keys[][] laneKeyBindings = (Keys[][])laneKeyBindingsField.GetValue(form)!;
            Expect(laneKeyBindings[3][0] == Keys.A, "lane key assignment works");
            Expect((int)keyBindingCaptureField.GetValue(form)! == -1, "lane capture ends after assignment");
            keyBindingKeyDown.Invoke(form, [Keys.A]);
            bool[] pressed = (bool[])keyTestPressedField.GetValue(form)!;
            Expect(pressed[0], "ghosting test highlights pressed key");
            keyBindingKeyUp.Invoke(form, [Keys.A]);
            Expect(!pressed[0], "ghosting test clears released key");
            Rectangle keyResetBounds = (Rectangle)getKeyBindingResetBounds.Invoke(form, [])!;
            keyBindingMouseDown.Invoke(form, [keyResetBounds.Center()]);
            laneKeyBindings = (Keys[][])laneKeyBindingsField.GetValue(form)!;
            Expect(laneKeyBindings[3][0] == Keys.S, "key binding reset restores 7K defaults");
            Rectangle keyDoneBounds = (Rectangle)getKeyBindingDoneBounds.Invoke(form, [])!;
            keyBindingMouseDown.Invoke(form, [keyDoneBounds.Center()]);
            Expect(screenField.GetValue(form)!.ToString() == "Settings", "key binding done returns to settings");

            screenField.SetValue(form, settingsValue);
            tabField.SetValue(form, 2);
            Rectangle calibrationBounds = (Rectangle)getCalibrationBounds.Invoke(form, [])!;
            mouseDown.Invoke(form, [calibrationBounds.Center()]);
            Expect(screenField.GetValue(form)!.ToString() == "InputCalibration", "calibration page opens");
            var calibrationStopwatch = (Stopwatch)calibrationStopwatchField.GetValue(form)!;
            Expect(!calibrationStopwatch.IsRunning, "calibration waits for start on entry");
            DrawToBitmapAndAssert(form, form.ClientSize, "Input calibration ready");
            Rectangle calibrationStartBounds = (Rectangle)getCalibrationStartBounds.Invoke(form, [])!;
            inputCalibrationMouseDown.Invoke(form, [calibrationStartBounds.Center()]);
            Expect(calibrationStopwatch.IsRunning, "calibration start button starts sampling");
            var calibrationTargets = (List<float>)calibrationTargetsField.GetValue(form)!;
            Expect(calibrationTargets.Count == 10, "calibration schedules 10 target beats");
            Expect(CalibrationScheduleHasMixedIntervals(calibrationTargets), "calibration schedule mixes fast slow offbeat steady timing");
            var calibrationOffsets = (List<float>)calibrationOffsetsField.GetValue(form)!;
            calibrationOffsets.Clear();
            for (int i = 0; i < 8; i++)
                calibrationOffsets.Add(0.012f);
            calibrationOffsets.Add(0.250f);
            calibrationOffsets.Add(-0.200f);
            saveCalibrationResult.Invoke(form, []);
            Expect((bool)calibrationSavedField.GetValue(form)!, "calibration result saves after samples");
            Expect(Math.Abs((int)audioOffsetField.GetValue(form)! - 12) <= 2, "auto sync rejects calibration outliers");
            DrawToBitmapAndAssert(form, form.ClientSize, "Input calibration saved");
            Rectangle calibrationBackBounds = (Rectangle)getCalibrationBackBounds.Invoke(form, [])!;
            inputCalibrationMouseDown.Invoke(form, [calibrationBackBounds.Center()]);
            Expect(screenField.GetValue(form)!.ToString() == "Settings", "calibration back returns to settings");

            screenField.SetValue(form, settingsValue);
            tabField.SetValue(form, 3);
            Size oldSize = form.ClientSize;
            Rectangle resolutionBounds = (Rectangle)getSegmentBounds.Invoke(form, ["display"])!;
            mouseDown.Invoke(form, [resolutionBounds.Center()]);
            Rectangle resolutionItem = (Rectangle)getDropdownItemBounds.Invoke(form, ["display", 0])!;
            mouseDown.Invoke(form, [resolutionItem.Center()]);
            Expect(form.ClientSize != oldSize, "resolution control changes window size");

            screenField.SetValue(form, settingsValue);
            tabField.SetValue(form, 4);
            textScaleField.SetValue(form, 130);
            Rectangle resetBounds = (Rectangle)getSystemResetBounds.Invoke(form, [])!;
            mouseDown.Invoke(form, [resetBounds.Center()]);
            Expect((int)textScaleField.GetValue(form)! == 100, "system reset restores defaults");
        }
        finally
        {
            UserSettingsStore.DefaultSaveFilePathOverride = null;
            AchievementProgressStore.DefaultSaveFilePathOverride = null;
        }
    }

    private static void DrawSettingsTabs(GameForm form)
    {
        FieldInfo screenField = typeof(GameForm).GetField("_screen", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("_screen field not found.");
        FieldInfo tabField = typeof(GameForm).GetField("_settingsTabIndex", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("_settingsTabIndex field not found.");
        object settingsValue = Enum.Parse(screenField.FieldType, "Settings");
        screenField.SetValue(form, settingsValue);

        for (int tab = 0; tab < 5; tab++)
        {
            tabField.SetValue(form, tab);
            DrawToBitmapAndAssert(form, form.ClientSize, $"Settings tab {tab}");
        }
    }

    private static bool CalibrationScheduleHasMixedIntervals(List<float> targets)
    {
        if (targets.Count < 10)
            return false;

        HashSet<int> roundedIntervals = [];
        for (int i = 1; i < targets.Count; i++)
            roundedIntervals.Add((int)MathF.Round((targets[i] - targets[i - 1]) * 100f));

        return roundedIntervals.Contains(62) &&
            roundedIntervals.Contains(84) &&
            roundedIntervals.Contains(54) &&
            roundedIntervals.Contains(90) &&
            roundedIntervals.Contains(72) &&
            !roundedIntervals.Contains(48) &&
            !roundedIntervals.Contains(92);
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
