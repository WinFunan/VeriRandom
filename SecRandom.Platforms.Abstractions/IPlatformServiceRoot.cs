namespace SecRandom.Platforms.Abstractions;

public interface IPlatformServiceRoot
{
    PlatformKind Kind { get; }

    PlatformCapabilities Capabilities { get; }

    IWindowFeatureService WindowFeatures { get; }

    IRemovableStorageCatalog RemovableStorage { get; }

    IRemovableStorageBindingMarker RemovableStorageBindingMarker { get; }

    IPlatformCameraDeviceCatalog CameraDevices { get; }

    IForegroundWindowService ForegroundWindow { get; }

    /// <summary>
    ///     Courseware-marker probe. Roots without one return the unsupported implementation rather than a silently
    ///     empty sampler, because "the feature is off here" and "no marker was found" must not look alike.
    /// </summary>
    ISlideTriggerScreenProbe ScreenProbe { get; }
}
