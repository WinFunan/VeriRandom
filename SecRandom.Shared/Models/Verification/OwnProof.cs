using System.Text.Json.Serialization;

namespace SecRandom.Shared.Models.Verification;

/// <summary>
///     The fork-owned supplementary record that sits beside an Up proof. It is deliberately NOT a second
///     trust anchor: it exists so a verifier can be told "this draw's seed can be checked against an
///     external beacon", and so the reference chain can be shown to predate the Up chain's own time stamp.
///     Its only additions over the Up proof are the beacon pulse index and the seed's monotonic factor
///     inside that pulse.
/// </summary>
public sealed record class OwnProof
{
    public const string FormatId = "verirandom-own-proof/v1";

    [JsonPropertyName("format")]
    public string Format { get; init; } = FormatId;

    // Identifies the Up proof this node refers to. One Own node exists per Up proof.
    [JsonPropertyName("proofId")]
    public Guid ProofId { get; init; }

    [JsonPropertyName("createdAtUtc")]
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("up")]
    public OwnProofUpReference Up { get; init; } = new();

    // The raw published pulse, including its signature, so the reference stays checkable offline. It also
    // carries the seed's increment factor for this pulse (<see cref="DrawProofBeacon.Sequence" />).
    [JsonPropertyName("beacon")]
    public DrawProofBeacon Beacon { get; init; } = new();

    [JsonPropertyName("chain")]
    public OwnProofChain Chain { get; init; } = new();

    // RFC 3161 token over this node's own chain hash. It is requested BEFORE the Up chain's token, which is
    // what lets a verifier see that this reference node was not added after the Up proof was stamped.
    [JsonPropertyName("timestampToken")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TimestampToken { get; init; }
}

/// <summary>
///     Points at the Up chain node this reference node depends on, so the reference cannot be re-pointed at
///     a different draw after the fact.
/// </summary>
public sealed class OwnProofUpReference
{
    [JsonPropertyName("chainIndex")]
    public long ChainIndex { get; init; }

    [JsonPropertyName("chainHash")]
    public string ChainHash { get; init; } = string.Empty;

    [JsonPropertyName("proofHash")]
    public string ProofHash { get; init; } = string.Empty;
}

/// <summary>
///     Position of one reference node inside the fork's own append-only chain. The chain is a reference
///     source only: it is never used to decide whether a draw was fair.
/// </summary>
public sealed class OwnProofChain
{
    [JsonPropertyName("formatVersion")]
    public int FormatVersion { get; init; } = 1;

    [JsonPropertyName("index")]
    public long Index { get; init; }

    [JsonPropertyName("prevHash")]
    public string PrevHash { get; init; } = string.Empty;

    // Hash over (index, prevHash, Up chain index, Up chain hash, beacon pulse index, beacon sequence).
    [JsonPropertyName("selfHash")]
    public string SelfHash { get; init; } = string.Empty;
}
