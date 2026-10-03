using SecRandom.Platforms.Abstractions;

namespace SecRandom.Platforms.Linux;

internal sealed class LinuxRemovableStorageBindingMarker : IRemovableStorageBindingMarker
{
    public string FileName => ".VeriRandom.safety.key";

    public bool TryHide(string path) => true;
}
