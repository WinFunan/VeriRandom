namespace SecRandom.Core.Models.Verification;

/// <summary>
///     One published external randomness pulse (a NIST Beacon v2 pulse, a drand round, or a fetched random.org block).
///     The caller must treat this as the sole entropy input for the draw seed: it carries the published output and,
///     where the source has one, the authority signature over that output, but no local or user-supplied material may
///     be mixed in. <paramref name="OutputValue" /> is whatever width the source publishes — 128 hex characters for
///     NIST's 512 bits, 64 for drand's 256-bit randomness and for a 32-byte random.org fetch — and the derivation
///     sizes itself from it.
/// </summary>
/// <param name="CipherSuite">
///     The authority's cipher suite for this pulse. The canonical byte layout a signature is computed over
///     depends on it, so a verifier must refuse an unknown value instead of assuming one; -1 means the
///     caller did not record it.
/// </param>
public sealed record BeaconPulse(
    long PulseIndex,
    long ChainIndex,
    int PeriodSeconds,
    DateTimeOffset TimeStamp,
    string OutputValue,
    string SignatureValue,
    string CertificateId,
    int CipherSuite = -1,
    string? SignaturePayload = null,
    bool? SignatureVerified = null);
