using SecRandom.Platforms.Abstractions;

namespace SecRandom.Platforms;

/// <summary>
///     Null object for platforms without a foreground-window implementation. It exists so those roots can say
///     "not supported" honestly instead of a caller having to guess from a null service.
/// </summary>
public sealed class UnsupportedForegroundWindowService : IForegroundWindowService
{
    public static UnsupportedForegroundWindowService Instance { get; } = new();

    private UnsupportedForegroundWindowService()
    {
    }

    public bool IsSupported => false;

    public PlatformWindowHandle? Capture() => null;

    public bool TryRestore(PlatformWindowHandle target) => false;
}
