using System;
using System.Threading;
using System.Threading.Tasks;
using SecRandom.Core.Models.Verification;
using SecRandom.Shared.Models.Verification;

namespace SecRandom.Services.Verification;

public interface INistBeaconClient
{
    /// <summary>
    ///     Fetches the most recently published pulse from <paramref name="endpoint" />. Failures are
    ///     surfaced as exceptions; callers must never fall back to a local seed.
    /// </summary>
    Task<BeaconPulse> GetLatestPulseAsync(Uri endpoint, CancellationToken cancellationToken);
}

/// <summary>
///     One reserved beacon seed. The sequence is already persisted by the provider, so a failed draw
///     leaves a visible gap instead of allowing the same seed to be used twice.
/// </summary>
public sealed record BeaconSeedReservation(byte[] Seed, DrawProofBeacon Beacon);

/// <summary>
///     Raised when the beacon is unavailable or its sequence anchor is inconsistent. It is deliberately
///     not recoverable into a local draw: substituting local entropy would defeat this path entirely.
/// </summary>
public sealed class BeaconEntropyException : Exception
{
    public BeaconEntropyException(string message)
        : base(message)
    {
    }

    public BeaconEntropyException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
