using System.Text.Json.Serialization;

namespace SecRandom.Shared.Models.Verification;

/// <summary>
///     A self-contained, privacy-preserving draw record. Candidate display data deliberately stays outside this contract.
/// </summary>
public sealed record class DrawProof
{
    [JsonPropertyName("format")]
    public string Format { get; init; } = "secrandom-draw-proof/v1";

    [JsonPropertyName("proofId")]
    public Guid ProofId { get; init; } = Guid.NewGuid();

    [JsonPropertyName("parentProofId")]
    public Guid? ParentProofId { get; init; }

    [JsonPropertyName("mode")]
    public VerificationProofMode Mode { get; init; }

    [JsonPropertyName("createdAtUtc")]
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("algorithmId")]
    public string AlgorithmId { get; init; } = string.Empty;

    [JsonPropertyName("algorithmEngineVersion")]
    public string AlgorithmEngineVersion { get; init; } = string.Empty;

    // Keeps deserializing v1 proof files while new exports use algorithmEngineVersion.
    [JsonPropertyName("kernelVersion")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LegacyKernelVersion { get; init; }

    [JsonPropertyName("inputHash")]
    public string InputHash { get; init; } = string.Empty;

    [JsonPropertyName("payload")]
    public string Payload { get; init; } = string.Empty;

    // JSON bytes encoded as Base64Url. It contains anonymous candidate/history evidence only.
    [JsonPropertyName("auditPayload")]
    public string AuditPayload { get; init; } = string.Empty;

    [JsonPropertyName("result")]
    public DrawProofResult Result { get; init; } = new();

    // Local append-only evidence chain. Absent on proofs written before the chain existed (and on
    // proofs whose chain could not be advanced); such files are reported as "unchained" instead of broken.
    [JsonPropertyName("chain")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DrawProofChain? Chain { get; init; }

    // Optional external-entropy anchor (for example a NIST Beacon pulse). It deliberately stays outside
    // WitnessClient.ComputeAttestedProofHash: the server recomputes that hash from a fixed field list, so
    // adding a field there would invalidate every receipt. The seed this anchor produced is committed
    // through the payload, so a substituted pulse is still detectable by replaying the payload seed.
    [JsonPropertyName("beacon")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DrawProofBeacon? Beacon { get; init; }

    [JsonPropertyName("witness")]
    public DrawProofWitness? Witness { get; init; }
}

/// <summary>
///     The external randomness pulse a proof's seed was derived from. It records the raw published pulse,
///     including its signature, so a verifier can independently re-check the pulse against the authority
///     even when the pulse was fetched through a mirror endpoint.
/// </summary>
public sealed class DrawProofBeacon
{
    [JsonPropertyName("provider")]
    public string Provider { get; init; } = "nist-beacon-v2";

    [JsonPropertyName("endpoint")]
    public string Endpoint { get; init; } = string.Empty;

    [JsonPropertyName("pulseIndex")]
    public long PulseIndex { get; init; }

    [JsonPropertyName("chainIndex")]
    public long ChainIndex { get; init; }

    [JsonPropertyName("periodSeconds")]
    public int PeriodSeconds { get; init; }

    [JsonPropertyName("pulseTimeStamp")]
    public DateTimeOffset PulseTimeStamp { get; init; }

    [JsonPropertyName("outputValue")]
    public string OutputValue { get; init; } = string.Empty;

    [JsonPropertyName("signatureValue")]
    public string SignatureValue { get; init; } = string.Empty;

    [JsonPropertyName("certificateId")]
    public string CertificateId { get; init; } = string.Empty;

    /// <summary>
    ///     The authority's cipher suite for the recorded pulse. The canonical byte layout its signature was
    ///     computed over depends on this value, so a verifier must refuse an unknown suite rather than assume
    ///     one; -1 means the node was written before this field existed.
    /// </summary>
    [JsonPropertyName("cipherSuite")]
    public int CipherSuite { get; init; } = -1;

    /// <summary>
    ///     Which beacon source(s) contributed to this seed, as stable source identifiers. A single-source draw
    ///     has exactly one entry; the planned beacon-mixing capability appends further sources without
    ///     changing this shape, so a verifier can always tell whose pulse it has to check a signature against
    ///     instead of assuming the NIST beacon.
    /// </summary>
    [JsonPropertyName("beaconSources")]
    public List<string> BeaconSources { get; init; } = [];

    // Zero-based, gapless, monotonic counter scoped to one pulse. It must never restart below its stored
    // value for the same pulse, otherwise a draw could reuse a previously used seed.
    [JsonPropertyName("sequence")]
    public long Sequence { get; init; }

    [JsonPropertyName("derivation")]
    public string Derivation { get; init; } = string.Empty;
}

/// <summary>
///     Position of one proof inside the local append-only chain. Deleting or rewriting a chained proof
///     becomes visible because the surrounding files still link to its hash.
/// </summary>
public sealed class DrawProofChain
{
    [JsonPropertyName("formatVersion")]
    public int FormatVersion { get; init; } = 1;

    [JsonPropertyName("index")]
    public long Index { get; init; }

    [JsonPropertyName("prevHash")]
    public string PrevHash { get; init; } = string.Empty;

    // Hash over (index, prevHash, canonical proof hash). It excludes itself, so it cannot be self-referential.
    [JsonPropertyName("selfHash")]
    public string SelfHash { get; init; } = string.Empty;
}

public sealed class DrawProofResult
{
    [JsonPropertyName("winnerRecordIds")]
    public IReadOnlyList<Guid> WinnerRecordIds { get; init; } = [];
}

public sealed class DrawProofWitness
{
    [JsonPropertyName("challenge")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Challenge { get; init; }

    [JsonPropertyName("receipt")]
    public string? Receipt { get; init; }

    // RFC 3161 time-stamp token over the canonical proof hash, requested from an independent authority.
    [JsonPropertyName("timestampToken")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TimestampToken { get; init; }

    [JsonPropertyName("keyId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? KeyId { get; init; }
}
