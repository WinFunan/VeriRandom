using SecRandom.Core.Models.Verification;

namespace SecRandom.Core.Services.Verification;

/// <summary>
///     Persisted beacon consumption position: which pulse was last used and the last sequence number
///     consumed inside that pulse.
/// </summary>
public readonly record struct BeaconSequenceState(long PulseIndex, long ChainIndex, string OutputValue, long Sequence);

public enum BeaconSequenceFailure
{
    None,

    /// <summary>The fetched pulse is older than the stored one; the anchor must never move backwards.</summary>
    PulseIndexRegression,

    /// <summary>The same pulse index published a different output value, so the pulse cannot be trusted.</summary>
    PulseValueConflict
}

/// <summary>
///     Resolves the next per-pulse sequence number. The value is forced to start at 0 for every new pulse
///     and then increases by exactly one per draw, so an operator cannot skip a prefix and choose a
///     favourable starting point. A gap is only ever visible as a missing sequence, never as a reused one.
/// </summary>
public static class BeaconSequencePolicy
{
    public static long ResolveNext(BeaconSequenceState? state, BeaconPulse pulse, out BeaconSequenceFailure failure)
    {
        ArgumentNullException.ThrowIfNull(pulse);
        failure = BeaconSequenceFailure.None;

        if (state is not { } current)
            return 0;

        if (pulse.PulseIndex > current.PulseIndex)
            return 0;

        if (pulse.PulseIndex < current.PulseIndex)
        {
            failure = BeaconSequenceFailure.PulseIndexRegression;
            return -1;
        }

        if (!string.Equals(pulse.OutputValue, current.OutputValue, StringComparison.OrdinalIgnoreCase))
        {
            failure = BeaconSequenceFailure.PulseValueConflict;
            return -1;
        }

        return checked(current.Sequence + 1);
    }
}
