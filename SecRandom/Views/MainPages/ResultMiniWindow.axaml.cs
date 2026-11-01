using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using SecRandom.ViewModels.MainPages;

namespace SecRandom.Views.MainPages;

/// <summary>
///     Borderless topmost window that shows the current roll-call result while the main window steps aside, so a
///     classroom can keep the courseware on screen and still see who answers next.
/// </summary>
/// <remarks>
///     <para>
///         It hosts the same <see cref="RollCallResultPresenter" /> over the Host's singleton
///         <see cref="RollCallPageViewModel" />, so nothing is copied: the result, its display settings, and its
///         animation all stay the shared session's. Closing always means "go back to the main window", because leaving
///         the app as a floating card with no window is a worse trap than an extra click.
///     </para>
///     <para>
///         <b>Two input paths, deliberately.</b> A mouse uses the platform's own <c>BeginMoveDrag</c>/<c>BeginResizeDrag</c>
///         loops — that is what gives multi-monitor edge behaviour and the system drag feel. A <em>touch</em> contact
///         cannot use them: those loops are mouse-shaped, so a finger press on the old code did nothing at all. Touch
///         therefore moves and resizes by hand, which also makes it testable reasoning.
///     </para>
///     <para>
///         <b>One pointer at a time.</b> The window crashed when a second finger landed during a drag: both presses
///         entered the native move loop, and re-entering it is not a supported thing to do. Every entry point now checks
///         <see cref="_manualPointer" /> first, so a second contact is ignored until the first is released.
///     </para>
/// </remarks>
public sealed partial class ResultMiniWindow : Window
{
    private const double FallbackMinimumWidth = 240;
    private const double FallbackMinimumHeight = 220;

    private readonly Action _restore;
    private bool _allowClose;

    /// <summary>The touch contact currently moving or resizing the window; null when nothing is being dragged.</summary>
    private IPointer? _manualPointer;

    private WindowEdge _manualEdge;
    private bool _isManuallyResizing;
    private Point _startPointer;
    private PixelPoint _startScreenPointer;
    private PixelPoint _startPosition;
    private Size _startSize;

    public ResultMiniWindow(RollCallPageViewModel viewModel, Action restore, double width, double height)
    {
        _restore = restore;
        DataContext = viewModel;

        Width = width;
        Height = height;

        InitializeComponent();

        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Closing += OnClosing;

        ConfigureResizeGrips();

        // Capturing the pointer to the window makes every subsequent move/release land here, even when the finger
        // slides off the card — which is exactly what a drag out to the edge of a large screen does.
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;
        PointerCaptureLost += OnPointerCaptureLost;
    }

