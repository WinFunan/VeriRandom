namespace SecRandom.Core.Models.Verification;

/// <summary>
///     One published external randomness pulse (currently a NIST Beacon v2 pulse). The caller must treat
///     this as the sole entropy input for the draw seed: it carries the published output and the authority
///     signature over that output, but no local or user-supplied material may be mixed in.
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
    int CipherSuite = -1);
