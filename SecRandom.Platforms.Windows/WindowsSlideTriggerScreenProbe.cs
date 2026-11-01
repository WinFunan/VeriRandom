using System.Runtime.InteropServices;
using SecRandom.Platforms.Abstractions;

namespace SecRandom.Platforms.Windows;

/// <summary>
///     Windows implementation of the courseware-marker probe: last-input time, "is a slideshow in front", and a frame
///     of that slideshow's display.
/// </summary>
/// <remarks>
///     <para>
///         <c>GetLastInputInfo</c> is the whole reason this feature can exist without a keyboard hook: it reports only
///         <em>when</em> input last happened, never what it was, needs no elevation, and does not look like a keylogger
///         to endpoint protection. A hook would have given per-key detail nobody needs here.
///     </para>
///     <para>
///         The slide is captured from the screen device context rather than through any window handle: a slideshow is
///         usually a borderless window that may even be excluded from capture APIs, while what the class actually sees is
///         the framebuffer. Capturing the framebuffer is also exactly the surface the projector shows, which is the
///         privacy argument for capturing it at all.
///     </para>
/// </remarks>
public sealed class WindowsSlideTriggerScreenProbe : ISlideTriggerScreenProbe
{
    private const int MonitorDefaultToNull = 0;
    private const int MonitorDefaultToPrimary = 1;
    private const int MonitorDefaultToNearest = 2;

    private const int Srccopy = 0x00CC0020;
    private const int Captureblt = 0x40000000;
    private const uint DibRgbColors = 0;

    public bool IsSupported => true;

    public DateTimeOffset? GetLastInputAt()
    {
        var info = new LastInputInfo { cbSize = (uint)Marshal.SizeOf<LastInputInfo>() };
        if (!GetLastInputInfo(ref info))
            return null;

        // dwTime is a 32-bit tick count, so the subtraction has to wrap in 32 bits and then widen.
        var idle = unchecked((uint)Environment.TickCount - info.dwTime);
        return DateTimeOffset.UtcNow - TimeSpan.FromMilliseconds(idle);
    }

    public bool IsPresentationForeground()
    {
        var window = GetForegroundWindow();
        if (window == IntPtr.Zero)
            return false;

        if (!GetWindowRect(window, out var windowRect) || !TryGetMonitorRect(window, out var monitorRect))
            return false;

        // A slideshow covers its display completely. A window merely maximised inside the work area leaves the taskbar
        // visible and therefore fails this test, which is the behaviour we want: the sampling gate should be narrow.
        return windowRect.Left <= monitorRect.Left
               && windowRect.Top <= monitorRect.Top
               && windowRect.Right >= monitorRect.Right
               && windowRect.Bottom >= monitorRect.Bottom;
    }

    public CapturedScreen? CaptureForegroundDisplay()
    {
        var window = GetForegroundWindow();
        if (!TryGetMonitorRect(window, out var rect))
            return null;

        return CaptureRect(rect);
    }

    /// <summary>
    ///     Reads every display once. Used by the manual check, where the foreground display is the monitor showing our
    ///     own settings window rather than the projector.
    /// </summary>
    public IReadOnlyList<CapturedScreen> CaptureAllDisplays()
    {
        // A delegate receiving `ref` parameters cannot be a lambda, so the rects are collected through this instance
        // method. The manual check is a single foreground action, never called concurrently.
        _monitorRects = [];
        try
        {
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, CollectMonitorRect, IntPtr.Zero);

            if (_monitorRects.Count == 0 && TryGetMonitorRect(GetForegroundWindow(), out var fallback))
                _monitorRects.Add(fallback);

            var frames = new List<CapturedScreen>(_monitorRects.Count);
            foreach (var rect in _monitorRects)
                if (CaptureRect(rect) is { } frame)
                    frames.Add(frame);

            return frames;
        }
        finally
        {
            _monitorRects = null;
        }
    }

    private List<Rect>? _monitorRects;

    private bool CollectMonitorRect(IntPtr hMonitor, IntPtr hdcMonitor, ref Rect monitorRect, IntPtr data)
    {
        _monitorRects?.Add(monitorRect);
        return true;
    }

    private static CapturedScreen? CaptureRect(Rect rect)
    {
        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        if (width <= 0 || height <= 0)
            return null;

        var screenDc = GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero)
            return null;

        var memoryDc = IntPtr.Zero;
        var bitmap = IntPtr.Zero;
        var previous = IntPtr.Zero;

        try
        {
            memoryDc = CreateCompatibleDC(screenDc);
            bitmap = CreateCompatibleBitmap(screenDc, width, height);
            if (memoryDc == IntPtr.Zero || bitmap == IntPtr.Zero)
                return null;

            previous = SelectObject(memoryDc, bitmap);

            // CAPTUREBLT so layered windows (which is how many presentation tools compose their output) are included.
            if (!BitBlt(memoryDc, 0, 0, width, height, screenDc, rect.Left, rect.Top, Srccopy | Captureblt))
                return null;

            return ReadPixels(memoryDc, bitmap, width, height);
        }
        finally
        {
            if (memoryDc != IntPtr.Zero && previous != IntPtr.Zero)
                SelectObject(memoryDc, previous);
            if (bitmap != IntPtr.Zero)
                DeleteObject(bitmap);
            if (memoryDc != IntPtr.Zero)
                DeleteDC(memoryDc);
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    private static CapturedScreen? ReadPixels(IntPtr memoryDc, IntPtr bitmap, int width, int height)
    {
        var stride = width * 4;
        var buffer = new byte[stride * height];

        var header = new BitmapInfoHeader
        {
            biSize = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
            biWidth = width,
            // Negative height requests top-down rows, which is what the decoder expects.
            biHeight = -height,
            biPlanes = 1,
            biBitCount = 32,
            biCompression = 0,
            biSizeImage = (uint)buffer.Length
        };

        var info = new BitmapInfo { bmiHeader = header };

        var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try
        {
            var lines = GetDIBits(memoryDc, bitmap, 0, (uint)height, handle.AddrOfPinnedObject(), ref info, DibRgbColors);
            if (lines == 0)
                return null;
        }
        finally
        {
            handle.Free();
        }

        return new CapturedScreen(width, height, stride, buffer);
    }

    private static bool TryGetMonitorRect(IntPtr window, out Rect rect)
    {
        rect = default;

        var monitor = window != IntPtr.Zero
            ? MonitorFromWindow(window, MonitorDefaultToNearest)
            : MonitorFromPoint(default, MonitorDefaultToPrimary);

        if (monitor == IntPtr.Zero || monitor == new IntPtr(MonitorDefaultToNull + 1))
            return false;

        var info = new MonitorInfo { cbSize = (uint)Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info))
            return false;

        rect = info.rcMonitor;
        return true;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint cbSize;
        public uint dwTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public uint cbSize;
        public Rect rcMonitor;
        public Rect rcWork;
        public uint dwFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public BitmapInfoHeader bmiHeader;
        public uint bmiColors;
    }

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LastInputInfo plii);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out Rect lpRect);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(Point pt, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfo lpmi);

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref Rect lprcMonitor, IntPtr dwData);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(
        IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int width, int height);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(
        IntPtr hdcDest, int xDest, int yDest, int width, int height, IntPtr hdcSrc, int xSrc, int ySrc, int rop);

    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(
        IntPtr hdc, IntPtr hbm, uint start, uint lines, IntPtr bits, ref BitmapInfo info, uint usage);
}
