using System.Reflection;
using RhythmGame;

namespace MuWorld.SelfTests;

internal sealed partial class SelfTestRunner
{
    private static void TestInterfaceLayoutAndActions()
    {
        string directory = Path.Combine(Path.GetTempPath(), "muworld-interface-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string? previous = UserSettingsStore.DefaultSaveFilePathOverride;
        UserSettingsStore.DefaultSaveFilePathOverride = Path.Combine(directory, "settings.json");
        try
        {
            using var form = new GameForm(selfTestMode: true);
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
            object? Call(string name, params object[] args) => typeof(GameForm).GetMethod(name, flags)!.Invoke(form, args);
            void Set(string name, object value) => typeof(GameForm).GetField(name, flags)!.SetValue(form, value);
            object Get(string name) => typeof(GameForm).GetField(name, flags)!.GetValue(form)!;
            FieldInfo screen = typeof(GameForm).GetField("_screen", flags)!;
            foreach (Size size in new[] { new Size(960, 640), new Size(1366, 768), new Size(1920, 1080), new Size(2560, 1080) })
            {
                form.ClientSize = size;
                screen.SetValue(form, Enum.Parse(screen.FieldType, "SongSelect"));
                foreach (int scale in new[] { 100, 125, 140 })
                {
                    Set("_textScalePercent", scale);
                    DrawToBitmapAndAssert(form, size, $"Library text {scale}");
                    var targets = new List<Rectangle>();
                    for (int i = 0; i < 4; i++)
                    {
                        Rectangle bounds = (Rectangle)Call("GetLibraryLaneBounds", i)!;
                        Expect((int)Call("GetSongSelectHoverCode", bounds.Center())! == 60 + i, "lane hit target matches drawing");
                        targets.Add(bounds);
                        Rectangle tool = (Rectangle)Call("GetLibraryToolBounds", i)!;
                        Expect((int)Call("GetSongSelectHoverCode", tool.Center())! == 65 + i, "tool hit target matches drawing");
                        targets.Add(tool);
                    }
                    for (int a = 0; a < targets.Count; a++)
                        for (int b = a + 1; b < targets.Count; b++)
                            Expect(!targets[a].IntersectsWith(targets[b]), "library actions do not overlap");
                    float x = (float)Get("_layoutOffsetX"), y = (float)Get("_layoutOffsetY");
                    foreach (Rectangle bounds in targets)
                    {
                        Rectangle client = bounds;
                        client.Offset((int)x, (int)y);
                        Expect(new Rectangle(Point.Empty, size).Contains(client), "action stays inside viewport including ultrawide");
                    }
                }
                Set("_textScalePercent", 100);
            }
            Call("HandleSongSelectMouseDown", ((Rectangle)Call("GetLibraryLaneBounds", 3)!).Center());
            Expect((int)Get("_laneModeIndex") == 3, "7K control updates active mode");
            Set("_libraryGenre", "missing genre");
            Set("_songFavoritesOnly", true);
            Call("HandleSongSelectMouseDown", ((Rectangle)Call("GetLibraryClearFiltersBounds")!).Center());
            Expect((string)Get("_libraryGenre") == "" && !(bool)Get("_songFavoritesOnly"), "clear filters resets conditions");
            Set("_songSelectSelectedIndex", 0); Set("_songListFirstIndex", 0);
            var songs = (Array)Call("GetFilteredSongs")!;
            if (songs.Length > 6)
            {
                Call("MoveSongSelection", 6);
                Expect((int)Call("GetSongFirstVisibleIndex")! == 1, "selection scrolls by one row across former page boundary");
            }
            form.ClientSize = new Size(1366, 768);
            Set("_textScalePercent", 140);
            Set("_analyzeScore", 1000000);
            Set("_analyzeMaxCombo", 99999);
            foreach (string name in new[] { "MainMenu", "Settings", "ChartEditor", "Analyze" })
            {
                screen.SetValue(form, Enum.Parse(screen.FieldType, name));
                DrawToBitmapAndAssert(form, form.ClientSize, $"{name} text 140");
            }
            Call("CancelSettingsDraft");
            Call("FlushPendingUserSettingsSave");
        }
        finally { UserSettingsStore.DefaultSaveFilePathOverride = previous; }
    }

    private static void TestSettingsDraftAndEditorValues()
    {
        string directory = Path.Combine(Path.GetTempPath(), "muworld-settings-design-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string? previous = UserSettingsStore.DefaultSaveFilePathOverride;
        UserSettingsStore.DefaultSaveFilePathOverride = Path.Combine(directory, "settings.json");
        try
        {
            using var form = new GameForm(selfTestMode: true);
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
            object? Call(string name, params object[] args) => typeof(GameForm).GetMethod(name, flags)!.Invoke(form, args);
            void Set(string name, object value) => typeof(GameForm).GetField(name, flags)!.SetValue(form, value);
            object Get(string name) => typeof(GameForm).GetField(name, flags)!.GetValue(form)!;
            int original = (int)Get("_bgmVolume");
            Call("BeginSettingsDraft");
            Set("_bgmVolume", 13);
            Call("SaveUserSettings");
            Call("CancelSettingsDraft");
            Expect((int)Get("_bgmVolume") == original, "cancel restores previewed setting");
            Call("BeginSettingsDraft"); Set("_bgmVolume", 27); Call("CommitSettingsDraft");
            Set("_bgmVolume", 31); Call("CancelSettingsDraft");
            Expect((int)Get("_bgmVolume") == 27, "cancel after apply restores latest committed value");
            Call("FlushPendingUserSettingsSave");
            Expect(new UserSettingsStore().Load().BgmVolume == 27, "only applied draft persists");

            Set("_chartEditorSongDuration", 60f);
            Set("_chartEditorNotes", new List<LaneNote> { new(1, 0, NoteType.Long, 1, 0), new(4, 1) });
            Set("_chartEditorSelectedIndex", 0);
            Set("_chartEditorBpm", 120f);
            Expect(!(bool)Call("TryApplyEditorProperties", float.NaN, 1f, 0, 1f, 0)!, "non-finite BPM rejected");
            Expect(!(bool)Call("TryApplyEditorProperties", 130f, 4f, 1, 1f, 1)!, "overlap rejected");
            Expect((float)Get("_chartEditorBpm") == 120f, "rejected batch preserves BPM");
            Expect((bool)Call("TryApplyEditorProperties", 130f, 2f, 2, 1.5f, 2)!, "valid properties accepted");
            Call("UndoChartEditor");
            Expect((float)Get("_chartEditorBpm") == 120f &&
                ((List<LaneNote>)Get("_chartEditorNotes"))[0].Time == 1f, "property edit undo restores note and BPM");
        }
        finally { UserSettingsStore.DefaultSaveFilePathOverride = previous; }
    }
}
