using System.Text.Json;

namespace RhythmGame;

internal sealed class UserSettings
{
    public int BgmVolume { get; set; } = 80;
    public int SfxVolume { get; set; } = 60;
    public int ThemeColorIndex { get; set; }
    public int LaneBrightness { get; set; } = 70;
    public int FrameRateMode { get; set; } = 2;
    public bool VSyncEnabled { get; set; }
    public bool DarkModeEnabled { get; set; }
    public int AudioOffsetMs { get; set; }
}

internal sealed class UserSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string _saveFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RhythmGame",
        "user_settings.json");

    public UserSettings Load()
    {
        try
        {
            if (!File.Exists(_saveFilePath))
                return new UserSettings();

            string json = File.ReadAllText(_saveFilePath);
            return JsonSerializer.Deserialize<UserSettings>(json, JsonOptions) ?? new UserSettings();
        }
        catch
        {
            return new UserSettings();
        }
    }

    public void Save(UserSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_saveFilePath)!);
        string json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(_saveFilePath, json);
    }
}
