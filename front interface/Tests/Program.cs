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
        Run("NoteLane all lane-mode normalization", TestLaneModeChartNormalization);
        Run("ChartGenerator filename and tempo map", TestChartGenerator);
        Run("UserSettingsStore backup recovery", TestUserSettingsStore);
        Run("All lane-mode key binding persistence", TestLaneModeKeyBindingPersistence);
        Run("SongDataStore lane-mode records/history", TestSongDataStore);
        Run("Statistics snapshot uses real play history", TestStatisticsSnapshot);
        Run("Result feedback timing and miss guidance", TestResultFeedbackSummary);
        Run("Replay v3 compatibility and semantic result verification", TestReplayCompatibility);
        Run("Replay frame ordering and live input isolation", TestReplayFrameOrderingAndLiveInputIsolation);
        Run("Replay pause-resume preserves hold state", TestReplayPauseResumePreservesHoldState);
        Run("Song Select uses actual song files", TestSongSelectUsesActualSongFiles);
        Run("Song difficulty preserves selected song", TestSongDifficultyPreservesSelection);
        Run("Judgment timing simulation", TestJudgmentTimingSimulation);
        Run("GameEngine clock and pause-resume hold grace", TestEngineClockAndPauseResumeGrace);
        Run("Hold grace scoring and late reacquire", TestHoldGraceScoringAndLateReacquire);
        Run("Audio clock diagnostics telemetry", TestAudioClockDiagnostics);
        Run("Long and slide note behavior", TestLongAndSlideNotes);
        Run("Judgment event phase and failure taxonomy", TestJudgmentEventFailureTaxonomy);
        Run("Combo, speed, and live lane switching", TestComboSpeedAndLiveLaneSwitching);
        Run("Perspective note lane alignment", TestPerspectiveNoteLaneAlignment);
        Run("Analyze layout bounds", TestAnalyzeLayoutBounds);
        Run("Analyze accessibility and replay status contrast", TestAnalyzeAccessibilityAndReplayStatusContrast);
        Run("Analyze Enter returns to song select", TestAnalyzeEnterReturnsToSongSelect);
        Run("Analyze retry and next follow song identity", TestAnalyzeSongIdentityAcrossSortChanges);
        Run("Countdown accessibility replaces song select", TestCountdownAccessibility);
        Run("UI smoke and resolution draw", TestUiSmokeAndResolutionDraw);
        Run("Game frame pacing and background cache", TestGameFramePacingAndBackgroundCache);
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

    private static void TestLaneModeChartNormalization()
    {
        string title = "self_test_lane_coverage_" + Guid.NewGuid().ToString("N");
        string chartDirectory = Path.Combine(Path.GetTempPath(), "MuWorld.SelfTests", Guid.NewGuid().ToString("N"), "Charts");
        ChartGenerator.UserChartDirectoryOverride = chartDirectory;
        string path = ChartGenerator.GetUserChartPath(title, difficultyIndex: 0);
        Directory.CreateDirectory(chartDirectory);

        var lines = new List<string> { "#BPM 120" };
        for (int measure = 0; measure < 4; measure++)
        {
            for (int lane = 0; lane < 4; lane++)
                lines.Add($"#{measure:D3}{lane + 11:D2}:01");
        }
        File.WriteAllLines(path, lines);

        try
        {
            const int sourceNoteCount = 16;
            foreach (int laneCount in new[] { 4, 5, 6, 7 })
            {
                ChartValidationResult chart = NoteLane.LoadValidatedChart(title, "self", difficultyIndex: 0, laneCount);
                Expect(chart.Notes.Count == sourceNoteCount, $"{laneCount}K normalization preserves every source note");
                Expect(chart.Diagnostics.Count == 0, $"{laneCount}K normalization has no diagnostics");
                Expect(chart.Notes.All(note =>
                    note.Lane >= 0 && note.Lane < laneCount &&
                    note.EndLane >= 0 && note.EndLane < laneCount), $"{laneCount}K normalized lanes stay in range");

                HashSet<int> usedLanes = chart.Notes.Select(note => note.Lane).ToHashSet();
                Expect(usedLanes.SetEquals(Enumerable.Range(0, laneCount)), $"{laneCount}K normalization covers every lane");
            }

            ChartValidationResult sixKeyChart = NoteLane.LoadValidatedChart(title, "self", difficultyIndex: 0, laneCount: 6);
            Expect(sixKeyChart.Notes.Any(note => note.Lane == 5), "6K normalization reaches lane 6 without creating out-of-range lane 7");
        }
        finally
        {
            ChartGenerator.UserChartDirectoryOverride = null;
            TryDeleteDirectory(chartDirectory);
        }
    }

    private static void TestChartGenerator()
    {
        Expect(ChartGenerator.GetChartFileName("A Song: Test", 0) == "easy_a_song_test.bms", "easy filename normalization");
        Expect(ChartGenerator.GetChartFileName("A Song: Test", 2, 9).EndsWith("_7k.bms", StringComparison.Ordinal), "lane filename clamp");

        string generatedDirectory = Path.Combine(Path.GetTempPath(), "MuWorld.SelfTests", Guid.NewGuid().ToString("N"), "GeneratedCharts");
        string userDirectory = Path.Combine(Path.GetTempPath(), "MuWorld.SelfTests", Guid.NewGuid().ToString("N"), "UserCharts");
        ChartGenerator.GeneratedChartDirectoryOverride = generatedDirectory;
        ChartGenerator.UserChartDirectoryOverride = userDirectory;
        try
        {
            const string precomputedTitle = "Precomputed Song";
            Expect(!ChartGenerator.HasAllPrecomputedCharts(precomputedTitle), "song is not ready before precomputed charts exist");
            Directory.CreateDirectory(generatedDirectory);
            MethodInfo generateBms = typeof(ChartGenerator).GetMethod("GenerateBms", BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new InvalidOperationException("GenerateBms not found.");
            var generatedBeats = Enumerable.Range(0, 160)
                .Select(index => new WavAnalyzer.BeatInfo(index * 0.25f, 0.5f + index % 5 * 0.1f, Confidence: 0.8f))
                .ToList();
            for (int difficulty = 0; difficulty < 3; difficulty++)
            for (int laneCount = 4; laneCount <= 7; laneCount++)
            {
                string path = ChartGenerator.GetGeneratedChartPath(precomputedTitle, difficulty, laneCount);
                string content = (string)generateBms.Invoke(null, [precomputedTitle, generatedBeats, difficulty, 40f, laneCount])!;
                File.WriteAllText(path, content);
                Expect(ChartGenerator.HasPrecomputedChart(precomputedTitle, difficulty, laneCount), $"{difficulty}:{laneCount}K precomputed chart detected");
                ChartValidationResult generatedChart = NoteLane.LoadValidatedChart(precomputedTitle, "self", difficulty, laneCount);
                Expect(generatedChart.Notes.Count > 0, $"{difficulty}:{laneCount}K precomputed chart parses notes");
                Expect(generatedChart.Notes.All(note => note.Lane >= 0 && note.Lane < laneCount), $"{difficulty}:{laneCount}K precomputed chart stays in lane range");
            }

            Expect(ChartGenerator.HasAllPrecomputedCharts(precomputedTitle), "all difficulty and lane charts mark song ready");
        }
        finally
        {
            ChartGenerator.GeneratedChartDirectoryOverride = null;
            ChartGenerator.UserChartDirectoryOverride = null;
            TryDeleteDirectory(Path.GetDirectoryName(generatedDirectory)!);
            TryDeleteDirectory(Path.GetDirectoryName(userDirectory)!);
        }

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

    private static void TestLaneModeKeyBindingPersistence()
    {
        string settingsPath = TempFile("lane_mode_settings.json");
        string achievementPath = TempFile("lane_mode_achievements.json");
        UserSettingsStore.DefaultSaveFilePathOverride = settingsPath;
        AchievementProgressStore.DefaultSaveFilePathOverride = achievementPath;

        Keys[][] expectedBindings =
        [
            [Keys.Z, Keys.X, Keys.C, Keys.V],
            [Keys.B, Keys.N, Keys.M, Keys.Oemcomma, Keys.OemPeriod],
            [Keys.A, Keys.S, Keys.D, Keys.F, Keys.G, Keys.H],
            [Keys.Q, Keys.W, Keys.E, Keys.R, Keys.T, Keys.Y, Keys.U],
        ];
        Keys[][] defaultBindings =
        [
            [Keys.D, Keys.F, Keys.J, Keys.K],
            [Keys.D, Keys.F, Keys.Space, Keys.J, Keys.K],
            [Keys.S, Keys.D, Keys.F, Keys.J, Keys.K, Keys.L],
            [Keys.S, Keys.D, Keys.F, Keys.Space, Keys.J, Keys.K, Keys.L],
        ];

        try
        {
            var store = new UserSettingsStore(settingsPath);
            using (var sourceForm = new GameForm(selfTestMode: true) { ShowInTaskbar = false })
            {
                Keys[][] bindings = GetLaneKeyBindings(sourceForm);
                for (int mode = 0; mode < expectedBindings.Length; mode++)
                    bindings[mode] = expectedBindings[mode].ToArray();

                store.Save(CreateSettingsSnapshot(sourceForm));
            }

            UserSettings persisted = store.Load();
            Expect(persisted.KeyBindings4K.SequenceEqual(expectedBindings[0].Select(key => key.ToString())), "4K bindings serialized to settings");
            Expect(persisted.KeyBindings5K.SequenceEqual(expectedBindings[1].Select(key => key.ToString())), "5K bindings serialized to settings");
            Expect(persisted.KeyBindings6K.SequenceEqual(expectedBindings[2].Select(key => key.ToString())), "6K bindings serialized to dedicated field");
            Expect(persisted.KeyBindings7K.SequenceEqual(expectedBindings[3].Select(key => key.ToString())), "7K bindings serialized independently from 6K");

            using (var reloadedForm = new GameForm(selfTestMode: true) { ShowInTaskbar = false })
                ExpectLaneBindings(reloadedForm, expectedBindings, "new-schema reload");

            string legacyPath = TempFile("legacy_lane_mode_settings.json");
            UserSettingsStore.DefaultSaveFilePathOverride = legacyPath;
            var legacyStore = new UserSettingsStore(legacyPath);
            Keys[] legacySixKeyBindings = [Keys.Q, Keys.W, Keys.E, Keys.R, Keys.T, Keys.Y];
            legacyStore.Save(new UserSettings
            {
                KeyBindings7K = legacySixKeyBindings.Select(key => key.ToString()).ToArray(),
            });

            using (var legacyForm = new GameForm(selfTestMode: true) { ShowInTaskbar = false })
            {
                Keys[][] bindings = GetLaneKeyBindings(legacyForm);
                Expect(bindings[2].SequenceEqual(legacySixKeyBindings), "legacy six-entry KeyBindings7K migrates into 6K runtime bindings");
                Expect(bindings[3].SequenceEqual(defaultBindings[3]), "legacy six-entry KeyBindings7K does not corrupt 7K defaults");
                legacyStore.Save(CreateSettingsSnapshot(legacyForm));
            }

            UserSettings migrated = legacyStore.Load();
            Expect(migrated.KeyBindings6K.SequenceEqual(legacySixKeyBindings.Select(key => key.ToString())), "legacy 6K bindings save into dedicated field");
            Expect(migrated.KeyBindings7K.SequenceEqual(defaultBindings[3].Select(key => key.ToString())), "legacy migration writes a real seven-entry 7K field");
            using (var migratedReload = new GameForm(selfTestMode: true) { ShowInTaskbar = false })
            {
                Keys[][] migratedBindings = GetLaneKeyBindings(migratedReload);
                Expect(migratedBindings[2].SequenceEqual(legacySixKeyBindings), "migrated 6K bindings survive a second form reload");
                Expect(migratedBindings[3].SequenceEqual(defaultBindings[3]), "migrated 7K bindings survive a second form reload");
            }

            string invalidPath = TempFile("invalid_lane_mode_settings.json");
            UserSettingsStore.DefaultSaveFilePathOverride = invalidPath;
            new UserSettingsStore(invalidPath).Save(new UserSettings
            {
                KeyBindings4K = ["Q"],
                KeyBindings5K = ["A", "A", "C", "D", "E"],
                KeyBindings6K = ["Escape", "B", "C", "D", "E", "F"],
                KeyBindings7K = ["Q", "W", "E", "DefinitelyNotAKey", "T", "Y", "U"],
            });

            using var invalidReload = new GameForm(selfTestMode: true) { ShowInTaskbar = false };
            ExpectLaneBindings(invalidReload, defaultBindings, "invalid-array fallback");

            string reservedPath = TempFile("reserved_lane_mode_settings.json");
            UserSettingsStore.DefaultSaveFilePathOverride = reservedPath;
            new UserSettingsStore(reservedPath).Save(new UserSettings
            {
                KeyBindings4K = ["P", "F", "J", "K"],
                KeyBindings5K = ["D1", "F", "Space", "J", "K"],
                KeyBindings6K = ["S", "D6", "F", "J", "K", "L"],
                KeyBindings7K = ["S", "D", "F", "Space", "J", "K", "NumPad7"],
            });

            using var reservedReload = new GameForm(selfTestMode: true) { ShowInTaskbar = false };
            ExpectLaneBindings(reservedReload, defaultBindings, "reserved-key settings fallback");
            MethodInfo tryAssignKeyBinding = typeof(GameForm).GetMethod("TryAssignKeyBinding", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("TryAssignKeyBinding not found.");
            Keys[] reservedKeys = [Keys.P, Keys.D1, Keys.D6, Keys.NumPad7];
            for (int mode = 0; mode < reservedKeys.Length; mode++)
            {
                bool assigned = (bool)tryAssignKeyBinding.Invoke(reservedReload, [mode, 0, reservedKeys[mode]])!;
                Expect(!assigned, $"{reservedKeys[mode]} is rejected as reserved in {defaultBindings[mode].Length}K mode");
                Expect(GetLaneKeyBindings(reservedReload)[mode].SequenceEqual(defaultBindings[mode]), $"rejected {reservedKeys[mode]} does not mutate {defaultBindings[mode].Length}K bindings");
            }

            string nullBindingsPath = TempFile("null_lane_mode_settings.json");
            UserSettingsStore.DefaultSaveFilePathOverride = nullBindingsPath;
            File.WriteAllText(
                nullBindingsPath,
                "{\"KeyBindings4K\":null,\"KeyBindings5K\":null,\"KeyBindings6K\":null,\"KeyBindings7K\":null}");
            UserSettings normalizedNullBindings = new UserSettingsStore(nullBindingsPath).Load();
            Expect(normalizedNullBindings.KeyBindings4K.Length == 0 && normalizedNullBindings.KeyBindings5K.Length == 0 &&
                normalizedNullBindings.KeyBindings6K.Length == 0 && normalizedNullBindings.KeyBindings7K.Length == 0,
                "settings store normalizes explicit-null key arrays before consumers see them");
            using var nullBindingsReload = new GameForm(selfTestMode: true) { ShowInTaskbar = false };
            ExpectLaneBindings(nullBindingsReload, defaultBindings, "explicit-null key-array fallback");
        }
        finally
        {
            UserSettingsStore.DefaultSaveFilePathOverride = null;
            AchievementProgressStore.DefaultSaveFilePathOverride = null;
        }
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

    private static void TestResultFeedbackSummary()
    {
        var stableScore = new ScoreManager();
        stableScore.AddHit(Judgment.Perfect, -0.002f);
        stableScore.AddHit(Judgment.Perfect, 0.002f);
        ResultFeedbackSummary stable = ResultFeedbackSummary.Create(stableScore, [], 0f, 10f);
        Expect(stable.TimingLabel == "STABLE 0ms", "balanced timing is summarized as stable");
        Expect(stable.NextGoal == "NEXT: TRY A HARDER CHART", "near-perfect stable play recommends a harder chart");
        Expect(stable.FailureLabel == "NO MISS BREAKS" && stable.MissPositions.Length == 0, "no-miss summary stays empty");

        var earlyScore = new ScoreManager();
        var lateScore = new ScoreManager();
        for (int i = 0; i < 4; i++)
        {
            earlyScore.AddHit(Judgment.Perfect, -0.020f);
            lateScore.AddHit(Judgment.Perfect, 0.030f);
        }

        ResultFeedbackSummary early = ResultFeedbackSummary.Create(earlyScore, [], 0f, 10f);
        ResultFeedbackSummary late = ResultFeedbackSummary.Create(lateScore, [], 0f, 10f);
        Expect(early.TimingLabel == "EARLY 20ms", "negative timing bias is summarized as early");
        Expect(late.TimingLabel == "LATE 30ms", "positive timing bias is summarized as late");
        Expect(early.NextGoal == "NEXT: CALIBRATE OR ADJUST INPUT TIMING", "early bias recommends timing calibration");
        Expect(late.NextGoal == "NEXT: CALIBRATE OR ADJUST INPUT TIMING", "late bias recommends timing calibration");

        var missScore = new ScoreManager();
        missScore.AddMiss();
        missScore.AddMiss();
        missScore.AddMiss();
        NoteJudgmentEvent[] misses =
        [
            new(20f, 20f, 0, 0, NoteType.Tap, NoteJudgmentPhase.Tap, null, NoteFailureReason.TapMiss, 0.2f),
            new(50f, 49f, 1, 1, NoteType.Long, NoteJudgmentPhase.Hold, null, NoteFailureReason.LongHoldBreak, 1f),
            new(90f, 90f, 2, 2, NoteType.Long, NoteJudgmentPhase.End, null, NoteFailureReason.LongEndMiss, 0f),
        ];
        ResultFeedbackSummary missSummary = ResultFeedbackSummary.Create(missScore, misses, 0f, 100f);
        Expect(missSummary.RecordedMissCount == missScore.MissCount, "feedback miss count matches score miss count");
        Expect(missSummary.FailureLabel == "TAP 1  START 0  HOLD 1  END 1", "feedback separates tap, start, hold, and end failures");
        Expect(missSummary.MissPositions.Length == 3, "feedback exposes every miss on the timeline");
        ExpectNear(missSummary.MissPositions[0], 0.2f, 0.001f, "first miss timeline position");
        ExpectNear(missSummary.MissPositions[1], 0.5f, 0.001f, "hold miss timeline position");
        ExpectNear(missSummary.MissPositions[2], 0.9f, 0.001f, "end miss timeline position");
        Expect(missSummary.NextGoal == "NEXT: KEEP HOLD / SLIDE LANES PRESSED", "hold break receives a specific next goal");

        var clusterScore = new ScoreManager();
        clusterScore.AddMiss();
        clusterScore.AddMiss();
        NoteJudgmentEvent[] clusteredMisses =
        [
            new(20f, 20f, 0, 0, NoteType.Tap, NoteJudgmentPhase.Tap, null, NoteFailureReason.TapMiss, 0f),
            new(21f, 21f, 1, 1, NoteType.Tap, NoteJudgmentPhase.Tap, null, NoteFailureReason.TapMiss, 0f),
        ];
        ResultFeedbackSummary cluster = ResultFeedbackSummary.Create(clusterScore, clusteredMisses, 0f, 120f);
        Expect(cluster.NextGoal.StartsWith("NEXT: REVIEW MISS CLUSTER ", StringComparison.Ordinal), "clustered misses identify a review timestamp");

        var denseScore = new ScoreManager();
        var denseMisses = new NoteJudgmentEvent[300];
        for (int i = 0; i < denseMisses.Length; i++)
        {
            denseScore.AddMiss();
            denseMisses[i] = new NoteJudgmentEvent(i, i, i % 4, i % 4, NoteType.Tap, NoteJudgmentPhase.Tap, null, NoteFailureReason.TapMiss, 0f);
        }

        ResultFeedbackSummary dense = ResultFeedbackSummary.Create(denseScore, denseMisses, 0f, 299f);
        Expect(dense.MissPositions.Length == 256, "dense miss timeline remains bounded to 256 samples");
        ExpectNear(dense.MissPositions[0], 0f, 0.001f, "dense miss timeline preserves the first miss");
        ExpectNear(dense.MissPositions[^1], 1f, 0.001f, "dense miss timeline preserves the last miss");
    }

    private static void TestReplayCompatibility()
    {
        var score = new ScoreManager();
        score.AddHit(Judgment.Perfect);
        score.AddHit(Judgment.Great, 0.020f);
        score.AddMiss();
        ResultGrade grade = score.Grade;
        ClearType clearType = score.ClearType;
        List<LaneNote> replayChart =
        [
            new LaneNote(1f, 0),
            new LaneNote(2f, 1),
            new LaneNote(3f, 2),
        ];
        string chartVersion = ReplayCompatibility.BuildChartVersion("replay-song", 1, 4, replayChart);

        var replay = new ReplayRecord
        {
            ReplayVersion = ReplayCompatibility.CurrentReplayVersion,
            GameVersion = ReplayCompatibility.CurrentGameVersion,
            ChartVersion = chartVersion,
            AudioFingerprint = "sha256:self-test",
            SongId = "replay-song",
            SongTitle = "Replay Song",
            Artist = "Self Test",
            DifficultyIndex = 1,
            Difficulty = "Normal",
            LaneCount = 4,
            AudioOffsetMs = 12,
            SpeedMultiplier = 1.2f,
            Score = score.Score,
            Accuracy = score.Accuracy,
            Grade = ScoreManager.FormatGrade(grade),
            ClearType = ScoreManager.FormatClearType(clearType),
            Settings = new ReplaySettingsSnapshot
            {
                AudioOffsetMs = 12,
                NoteSpeedPercent = 120,
                PlayModeIndex = 0,
                GameModeIndex = 0,
                LaneCount = 4,
            },
            Result = new ReplayResultSnapshot
            {
                Score = score.Score,
                Accuracy = score.Accuracy,
                Grade = ScoreManager.FormatGrade(grade),
                ClearType = ScoreManager.FormatClearType(clearType),
                PerfectCount = score.PerfectCount,
                GreatCount = score.GreatCount,
                BetterCount = score.BetterCount,
                GoodCount = score.GoodCount,
                BadCount = score.BadCount,
                MissCount = score.MissCount,
                MaxCombo = score.MaxCombo,
                MaxMissStreak = score.MaxMissStreak,
            },
            Chart = replayChart,
            Events =
            [
                new InputLogEvent(1.0f, 0, "D", true, "Perfect", "keyboard"),
                new InputLogEvent(1.1f, 0, "D", false, string.Empty, "keyboard"),
            ],
            Judgments =
            [
                new NoteJudgmentEvent(1.000f, 1.000f, 0, 0, NoteType.Tap, NoteJudgmentPhase.Tap, Judgment.Perfect, NoteFailureReason.None, 0f),
                new NoteJudgmentEvent(2.020f, 2.000f, 1, 1, NoteType.Tap, NoteJudgmentPhase.Tap, Judgment.Great, NoteFailureReason.None, 0.020f),
                new NoteJudgmentEvent(3.190f, 3.000f, 2, 2, NoteType.Tap, NoteJudgmentPhase.Tap, null, NoteFailureReason.TapMiss, 0.190f),
            ],
        };

        Expect(ReplayCompatibility.CurrentReplayVersion == "3", "replay compatibility version is v3");
        ReplayValidationResult ready = ReplayCompatibility.Validate(replay, "replay-song", 1, 4, chartVersion);
        Expect(ready.CanPlay && ready.UserMessage == "REPLAY READY", "valid v3 replay is accepted");

        string audioPath = TempFile("replay_audio_fingerprint.bin");
        try
        {
            File.WriteAllBytes(audioPath, [0x10, 0x20, 0x30, 0x40]);
            string firstAudioFingerprint = ReplayCompatibility.BuildAudioFingerprint(audioPath);
            replay.AudioFingerprint = firstAudioFingerprint;
            ReplayValidationResult matchingAudio = ReplayCompatibility.ValidateForPlayback(replay, "replay-song", 1, 4, firstAudioFingerprint);
            Expect(matchingAudio.CanPlay, "v3 replay accepts the matching current audio fingerprint");

            File.WriteAllBytes(audioPath, [0x10, 0x20, 0x30, 0x41]);
            string changedAudioFingerprint = ReplayCompatibility.BuildAudioFingerprint(audioPath);
            Expect(!string.Equals(firstAudioFingerprint, changedAudioFingerprint, StringComparison.Ordinal), "audio fingerprint changes when same-length file content changes");
            ReplayValidationResult changedAudio = ReplayCompatibility.ValidateForPlayback(replay, "replay-song", 1, 4, changedAudioFingerprint);
            Expect(!changedAudio.CanPlay && changedAudio.UserMessage.Contains("AUDIO CHANGED", StringComparison.Ordinal), "playback validation blocks a replay when current audio content changed");

            using var canceledFingerprint = new CancellationTokenSource();
            canceledFingerprint.Cancel();
            bool fingerprintCanceled = false;
            try
            {
                ReplayCompatibility.BuildAudioFingerprintAsync(audioPath, canceledFingerprint.Token).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
                fingerprintCanceled = true;
            }
            Expect(fingerprintCanceled, "audio fingerprint I/O honors cancellation before a stale replay request can accumulate");
        }
        finally
        {
            TryDelete(audioPath);
            replay.AudioFingerprint = "sha256:self-test";
        }

        replay.ChartVersion = "changed-chart";
        ReplayValidationResult chartMismatch = ReplayCompatibility.Validate(replay, "replay-song", 1, 4, chartVersion);
        Expect(!chartMismatch.CanPlay && chartMismatch.UserMessage.Contains("CHART", StringComparison.Ordinal), "changed chart blocks replay");
        replay.ChartVersion = chartVersion;

        LaneNote originalChartNote = replay.Chart[1];
        replay.Chart[1] = originalChartNote with { Lane = 3 };
        ReplayValidationResult embeddedChartMismatch = ReplayCompatibility.Validate(replay, "replay-song", 1, 4, chartVersion);
        Expect(!embeddedChartMismatch.CanPlay && embeddedChartMismatch.UserMessage.Contains("CHART", StringComparison.Ordinal), "tampered embedded chart is rejected by its v3 snapshot hash");
        replay.Chart[1] = originalChartNote;

        replay.GameVersion = "different-game-version";
        ReplayValidationResult gameMismatch = ReplayCompatibility.Validate(replay, "replay-song", 1, 4, chartVersion);
        Expect(!gameMismatch.CanPlay && gameMismatch.UserMessage.Contains("GAME VERSION", StringComparison.Ordinal), "changed game version blocks replay");
        replay.GameVersion = ReplayCompatibility.CurrentGameVersion;

        replay.Settings.AudioOffsetMs = 151;
        ReplayValidationResult settingsMismatch = ReplayCompatibility.Validate(replay, "replay-song", 1, 4, chartVersion);
        Expect(!settingsMismatch.CanPlay && settingsMismatch.UserMessage.Contains("SETTINGS", StringComparison.Ordinal), "invalid settings snapshot blocks replay");
        replay.Settings.AudioOffsetMs = 12;

        InputLogEvent validSecondEvent = replay.Events[1];
        replay.Events[1] = validSecondEvent with { Time = 0.5f };
        ReplayValidationResult eventMismatch = ReplayCompatibility.Validate(replay, "replay-song", 1, 4, chartVersion);
        Expect(!eventMismatch.CanPlay && eventMismatch.UserMessage.Contains("INPUT DATA", StringComparison.Ordinal), "out-of-order replay event blocks replay");
        replay.Events[1] = validSecondEvent;

        replay.ReplayVersion = "2";
        ReplayValidationResult outdated = ReplayCompatibility.Validate(replay, "replay-song", 1, 4, chartVersion);
        Expect(!outdated.CanPlay && outdated.UserMessage.Contains("OUTDATED", StringComparison.Ordinal), "v2 replay is blocked by v3 compatibility gate");
        replay.ReplayVersion = ReplayCompatibility.CurrentReplayVersion;

        List<NoteJudgmentEvent> sampledJudgments = replay.Judgments.ToList();
        sampledJudgments[0] = sampledJudgments[0] with { ChartTime = 1.016f, OffsetSeconds = 0.016f };
        sampledJudgments[1] = sampledJudgments[1] with { ChartTime = 2.004f, OffsetSeconds = 0.004f };
        Expect(
            ReplayCompatibility.CompareResult(replay, score, grade, clearType, sampledJudgments) == "REPLAY VERIFIED",
            "matching semantic judgments tolerate ChartTime and Offset sampling differences");

        List<NoteJudgmentEvent> targetMismatch = sampledJudgments.ToList();
        targetMismatch[0] = targetMismatch[0] with { TargetTime = targetMismatch[0].TargetTime + 0.010f };
        Expect(
            ReplayCompatibility.CompareResult(replay, score, grade, clearType, targetMismatch) == "REPLAY MISMATCH",
            "changed judgment TargetTime is a semantic replay mismatch");

        List<NoteJudgmentEvent> phaseMismatch = sampledJudgments.ToList();
        phaseMismatch[0] = phaseMismatch[0] with { Phase = NoteJudgmentPhase.End };
        Expect(
            ReplayCompatibility.CompareResult(replay, score, grade, clearType, phaseMismatch) == "REPLAY MISMATCH",
            "changed judgment phase is a semantic replay mismatch");

        List<NoteJudgmentEvent> outcomeMismatch = sampledJudgments.ToList();
        outcomeMismatch[0] = outcomeMismatch[0] with { Judgment = Judgment.Great };
        Expect(
            ReplayCompatibility.CompareResult(replay, score, grade, clearType, outcomeMismatch) == "REPLAY MISMATCH",
            "changed judgment outcome is a semantic replay mismatch");

        replay.Result.GreatCount++;
        Expect(ReplayCompatibility.CompareResult(replay, score, grade, clearType, sampledJudgments) == "REPLAY MISMATCH", "changed replay judgment distribution is reported as mismatch");
        replay.Result.GreatCount--;

        string replayDirectory = Path.Combine(Path.GetTempPath(), "MuWorld.SelfTests", Guid.NewGuid().ToString("N"), "replays");
        try
        {
            var replayStore = new ReplayStore(replayDirectory);
            replay.PlayedUtc = "2026-07-11T00:00:00.0000000Z";
            int originalScore = replay.Score;
            int originalResultScore = replay.Result.Score;

            replay.Score = 101;
            replay.Result.Score = 101;
            string firstPath = replayStore.Save(replay);
            replay.Score = 202;
            replay.Result.Score = 202;
            string secondPath = replayStore.Save(replay);
            Expect(firstPath.Length > 0 && secondPath.Length > 0 && !string.Equals(firstPath, secondPath, StringComparison.Ordinal), "two replay saves with identical PlayedUtc metadata use unique GUID paths");
            Expect(File.Exists(firstPath) && File.Exists(secondPath), "both uniquely named replay files are persisted");

            DateTime orderingBase = DateTime.UtcNow.AddMinutes(-5);
            File.SetLastWriteTimeUtc(firstPath, orderingBase);
            File.SetLastWriteTimeUtc(secondPath, orderingBase.AddMinutes(1));

            string originalSongId = replay.SongId;
            string originalChartVersion = replay.ChartVersion;
            replay.SongId = "other-replay-song";
            replay.ChartVersion = ReplayCompatibility.BuildChartVersion(replay.SongId, replay.DifficultyIndex, replay.LaneCount, replay.Chart);
            replay.Score = 303;
            replay.Result.Score = 303;
            string foreignPath = replayStore.Save(replay);
            File.SetLastWriteTimeUtc(foreignPath, orderingBase.AddMinutes(2));
            replay.SongId = originalSongId;
            replay.ChartVersion = originalChartVersion;
            replay.Score = originalScore;
            replay.Result.Score = originalResultScore;

            string corruptPath = Path.Combine(replayDirectory, "replay_corrupt.json");
            File.WriteAllText(corruptPath, "{ invalid replay json");
            File.SetLastWriteTimeUtc(corruptPath, orderingBase.AddMinutes(3));

            IReadOnlyList<ReplayRecord> candidates = replayStore.LoadCandidates("replay-song", 1, 4);
            Expect(candidates.Count == 2, "replay candidate loading filters identity and skips corrupt JSON without dropping valid files");
            Expect(candidates[0].Score == 202 && candidates[1].Score == 101, "replay candidates are ordered by newest file first even when PlayedUtc metadata matches");
            Expect(replayStore.LoadLatest("replay-song", 1, 4)?.Score == 202, "LoadLatest returns the newest matching valid replay candidate");
            Expect(replayStore.LoadCandidates("other-replay-song", 1, 4).Count == 1, "replay candidate identity filter retains the separate song candidate only for its own query");

            using var canceledLoad = new CancellationTokenSource();
            canceledLoad.Cancel();
            bool replayLoadCanceled = false;
            try
            {
                replayStore.LoadCandidates("replay-song", 1, 4, canceledLoad.Token);
            }
            catch (OperationCanceledException)
            {
                replayLoadCanceled = true;
            }
            Expect(replayLoadCanceled, "replay candidate deserialization honors a canceled UI request");
        }
        finally
        {
            TryDeleteDirectory(replayDirectory);
        }
    }

    private static void TestReplayFrameOrderingAndLiveInputIsolation()
    {
        UserSettingsStore.DefaultSaveFilePathOverride = TempFile("replay_frame_settings.json");
        AchievementProgressStore.DefaultSaveFilePathOverride = TempFile("replay_frame_achievements.json");
        try
        {
            using var form = new GameForm(selfTestMode: true) { ShowInTaskbar = false };
            FieldInfo engineField = typeof(GameForm).GetField("_engine", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("_engine field not found.");
            FieldInfo replayField = typeof(GameForm).GetField("_isReplayPlayback", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("_isReplayPlayback field not found.");
            FieldInfo activeReplayField = typeof(GameForm).GetField("_activeReplay", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("_activeReplay field not found.");
            FieldInfo replayIndexField = typeof(GameForm).GetField("_replayEventIndex", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("_replayEventIndex field not found.");
            FieldInfo lanePressedField = typeof(GameForm).GetField("_lanePressed", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("_lanePressed field not found.");
            FieldInfo mouseLaneField = typeof(GameForm).GetField("_mouseHeldLane", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("_mouseHeldLane field not found.");
            FieldInfo engineLaneHeldField = typeof(GameEngine).GetField("_laneHeld", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("GameEngine._laneHeld field not found.");
            MethodInfo updateReplay = typeof(GameForm).GetMethod("UpdateReplayPlayback", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("UpdateReplayPlayback not found.");
            MethodInfo onKeyDown = typeof(GameForm).GetMethod("OnKeyDown", BindingFlags.NonPublic | BindingFlags.Instance, null, [typeof(KeyEventArgs)], null)
                ?? throw new InvalidOperationException("OnKeyDown(KeyEventArgs) not found.");
            MethodInfo onKeyUp = typeof(GameForm).GetMethod("OnKeyUp", BindingFlags.NonPublic | BindingFlags.Instance, null, [typeof(KeyEventArgs)], null)
                ?? throw new InvalidOperationException("OnKeyUp(KeyEventArgs) not found.");
            MethodInfo getPlayArea = typeof(GameForm).GetMethod("GetPlayAreaBounds", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("GetPlayAreaBounds not found.");
            MethodInfo mouseDown = typeof(GameForm).GetMethod("OnMenuMouseDown", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("OnMenuMouseDown not found.");
            MethodInfo mouseUp = typeof(GameForm).GetMethod("OnMenuMouseUp", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("OnMenuMouseUp not found.");

            var engine = (GameEngine)engineField.GetValue(form)!;
            var replay = new ReplayRecord
            {
                Settings = new ReplaySettingsSnapshot { LaneCount = 4, NoteSpeedPercent = 100 },
                Events =
                [
                    new InputLogEvent(1.00f, 0, "D", true, "Perfect", "replay"),
                    new InputLogEvent(1.05f, 0, "D", false, string.Empty, "replay"),
                ],
            };
            replayField.SetValue(form, true);
            activeReplayField.SetValue(form, replay);
            replayIndexField.SetValue(form, 0);
            engine.Start(form.ClientSize.Height, [new LaneNote(1f, 0)], 4);

            updateReplay.Invoke(form, [2f, (float?)2f]);
            Expect((int)replayIndexField.GetValue(form)! == 2, "large replay frame consumes both events in timestamp order");
            Expect(engine.Score.PerfectCount == 1 && engine.Score.MissCount == 0, "replay input is judged before large frame can auto-miss note");
            Expect(engine.JudgmentHistory.Count == 1 && engine.JudgmentHistory[0].Phase == NoteJudgmentPhase.Tap, "large replay frame records tap judgment at event time");
            ExpectNear(engine.JudgmentHistory[0].ChartTime, 1f, 0.001f, "replay judgment retains recorded event timestamp");

            engine.Start(form.ClientSize.Height, [new LaneNote(1f, 0, NoteType.Long, 1f, 0)], 4);
            engine.Update(0f, 1f);
            replayIndexField.SetValue(form, 0);
            activeReplayField.SetValue(form, new ReplayRecord
            {
                Settings = new ReplaySettingsSnapshot { LaneCount = 4, NoteSpeedPercent = 100 },
                Events = [new InputLogEvent(5f, 0, "D", true, string.Empty, "replay")],
            });

            var keyDownArgs = new KeyEventArgs(Keys.D);
            onKeyDown.Invoke(form, [keyDownArgs]);
            onKeyUp.Invoke(form, [new KeyEventArgs(Keys.D)]);
            Rectangle playArea = (Rectangle)getPlayArea.Invoke(form, [])!;
            Point lanePoint = new(playArea.Left + Math.Max(1, playArea.Width / 8), playArea.Top + Math.Max(1, playArea.Height / 2));
            mouseDown.Invoke(form, [null, new MouseEventArgs(MouseButtons.Left, 1, lanePoint.X, lanePoint.Y, 0)]);
            mouseUp.Invoke(form, [null, new MouseEventArgs(MouseButtons.Left, 1, lanePoint.X, lanePoint.Y, 0)]);

            bool[] lanePressed = (bool[])lanePressedField.GetValue(form)!;
            bool[] laneHeld = (bool[])engineLaneHeldField.GetValue(engine)!;
            Expect(keyDownArgs.SuppressKeyPress, "physical key is consumed while replay is active");
            Expect(engine.Score.TotalJudgedNotes == 0 && engine.Notes.Single().State == NoteState.Active, "physical key and mouse cannot change replay score");
            Expect(lanePressed.All(value => !value) && laneHeld.All(value => !value), "physical key and mouse cannot change replay lane-held state");
            Expect((int)mouseLaneField.GetValue(form)! == -1, "physical mouse cannot capture a replay lane");
        }
        finally
        {
            UserSettingsStore.DefaultSaveFilePathOverride = null;
            AchievementProgressStore.DefaultSaveFilePathOverride = null;
        }
    }

    private static void TestReplayPauseResumePreservesHoldState()
    {
        UserSettingsStore.DefaultSaveFilePathOverride = TempFile("replay_pause_settings.json");
        AchievementProgressStore.DefaultSaveFilePathOverride = TempFile("replay_pause_achievements.json");
        try
        {
            using var form = new GameForm(selfTestMode: true) { ShowInTaskbar = false };
            FieldInfo engineField = typeof(GameForm).GetField("_engine", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("_engine field not found.");
            FieldInfo replayField = typeof(GameForm).GetField("_isReplayPlayback", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("_isReplayPlayback field not found.");
            FieldInfo activeReplayField = typeof(GameForm).GetField("_activeReplay", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("_activeReplay field not found.");
            FieldInfo replayIndexField = typeof(GameForm).GetField("_replayEventIndex", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("_replayEventIndex field not found.");
            FieldInfo lanePressedField = typeof(GameForm).GetField("_lanePressed", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("_lanePressed field not found.");
            FieldInfo engineLaneHeldField = typeof(GameEngine).GetField("_laneHeld", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("GameEngine._laneHeld field not found.");
            FieldInfo pausedField = typeof(GameForm).GetField("_isGamePaused", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("_isGamePaused field not found.");
            MethodInfo updateReplay = typeof(GameForm).GetMethod("UpdateReplayPlayback", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("UpdateReplayPlayback not found.");
            MethodInfo pauseGame = typeof(GameForm).GetMethod("PauseGame", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("PauseGame not found.");
            MethodInfo resumeGame = typeof(GameForm).GetMethod("ResumeGame", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("ResumeGame not found.");

            var engine = (GameEngine)engineField.GetValue(form)!;
            var replay = new ReplayRecord
            {
                Settings = new ReplaySettingsSnapshot { LaneCount = 4, NoteSpeedPercent = 100 },
                Events =
                [
                    new InputLogEvent(2.00f, 0, "D", true, "Perfect", "replay"),
                    new InputLogEvent(2.90f, 0, "D", false, "Great", "replay"),
                ],
            };
            replayField.SetValue(form, true);
            activeReplayField.SetValue(form, replay);
            replayIndexField.SetValue(form, 0);
            engine.Start(form.ClientSize.Height, [new LaneNote(2f, 0, NoteType.Long, 1f, 0)], 4);

            updateReplay.Invoke(form, [2.1f, (float?)2.1f]);
            Expect((int)replayIndexField.GetValue(form)! == 1, "replay consumes long-note press before pause");
            Expect(engine.Notes.Single().State == NoteState.Holding && engine.Score.TotalJudgedNotes == 1, "replay long note is holding before pause");
            bool[] lanePressedBefore = (bool[])lanePressedField.GetValue(form)!;
            bool[] laneHeldBefore = (bool[])engineLaneHeldField.GetValue(engine)!;
            Expect(lanePressedBefore[0] && laneHeldBefore[0], "replay hold lane is pressed before pause");

            pauseGame.Invoke(form, []);
            resumeGame.Invoke(form, []);
            Expect(!(bool)pausedField.GetValue(form)!, "replay resumes from pause");
            Expect((int)replayIndexField.GetValue(form)! == 1, "replay pause-resume does not rewind consumed events");
            Expect(((bool[])lanePressedField.GetValue(form)!)[0] && ((bool[])engineLaneHeldField.GetValue(engine)!)[0], "replay pause-resume preserves long hold lane state");
            Expect(engine.Notes.Single().State == NoteState.Holding && engine.Score.TotalJudgedNotes == 1, "replay pause-resume preserves consumed long judgment");

            updateReplay.Invoke(form, [0.8f, (float?)2.9f]);
            Expect((int)replayIndexField.GetValue(form)! == 2, "replay consumes long-note release after resume");
            Expect(engine.Score.MissCount == 0, "replay long hold completes without pause-induced miss");
            Expect(engine.JudgmentHistory.Select(item => item.Phase).SequenceEqual([NoteJudgmentPhase.Start, NoteJudgmentPhase.End]), "replay long hold keeps start/end judgment history");
            Expect(!((bool[])lanePressedField.GetValue(form)!)[0] && !((bool[])engineLaneHeldField.GetValue(engine)!)[0], "replay release clears held lane after resume");
        }
        finally
        {
            UserSettingsStore.DefaultSaveFilePathOverride = null;
            AchievementProgressStore.DefaultSaveFilePathOverride = null;
        }
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

    private static void TestEngineClockAndPauseResumeGrace()
    {
        GameEngine clockEngine = StartEngineWith([new LaneNote(20f, 0)]);
        clockEngine.Update(0.016f, 2f);
        ExpectNear(clockEngine.CurrentChartTime, 2f, 0.001f, "clock accepts a finite playback sample");

        clockEngine.Update(0.016f, 1.5f);
        ExpectNear(clockEngine.CurrentChartTime, 2.016f, 0.001f, "stale playback sample cannot freeze or rewind judgment clock");

        clockEngine.Update(0.025f, float.NaN);
        ExpectNear(clockEngine.CurrentChartTime, 2.041f, 0.001f, "NaN playback sample falls back to delta clock");
        clockEngine.Update(0.025f, float.PositiveInfinity);
        ExpectNear(clockEngine.CurrentChartTime, 2.066f, 0.001f, "positive infinity playback sample falls back to delta clock");
        clockEngine.Update(0.025f, float.NegativeInfinity);
        ExpectNear(clockEngine.CurrentChartTime, 2.091f, 0.001f, "negative infinity playback sample falls back to delta clock");
        clockEngine.Update(float.NaN, null);
        clockEngine.Update(-1f, null);
        ExpectNear(clockEngine.CurrentChartTime, 2.091f, 0.001f, "invalid or negative frame delta cannot rewind or poison chart clock");
        Expect(float.IsFinite(clockEngine.CurrentChartTime) && float.IsFinite(clockEngine.VisualChartTime), "invalid playback samples never poison engine clocks");

        string settingsPath = TempFile("pause_resume_settings.json");
        string achievementPath = TempFile("pause_resume_achievements.json");
        UserSettingsStore.DefaultSaveFilePathOverride = settingsPath;
        AchievementProgressStore.DefaultSaveFilePathOverride = achievementPath;
        try
        {
            using var form = new GameForm(selfTestMode: true) { ShowInTaskbar = false };
            FieldInfo engineField = typeof(GameForm).GetField("_engine", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("_engine field not found.");
            FieldInfo pausedField = typeof(GameForm).GetField("_isGamePaused", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("_isGamePaused field not found.");
            FieldInfo pauseHeldAwaitingKeyUpField = typeof(GameForm).GetField("_pauseHeldLaneAwaitingKeyUp", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("_pauseHeldLaneAwaitingKeyUp field not found.");
            FieldInfo lanePressedField = typeof(GameForm).GetField("_lanePressed", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("_lanePressed field not found.");
            MethodInfo pauseGame = typeof(GameForm).GetMethod("PauseGame", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("PauseGame not found.");
            MethodInfo resumeGame = typeof(GameForm).GetMethod("ResumeGame", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("ResumeGame not found.");
            MethodInfo onKeyDown = typeof(GameForm).GetMethod("OnKeyDown", BindingFlags.NonPublic | BindingFlags.Instance, null, [typeof(KeyEventArgs)], null)
                ?? throw new InvalidOperationException("OnKeyDown(KeyEventArgs) not found.");
            MethodInfo onKeyUp = typeof(GameForm).GetMethod("OnKeyUp", BindingFlags.NonPublic | BindingFlags.Instance, null, [typeof(KeyEventArgs)], null)
                ?? throw new InvalidOperationException("OnKeyUp(KeyEventArgs) not found.");

            var engine = (GameEngine)engineField.GetValue(form)!;
            Keys laneKey = GetLaneKeyBindings(form)[0][0];
            engine.Start(form.ClientSize.Height, [new LaneNote(2f, 0, NoteType.Long, 1f, 0)], 4);
            engine.Update(0.016f, 0.55f);
            engine.Update(0.016f, 2f);
            onKeyDown.Invoke(form, [new KeyEventArgs(laneKey)]);
            Expect(engine.Score.PerfectCount == 1 && engine.Notes.Any(note => note.State == NoteState.Holding), "form lane KeyDown starts Long note before pause");

            pauseGame.Invoke(form, []);
            Expect((bool)pausedField.GetValue(form)!, "pause flow marks game paused");
            Expect(((bool[])pauseHeldAwaitingKeyUpField.GetValue(form)!)[0], "pause snapshots the physically held lane until its first KeyUp");
            resumeGame.Invoke(form, []);
            Expect(!(bool)pausedField.GetValue(form)!, "resume flow clears paused state");
            Expect(((bool[])pauseHeldAwaitingKeyUpField.GetValue(form)!)[0], "resume keeps held lane blocked until stale KeyUp arrives");

            int judgmentsBeforeStaleKeyUp = engine.JudgmentHistory.Count;
            onKeyUp.Invoke(form, [new KeyEventArgs(laneKey)]);
            Expect(!((bool[])pauseHeldAwaitingKeyUpField.GetValue(form)!)[0], "first post-resume KeyUp clears the paused-held lane gate");
            Expect(engine.Score.MissCount == 0 && engine.JudgmentHistory.Count == judgmentsBeforeStaleKeyUp, "first post-resume KeyUp is consumed without release judgment or miss");

            engine.Update(0.016f, 2.20f);
            Expect(engine.Score.MissCount == 0 && engine.Notes.Any(note => note.State == NoteState.Holding), "resume grace protects a released hold while keys are reacquired");
            onKeyDown.Invoke(form, [new KeyEventArgs(laneKey)]);
            Expect(((bool[])lanePressedField.GetValue(form)!)[0], "fresh KeyDown reacquires the paused Long lane");
            engine.Update(0.016f, 2.40f);
            Expect(engine.Score.MissCount == 0, "reacquired lane remains valid after grace expires");
            engine.Update(0.016f, 2.90f);
            onKeyUp.Invoke(form, [new KeyEventArgs(laneKey)]);
            Expect(engine.JudgmentHistory.Any(item => item.Phase == NoteJudgmentPhase.End && !item.IsMiss) && engine.Score.MissCount == 0, "pause-resumed hold can finish normally through form KeyUp");
        }
        finally
        {
            UserSettingsStore.DefaultSaveFilePathOverride = null;
            AchievementProgressStore.DefaultSaveFilePathOverride = null;
        }

        GameEngine expiredGrace = StartHoldingNote(NoteType.Long);
        expiredGrace.SetLaneHeld(0, false);
        expiredGrace.GrantHoldResumeGrace(0.10f);
        expiredGrace.Update(0.016f, 2.05f);
        Expect(expiredGrace.Score.MissCount == 0, "hold remains protected inside explicit grace window");
        expiredGrace.Update(0.016f, 2.11f);
        ExpectMissEvent(expiredGrace, NoteType.Long, NoteJudgmentPhase.Hold, NoteFailureReason.LongHoldBreak, "expired resume grace");
    }

    private static void TestHoldGraceScoringAndLateReacquire()
    {
        GameEngine engine = StartEngineWith([new LaneNote(2f, 0, NoteType.Long, 1f, 0)]);
        engine.Update(0.016f, 0.55f);
        engine.SetLaneHeld(0, true);
        engine.Update(0.016f, 2.14f);
        GameEngine.HitResult? start = engine.TryHit(0);
        Expect(start.HasValue && start.Value.Judgment == Judgment.Bad, "late hold-grace fixture starts with a finite Bad judgment");
        Note heldNote = engine.Notes.Single(note => note.State == NoteState.Holding);
        int scoreBeforeGrace = engine.Score.Score;

        engine.SetLaneHeld(0, false);
        engine.GrantHoldResumeGrace(1f);
        engine.Update(0.016f, 2.80f);
        Expect(heldNote.State == NoteState.Holding && engine.Score.MissCount == 0, "released hold remains pending during resume grace");
        Expect(heldNote.HoldTicksAwarded == 0, "resume grace does not award unheld hold ticks");
        Expect(engine.Score.Score == scoreBeforeGrace, "resume grace does not add hidden hold-tick score");

        engine.SetLaneHeld(0, true);
        engine.Update(0.016f, 3.16f);
        int reacquiredBaseScore = ScoreManager.CalculateNormalizedScore(
            engine.Score.PerfectCount,
            engine.Score.GreatCount,
            engine.Score.BetterCount,
            engine.Score.GoodCount,
            engine.Score.BadCount,
            engine.Score.MissCount);
        Expect(engine.Score.Score == reacquiredBaseScore, "timely grace reacquire advances the tick cursor without backfilling hidden hold score");
        Expect(engine.JudgmentHistory.Any(item => item.Phase == NoteJudgmentPhase.End && item.Judgment == Judgment.Perfect), "timely grace reacquire survives an end-crossing frame and auto-completes perfectly");

        GameEngine frameJump = StartEngineWith([new LaneNote(2f, 0, NoteType.Long, 1f, 0)]);
        frameJump.Update(0.016f, 0.55f);
        frameJump.SetLaneHeld(0, true);
        frameJump.Update(0.016f, 2f);
        GameEngine.HitResult? jumpStart = frameJump.TryHit(0);
        Expect(jumpStart.HasValue && jumpStart.Value.Judgment == Judgment.Perfect, "large-frame hold fixture starts perfectly");
        Note jumpNote = frameJump.Notes.Single(note => note.State == NoteState.Holding);

        frameJump.Update(0.50f, 3.45f);
        NoteJudgmentEvent jumpEnd = frameJump.JudgmentHistory.Single(item => item.Phase == NoteJudgmentPhase.End);
        Expect(jumpNote.HoldTicksAwarded == 3, "continuous held Long catches up all pre-end ticks across a large frame jump");
        Expect(frameJump.Score.MissCount == 0 && frameJump.Score.PerfectCount == 2, "held Long auto-end remains PERFECT after a large frame and audio-position jump");
        Expect(jumpEnd.Judgment == Judgment.Perfect && jumpEnd.FailureReason == NoteFailureReason.None, "large-jump auto-end records a successful PERFECT outcome");
        ExpectNear(jumpEnd.TargetTime, 3f, 0.001f, "large-jump auto-end keeps the chart target time");
        ExpectNear(jumpEnd.OffsetSeconds, 0f, 0.001f, "large-jump auto-end does not turn timer lateness into judgment offset");
    }

    private static void TestAudioClockDiagnostics()
    {
        var diagnostics = new AudioClockDiagnostics();
        diagnostics.Start(".WAV", 0f, 0d);
        diagnostics.Record(0.010f, 0.010d);
        diagnostics.Record(0.005f, 0.020d);
        diagnostics.Record(0.005f, 0.050d);
        diagnostics.Record(0.200f, 0.060d);
        diagnostics.Record(float.NaN, 0.070d);
        diagnostics.RecordQueryFailure();
        diagnostics.ResetBaseline(0.200f, 0.100d);
        diagnostics.Record(0.220f, 0.120d);

        AudioClockSnapshot snapshot = diagnostics.Snapshot();
        Expect(snapshot.SourceFormat == "wav", "audio clock normalizes source format");
        Expect(snapshot.Samples == 5, "audio clock records meaningful samples across segments");
        Expect(snapshot.QueryFailures == 2, "audio clock counts invalid values and query failures");
        Expect(snapshot.BackwardJumps == 1, "audio clock detects backward jumps");
        Expect(snapshot.ForwardJumps == 1, "audio clock detects forward jumps");
        Expect(snapshot.Stalls == 1, "audio clock detects stalled position");
        Expect(snapshot.Segments == 2, "pause-resume baseline creates a new segment");
        Expect(snapshot.MaximumAbsoluteJitterMs >= snapshot.MeanAbsoluteJitterMs, "audio clock jitter summary is internally consistent");
        Expect(snapshot.ToLogMessage().Contains("format=wav", StringComparison.Ordinal), "audio clock log includes source format");
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

    private static void TestSongDifficultyPreservesSelection()
    {
        UserSettingsStore.DefaultSaveFilePathOverride = TempFile("song_difficulty_selection_settings.json");
        AchievementProgressStore.DefaultSaveFilePathOverride = TempFile("song_difficulty_selection_achievements.json");
        SongDataStore.DefaultSaveFilePathOverride = TempFile("song_difficulty_selection_data.json");
        try
        {
            using var form = new GameForm(selfTestMode: true) { ShowInTaskbar = false };
            FieldInfo selectedField = typeof(GameForm).GetField("_songSelectSelectedIndex", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("_songSelectSelectedIndex field not found.");
            FieldInfo pageField = typeof(GameForm).GetField("_songSelectPageIndex", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("_songSelectPageIndex field not found.");
            FieldInfo difficultyField = typeof(GameForm).GetField("_songSelectDifficultyIndex", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("_songSelectDifficultyIndex field not found.");
            FieldInfo sortField = typeof(GameForm).GetField("_songSortModeIndex", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("_songSortModeIndex field not found.");
            FieldInfo rowsPerPageField = typeof(GameForm).GetField("SongRowsPerPage", BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new InvalidOperationException("SongRowsPerPage field not found.");
            MethodInfo getFilteredSongs = typeof(GameForm).GetMethod("GetFilteredSongs", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("GetFilteredSongs not found.");
            MethodInfo getSelectedSong = typeof(GameForm).GetMethod("GetSelectedSong", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("GetSelectedSong not found.");
            MethodInfo setDifficulty = typeof(GameForm).GetMethod("SetSongDifficulty", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("SetSongDifficulty not found.");
            MethodInfo changeDifficulty = typeof(GameForm).GetMethod("ChangeSongDifficulty", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("ChangeSongDifficulty not found.");

            sortField.SetValue(form, 6); // Difficulty sort can reorder when the difficulty changes.
            difficultyField.SetValue(form, 0);
            Array songs = (Array)getFilteredSongs.Invoke(form, [])!;
            Expect(songs.Length >= 2, "difficulty selection fixture has multiple songs");

            int initialIndex = songs.Length - 1;
            int rowsPerPage = (int)rowsPerPageField.GetRawConstantValue()!;
            selectedField.SetValue(form, initialIndex);
            pageField.SetValue(form, initialIndex / rowsPerPage);
            object selectedBefore = getSelectedSong.Invoke(form, [])!;
            string selectedSongId = (string)selectedBefore.GetType().GetProperty("SongId")!.GetValue(selectedBefore)!;

            setDifficulty.Invoke(form, [2]);
            object selectedAfterMousePath = getSelectedSong.Invoke(form, [])!;
            string afterMouseSongId = (string)selectedAfterMousePath.GetType().GetProperty("SongId")!.GetValue(selectedAfterMousePath)!;
            Expect((int)difficultyField.GetValue(form)! == 2, "direct difficulty selection changes difficulty");
            Expect(afterMouseSongId == selectedSongId, "direct difficulty selection preserves selected song ID");

            changeDifficulty.Invoke(form, [-1]);
            object selectedAfterKeyboardPath = getSelectedSong.Invoke(form, [])!;
            string afterKeyboardSongId = (string)selectedAfterKeyboardPath.GetType().GetProperty("SongId")!.GetValue(selectedAfterKeyboardPath)!;
            Expect((int)difficultyField.GetValue(form)! == 1, "keyboard difficulty change changes difficulty");
            Expect(afterKeyboardSongId == selectedSongId, "keyboard difficulty change preserves selected song ID");

            int selectedIndex = (int)selectedField.GetValue(form)!;
            Expect((int)pageField.GetValue(form)! == selectedIndex / rowsPerPage, "difficulty change follows selected song to its page");
        }
        finally
        {
            UserSettingsStore.DefaultSaveFilePathOverride = null;
            AchievementProgressStore.DefaultSaveFilePathOverride = null;
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

    private static void TestJudgmentEventFailureTaxonomy()
    {
        (NoteType type, NoteJudgmentPhase phase, NoteFailureReason reason, int endLane)[] startMissCases =
        [
            (NoteType.Tap, NoteJudgmentPhase.Tap, NoteFailureReason.TapMiss, 0),
            (NoteType.Long, NoteJudgmentPhase.Start, NoteFailureReason.LongStartMiss, 0),
            (NoteType.Slide, NoteJudgmentPhase.Start, NoteFailureReason.SlideStartMiss, 2),
        ];
        foreach ((NoteType type, NoteJudgmentPhase phase, NoteFailureReason reason, int endLane) in startMissCases)
        {
            float duration = type == NoteType.Tap ? 0f : 1f;
            GameEngine engine = StartEngineWith([new LaneNote(2f, 0, type, duration, endLane)]);
            engine.Update(0.016f, 0.55f);
            engine.Update(0.25f, 2.19f);
            ExpectMissEvent(engine, type, phase, reason, $"{type} start miss");
        }

        GameEngine longHoldBreak = StartHoldingNote(NoteType.Long);
        longHoldBreak.SetLaneHeld(0, false);
        longHoldBreak.Update(0.016f, 2.40f);
        ExpectMissEvent(longHoldBreak, NoteType.Long, NoteJudgmentPhase.Hold, NoteFailureReason.LongHoldBreak, "long hold break");

        GameEngine slidePathBreak = StartHoldingNote(NoteType.Slide, endLane: 2);
        slidePathBreak.Update(0.016f, 2.60f);
        ExpectMissEvent(slidePathBreak, NoteType.Slide, NoteJudgmentPhase.Hold, NoteFailureReason.SlidePathBreak, "slide path break");

        GameEngine longEndMiss = StartHoldingNote(NoteType.Long);
        longEndMiss.SetLaneHeld(0, false);
        longEndMiss.Update(0.016f, 3.00f);
        ExpectMissEvent(longEndMiss, NoteType.Long, NoteJudgmentPhase.End, NoteFailureReason.LongEndMiss, "long end miss");

        GameEngine slideEndMiss = StartHoldingNote(NoteType.Slide, endLane: 2);
        slideEndMiss.SetLaneHeld(0, false);
        slideEndMiss.SetLaneHeld(2, true);
        slideEndMiss.Update(0.016f, 2.60f);
        slideEndMiss.SetLaneHeld(2, false);
        slideEndMiss.Update(0.016f, 3.00f);
        ExpectMissEvent(slideEndMiss, NoteType.Slide, NoteJudgmentPhase.End, NoteFailureReason.SlideEndMiss, "slide end miss");

        GameEngine tapHit = StartEngineWith([new LaneNote(2f, 0)]);
        tapHit.Update(0.016f, 0.55f);
        tapHit.Update(0.016f, 2f);
        Expect(tapHit.TryHit(0).HasValue, "tap success exists");
        NoteJudgmentEvent tapEvent = tapHit.JudgmentHistory.Single();
        Expect(tapEvent.NoteType == NoteType.Tap && tapEvent.Phase == NoteJudgmentPhase.Tap && !tapEvent.IsMiss && tapEvent.Judgment.HasValue, "tap success records tap phase and judgment");

        GameEngine longSuccess = StartHoldingNote(NoteType.Long);
        longSuccess.Update(0.016f, 2.90f);
        longSuccess.SetLaneHeld(0, false);
        Expect(longSuccess.TryRelease(0).HasValue, "long success release exists");
        Expect(longSuccess.JudgmentHistory.Select(item => item.Phase).SequenceEqual([NoteJudgmentPhase.Start, NoteJudgmentPhase.End]), "long success records distinct start and end phases");
        Expect(longSuccess.JudgmentHistory.All(item => !item.IsMiss && item.Judgment.HasValue) && longSuccess.Score.MissCount == 0, "long success contains no failure event");

        GameEngine slideSuccess = StartHoldingNote(NoteType.Slide, endLane: 2);
        slideSuccess.SetLaneHeld(0, false);
        slideSuccess.SetLaneHeld(2, true);
        slideSuccess.Update(0.016f, 2.60f);
        slideSuccess.Update(0.016f, 2.90f);
        slideSuccess.SetLaneHeld(2, false);
        Expect(slideSuccess.TryRelease(2).HasValue, "slide success release exists");
        Expect(slideSuccess.JudgmentHistory.Select(item => item.Phase).SequenceEqual([NoteJudgmentPhase.Start, NoteJudgmentPhase.End]), "slide success records distinct start and end phases");
        Expect(slideSuccess.JudgmentHistory.All(item => !item.IsMiss && item.Judgment.HasValue) && slideSuccess.Score.MissCount == 0, "slide success contains no failure event");
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
        ExpectNear(smoothEngine.CurrentChartTime, 0.032f, 0.001f, "judgment clock advances between coarse playback samples");
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

    private static GameEngine StartHoldingNote(NoteType type, int endLane = 0)
    {
        var engine = StartEngineWith([new LaneNote(2f, 0, type, 1f, endLane)]);
        engine.Update(0.016f, 0.55f);
        engine.SetLaneHeld(0, true);
        engine.Update(0.016f, 2f);
        Expect(engine.TryHit(0).HasValue, $"{type} test note starts holding");
        return engine;
    }

    private static void ExpectMissEvent(
        GameEngine engine,
        NoteType noteType,
        NoteJudgmentPhase phase,
        NoteFailureReason reason,
        string label)
    {
        NoteJudgmentEvent[] misses = engine.JudgmentHistory.Where(item => item.IsMiss).ToArray();
        Expect(misses.Length == engine.Score.MissCount, $"{label} event count matches Score.MissCount");
        Expect(misses.Length == 1, $"{label} records exactly one failure event");
        NoteJudgmentEvent miss = misses[0];
        Expect(miss.NoteType == noteType, $"{label} note type");
        Expect(miss.Phase == phase, $"{label} phase");
        Expect(miss.FailureReason == reason, $"{label} failure reason");
        Expect(!miss.Judgment.HasValue, $"{label} miss has no successful judgment");
        Expect(float.IsFinite(miss.ChartTime) && float.IsFinite(miss.TargetTime) && float.IsFinite(miss.OffsetSeconds), $"{label} timing payload remains finite");
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
        MethodInfo getPlayArea = typeof(GameForm).GetMethod("GetPlayAreaBounds", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("GetPlayAreaBounds not found.");
        MethodInfo getArtwork = typeof(GameForm).GetMethod("GetSongArtworkBounds", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("GetSongArtworkBounds not found.");
        MethodInfo getProgress = typeof(GameForm).GetMethod("GetProgressRailBounds", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("GetProgressRailBounds not found.");
        MethodInfo getDifficulty = typeof(GameForm).GetMethod("GetDifficultyBadgeBounds", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("GetDifficultyBadgeBounds not found.");
        MethodInfo getScore = typeof(GameForm).GetMethod("GetScorePanelBounds", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("GetScorePanelBounds not found.");
        MethodInfo getLaneKey = typeof(GameForm).GetMethod("GetFloatingLaneKeyBounds", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("GetFloatingLaneKeyBounds not found.");
        MethodInfo getPauseKey = typeof(GameForm).GetMethod("GetPauseKeyBounds", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("GetPauseKeyBounds not found.");

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

        RectangleF viewport = new(0f, 0f, size.Width, size.Height);
        Rectangle playArea = (Rectangle)getPlayArea.Invoke(form, [])!;
        RectangleF artwork = (RectangleF)getArtwork.Invoke(form, [])!;
        RectangleF progress = (RectangleF)getProgress.Invoke(form, [])!;
        RectangleF difficulty = (RectangleF)getDifficulty.Invoke(form, [])!;
        RectangleF scorePanel = (RectangleF)getScore.Invoke(form, [])!;
        RectangleF pauseKey = (RectangleF)getPauseKey.Invoke(form, [])!;
        Expect(viewport.Contains(speedPanel), $"speed panel stays inside gameplay viewport at {size.Width}x{size.Height}");
        Expect(viewport.Contains(artwork), $"song artwork stays inside gameplay viewport at {size.Width}x{size.Height}");
        Expect(viewport.Contains(progress), $"progress rail stays inside gameplay viewport at {size.Width}x{size.Height}");
        Expect(viewport.Contains(difficulty), $"difficulty badge stays inside gameplay viewport at {size.Width}x{size.Height}");
        Expect(viewport.Contains(scorePanel), $"score panel stays inside gameplay viewport at {size.Width}x{size.Height}");
        Expect(viewport.Contains(pauseKey), $"pause key stays inside gameplay viewport at {size.Width}x{size.Height}");
        Expect(progress.Right < scorePanel.Left, $"progress rail does not overlap score panel at {size.Width}x{size.Height}");

        int hitY = (int)MathF.Round(GameEngine.GetHitZoneY(size.Height));
        for (int lane = 0; lane < 4; lane++)
        {
            RectangleF laneKey = (RectangleF)getLaneKey.Invoke(form, [playArea, hitY, lane])!;
            Expect(viewport.Contains(laneKey), $"lane key {lane + 1} stays inside gameplay viewport at {size.Width}x{size.Height}");
            Expect(laneKey.Bottom <= size.Height - 8f, $"lane key {lane + 1} keeps a bottom safe margin at {size.Width}x{size.Height}");
        }

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
            int hitY = (int)MathF.Round(GameEngine.GetHitZoneY(form.ClientSize.Height));
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

    private static void TestAnalyzeAccessibilityAndReplayStatusContrast()
    {
        UserSettingsStore.DefaultSaveFilePathOverride = TempFile("analyze_accessibility_settings.json");
        AchievementProgressStore.DefaultSaveFilePathOverride = TempFile("analyze_accessibility_achievements.json");
        SongDataStore.DefaultSaveFilePathOverride = TempFile("analyze_accessibility_songs.json");
        try
        {
            using var form = new GameForm(selfTestMode: true) { ClientSize = new Size(1366, 768), ShowInTaskbar = false };
            FieldInfo screenField = typeof(GameForm).GetField("_screen", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("_screen field not found.");
            FieldInfo replayStatusField = typeof(GameForm).GetField("_analyzeReplayStatus", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("_analyzeReplayStatus field not found.");
            FieldInfo highContrastField = typeof(GameForm).GetField("_highContrastEnabled", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("_highContrastEnabled field not found.");
            MethodInfo handleAccessibilityKey = typeof(GameForm).GetMethod("HandleAccessibilityKeyDown", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("HandleAccessibilityKeyDown not found.");
            MethodInfo getLearningSummaryColor = typeof(GameForm).GetMethod("GetAnalyzeLearningSummaryColor", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("GetAnalyzeLearningSummaryColor not found.");

            screenField.SetValue(form, Enum.Parse(screenField.FieldType, "Analyze"));
            replayStatusField.SetValue(form, "REPLAY VERIFIED");
            AccessibleObject accessibility = form.AccessibilityObject;
            AccessibleObject summary = accessibility.GetChild(0)
                ?? throw new InvalidOperationException("Analyze result summary accessibility node not found.");
            Expect(summary.Role == AccessibleRole.StaticText, "analyze result summary remains static text");
            Expect((summary.State & AccessibleStates.Focusable) == 0, "static result summary is not exposed as focusable");
            Expect(string.IsNullOrEmpty(summary.DefaultAction), "static result summary has no Press default action");
            Expect(summary.Description?.Contains("REPLAY VERIFIED", StringComparison.Ordinal) == true, "accessible result summary includes replay verification status");

            summary.Select(AccessibleSelection.TakeFocus);
            summary.DoDefaultAction();
            Expect(screenField.GetValue(form)!.ToString() == "Analyze", "static result summary cannot invoke an Analyze action");
            Expect(!string.Equals(accessibility.GetFocused()?.Name, summary.Name, StringComparison.Ordinal), "static result summary cannot take accessibility focus");

            var forward = new KeyEventArgs(Keys.Tab);
            Expect((bool)handleAccessibilityKey.Invoke(form, [forward])!, "Tab focus navigation is handled on Analyze");
            Expect(forward.SuppressKeyPress && accessibility.GetFocused()?.Name == "Retry", "Tab skips result summary and focuses first interactive Analyze action");
            var down = new KeyEventArgs(Keys.Down);
            Expect((bool)handleAccessibilityKey.Invoke(form, [down])!, "Down-arrow focus navigation is handled on Analyze");
            Expect(down.SuppressKeyPress && accessibility.GetFocused()?.Name == "Song Select", "Down arrow advances only to the next interactive Analyze action");
            var up = new KeyEventArgs(Keys.Up);
            Expect((bool)handleAccessibilityKey.Invoke(form, [up])!, "Up-arrow focus navigation is handled on Analyze");
            Expect(up.SuppressKeyPress && accessibility.GetFocused()?.Name == "Retry", "Up arrow reverses only across interactive Analyze actions");

            screenField.SetValue(form, Enum.Parse(screenField.FieldType, "ChartEditor"));
            int chartEditorChildren = accessibility.GetChildCount();
            AccessibleObject? chartGrid = null;
            for (int i = 0; i < chartEditorChildren; i++)
            {
                AccessibleObject? child = accessibility.GetChild(i);
                if (child?.Role == AccessibleRole.Graphic)
                {
                    chartGrid = child;
                    break;
                }
            }

            Expect(chartGrid is not null, "chart editor exposes its graphic grid node");
            Expect((chartGrid!.State & AccessibleStates.Focusable) == 0, "graphic chart grid is not exposed as focusable");
            Expect(string.IsNullOrEmpty(chartGrid.DefaultAction), "graphic chart grid has no Press default action");
            var backward = new KeyEventArgs(Keys.Shift | Keys.Tab);
            Expect((bool)handleAccessibilityKey.Invoke(form, [backward])!, "reverse focus navigation is handled on chart editor");
            Expect(backward.SuppressKeyPress && accessibility.GetFocused()?.Name == "Preview", "reverse Tab skips trailing graphic node and focuses last interactive action");

            screenField.SetValue(form, Enum.Parse(screenField.FieldType, "Analyze"));
            highContrastField.SetValue(form, true);
            replayStatusField.SetValue(form, "REPLAY VERIFIED");
            Color verified = (Color)getLearningSummaryColor.Invoke(form, [])!;
            Expect(verified.G > verified.R && verified.G > verified.B, "verified replay status uses a green color family");
            foreach (string failedStatus in new[] { "REPLAY MISMATCH", "REPLAY SAVE FAILED", "REPLAY NOT SAVED - invalid recording" })
            {
                replayStatusField.SetValue(form, failedStatus);
                Color failed = (Color)getLearningSummaryColor.Invoke(form, [])!;
                Expect(failed.R > failed.G && failed.R > failed.B, $"{failedStatus} uses a red color family");
            }

            DrawToBitmapAndAssert(form, form.ClientSize, "Analyze high contrast replay status");
        }
        finally
        {
            UserSettingsStore.DefaultSaveFilePathOverride = null;
            AchievementProgressStore.DefaultSaveFilePathOverride = null;
            SongDataStore.DefaultSaveFilePathOverride = null;
        }
    }

    private static void TestAnalyzeEnterReturnsToSongSelect()
    {
        UserSettingsStore.DefaultSaveFilePathOverride = TempFile("analyze_enter_settings.json");
        AchievementProgressStore.DefaultSaveFilePathOverride = TempFile("analyze_enter_achievements.json");
        try
        {
            using var form = new GameForm(selfTestMode: true) { ShowInTaskbar = false };
            FieldInfo screenField = typeof(GameForm).GetField("_screen", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("_screen field not found.");
            FieldInfo hoverActionField = typeof(GameForm).GetField("_hoverAnalyzeAction", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("_hoverAnalyzeAction field not found.");
            FieldInfo engineField = typeof(GameForm).GetField("_engine", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("_engine field not found.");
            FieldInfo countdownField = typeof(GameForm).GetField("_isCountdownActive", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("_isCountdownActive field not found.");
            MethodInfo onKeyDown = typeof(GameForm).GetMethod(
                "OnKeyDown",
                BindingFlags.NonPublic | BindingFlags.Instance,
                binder: null,
                types: [typeof(KeyEventArgs)],
                modifiers: null)
                ?? throw new InvalidOperationException("OnKeyDown(KeyEventArgs) not found.");

            screenField.SetValue(form, Enum.Parse(screenField.FieldType, "Analyze"));
            hoverActionField.SetValue(form, -1);
            var engine = (GameEngine)engineField.GetValue(form)!;
            Expect(!engine.IsRunning && !(bool)countdownField.GetValue(form)!, "analyze Enter test begins with stopped engine");

            var args = new KeyEventArgs(Keys.Enter);
            onKeyDown.Invoke(form, [args]);

            Expect(args.SuppressKeyPress, "analyze Enter is consumed by analyze navigation");
            Expect(screenField.GetValue(form)!.ToString() == "SongSelect", "unfocused analyze Enter returns to song select");
            Expect(!engine.IsRunning, "unfocused analyze Enter does not start game engine");
            Expect(!(bool)countdownField.GetValue(form)!, "unfocused analyze Enter does not start game countdown");
        }
        finally
        {
            UserSettingsStore.DefaultSaveFilePathOverride = null;
            AchievementProgressStore.DefaultSaveFilePathOverride = null;
        }
    }

    private static void TestAnalyzeSongIdentityAcrossSortChanges()
    {
        UserSettingsStore.DefaultSaveFilePathOverride = TempFile("analyze_identity_settings.json");
        AchievementProgressStore.DefaultSaveFilePathOverride = TempFile("analyze_identity_achievements.json");
        SongDataStore.DefaultSaveFilePathOverride = TempFile("analyze_identity_songs.json");
        try
        {
            using var form = new GameForm(selfTestMode: true) { ShowInTaskbar = false };
            FieldInfo sortField = typeof(GameForm).GetField("_songSortModeIndex", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("_songSortModeIndex field not found.");
            FieldInfo selectedField = typeof(GameForm).GetField("_songSelectSelectedIndex", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("_songSelectSelectedIndex field not found.");
            FieldInfo analyzeSongIdField = typeof(GameForm).GetField("_analyzeSongId", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("_analyzeSongId field not found.");
            FieldInfo screenField = typeof(GameForm).GetField("_screen", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("_screen field not found.");
            FieldInfo countdownField = typeof(GameForm).GetField("_isCountdownActive", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("_isCountdownActive field not found.");
            MethodInfo getFilteredSongs = typeof(GameForm).GetMethod("GetFilteredSongs", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("GetFilteredSongs not found.");
            MethodInfo activateAnalyzeAction = typeof(GameForm).GetMethod("ActivateAnalyzeAction", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("ActivateAnalyzeAction not found.");
            MethodInfo cancelCountdown = typeof(GameForm).GetMethod("CancelCountdown", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("CancelCountdown not found.");

            string[] GetSongIds()
            {
                Array songs = (Array)getFilteredSongs.Invoke(form, [])!;
                var ids = new string[songs.Length];
                for (int i = 0; i < songs.Length; i++)
                {
                    object song = songs.GetValue(i)!;
                    PropertyInfo songId = song.GetType().GetProperty("SongId")
                        ?? throw new InvalidOperationException("SongEntry.SongId not found.");
                    ids[i] = (string)songId.GetValue(song)!;
                }
                return ids;
            }

            sortField.SetValue(form, 0);
            string[] titleOrder = GetSongIds();
            Expect(titleOrder.Length >= 3, "analyze identity test has enough real songs");

            int changedSortMode = -1;
            int staleIndex = -1;
            int playedIndex = -1;
            string playedSongId = string.Empty;
            string[] sortedOrder = [];
            foreach (int sortMode in new[] { 1, 2, 3, 4, 5, 7 })
            {
                sortField.SetValue(form, sortMode);
                string[] candidateOrder = GetSongIds();
                for (int oldIndex = 0; oldIndex < titleOrder.Length; oldIndex++)
                {
                    int currentIndex = Array.IndexOf(candidateOrder, titleOrder[oldIndex]);
                    if (currentIndex >= 0 && currentIndex != oldIndex && currentIndex < candidateOrder.Length - 1)
                    {
                        changedSortMode = sortMode;
                        staleIndex = oldIndex;
                        playedIndex = currentIndex;
                        playedSongId = titleOrder[oldIndex];
                        sortedOrder = candidateOrder;
                        break;
                    }
                }

                if (changedSortMode >= 0)
                    break;
            }

            Expect(changedSortMode >= 0 && staleIndex != playedIndex, "fixture finds a song whose index changes after sorting");
            sortField.SetValue(form, changedSortMode);
            analyzeSongIdField.SetValue(form, playedSongId);
            selectedField.SetValue(form, staleIndex);
            screenField.SetValue(form, Enum.Parse(screenField.FieldType, "Analyze"));
            activateAnalyzeAction.Invoke(form, [0]);

            Expect((int)selectedField.GetValue(form)! == playedIndex, "Analyze Retry restores current index by analyzed song ID");
            Expect(GetSongIds()[(int)selectedField.GetValue(form)!] == playedSongId, "Analyze Retry selects the same analyzed song after sort change");
            Expect((bool)countdownField.GetValue(form)!, "Analyze Retry starts countdown only after restoring analyzed song");

            cancelCountdown.Invoke(form, []);
            sortField.SetValue(form, changedSortMode);
            analyzeSongIdField.SetValue(form, playedSongId);
            selectedField.SetValue(form, staleIndex);
            screenField.SetValue(form, Enum.Parse(screenField.FieldType, "Analyze"));
            activateAnalyzeAction.Invoke(form, [2]);

            int expectedNextIndex = playedIndex + 1;
            Expect((int)selectedField.GetValue(form)! == expectedNextIndex, "Analyze Next advances from current analyzed-song index");
            Expect(GetSongIds()[expectedNextIndex] == sortedOrder[expectedNextIndex], "Analyze Next selects the exact next song in current sort order");
            Expect(GetSongIds()[expectedNextIndex] != playedSongId, "Analyze Next does not retry stale selected index");
            Expect((bool)countdownField.GetValue(form)!, "Analyze Next starts countdown for resolved next song");
        }
        finally
        {
            UserSettingsStore.DefaultSaveFilePathOverride = null;
            AchievementProgressStore.DefaultSaveFilePathOverride = null;
            SongDataStore.DefaultSaveFilePathOverride = null;
        }
    }

    private static void TestCountdownAccessibility()
    {
        UserSettingsStore.DefaultSaveFilePathOverride = TempFile("countdown_accessibility_settings.json");
        AchievementProgressStore.DefaultSaveFilePathOverride = TempFile("countdown_accessibility_achievements.json");
        SongDataStore.DefaultSaveFilePathOverride = TempFile("countdown_accessibility_songs.json");
        try
        {
            using var form = new GameForm(selfTestMode: true) { ClientSize = new Size(1366, 768), ShowInTaskbar = false };
            FieldInfo screenField = typeof(GameForm).GetField("_screen", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("_screen field not found.");
            FieldInfo countdownField = typeof(GameForm).GetField("_isCountdownActive", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("_isCountdownActive field not found.");
            FieldInfo countdownSecondsField = typeof(GameForm).GetField("_countdownSeconds", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("_countdownSeconds field not found.");
            FieldInfo countdownStartTimeField = typeof(GameForm).GetField("_countdownStartTime", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("_countdownStartTime field not found.");

            screenField.SetValue(form, Enum.Parse(screenField.FieldType, "SongSelect"));
            AccessibleObject accessibility = form.AccessibilityObject;
            int songSelectNodes = accessibility.GetChildCount();
            Expect(songSelectNodes > 1, "song select exposes its normal accessibility nodes before countdown");

            countdownSecondsField.SetValue(form, 3);
            countdownStartTimeField.SetValue(form, DateTime.Now.AddSeconds(-1.1));
            countdownField.SetValue(form, true);
            int countdownNodes = accessibility.GetChildCount();
            AccessibleObject? countdown = accessibility.GetChild(0);
            Expect(countdownNodes == 1 && countdown is not null, "countdown replaces cached song-select accessibility tree");
            Expect(countdown!.Name == "Game countdown", "countdown exposes dedicated accessible name");
            Expect(countdown.Role == AccessibleRole.StaticText, "countdown node is announced as static text");
            Expect(countdown.Description?.Contains("2 seconds", StringComparison.Ordinal) == true, "countdown accessible description announces ceil remaining time from countdown start");
            Expect((countdown.State & AccessibleStates.Focusable) == 0, "countdown static text is not exposed as focusable");
            Expect(string.IsNullOrEmpty(countdown.DefaultAction), "countdown static text has no Press default action");
            countdownStartTimeField.SetValue(form, DateTime.Now.AddSeconds(-2.1));
            AccessibleObject? refreshedCountdown = accessibility.GetChild(0);
            Expect(refreshedCountdown?.Description?.Contains("1 second", StringComparison.Ordinal) == true, "countdown accessibility cache refreshes when ceil remaining second changes");
            for (int i = 0; i < countdownNodes; i++)
                Expect(!string.Equals(accessibility.GetChild(i)?.Name, "Play selected song", StringComparison.Ordinal), "countdown does not leak song-select action nodes");
        }
        finally
        {
            UserSettingsStore.DefaultSaveFilePathOverride = null;
            AchievementProgressStore.DefaultSaveFilePathOverride = null;
            SongDataStore.DefaultSaveFilePathOverride = null;
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

    private static void TestGameFramePacingAndBackgroundCache()
    {
        UserSettingsStore.DefaultSaveFilePathOverride = TempFile("frame_pacing_settings.json");
        AchievementProgressStore.DefaultSaveFilePathOverride = TempFile("frame_pacing_achievements.json");
        try
        {
            using var form = new GameForm(selfTestMode: true) { ClientSize = new Size(1366, 768), ShowInTaskbar = false };
            FieldInfo timerField = typeof(GameForm).GetField("_timer", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("_timer field not found.");
            FieldInfo engineField = typeof(GameForm).GetField("_engine", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("_engine field not found.");
            FieldInfo backgroundCacheField = typeof(GameForm).GetField("_gameBackgroundCache", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("_gameBackgroundCache field not found.");
            FieldInfo screenField = typeof(GameForm).GetField("_screen", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("_screen field not found.");
            MethodInfo enterLowLatency = typeof(GameForm).GetMethod("EnterGameLowLatencyMode", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("EnterGameLowLatencyMode not found.");
            MethodInfo exitLowLatency = typeof(GameForm).GetMethod("ExitGameLowLatencyMode", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("ExitGameLowLatencyMode not found.");

            var timer = (System.Windows.Forms.Timer)timerField.GetValue(form)!;
            timer.Interval = 7;
            enterLowLatency.Invoke(form, []);
            Expect(timer.Interval == 7, "low-latency mode preserves selected render interval");
            exitLowLatency.Invoke(form, []);
            Expect(timer.Interval == 7, "leaving low-latency mode preserves selected render interval");

            var engine = (GameEngine)engineField.GetValue(form)!;
            screenField.SetValue(form, Enum.Parse(screenField.FieldType, "SongSelect"));
            engine.Start(form.ClientSize.Height, [new LaneNote(10f, 0)], 4);
            DrawToBitmapAndAssert(form, form.ClientSize, "Cached game background first frame");
            object? firstCache = backgroundCacheField.GetValue(form);
            Expect(firstCache is Bitmap, "first game frame creates static background cache");
            DrawToBitmapAndAssert(form, form.ClientSize, "Cached game background second frame");
            object? secondCache = backgroundCacheField.GetValue(form);
            Expect(ReferenceEquals(firstCache, secondCache), "unchanged game layout reuses static background cache");

            MethodInfo onPaint = typeof(GameForm).GetMethod("OnPaint", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("OnPaint not found.");
            using var benchmarkBitmap = new Bitmap(form.ClientSize.Width, form.ClientSize.Height);
            using Graphics benchmarkGraphics = Graphics.FromImage(benchmarkBitmap);
            using var benchmarkArgs = new PaintEventArgs(benchmarkGraphics, new Rectangle(Point.Empty, form.ClientSize));
            const int benchmarkFrames = 12;
            Stopwatch renderStopwatch = Stopwatch.StartNew();
            for (int i = 0; i < benchmarkFrames; i++)
                onPaint.Invoke(form, [benchmarkArgs]);
            renderStopwatch.Stop();
            Console.WriteLine($"INFO cached game render average={renderStopwatch.Elapsed.TotalMilliseconds / benchmarkFrames:F2}ms at {form.ClientSize.Width}x{form.ClientSize.Height}");
            engine.Stop();
        }
        finally
        {
            UserSettingsStore.DefaultSaveFilePathOverride = null;
            AchievementProgressStore.DefaultSaveFilePathOverride = null;
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
        using (Graphics graphics = Graphics.FromImage(bitmap))
        using (var args = new PaintEventArgs(graphics, new Rectangle(Point.Empty, size)))
        {
            typeof(GameForm)
                .GetMethod("OnPaint", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(form, [args]);
        }

        Expect(HasNonBlankPixels(bitmap), $"{screenName} bitmap nonblank at {size.Width}x{size.Height}");

        string? captureDirectory = Environment.GetEnvironmentVariable("MUWORLD_CAPTURE_DIR");
        if (!string.IsNullOrWhiteSpace(captureDirectory))
        {
            Directory.CreateDirectory(captureDirectory);
            string capturePath = Path.Combine(captureDirectory, BuildCaptureFileName(screenName, size));
            bitmap.Save(capturePath, System.Drawing.Imaging.ImageFormat.Png);
        }
    }

    private static string BuildCaptureFileName(string screenName, Size size)
    {
        HashSet<char> invalid = Path.GetInvalidFileNameChars().ToHashSet();
        string safeName = new(screenName
            .Select(character => invalid.Contains(character) || char.IsControl(character) || char.IsWhiteSpace(character) ? '_' : character)
            .ToArray());
        safeName = safeName.Trim('_', '.');
        if (safeName.Length == 0)
            safeName = "Screen";
        if (safeName.Length > 80)
            safeName = safeName[..80];

        return $"MuWorld_{safeName}_{Math.Max(1, size.Width)}x{Math.Max(1, size.Height)}.png";
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

    private static Keys[][] GetLaneKeyBindings(GameForm form)
    {
        FieldInfo field = typeof(GameForm).GetField("_laneKeyBindings", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("_laneKeyBindings field not found.");
        return (Keys[][])field.GetValue(form)!;
    }

    private static UserSettings CreateSettingsSnapshot(GameForm form)
    {
        MethodInfo method = typeof(GameForm).GetMethod("CreateUserSettingsSnapshot", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("CreateUserSettingsSnapshot not found.");
        return (UserSettings)method.Invoke(form, [])!;
    }

    private static void ExpectLaneBindings(GameForm form, IReadOnlyList<Keys[]> expected, string label)
    {
        Keys[][] actual = GetLaneKeyBindings(form);
        Expect(actual.Length == expected.Count, $"{label} lane-mode count");
        for (int mode = 0; mode < expected.Count; mode++)
            Expect(actual[mode].SequenceEqual(expected[mode]), $"{label} {expected[mode].Length}K bindings");
    }

    private static string TempFile(string fileName)
    {
        string directory = Path.Combine(Path.GetTempPath(), "MuWorld.SelfTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, fileName);
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Test cleanup must not hide assertion failures.
        }
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
