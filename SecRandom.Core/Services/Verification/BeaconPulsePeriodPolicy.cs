using System;

namespace SecRandom.Core.Services.Verification;

/// <summary>
///     How a recorded beacon pulse lines up with the period in which the proof was time-stamped.
/// </summary>
public enum BeaconPeriodMatch
{
    /// <summary>The pulse is the one published in the period the proof was stamped in.</summary>
    CurrentPeriod,

    /// <summary>
    ///     The pulse belongs to an earlier period, but is still inside the accepted tolerance. A draw is
    ///     stamped a little after it fetched its pulse, so one period of slack is expected and accepted.
    /// </summary>
    PreviousPeriodWithinTolerance,

    /// <summary>The pulse is too old, or is newer than the stamp, so it cannot be this proof's pulse.</summary>
    OutsideTolerance,

    /// <summary>The pulse carries no usable period information.</summary>
    Undetermined
}

/// <summary>
///     The preset a verifier applies when it has no beacon field to read: the proof's time stamp fixes the
///     period, and the pulse must be that period's pulse (or the previous one, within tolerance). This is why
///     the Up proof does not need to carry the beacon at all — it is re-derived from the published pulse.
/// </summary>
public static class BeaconPulsePeriodPolicy
{
    /// <summary>One period of slack: the pulse may be the previous period's instead of the current one.</summary>
    public const int TolerancePeriods = 1;

    /// <summary>
    ///     The client clock is not authoritative and the authority answers a little late, so a pulse that
    ///     looks marginally newer than the stamp is still accepted rather than reported as a substitution.
    /// </summary>
    public static readonly TimeSpan ClockSkewAllowance = TimeSpan.FromSeconds(60);

    public static BeaconPeriodMatch Match(
        DateTimeOffset stampedAtUtc,
        DateTimeOffset pulseTimeStampUtc,
        int periodSeconds,
        int tolerancePeriods = TolerancePeriods)
    {
        if (periodSeconds <= 0 || tolerancePeriods < 0)
            return BeaconPeriodMatch.Undetermined;

        var age = stampedAtUtc - pulseTimeStampUtc;
        if (age < -ClockSkewAllowance)
            return BeaconPeriodMatch.OutsideTolerance;

        if (age < TimeSpan.FromSeconds(periodSeconds))
            return BeaconPeriodMatch.CurrentPeriod;

        var periods = Math.Floor(age.TotalSeconds / periodSeconds);
        return periods <= tolerancePeriods
            ? BeaconPeriodMatch.PreviousPeriodWithinTolerance
            : BeaconPeriodMatch.OutsideTolerance;
    }

    public static bool IsWithinTolerance(BeaconPeriodMatch match) =>
        match is BeaconPeriodMatch.CurrentPeriod or BeaconPeriodMatch.PreviousPeriodWithinTolerance;
}
