using SecRandom.Platforms.Abstractions;

namespace SecRandom.Platforms;

/// <summary>
///     Platform without a courseware-marker probe. Reports honestly, so the app leaves the feature off instead of
///     sampling nothing and claiming "no marker found".
/// </summary>
public sealed class UnsupportedSlideTriggerScreenProbe : ISlideTriggerScreenProbe
{
    public static UnsupportedSlideTriggerScreenProbe Instance { get; } = new();

    public bool IsSupported => false;

    public DateTimeOffset? GetLastInputAt() => null;

    public bool IsPresentationForeground() => false;

    public CapturedScreen? CaptureForegroundDisplay() => null;

    public IReadOnlyList<CapturedScreen> CaptureAllDisplays() => [];
}
