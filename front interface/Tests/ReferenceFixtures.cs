using System.Security.Cryptography;
using System.Text.Json;
using RhythmGame;

namespace MuWorld.SelfTests;

internal static class ReferenceFixtures
{
    internal static IReadOnlyList<LaneNote> Build(int difficulty)
    {
        var notes = new List<LaneNote>();
        float interval = difficulty switch { 0 => 1f, 1 => 0.5f, _ => 0.125f };
        int index = 0;
        for (float time = 2; time < 20; time += interval)
            notes.Add(new LaneNote(time, index++ % 4));
        // Rest 20..24, followed by isolated hold/slide and chords.
        notes.Add(new LaneNote(24, 0, NoteType.Long, 2, 0));
        notes.Add(new LaneNote(28, 2, NoteType.Slide, 2, 3));
        for (float time = 34; time < 58; time += difficulty == 2 ? 0.2f : 0.8f)
        {
            notes.Add(new LaneNote(time, 0));
            notes.Add(new LaneNote(time, 3));
        }
        return notes;
    }

    internal static int Export(string output)
    {
        string? previous = ChartGenerator.UserChartDirectoryOverride;
        ChartGenerator.UserChartDirectoryOverride = Path.Combine(output, "Charts");
        try
        {
            var manifest = new List<object>();
            for (int difficulty = 0; difficulty < 3; difficulty++)
            {
                string title = new[] { "Reference Tutorial", "Reference Medium", "Reference Dense" }[difficulty];
                string audio = Path.Combine(output, title + ".wav");
                WriteTrack(audio);
                ChartGenerator.SaveUserChart(title, difficulty, 4, 120, Build(difficulty), [new(0, 120), new(32, 150)]);
                string chart = ChartGenerator.GetUserChartPath(title, difficulty, 4);
                File.WriteAllText(Path.ChangeExtension(audio, ".json"), JsonSerializer.Serialize(new
                {
                    title, artist = "MuWorld reference pulse track", bpm = 120, durationSeconds = 60,
                    genre = "Validation", source = "MuWorld built-in fixture v1",
                }));
                manifest.Add(new { title, difficulty, laneCount = 4,
                    audioSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(audio))),
                    chartSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(chart))) });
            }
            File.WriteAllText(Path.Combine(output, "fixtures.json"), JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine("Exported three fixed 60-second pulse tracks, charts, metadata and SHA-256 manifest.");
            return 0;
        }
        finally { ChartGenerator.UserChartDirectoryOverride = previous; }
    }

    private static void WriteTrack(string path)
    {
        const int rate = 22050;
        const int samples = rate * 60;
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write("RIFF"u8); writer.Write(36 + samples * 2); writer.Write("WAVEfmt "u8);
        writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(rate);
        writer.Write(rate * 2); writer.Write((short)2); writer.Write((short)16); writer.Write("data"u8); writer.Write(samples * 2);
        for (int i = 0; i < samples; i++)
        {
            double time = i / (double)rate;
            double phase = time < 32 ? time % 0.5 : (time - 32) % 0.4;
            double value = time is >= 20 and < 24 ? 0 : phase < 0.04 ? Math.Sin(2 * Math.PI * 660 * phase) * (1 - phase / 0.04) * 5000 : 0;
            writer.Write((short)value);
        }
    }
}
