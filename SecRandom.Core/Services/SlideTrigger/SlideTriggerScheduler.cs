using System;
using SecRandom.Core.Models.SlideTrigger;

namespace SecRandom.Core.Services.SlideTrigger;

/// <summary>What the app should do with a marker it has already detected and armed.</summary>
public enum SlideTriggerDecision
{
    /// <summary>Stay armed; neither condition is met yet.</summary>
    Wait,

    /// <summary>Fire now.</summary>
    Fire,

    /// <summary>Disarm without firing: the armed window ran out while waiting for input.</summary>
    Expired
}

/// <summary>
///     Decides when an armed marker fires. Pure, so the timing rules can be reasoned about (and tested) without a
///     screen, a clicker, or a window.
/// </summary>
/// <remarks>
///     <para>
///         Detection and firing are separate moments on purpose. A marker is <em>detected</em> when the app reads it
///         off the projected slide; it <em>fires</em> when the teacher's intent and the content are both settled. For a
///         clicker user those are almost never the same instant: the press that reveals slide 12 is also the press that
///         starts its entrance animation, so firing immediately draws while the slide is still moving.
///     </para>
///     <para>
///         <see cref="SlideTriggerTiming.AfterInputs" /> therefore waits for the teacher's <em>next</em> presses, and
///         <see cref="SlideTriggerParameters.ArmedTimeoutSeconds" /> bounds that wait. Without the bound, a marker armed
///         on slide 12 would still be waiting twenty minutes later and fire on some unrelated click — the one failure
///         mode of this whole feature that a classroom would actually notice.
///     </para>
/// </remarks>
public static class SlideTriggerScheduler
{
    public static SlideTriggerDecision Evaluate(
        SlideTriggerParameters parameters,
        TimeSpan sinceDetection,
        int inputsSinceDetection)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        var elapsed = sinceDetection < TimeSpan.Zero ? TimeSpan.Zero : sinceDetection;
        var inputs = Math.Max(0, inputsSinceDetection);

        return parameters.Timing switch
        {
            SlideTriggerTiming.AfterInputs => EvaluateInputs(parameters, elapsed, inputs),
            _ => elapsed.TotalSeconds >= parameters.DelaySeconds
                ? SlideTriggerDecision.Fire
                : SlideTriggerDecision.Wait
        };
    }

    private static SlideTriggerDecision EvaluateInputs(
        SlideTriggerParameters parameters,
        TimeSpan elapsed,
        int inputs)
    {
        // The input condition wins over the timeout: a teacher who presses the clicker exactly as the window closes
        // gets the draw they asked for, rather than a silently dropped marker.
        if (inputs >= Math.Max(1, parameters.TriggerAfterInputs))
            return SlideTriggerDecision.Fire;

        return elapsed.TotalSeconds >= parameters.ArmedTimeoutSeconds
            ? SlideTriggerDecision.Expired
            : SlideTriggerDecision.Wait;
    }
}
