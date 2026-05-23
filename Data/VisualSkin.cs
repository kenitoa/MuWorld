using System.Drawing;
using System.Globalization;
using System.Text.Json;

namespace RhythmGame;

internal sealed record VisualSkin
{
    public const string DefaultName = "default";

    public string Name { get; private init; } = DefaultName;
    public Bitmap? NoteBody { get; private init; }
    public Bitmap? LongTail { get; private init; }
    public Bitmap? SlideArrow { get; private init; }
    public Bitmap? HitBurst { get; private init; }
    public Bitmap? MissEffect { get; private init; }
    public Color[] LaneColors { get; private init; } = [];
    public Color? LanePressedTint { get; private init; }
    public Color? LaneHoldTint { get; private init; }
    public Color? LaneSeparator { get; private init; }
    public Color? HitLine { get; private init; }
    public Color? HitGlow { get; private init; }
    public Color? HitGlowBottom { get; private init; }
    public Color? KeyTop { get; private init; }
    public Color? KeyBottom { get; private init; }
    public Color? KeyPressedTop { get; private init; }
    public Color? KeyPressedBottom { get; private init; }

    public static string[] DiscoverSkins()
    {
        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase) { DefaultName };
        foreach (string root in GetSkinRoots())
        {
            if (!Directory.Exists(root))
                continue;

            foreach (string directory in Directory.GetDirectories(root))
                names.Add(Path.GetFileName(directory));
        }

        return names
            .OrderBy(name => string.Equals(name, DefaultName, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static VisualSkin Load(string name)
    {
        string normalized = string.IsNullOrWhiteSpace(name) ? DefaultName : name.Trim();
        string? directory = FindSkinDirectory(normalized);
        if (directory is null)
            return new VisualSkin { Name = DefaultName };

        VisualSkin skin = new()
        {
            Name = normalized,
            NoteBody = TryLoadBitmap(directory, "note_body"),
            LongTail = TryLoadBitmap(directory, "long_tail"),
            SlideArrow = TryLoadBitmap(directory, "slide_arrow"),
            HitBurst = TryLoadBitmap(directory, "hit_burst"),
            MissEffect = TryLoadBitmap(directory, "miss_effect"),
        };

        return ApplyManifest(skin, Path.Combine(directory, "skin.json"));
    }

    public Color GetLaneColor(int lane, Color fallback)
    {
        return LaneColors.Length == 0 ? fallback : LaneColors[Math.Clamp(lane, 0, LaneColors.Length - 1) % LaneColors.Length];
    }

    private static VisualSkin ApplyManifest(VisualSkin skin, string manifestPath)
    {
        if (!File.Exists(manifestPath))
            return skin;

        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(manifestPath));
            JsonElement root = document.RootElement;
            return skin with
            {
                LaneColors = ReadColorArray(root, "laneColors"),
                LanePressedTint = ReadColor(root, "lanePressedTint"),
                LaneHoldTint = ReadColor(root, "laneHoldTint"),
                LaneSeparator = ReadColor(root, "laneSeparator"),
                HitLine = ReadColor(root, "hitLine"),
                HitGlow = ReadColor(root, "hitGlow"),
                HitGlowBottom = ReadColor(root, "hitGlowBottom"),
                KeyTop = ReadColor(root, "keyTop"),
                KeyBottom = ReadColor(root, "keyBottom"),
                KeyPressedTop = ReadColor(root, "keyPressedTop"),
                KeyPressedBottom = ReadColor(root, "keyPressedBottom"),
            };
        }
        catch (Exception ex)
        {
            AppLogger.Error($"Failed to read visual skin manifest {manifestPath}.", ex);
            return skin;
        }
    }

    private static IEnumerable<string> GetSkinRoots()
    {
        yield return Path.Combine(AppContext.BaseDirectory, "Skins", "Visual");
        yield return Path.Combine(AppContext.BaseDirectory, "Songs", "Skins", "Visual");
    }

    private static string? FindSkinDirectory(string name)
    {
        foreach (string root in GetSkinRoots())
        {
            string directory = Path.Combine(root, name);
            if (Directory.Exists(directory))
                return directory;
        }

        return null;
    }

    private static Bitmap? TryLoadBitmap(string directory, string stem)
    {
        foreach (string extension in new[] { ".png", ".jpg", ".jpeg", ".bmp" })
        {
            string path = Path.Combine(directory, stem + extension);
            if (!File.Exists(path))
                continue;

            try
            {
                using var source = new Bitmap(path);
                return new Bitmap(source);
            }
            catch (Exception ex)
            {
                AppLogger.Error($"Failed to load visual skin image {path}.", ex);
                return null;
            }
        }

        return null;
    }

    private static Color[] ReadColorArray(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out JsonElement element) || element.ValueKind != JsonValueKind.Array)
            return [];

        List<Color> colors = [];
        foreach (JsonElement colorElement in element.EnumerateArray())
            if (TryParseColor(colorElement, out Color color))
                colors.Add(color);

        return colors.ToArray();
    }

    private static Color? ReadColor(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out JsonElement element) && TryParseColor(element, out Color color)
            ? color
            : null;
    }

    private static bool TryParseColor(JsonElement element, out Color color)
    {
        color = Color.Empty;
        if (element.ValueKind != JsonValueKind.String)
            return false;

        string? raw = element.GetString();
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        string value = raw.Trim().TrimStart('#');
        if (value.Length == 6 && int.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int rgb))
        {
            color = Color.FromArgb(255, (rgb >> 16) & 0xff, (rgb >> 8) & 0xff, rgb & 0xff);
            return true;
        }

        if (value.Length == 8 && int.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int argb))
        {
            color = Color.FromArgb((argb >> 24) & 0xff, (argb >> 16) & 0xff, (argb >> 8) & 0xff, argb & 0xff);
            return true;
        }

        return false;
    }
}
