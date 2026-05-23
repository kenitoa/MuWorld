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
}