    internal void AllowClose() => _allowClose = true;

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_allowClose || e.CloseReason is WindowCloseReason.ApplicationShutdown or WindowCloseReason.OSShutdown)
            return;

        e.Cancel = true;
        _restore();
    }

    private void Restore(object? sender, RoutedEventArgs e) => _restore();

    // 拖动沿用计时器小窗的做法：整块可拖动，但按在按钮上时交给按钮。
    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!IsPrimaryContact(e) || _manualPointer is not null || IsButtonDescendant(e.Source as Visual))
            return;

        if (e.Pointer.Type != PointerType.Touch)
        {
            // Mouse/pen: the platform loop is better than anything reimplemented here.
            BeginMoveDrag(e);
            return;
        }

        StartManualGesture(e, WindowEdge.West, isResizing: false);
    }

    /// <summary>
    ///     One grip per window edge and corner. A mouse uses the platform's resize loop; a touch contact resizes by hand
    ///     for the same reason it moves by hand.
    /// </summary>
    private void ResizeGrip_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { Tag: string edgeName } || !IsPrimaryContact(e))
            return;

        // The second-finger case that used to crash the window: re-entering a resize loop is not allowed.
        if (_manualPointer is not null)
            return;

        if (!Enum.TryParse<WindowEdge>(edgeName, out var edge))
            return;

        e.Handled = true;

        if (e.Pointer.Type != PointerType.Touch)
        {
            BeginResizeDrag(edge, e);
            return;
        }

        StartManualGesture(e, edge, isResizing: true);
    }

    private void StartManualGesture(PointerPressedEventArgs e, WindowEdge edge, bool isResizing)
    {
        _manualPointer = e.Pointer;
        _manualEdge = edge;
        _isManuallyResizing = isResizing;
        _startPointer = e.GetPosition(this);

        // Screen space, not window space. A window-relative measurement is self-referential once the window starts
        // moving: every applied move removes exactly as much from the next delta, so the window forever lags the finger,
        // the finger visibly creeps across the card, and the whole gesture reads as if it were scaled. Converting the
        // client point through the window origin gives an absolute screen position, so the deltas below are the finger's
        // real travel.
        _startScreenPointer = ToScreenPixels(_startPointer);

        _startPosition = Position;
        _startSize = new Size(Width, Height);

        e.Pointer.Capture(this);
        e.Handled = true;
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_manualPointer is null || !ReferenceEquals(e.Pointer, _manualPointer))
            return;

        var screenPointer = ToScreenPixels(e.GetPosition(this));
        var deltaPixelsX = screenPointer.X - _startScreenPointer.X;
        var deltaPixelsY = screenPointer.Y - _startScreenPointer.Y;

        if (_isManuallyResizing)
        {
            // Size is expressed in DIPs while the pointer moved in physical pixels, so the resize math needs the
            // scaled delta — the same conversion the window position needs in the other direction.
            var scaling = RenderScaling <= 0 ? 1 : RenderScaling;
            ApplyManualResize(deltaPixelsX / scaling, deltaPixelsY / scaling, scaling);
        }
        else
        {
            // Skip redundant moves: a touch panel reports well over 100 events per second, and re-posting the same
            // window position on every one of them makes the card shimmer even when the finger is still.
            var target = new PixelPoint(
                _startPosition.X + deltaPixelsX,
                _startPosition.Y + deltaPixelsY);

            if (target != Position)
                Position = target;
        }

        e.Handled = true;
    }

    /// <summary>
    ///     Applies the drag to whichever sides the grab belongs to. Pulling a west or north edge has to move the window
    ///     as well as grow it, otherwise the edge being dragged stays put and the card slides instead.
    /// </summary>
    /// <remarks>
    ///     The size arithmetic is in DIPs, but <see cref="Window.Position" /> is in physical pixels: a west/north edge
    ///     has to convert the size change back through <paramref name="scaling" /> or the card jumps by the scaling
    ///     factor the moment the finger touches a 150% or 200% display.
    /// </remarks>
    private void ApplyManualResize(double deltaX, double deltaY, double scaling)
    {
        var minimumWidth = MinWidth > 0 ? MinWidth : FallbackMinimumWidth;
        var minimumHeight = MinHeight > 0 ? MinHeight : FallbackMinimumHeight;

        var width = _startSize.Width;
        var height = _startSize.Height;
        var x = _startPosition.X;
        var y = _startPosition.Y;

        var edge = _manualEdge;

        if (edge is WindowEdge.East or WindowEdge.NorthEast or WindowEdge.SouthEast)
            width = Math.Max(minimumWidth, _startSize.Width + deltaX);
        else if (edge is WindowEdge.West or WindowEdge.NorthWest or WindowEdge.SouthWest)
        {
            var adjusted = Math.Max(minimumWidth, _startSize.Width - deltaX);
            x = _startPosition.X + (int)Math.Round((_startSize.Width - adjusted) * scaling);
            width = adjusted;
        }

        if (edge is WindowEdge.South or WindowEdge.SouthWest or WindowEdge.SouthEast)
            height = Math.Max(minimumHeight, _startSize.Height + deltaY);
        else if (edge is WindowEdge.North or WindowEdge.NorthWest or WindowEdge.NorthEast)
        {
            var adjusted = Math.Max(minimumHeight, _startSize.Height - deltaY);
            y = _startPosition.Y + (int)Math.Round((_startSize.Height - adjusted) * scaling);
            height = adjusted;
        }

        Width = width;
        Height = height;
        Position = new PixelPoint(x, y);
    }

    /// <summary>
    ///     Converts a point in the window's client area (DIPs) to screen pixels.
    /// </summary>
    /// <remarks>
    ///     The window origin is read fresh on every call. Because both halves are taken from the same instant — the
    ///     window's current origin and a client point measured against that same window — the sum is the pointer's real
    ///     screen position, which is what makes the delta below immune to our own movement.
    /// </remarks>
    private PixelPoint ToScreenPixels(Point clientPoint)
    {
        var scaling = RenderScaling <= 0 ? 1 : RenderScaling;
        var origin = Position;
        return new PixelPoint(
            origin.X + (int)Math.Round(clientPoint.X * scaling),
            origin.Y + (int)Math.Round(clientPoint.Y * scaling));
    }

    private void OnPointerReleased(object? sender, PointerEventArgs e)
    {
        if (_manualPointer is null || !ReferenceEquals(e.Pointer, _manualPointer))
            return;

        e.Pointer.Capture(null);
        ClearManualGesture();
    }

    /// <summary>
    ///     Capture can also be lost without a release — another window activating is enough. Leaving the drag state set
    ///     would make the card permanently un-draggable until restart, so it is cleared here as well.
    /// </summary>
    private void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e) => ClearManualGesture();

    private void ClearManualGesture()
    {
        _manualPointer = null;
        _isManuallyResizing = false;
    }

    /// <summary>
    ///     A finger press reports no "left button", so a mouse-only check silently disabled every gesture on the very
    ///     screens this card is made for.
    /// </summary>
    private bool IsPrimaryContact(PointerPressedEventArgs e) =>
        e.Pointer.Type == PointerType.Touch
        || e.GetCurrentPoint(this).Properties.IsLeftButtonPressed;

    private void ConfigureResizeGrips()
    {
        foreach (var grip in ResizeGrips.Children.OfType<Control>().Where(control => control.Tag is string))
        {
            grip.Cursor = new Cursor(CursorFor((string)grip.Tag!));
        }
    }

    private static StandardCursorType CursorFor(string edgeName) => edgeName switch
    {
        "North" or "South" => StandardCursorType.SizeNorthSouth,
        "West" or "East" => StandardCursorType.SizeWestEast,
        "NorthWest" => StandardCursorType.TopLeftCorner,
        "NorthEast" => StandardCursorType.TopRightCorner,
        "SouthWest" => StandardCursorType.BottomLeftCorner,
        "SouthEast" => StandardCursorType.BottomRightCorner,
        _ => StandardCursorType.Arrow
    };

    private static bool IsButtonDescendant(Visual? visual)
    {
        while (visual is not null)
        {
            if (visual is Button)
                return true;
            visual = visual.GetVisualParent();
        }

        return false;
    }
}
