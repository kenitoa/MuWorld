using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using RhythmGame;

namespace MuWorld.SelfTests;

internal static class QualityHarness
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    internal static int Run(string[] args)
    {
        string output = Path.Combine(Path.GetTempPath(), "MuWorld-quality-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(output);
        UserSettingsStore.DefaultSaveFilePathOverride = Path.Combine(output, "settings.json");
        AchievementProgressStore.DefaultSaveFilePathOverride = Path.Combine(output, "progress.json");
        SongDataStore.DefaultSaveFilePathOverride = Path.Combine(output, "songs.json");
        Console.WriteLine($"Quality artifacts: {output}");
        try
        {
            return args[0] switch
            {
                "--audio-repeat" => AudioRepeat(args, output),
                "--soak" => Soak(args, output),
                "--export-fixtures" => ReferenceFixtures.Export(output),
                _ => throw new ArgumentException("Use --audio-repeat [audioPath replayPath] or --soak [seconds>=600]."),
            };
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(output, "failure.txt"), ex.ToString());
            Console.WriteLine($"FAIL {ex.Message}");
            return 1;
        }
    }

    private static int AudioRepeat(string[] args, string output)
    {
        string audioPath;
        ReplayRecord replay;
        if (args.Length == 3)
        {
            audioPath = Path.GetFullPath(args[1]);
            replay = JsonSerializer.Deserialize<ReplayRecord>(File.ReadAllText(args[2]), JsonOptions)
                ?? throw new InvalidDataException("Replay is empty.");
        }
        else if (args.Length == 1)
        {
            audioPath = Path.Combine(output, "reference.wav");
            TutorialForm.WriteBeatTrack(audioPath);
            replay = CreateReferenceReplay(audioPath);
            File.WriteAllText(Path.Combine(output, "reference.replay.json"), JsonSerializer.Serialize(replay, JsonOptions));
        }
        else throw new ArgumentException("Specify both audio and replay paths, or neither.");

        string fingerprint = ReplayCompatibility.BuildAudioFingerprintAsync(audioPath).GetAwaiter().GetResult();
        ReplayValidationResult validation = ReplayCompatibility.ValidateForPlayback(replay,
            AudioFileCatalog.GetSongId(audioPath), replay.DifficultyIndex, replay.LaneCount, fingerprint);
        if (!validation.CanPlay) throw new InvalidDataException(validation.UserMessage);

        var results = new List<object>();
        for (int run = 0; run < 5; run++)
        {
            using var form = CreateReplayForm(replay);
            GameEngine engine = (GameEngine)Get(form, "_engine")!;
            using var audio = new AudioManager();
            audio.SetBgmVolume(0); // Exercise the actual output device without five audible repetitions.
            audio.PlayInGameBgm(audioPath);
            if (!audio.IsInGameBgmPlaying) throw new InvalidOperationException("Actual audio device playback failed.");
            float duration = audio.GetInGameBgmDurationSeconds() ?? throw new InvalidOperationException("Audio duration unavailable.");
            var clock = Stopwatch.StartNew();
            double last = 0;
            bool paused = false;
            MethodInfo update = typeof(GameForm).GetMethod("UpdateReplayPlayback", Flags)!;
            while (clock.Elapsed.TotalSeconds < duration + 4)
            {
                double now = clock.Elapsed.TotalSeconds;
                if (!paused && now >= 1)
                {
                    audio.PauseInGameBgm();
                    Thread.Sleep(150);
                    audio.ResumeInGameBgm();
                    paused = true;
                    last = clock.Elapsed.TotalSeconds;
                    continue;
                }
                float? position = audio.GetInGameBgmPositionSeconds();
                update.Invoke(form, [(float)(now - last), position]);
                last = now;
                if (engine.IsChartComplete && audio.IsInGameBgmFinished()) break;
                Application.DoEvents();
                Thread.Sleep(run % 2 == 0 ? 4 : 17);
            }
            if (!engine.IsChartComplete) throw new InvalidOperationException("Replay did not complete before timeout.");
            string comparison = ReplayCompatibility.CompareResult(replay, engine.Score,
                Enum.GetValues<ResultGrade>().First(g => ScoreManager.FormatGrade(g) == replay.Result.Grade), Enum.GetValues<ClearType>().First(c => ScoreManager.FormatClearType(c) == replay.Result.ClearType), engine.JudgmentHistory);
            audio.StopInGameBgm();
            results.Add(new { run = run + 1, comparison, engine.Score.Score, engine.Score.Accuracy,
                Clock = audio.ClockDiagnosticsSnapshot, Judgments = engine.JudgmentHistory.ToArray() });
            File.WriteAllText(Path.Combine(output, "audio-repeat.json"), JsonSerializer.Serialize(results, JsonOptions));
            if (comparison != "REPLAY VERIFIED") throw new InvalidOperationException(comparison);
            Console.WriteLine($"PASS actual audio replay {run + 1}/5: {engine.Score.Score}, {comparison}");
        }
        Console.WriteLine("Grade/clear-type labels supplied from the reference; gauge and UI transitions need gameplay E2E verification.");
        return 0;
    }

    private static ReplayRecord CreateReferenceReplay(string audioPath)
    {
        var replay = new ReplayRecord
        {
            GameVersion = ReplayCompatibility.CurrentGameVersion,
            SongId = AudioFileCatalog.GetSongId(audioPath), SongTitle = "Reference",
            AudioFingerprint = ReplayCompatibility.BuildAudioFingerprintAsync(audioPath).GetAwaiter().GetResult(),
            LaneCount = 4, DifficultyIndex = 1,
            Settings = new ReplaySettingsSnapshot { LaneCount = 4, PlayModeIndex = 1 },
            Chart = [new(2, 0), new(2, 3), new(3, 1, NoteType.Long, 1, 1), new(5, 0, NoteType.Slide, 1, 1), new(7, 2)],
        };
        foreach (LaneNote note in replay.Chart)
        {
            replay.Events.Add(new(note.Time, note.Lane, "fixture", true, "", "fixture"));
            if (note.Type == NoteType.Slide)
            {
                // Acquire destination just before the production engine's halfway boundary.
                replay.Events.Add(new(note.Time + note.Duration / 2 - 0.02f, note.EndLane, "fixture", true, "", "fixture"));
                replay.Events.Add(new(note.Time + note.Duration / 2 + 0.02f, note.Lane, "fixture", false, "", "fixture"));
            }
            replay.Events.Add(new(note.Time + Math.Max(0.05f, note.Duration), note.Type == NoteType.Slide ? note.EndLane : note.Lane, "fixture", false, "", "fixture"));
        }
        replay.Events = replay.Events.OrderBy(e => e.Time).ToList();
        replay.ChartVersion = ReplayCompatibility.BuildChartVersion(replay.SongId, replay.DifficultyIndex, 4, replay.Chart);
        using var form = CreateReplayForm(replay);
        var engine = (GameEngine)Get(form, "_engine")!;
        MethodInfo update = typeof(GameForm).GetMethod("UpdateReplayPlayback", Flags)!;
        for (int i = 1; i <= 9000; i++) update.Invoke(form, [0.001f, i / 1000f]);
        var score = engine.Score;
        ClearType clear = ScoreManager.CalculateClearType(score.PerfectCount, score.GreatCount, score.BetterCount,
            score.GoodCount, score.BadCount, score.MissCount);
        replay.Result = new ReplayResultSnapshot { Score = score.Score, Accuracy = score.Accuracy,
            Grade = ScoreManager.FormatGrade(ScoreManager.CalculateGrade(score.Accuracy, score.MissCount, score.MaxCombo, clear)), ClearType = ScoreManager.FormatClearType(clear),
            PerfectCount = score.PerfectCount, GreatCount = score.GreatCount, BetterCount = score.BetterCount,
            GoodCount = score.GoodCount, BadCount = score.BadCount, MissCount = score.MissCount,
            MaxCombo = score.MaxCombo, MaxMissStreak = score.MaxMissStreak };
        replay.Judgments = engine.JudgmentHistory.ToList();
        return replay;
    }

    private static GameForm CreateReplayForm(ReplayRecord replay)
    {
        var form = new GameForm(selfTestMode: true);
        Set(form, "_isReplayPlayback", true); Set(form, "_activeReplay", replay);
        Set(form, "_laneModeIndex", replay.LaneCount - 4);
        var engine = (GameEngine)Get(form, "_engine")!;
        engine.AudioOffsetSeconds = replay.Settings.AudioOffsetMs / 1000f;
        engine.Start(768, replay.Chart, replay.LaneCount);
        return form;
    }

    private static int Soak(string[] args, string output)
    {
        int seconds = args.Length > 1 ? int.Parse(args[1]) : 600;
        if (seconds is < 600 or > 3600) throw new ArgumentException("Soak duration must be 600..3600 seconds.");
        using var form = new GameForm(selfTestMode: true) { ClientSize = new Size(1366, 768) };
        var engine = (GameEngine)Get(form, "_engine")!;
        var chart = Enumerable.Range(0, (seconds + 20) * 8).Select(i => new LaneNote(2 + i / 8f, i % 4)).ToArray();
        engine.Start(768, chart, 4);
        using var bitmap = new Bitmap(1366, 768);
        using Graphics graphics = Graphics.FromImage(bitmap);
        using var paint = new PaintEventArgs(graphics, new Rectangle(0, 0, 1366, 768));
        MethodInfo draw = typeof(GameForm).GetMethod("OnPaint", Flags)!;
        var clock = Stopwatch.StartNew();
        var metrics = new FrameDiagnostics(); metrics.Start();
        var samples = new List<object>();
        int baselineGdi = -1; long baselineMemory = 0; int nextSample = 30;
        double last = 0;
        while (clock.Elapsed.TotalSeconds < seconds)
        {
            double now = clock.Elapsed.TotalSeconds;
            engine.Update((float)(now - last), (float)now); last = now;
            long start = Stopwatch.GetTimestamp();
            draw.Invoke(form, [paint]);
            metrics.Record(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            if (now >= nextSample)
            {
                using Process process = Process.GetCurrentProcess();
                int gdi = GdiResourceMonitor.GetCurrentGdiObjectCount();
                long memory = process.PrivateMemorySize64;
                if (baselineGdi < 0) { baselineGdi = gdi; baselineMemory = memory; }
                samples.Add(new { seconds = nextSample, gdi, privateBytes = memory, p95DrawMs = metrics.Percentile(0.95), p99DrawMs = metrics.Percentile(0.99) });
                File.WriteAllText(Path.Combine(output, "soak.json"), JsonSerializer.Serialize(samples, JsonOptions));
                Console.WriteLine($"SOAK {nextSample}s gdi={gdi}, privateMiB={memory / 1048576}, p99DrawMs={metrics.Percentile(0.99)}");
                if (gdi - baselineGdi > 64 || memory - baselineMemory > 128 * 1024 * 1024 || metrics.Percentile(0.99) > 50)
                    throw new InvalidOperationException("Soak regression budget exceeded: GDI +64, private +128 MiB or p99 draw >50ms.");
                nextSample += 30;
            }
            Application.DoEvents(); Thread.Sleep(8);
        }
        metrics.Log("render-soak");
        Console.WriteLine("PASS render soak; this does not verify physical input, audible output or screen-reader behavior.");
        return 0;
    }

    private static object? Get(GameForm form, string field) => typeof(GameForm).GetField(field, Flags)!.GetValue(form);
    private static void Set(GameForm form, string field, object value) => typeof(GameForm).GetField(field, Flags)!.SetValue(form, value);
}
