namespace RhythmGame;

internal enum AudioEngineKind
{
    Mci,
    NAudio,
    Bass,
    CSCore,
}

internal sealed record AudioEngineReview(
    AudioEngineKind Kind,
    bool IsAvailable,
    bool SupportsIndependentBgmAndSfx,
    bool SupportsPrecisePosition,
    bool SupportsBroadCodecSet,
    string Notes);

internal static class AudioEngineCatalog
{
    public static AudioEngineKind ActiveEngine => AudioEngineKind.Mci;

    public static IReadOnlyList<AudioEngineReview> Reviews { get; } =
    [
        new(
            AudioEngineKind.Mci,
            IsAvailable: true,
            SupportsIndependentBgmAndSfx: true,
            SupportsPrecisePosition: false,
            SupportsBroadCodecSet: false,
            "Built-in WinMM backend. Good enough for no-dependency playback, but codec and latency support depend on Windows."),
        new(
            AudioEngineKind.NAudio,
            IsAvailable: false,
            SupportsIndependentBgmAndSfx: true,
            SupportsPrecisePosition: true,
            SupportsBroadCodecSet: true,
            "Recommended managed replacement if this project accepts a NuGet dependency."),
        new(
            AudioEngineKind.Bass,
            IsAvailable: false,
            SupportsIndependentBgmAndSfx: true,
            SupportsPrecisePosition: true,
            SupportsBroadCodecSet: true,
            "Strong native engine option, but it adds binary distribution and licensing checks."),
        new(
            AudioEngineKind.CSCore,
            IsAvailable: false,
            SupportsIndependentBgmAndSfx: true,
            SupportsPrecisePosition: true,
            SupportsBroadCodecSet: true,
            "Managed alternative for WASAPI style playback; less commonly maintained than NAudio."),
    ];

    public static AudioEngineReview ActiveReview => Reviews.First(r => r.Kind == ActiveEngine);
}
