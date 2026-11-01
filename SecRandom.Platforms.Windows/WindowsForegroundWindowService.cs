using System;
using System.Runtime.InteropServices;
using SecRandom.Platforms.Abstractions;

namespace SecRandom.Platforms.Windows;

/// <summary>
///     Windows foreground hand-off for the quick-draw window: record who owned the foreground before our own
///     window took it, then hand it back when that window closes.
/// </summary>
/// <remarks>
///     <para>
///         <c>SetForegroundWindow</c> is governed by the foreground lock: a process may only give the foreground
///         to another window while it still owns the foreground itself. That is why the restore runs from the
///         closing window's <c>Closed</c> handler, before the window is gone, and why a refusal is reported as a
///         plain false instead of being retried — from the background there is no reliable way to force it.
///     </para>
///     <para>
///         Deriving the foreground window through the compositor instead of the classic API is not an option
///         here: the target is an external application's top-level window, and the classic handle is what both
///         the query and the hand-back operate on.
///     </para>
/// </remarks>
public sealed class WindowsForegroundWindowService : IForegroundWindowService
{
    /// <summary>Matches the descriptor the other Windows window features use for a top-level handle.</summary>
    public const string HandleDescriptor = "HWND";

    public bool IsSupported => true;

    public PlatformWindowHandle? Capture()
    {
        var handle = GetForegroundWindow();
        return handle == IntPtr.Zero ? null : new PlatformWindowHandle(handle, HandleDescriptor);
    }

    public bool TryRestore(PlatformWindowHandle target)
    {
        if (target.Value == IntPtr.Zero)
            return false;

        return SetForegroundWindow(target.Value);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}
