using System.Reflection;
using RhythmGame;

namespace MuWorld.SelfTests;

internal sealed partial class SelfTestRunner
{
    private static void TestChartEditingPersistence()
    {
        string directory = Path.Combine(Path.GetTempPath(), "muworld-chart-" + Guid.NewGuid().ToString("N"));
        ChartGenerator.UserChartDirectoryOverride = directory;
        try
        {
            LaneNote[] notes = [new(1.123456f, 0), new(2.2f, 1, NoteType.Long, 1.234f, 1),
                new(5.3f, 2, NoteType.Slide, 0.876f, 3)];
            ChartGenerator.SaveUserChart("round-trip", 1, 4, 127f, notes);
            EditableChart read = NoteLane.LoadEditableChart("round-trip", 1, 4);
            Expect(read.Notes.SequenceEqual(notes.Select(n => n.EndLane < 0 ? n with { EndLane = n.Lane } : n)), "all exact note fields survive save/load");
            Expect(read.Diagnostics.Count == 0, "extension does not produce BMS warnings");
            LaneNote[] many = Enumerable.Range(0, 20).Select(i => new LaneNote(1 + i, i % 4, NoteType.Tap, 0, i % 4)).ToArray();
            ChartTempoPoint[] tempo = [new(0, 127), new(10, 150)];
            ChartGenerator.SaveUserChart("authored", 2, 7, 127, many, tempo);
            Expect(NoteLane.LoadNotes("authored", "", 2, 7).SequenceEqual(many), "authored lanes/types are not automatically transformed");
            Expect(NoteLane.LoadEditableChart("authored", 2, 7).TempoMap.SequenceEqual(tempo), "tempo changes survive save/load");
            ExpectNear(ChartEditing.Snap(10.11f, tempo, 4), 10.1f, 0.0001f, "snap follows tempo segment");
            string original = File.ReadAllText(read.Path);
            bool rejected = false;
            try { ChartGenerator.SaveUserChart("round-trip", 1, 4, 127f, [notes[0], notes[0]]); }
            catch (InvalidDataException) { rejected = true; }
            Expect(rejected && File.ReadAllText(read.Path) == original, "invalid chart cannot overwrite original");
            ChartGenerator.SaveUserChart("round-trip", 1, 4, 128f, notes);
            Expect(File.ReadAllText(read.Path + ".bak") == original, "previous chart remains in backup");
            File.WriteAllText(read.Path, "#MUWORLD-NOTES 999\n#MWNOTES []");
            ChartValidationResult broken = NoteLane.LoadValidatedChart("round-trip", "", 1, 4);
            Expect(broken.Notes.Count == 0 && broken.Diagnostics.Any(d => d.Severity == ChartDiagnosticSeverity.Error), "unsupported chart reports error rather than random fallback");
            ExpectNear(ChartEditing.Snap(1.12f, 120f, 4), 1.125f, 0.0001f, "sixteenth snap");
            ExpectNear(ChartEditing.Snap(1.12f, 120f, 0), 1.12f, 0.0001f, "snap off");
            LaneNote slide = notes[2];
            Expect(ChartEditing.Mirror(ChartEditing.Mirror(slide, 4), 4) == slide, "mirror is reversible including end lane");
        }
        finally
        {
            ChartGenerator.UserChartDirectoryOverride = null;
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private static void TestChartEditorWorkflow()
    {
        using var form = new GameForm(selfTestMode: true);
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        object? Call(string name, params object[] args) => typeof(GameForm).GetMethod(name, flags)!.Invoke(form, args);
        FieldInfo bpm = typeof(GameForm).GetField("_chartEditorBpm", flags)!;
        bpm.SetValue(form, 120f);
        Call("HandleChartEditorAction", 5);
        Expect((float)bpm.GetValue(form)! == 121f, "BPM increment");
        Call("UndoChartEditor");
        Expect((float)bpm.GetValue(form)! == 120f, "undo restores BPM");
        Call("RedoChartEditor");
        Expect((float)bpm.GetValue(form)! == 121f, "redo restores BPM");
        foreach (Size size in new[] { new Size(1366, 768), new Size(1920, 1080), new Size(2560, 1080) })
        {
            form.ClientSize = size;
            Call("UpdateLayoutMetrics");
            FieldInfo screen = typeof(GameForm).GetField("_screen", flags)!;
            screen.SetValue(form, Enum.Parse(screen.FieldType, "ChartEditor"));
            FieldInfo chartNotes = typeof(GameForm).GetField("_chartEditorNotes", flags)!;
            chartNotes.SetValue(form, ReferenceFixtures.Build(1).ToList());
            DrawToBitmapAndAssert(form, size, "Chart Editor expanded");
            chartNotes.SetValue(form, new List<LaneNote>());
            var menuBounds = new[] { (Rectangle)Call("GetTutorialBounds")!, (Rectangle)Call("GetMenuTopSettingsButtonBounds")!,
                (Rectangle)Call("GetMenuPlayerBadgeBounds")!, (Rectangle)Call("GetExitButtonBounds")! };
            for (int a = 0; a < menuBounds.Length; a++)
                for (int b = a + 1; b < menuBounds.Length; b++)
                    Expect(!menuBounds[a].IntersectsWith(menuBounds[b]), "main menu targets do not overlap");
            string[] actions = (string[])Call("GetChartEditorActions")!;
            var rectangles = new List<Rectangle>();
            for (int i = 0; i < actions.Length; i++)
            {
                Rectangle bounds = (Rectangle)Call("GetChartEditorActionBounds", i)!;
                Expect(rectangles.All(r => !r.IntersectsWith(bounds)), "editor actions do not overlap");
                rectangles.Add(bounds);
            }
        }
        AccessibleObject accessibility = form.AccessibilityObject;
        accessibility.GetChildCount();
        Call("HandleChartEditorAction", 10);
        accessibility.GetChildCount();
        Expect(accessibility.GetChild(10)?.Name == "SNAP 1/6 BEAT", "accessible snap name tracks visible control");
        Call("HandleChartEditorAction", 19);
        FieldInfo notesField = typeof(GameForm).GetField("_chartEditorNotes", flags)!;
        var notes = (List<LaneNote>)notesField.GetValue(form)!;
        Expect(notes.Count == 1, "keyboard insertion works");
        Call("HandleChartEditorAction", 12);
        Call("HandleChartEditorAction", 13);
        Expect(((List<LaneNote>)notesField.GetValue(form)!).Count == 1, "overlapping paste is rejected atomically");
    }

    private static void TestTutorialAndFixtures()
    {
        for (int lanes = 4; lanes <= 7; lanes++)
            for (int stage = 0; stage < 5; stage++)
            {
                IReadOnlyList<LaneNote> chart = TutorialForm.BuildStage(stage, lanes);
                Expect(ChartValidator.ValidateAndFilter(chart, lanes).Notes.Count == chart.Count, "tutorial stage is playable");
            }
        using (var tutorial = new TutorialForm([Keys.D, Keys.F, Keys.J, Keys.K], 0))
        using (var bitmap = new Bitmap(tutorial.Width, tutorial.Height))
        {
            tutorial.ShowInTaskbar = false;
            tutorial.Opacity = 0;
            tutorial.Show();
            Application.DoEvents();
            Expect(tutorial.Controls.Count == 3 && tutorial.Controls.Cast<Control>().All(c => c.Visible), "tutorial instructions and actions are visible");
            tutorial.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
            tutorial.Hide();
            string? capture = Environment.GetEnvironmentVariable("MUWORLD_CAPTURE_DIR");
            if (!string.IsNullOrEmpty(capture))
            {
                Directory.CreateDirectory(capture);
                bitmap.Save(Path.Combine(capture, "Tutorial.png"), System.Drawing.Imaging.ImageFormat.Png);
            }
        }
        for (int difficulty = 0; difficulty < 3; difficulty++)
        {
            IReadOnlyList<LaneNote> chart = ReferenceFixtures.Build(difficulty);
            Expect(ChartValidator.ValidateAndFilter(chart, 4).Notes.Count == chart.Count, "reference chart is valid");
            Expect(chart.Any(n => n.Type == NoteType.Long) && chart.Any(n => n.Type == NoteType.Slide), "reference includes holds and slides");
        }
    }

    private static void TestFrameDiagnostics()
    {
        var metrics = new FrameDiagnostics(); metrics.Start();
        for (int i = 0; i < 95; i++) metrics.Record(8);
        for (int i = 0; i < 5; i++) metrics.Record(40);
        metrics.Record(double.NaN);
        Expect(metrics.Percentile(0.95) == 8 && metrics.Percentile(0.99) == 40, "histogram preserves tail latency");
    }

    private static void TestLibraryFilters()
    {
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        FieldInfo cache = typeof(GameForm).GetField("_cachedSongList", BindingFlags.NonPublic | BindingFlags.Static)!;
        object? original = cache.GetValue(null);
        using var form = new GameForm(selfTestMode: true);
        try
        {
            Type entry = typeof(GameForm).GetNestedType("SongEntry", BindingFlags.NonPublic)!;
            ConstructorInfo constructor = entry.GetConstructors().Single();
            Array songs = Array.CreateInstance(entry, 120);
            for (int i = 0; i < songs.Length; i++)
                songs.SetValue(constructor.Invoke([$"fixture-{i}", $"Song {i}", "Artist", 0, "", "WAV", 60f, 120f,
                    0f, 10f, i % 2 == 0 ? "Electronic" : "Acoustic", "Fixture", "", "", 0, "", "", i % 2 == 0, 1,
                    DateTime.UtcNow.AddDays(i % 2 == 0 ? -1 : -90).ToString("O")]), i);
            cache.SetValue(null, songs);
            MethodInfo filter = typeof(GameForm).GetMethod("GetFilteredSongs", flags)!;
            Array all = (Array)filter.Invoke(form, [])!;
            Expect(all.Length == 120, "all fixture songs visible");
            Expect(ReferenceEquals(all, filter.Invoke(form, [])), "unchanged query reuses list without sorting again");
            typeof(GameForm).GetField("_libraryGenre", flags)!.SetValue(form, "Electronic");
            typeof(GameForm).GetField("_libraryRecent", flags)!.SetValue(form, true);
            Expect(((Array)filter.Invoke(form, [])!).Length == 60, "genre and recent filters combine");
            typeof(GameForm).GetField("_librarySource", flags)!.SetValue(form, "Missing");
            Expect(((Array)filter.Invoke(form, [])!).Length == 0, "empty filter result handled");
        }
        finally { cache.SetValue(null, original); }
    }

    private static void TestSkinNames()
    {
        foreach (string invalid in new[] { "..", "../outside", "..\\outside", "C:\\outside", "x/y", "" })
            Expect(!VisualSkin.IsValidName(invalid), "unsafe skin name rejected");
        Expect(VisualSkin.IsValidName("my skin"), "ordinary skin names remain valid");
        using VisualSkin fallback = VisualSkin.Load("../outside");
        Expect(fallback.Name == VisualSkin.DefaultName, "invalid skin falls back");
    }
}
