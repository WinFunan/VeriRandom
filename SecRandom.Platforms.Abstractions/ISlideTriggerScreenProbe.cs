namespace SecRandom.Platforms.Abstractions;

/// <summary>
///     One captured frame of the display the foreground window is on, as raw pixels.
/// </summary>
/// <remarks>
///     Raw BGRA rather than an encoded image or a platform bitmap handle: the decoder lives in the app layer and only
///     needs a luminance array, and a handle here would drag native types across the abstraction boundary.
/// </remarks>
/// <param name="Width">Frame width in pixels.</param>
/// <param name="Height">Frame height in pixels.</param>
/// <param name="StrideBytes">Bytes per row — may exceed <c>Width * 4</c> when the driver pads rows.</param>
/// <param name="Bgra">Top-down 32-bit BGRA pixels.</param>
public sealed record CapturedScreen(int Width, int Height, int StrideBytes, byte[] Bgra);

/// <summary>
///     Answers the two questions a courseware marker needs: is the machine showing a presentation right now, and did
///     the teacher just press something — and it can hand back a frame of that presentation.
/// </summary>
/// <remarks>
///     <para>
///         Split from any capture policy on purpose. This contract is only "what the platform can observe"; when to look
///         (after input, for a bounded burst) and what to do with a decoded marker belong to the app layer, so the
///         classroom timing rules stay testable without a screen.
///     </para>
///     <para>
///         <see cref="IsSupported" /> is false on platforms without an implementation. Callers must then leave the
///         feature off and say so, rather than sampling nothing and reporting "no marker found" — a permanently silent
///         feature is worse than an honestly disabled one.
///     </para>
///     <para>
///         Implementations must never persist a frame, log pixels, or send one anywhere: the frame exists to be decoded
///         in memory and dropped.
///     </para>
/// </remarks>
public interface ISlideTriggerScreenProbe
{
    bool IsSupported { get; }

    /// <summary>
    ///     When keyboard or mouse input last reached the system, or null when unknown. Only the <em>moment</em> is
    ///     observable — never which key, and never any other content of the input.
    /// </summary>
    DateTimeOffset? GetLastInputAt();

    /// <summary>
    ///     True when the foreground window covers a whole display, which is what a slideshow looks like. Used as a gate:
    ///     nothing is captured while it is false, so an ordinary desktop session is never sampled.
    /// </summary>
    bool IsPresentationForeground();

    /// <summary>
    ///     Captures the display that holds the foreground window. Null when the platform refused or the frame is
    ///     unusable.
    /// </summary>
    CapturedScreen? CaptureForegroundDisplay();

    /// <summary>
    ///     Captures every display once, in system order. Empty when unsupported or nothing could be read.
    /// </summary>
    /// <remarks>
    ///     Separate from <see cref="CaptureForegroundDisplay" /> because the two callers have different questions. The
    ///     automatic watcher only ever cares about the slideshow in front, so it asks for that one display and the
    ///     presentation gate keeps it from reading anything else. A <em>manual</em> check is the opposite situation: the
    ///     teacher has just clicked a button in this application, so the foreground is our own settings window — asking
    ///     for the foreground display there either refuses outright or captures the wrong monitor on a dual-screen
    ///     classroom machine. Reading each display in turn answers "is there a marker on any of my screens", which is
    ///     what the button actually means.
    /// </remarks>
    IReadOnlyList<CapturedScreen> CaptureAllDisplays();
}
