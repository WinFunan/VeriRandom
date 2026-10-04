namespace SecRandom.Core.Services.Verification;

/// <summary>
///     How a source proves the pulse it published, which decides what a verifier has to hold beforehand.
/// </summary>
public enum BeaconSourceKind
{
    /// <summary>
    ///     The NIST interoperable randomness beacon. Its pulses are signed by a short-lived leaf certificate
    ///     issued by a public WebPKI CA, so no static public key can be embedded; the identity to require is
    ///     the certificate subject instead.
    /// </summary>
    NistBeaconV2,

    /// <summary>
    ///     A drand chain served by the League of Entropy. Its rounds are threshold BLS signatures under one
    ///     long-lived chain public key, so that key can and must be embedded.
    /// </summary>
    DrandChain
}

/// <summary>
///     One selectable external randomness source.
/// </summary>
/// <param name="Id">Stable identifier recorded in the reference node's beacon source list.</param>
/// <param name="PeriodSeconds">Publishing period in seconds. Sources report different units on the wire —
/// the NIST API reports milliseconds — so this field is always seconds.</param>
/// <param name="GenesisTimeUnixSeconds">First round's publication time; drand round numbers are relative to it.</param>
/// <param name="VerificationKey">
///     Embedded verification material: the chain public key for a drand chain. Empty for a source whose key
///     cannot be embedded, which is why <paramref name="ExpectedCertificateSubject" /> exists.
/// </param>
/// <param name="ExpectedCertificateSubject">
///     Certificate subject a pulse signature must be bound to, for sources that rotate their signing
///     certificate. Null when the embedded key is authoritative.
/// </param>
public sealed record BeaconSource(
    string Id,
    string ProviderId,
    BeaconSourceKind Kind,
    int PeriodSeconds,
    long GenesisTimeUnixSeconds,
    string ChainHash,
    string VerificationKey,
    string Endpoint,
    string? ExpectedCertificateSubject);

/// <summary>
///     The built-in list of external randomness sources a user can choose from. Exactly one is active at a
///     time: the seed derivation takes the pulse of a single source, so the choice is a selection rather than
///     a combination.
///     <para>
///         Every entry carries the material needed to verify its pulses, because a source that is fetched over
///         the network and not verified against an embedded key or an exact certificate identity is only as
///         trustworthy as the relay that answered. Embedding the drand chain key is what stops a hostile relay
///         from answering with a pulse signed under a key of its own.
///     </para>
/// </summary>
public static class BeaconSourceCatalog
{
    /// <summary>
    ///     The NIST beacon's signing certificate identity. Its leaf rotates about every six months and is
    ///     issued by a public WebPKI CA, so the identity — not a key — is what a verifier can pin.
    /// </summary>
    public const string NistCertificateSubject = "CN=engine.beacon.nist.gov";

    /// <summary>NIST publishes one pulse per minute.</summary>
    public const int NistPeriodSeconds = 60;

    public static BeaconSource NistBeaconV2 { get; } = new(
        Id: "nist-beacon-v2",
        ProviderId: "nist-beacon-v2",
        Kind: BeaconSourceKind.NistBeaconV2,
        PeriodSeconds: NistPeriodSeconds,
        GenesisTimeUnixSeconds: 0,
        ChainHash: string.Empty,
        VerificationKey: string.Empty,
        Endpoint: BeaconEndpointPolicy.DefaultEndpoint,
        ExpectedCertificateSubject: NistCertificateSubject);

    /// <summary>
    ///     drand's current mainnet chain. A 3-second period divides a NIST minute exactly (20 rounds), and its
    ///     genesis sits on the same 3-second grid as the minute boundary, so the two schedules interleave
    ///     without drifting. Unchained, so any single round verifies from the chain key alone and needs no
    ///     replay of earlier rounds.
    /// </summary>
    public static BeaconSource DrandQuicknet { get; } = new(
        Id: "drand-quicknet",
        ProviderId: "drand",
        Kind: BeaconSourceKind.DrandChain,
        PeriodSeconds: 3,
        GenesisTimeUnixSeconds: 1692803367,
        ChainHash: "52db9ba70e0cc0f6eaf7803dd07447a1f5477735fd3f661792ba94600c84e971",
        VerificationKey:
            "83cf0f2896adee7eb8b5f01fcad3912212c437e0073e911fb90022d3e760183c8c4b450b6a0a6c3ac6a5776a2d1064510d1fec758c921cc22b0e17e63aaf4bcb5ed66304de9cf809bd274ca73bab4af5a6e9c76a4bc09e76eae8991ef5ece45a",
        Endpoint: "https://api.drand.sh/",
        ExpectedCertificateSubject: null);

    public static IReadOnlyList<BeaconSource> All { get; } = [NistBeaconV2, DrandQuicknet];

    /// <summary>Resolves a recorded source id; null for an id this build does not know.</summary>
    public static BeaconSource? Find(string? id) =>
        string.IsNullOrWhiteSpace(id) ? null : All.FirstOrDefault(source => source.Id == id);

    /// <summary>
    ///     Round number published at a given instant, or null when the instant precedes the chain's genesis.
    /// </summary>
    public static long? RoundAt(BeaconSource source, DateTimeOffset instant)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Kind != BeaconSourceKind.DrandChain || source.PeriodSeconds <= 0)
            return null;

        var elapsed = instant.ToUnixTimeSeconds() - source.GenesisTimeUnixSeconds;
        return elapsed < 0 ? null : elapsed / source.PeriodSeconds;
    }

    /// <summary>
    ///     Publication time of a drand round. Used to line a round up with the pulse a proof recorded.
    /// </summary>
    public static DateTimeOffset? RoundTime(BeaconSource source, long round)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Kind != BeaconSourceKind.DrandChain)
            return null;

        return DateTimeOffset.FromUnixTimeSeconds(
            source.GenesisTimeUnixSeconds + (round * source.PeriodSeconds));
    }
}
