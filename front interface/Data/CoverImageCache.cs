namespace RhythmGame;

internal sealed class CoverImageCache : IDisposable
{
    private readonly Dictionary<string, (Bitmap? Image, DateTime Stamp, long Used)> _entries = new(StringComparer.OrdinalIgnoreCase);
    private long _sequence;
    internal Bitmap? Get(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        DateTime stamp;
        try { stamp = File.GetLastWriteTimeUtc(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            AppLogger.Error("Cover metadata cannot be read.", ex);
            return null;
        }
        if (_entries.TryGetValue(path, out var cached) && cached.Stamp == stamp)
        {
            _entries[path] = (cached.Image, stamp, ++_sequence);
            return cached.Image;
        }
        cached.Image?.Dispose();
        Bitmap? image = null;
        try
        {
            if (!File.Exists(path)) throw new FileNotFoundException("Cover is missing.");
            if (new FileInfo(path).Length > 16 * 1024 * 1024) throw new InvalidDataException("Cover exceeds 16 MiB.");
            using Image source = Image.FromFile(path);
            if (source.Width > 4096 || source.Height > 4096) throw new InvalidDataException("Cover exceeds 4096 pixels.");
            // Cache a thumbnail rather than full-resolution art for every list row.
            float scale = Math.Min(1f, 512f / Math.Max(source.Width, source.Height));
            image = new Bitmap(source, Math.Max(1, (int)(source.Width * scale)), Math.Max(1, (int)(source.Height * scale)));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or OutOfMemoryException)
        {
            AppLogger.Error("Cover unavailable; default artwork selected.", ex);
        }
        _entries[path] = (image, stamp, ++_sequence);
        if (_entries.Count > 24)
        {
            string oldest = _entries.MinBy(pair => pair.Value.Used).Key;
            _entries[oldest].Image?.Dispose();
            _entries.Remove(oldest);
        }
        return image;
    }

    public void Dispose()
    {
        foreach (var entry in _entries.Values) entry.Image?.Dispose();
        _entries.Clear();
    }
}
