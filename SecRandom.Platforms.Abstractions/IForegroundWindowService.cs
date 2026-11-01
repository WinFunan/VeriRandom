namespace SecRandom.Platforms.Abstractions;

/// <summary>
///     Remembers which window currently owns the foreground and can later give it back.
/// </summary>
/// <remarks>
///     <para>
///         Deliberately stateful, which is why it is not expressed as a
///         <see cref="WindowFeatureRequest" /> flag: a window feature applies a property to one handle, while
///         this has to carry a <em>different</em> window across time — the one that was in front before our own
///         window took the foreground.
///     </para>
///     <para>
///         Used by the quick-draw window: the operator is usually typing in another application when they press
///         the shortcut, and the draw window is topmost and takes focus, so without this the classroom loses the
///         cursor position it had. Platforms that cannot do it report <see cref="IsSupported" /> as false and
///         the calling setting must stay inert there rather than pretend.
///     </para>
/// </remarks>
public interface IForegroundWindowService
{
    /// <summary>
    ///     False when the platform has no implementation. Callers must not claim the feature works, and any
    ///     user-facing option that depends on it must say so instead of silently doing nothing.
    /// </summary>
    bool IsSupported { get; }

    /// <summary>
    ///     Handle of the window that owns the foreground right now, or null when there is none. Must be called
    ///     while that window is still in front — in particular before our own window is shown and before any
    ///     dialog that can activate another window of ours.
    /// </summary>
    PlatformWindowHandle? Capture();

    /// <summary>
    ///     Brings a previously captured window back to the foreground. Returns false when it could not be done,
    ///     which is a normal outcome: the target may have been closed in the meantime, and some platforms only
    ///     allow the switch while the caller still owns the foreground.
    /// </summary>
    bool TryRestore(PlatformWindowHandle target);
}
