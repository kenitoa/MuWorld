using System.Globalization;
using System.Text.Json;

namespace RhythmGame;

internal static class AudioFileCatalog
{
    public static readonly string[] SupportedExtensions = [".wav", ".mp3", ".ogg", ".flac"];

    public static string[] DiscoverSongFiles(string directory)
    {
        if (!Directory.Exists(directory))
            return [];

        var files = new List<string>();
        foreach (string extension in SupportedExtensions)
            files.AddRange(Directory.GetFiles(directory, "*" + extension, SearchOption.TopDirectoryOnly));

        files.Sort(StringComparer.OrdinalIgnoreCase);
        return files.ToArray();
    }

    public static string? FindSongFile(string directory, string title)
    {
        foreach (string extension in SupportedExtensions)
        {
            string path = Path.Combine(directory, title + extension);
            if (File.Exists(path))
                return path;
        }

        return null;
    }

    public static bool IsWav(string path)
    {
        return string.Equals(Path.GetExtension(path), ".wav", StringComparison.OrdinalIgnoreCase);
    }

    public static string GetFormatLabel(string path)
    {
        string extension = Path.GetExtension(path).TrimStart('.');
        return extension.Length == 0 ? "AUDIO" : extension.ToUpperInvariant();
    }

    public static string GetSongId(string path)
    {
        string name = Path.GetFileNameWithoutExtension(path);
        Span<char> buffer = stackalloc char[Math.Max(1, name.Length)];
        int index = 0;

        foreach (char ch in name)
        {
            if (char.IsLetterOrDigit(ch))
                buffer[index++] = char.ToLowerInvariant(ch);
            else if (index > 0 && buffer[index - 1] != '_')
                buffer[index++] = '_';
        }

        string normalized = new string(buffer[..index]).Trim('_');
        return normalized.Length == 0 ? "song" : normalized;
    }

    public static SongMetadata ReadSongMetadata(string path)
    {
        string fallbackTitle = Path.GetFileNameWithoutExtension(path);
        string format = GetFormatLabel(path);

        var metadata = new SongMetadata
        {
            SongId = GetSongId(path),
            Title = fallbackTitle,
            Artist = "Unknown Artist",
            Format = format,
            DurationSeconds = IsWav(path) ? WavAnalyzer.GetDuration(path) : 0f,
            Bpm = TryReadGeneratedChartBpm(fallbackTitle),
        };

        ApplySidecarMetadata(path, metadata);
        return metadata;
    }

    private static void ApplySidecarMetadata(string audioPath, SongMetadata metadata)
    {
        string sidecarPath = Path.ChangeExtension(audioPath, ".json");
        if (!File.Exists(sidecarPath))
            return;

        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(sidecarPath));
            JsonElement root = document.RootElement;

            if (TryGetString(root, "title", out string title))
                metadata.Title = title;
            if (TryGetString(root, "artist", out string artist))
                metadata.Artist = artist;
            if (TryGetFloat(root, "durationSeconds", out float durationSeconds))
                metadata.DurationSeconds = Math.Max(0f, durationSeconds);
            if (TryGetFloat(root, "bpm", out float bpm))
                metadata.Bpm = Math.Max(0f, bpm);
        }
        catch
        {
            // Invalid optional metadata should not block song discovery.
        }
    }

    private static bool TryGetString(JsonElement root, string propertyName, out string value)
    {
        value = string.Empty;
        if (!root.TryGetProperty(propertyName, out JsonElement element) || element.ValueKind != JsonValueKind.String)
            return false;

        string? text = element.GetString();
        if (string.IsNullOrWhiteSpace(text))
            return false;

        value = text.Trim();
        return true;
    }

    private static bool TryGetFloat(JsonElement root, string propertyName, out float value)
    {
        value = 0f;
        if (!root.TryGetProperty(propertyName, out JsonElement element))
            return false;

        if (element.ValueKind == JsonValueKind.Number && element.TryGetSingle(out value))
            return true;

        return element.ValueKind == JsonValueKind.String &&
               float.TryParse(element.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static float TryReadGeneratedChartBpm(string title)
    {
        string chartPath = Path.Combine(
            AppContext.BaseDirectory,
            "NoteLane",
            ChartGenerator.GetChartFileName(title, 1));

        if (!File.Exists(chartPath))
            return 0f;

        try
        {
            foreach (string rawLine in File.ReadLines(chartPath))
            {
                string line = rawLine.Trim();
                if (!line.StartsWith("#BPM", StringComparison.OrdinalIgnoreCase))
                    continue;

                string value = line[4..].Trim();
                if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float bpm) && bpm > 0f)
                    return bpm;
            }
        }
        catch
        {
            return 0f;
        }

        return 0f;
    }
}
