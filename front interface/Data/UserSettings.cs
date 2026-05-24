using System.Text.Json;

namespace RhythmGame;

internal sealed class UserSettings
{
    public int BgmVolume { get; set; } = 80;
    public int PreviewVolume { get; set; } = 45;
    public int SfxVolume { get; set; } = 60;
    public string HitSoundSkin { get; set; } = "CLASSIC";
    public string VisualSkin { get; set; } = "default";
    public int HitSoundPitch { get; set; } = 0;
    public bool HitSoundMuted { get; set; }
    public int ThemeColorIndex { get; set; }
    public int LaneBrightness { get; set; } = 70;
    public int FrameRateMode { get; set; } = 2;
    public bool VSyncEnabled { get; set; }
    public bool DarkModeEnabled { get; set; }
    public int AudioOffsetMs { get; set; }
    public int LaneModeIndex { get; set; }
    public int NoteSpeedPercent { get; set; } = 100;
    public int DisplayMode { get; set; }
    public int WindowWidth { get; set; }
    public int WindowHeight { get; set; }
    public int SplashDurationMs { get; set; } = 1600;
    public bool HighContrastEnabled { get; set; }
    public int ColorVisionMode { get; set; }
    public bool ReducedMotionEnabled { get; set; }
    public int TextScalePercent { get; set; } = 100;
    public int RenderQualityMode { get; set; } = 1;
    public int PlayModeIndex { get; set; }
    public string[] KeyBindings4K { get; set; } = [];
    public string[] KeyBindings5K { get; set; } = [];
    public string[] KeyBindings7K { get; set; } = [];
    public string LastSavedUtc { get; set; } = string.Empty;
}

internal sealed class UserSettingsStore
{
    internal static string? DefaultSaveFilePathOverride { get; set; }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string _saveFilePath;

    public UserSettingsStore()
        : this(DefaultSaveFilePathOverride ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RhythmGame",
            "user_settings.json"))
    {
    }

    internal UserSettingsStore(string saveFilePath)
    {
        _saveFilePath = saveFilePath;
    }

    public UserSettings Load()
    {
        try
        {
            if (!SafeJsonFile.TryReadWithBackup(_saveFilePath, out string json))
                return new UserSettings();

            return JsonSerializer.Deserialize<UserSettings>(json, JsonOptions) ?? new UserSettings();
        }
        catch (Exception ex)
        {
            AppLogger.Error("Failed to parse user_settings.json; trying backup.", ex);
            if (SafeJsonFile.TryReadBackup(_saveFilePath, restore: true, out string backupJson))
            {
                try
                {
                    return JsonSerializer.Deserialize<UserSettings>(backupJson, JsonOptions) ?? new UserSettings();
                }
                catch (Exception backupEx)
                {
                    AppLogger.Error("Failed to parse user_settings.json backup.", backupEx);
                }
            }

            return new UserSettings();
        }
    }

    public void Save(UserSettings settings)
    {
        settings.LastSavedUtc = DateTime.UtcNow.ToString("O");
        string json = JsonSerializer.Serialize(settings, JsonOptions);
        SafeJsonFile.WriteWithBackup(_saveFilePath, json);
    }
}
