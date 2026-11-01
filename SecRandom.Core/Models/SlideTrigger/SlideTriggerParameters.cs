namespace SecRandom.Core.Models.SlideTrigger;

/// <summary>
///     When a detected courseware marker is allowed to fire.
/// </summary>
public enum SlideTriggerTiming
{
    /// <summary>
    ///     Fire once <see cref="SlideTriggerParameters.DelaySeconds" /> has passed. This is the plain case: the teacher
    ///     turns to a slide and the draw starts by itself.
    /// </summary>
    AfterDelay,

    /// <summary>
    ///     Wait for the next keyboard/mouse event(s) instead of a timer. This exists because a clicker cannot click:
    ///     a slide often reveals its content in steps, and the teacher's next press is the moment they actually mean
    ///     "now". Waiting for input is also how a marker placed on an animated slide avoids firing mid-animation.
    /// </summary>
    AfterInputs
}

/// <summary>
///     Everything one courseware marker should do when it fires.
/// </summary>
/// <remarks>
///     <para>
///         These live in the app, not in the marker image. The marker carries only an id, which means editing
///         "draw three instead of one" never requires regenerating and re-inserting a PNG into a deck — and it keeps
///         the code small enough to decode reliably from a screenshot.
///     </para>
///     <para>
///         Roll-call scoped for now. A later lottery target can be added without touching the marker format, which is
///         exactly why the marker holds an id rather than the parameters.
///     </para>
/// </remarks>
public sealed class SlideTriggerParameters
{
    public const int MaximumCount = 50;
    public const double MaximumDelaySeconds = 10;
    public const int MaximumTriggerAfterInputs = 5;
    public const double MaximumArmedTimeoutSeconds = 60;

    /// <summary>Student list to draw from; null or empty means the currently active list.</summary>
    public string? StudentListName { get; set; }

    public int Count { get; set; } = 1;

    /// <summary>
    ///     Allow drawing a student who was already drawn. This is the repeat-control off switch; it does not clear
    ///     history or temporary records, which stays a separate manual action.
    /// </summary>
    public bool AllowRepeat { get; set; }

    /// <summary>
    ///     Ignore the fair-draw weighting (history count, interval, group/gender balance) and draw uniformly. It is a
    ///     separate switch from <see cref="AllowRepeat" /> on purpose: "don't avoid the same person" and "don't weight
    ///     by history" are different classroom intents, and a draw that ignores weights while still honouring repeat
    ///     control is a legitimate combination.
    /// </summary>
    public bool IgnoreHistoryWeights { get; set; }

    public SlideTriggerTiming Timing { get; set; } = SlideTriggerTiming.AfterDelay;

    /// <summary>Seconds between detection and firing when <see cref="Timing" /> is <see cref="SlideTriggerTiming.AfterDelay" />.</summary>
    public double DelaySeconds { get; set; } = 1.0;

    /// <summary>How many further input events to wait for when <see cref="Timing" /> is <see cref="SlideTriggerTiming.AfterInputs" />.</summary>
    public int TriggerAfterInputs { get; set; } = 1;

    /// <summary>
    ///     How long a detected marker stays armed while waiting for those inputs. Without this bound an armed marker
    ///     would fire on some unrelated press minutes later, which is worse than not firing at all.
    /// </summary>
    public double ArmedTimeoutSeconds { get; set; } = 15;

    public bool ShowAnimation { get; set; } = true;

    public bool VoiceEnabled { get; set; } = true;

    /// <summary>Optional gender restriction; null or empty means no restriction.</summary>
    public string? Gender { get; set; }

    /// <summary>Optional group restriction; null or empty means no restriction.</summary>
    public string? Group { get; set; }

    /// <summary>Clamps every value into its supported range and trims blank filters to null.</summary>
    public SlideTriggerParameters Normalize() => new()
    {
        StudentListName = string.IsNullOrWhiteSpace(StudentListName) ? null : StudentListName.Trim(),
        Count = Math.Clamp(Count, 1, MaximumCount),
        AllowRepeat = AllowRepeat,
        IgnoreHistoryWeights = IgnoreHistoryWeights,
        Timing = Timing,
        DelaySeconds = double.IsFinite(DelaySeconds)
            ? Math.Clamp(DelaySeconds, 0, MaximumDelaySeconds)
            : 1.0,
        TriggerAfterInputs = Math.Clamp(TriggerAfterInputs, 1, MaximumTriggerAfterInputs),
        ArmedTimeoutSeconds = double.IsFinite(ArmedTimeoutSeconds)
            ? Math.Clamp(ArmedTimeoutSeconds, 1, MaximumArmedTimeoutSeconds)
            : 15,
        ShowAnimation = ShowAnimation,
        VoiceEnabled = VoiceEnabled,
        Gender = string.IsNullOrWhiteSpace(Gender) ? null : Gender.Trim(),
        Group = string.IsNullOrWhiteSpace(Group) ? null : Group.Trim()
    };
}
