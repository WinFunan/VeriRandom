using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;

namespace SecRandom.Core.Models.Verification;

/// <summary>
///     The record written for one raw random number.
/// </summary>
/// <remarks>
///     <para>
///         It is deliberately <b>not</b> a <c>DrawProof</c>. A draw proof replays a candidate pool through the draw
///         kernel; a raw number has no pool — only a range and one value — so forcing it into that shape would mean
///         inventing a candidate list. What the two do share is the property that matters: everything needed to
///         recompute the value is recorded, so the file can be checked later instead of merely believed.
///     </para>
///     <para>
///         It is also never submitted to <c>fair.sectl.cn</c>: that service validates an anonymous audit payload against
///         a fixed property allow-list and a fixed set of algorithm ids, and a raw number has neither a roster nor a
///         draw payload to describe. Sending one would produce a permanent attestation failure, not extra trust.
///     </para>
///     <para>
///         <see cref="Verifiable" /> means "some authority artifact can be re-checked later", i.e. exactly one
///         publishing source was used. A mixed or locally seeded number is still recorded and still self-consistent;
///         it just cannot be re-anchored, and the field says so rather than letting a reader over-read the file.
///     </para>
/// </remarks>
public sealed class RawNumberProof
{
    public string ProofId { get; set; } = string.Empty;

    public string AlgorithmId { get; set; } = RawNumberProofFormat.AlgorithmId;

    public string Kind { get; set; } = RawNumberProofFormat.Kind;

    public DateTimeOffset CreatedAt { get; set; }

    public long LowerBound { get; set; }

    public long UpperBound { get; set; }

    public long Value { get; set; }

    /// <summary>
    ///     Every value produced in this generation, or null for a single-value record. <see cref="Value" /> repeats the
    ///     first entry so older readers still show something sensible.
    /// </summary>
    public List<long>? Results { get; set; }

    /// <summary>
    ///     The explicit candidate set the value was drawn from, or null for a plain range draw. Recorded because a set
    ///     cannot be reconstructed from a lower/upper bound, and without it the record could not be recomputed.
    /// </summary>
    public List<long>? Values { get; set; }

    /// <summary>
    ///     The resolved candidate space as closed intervals, or null for a plain range draw. This is what a candidate
    ///     list with exclusions or an enumeration generator produces, and it is recorded <em>after</em> resolution — not
    ///     as the user's expression — so replaying an old record never depends on re-parsing or re-evaluating anything.
    /// </summary>
    public List<long[]>? Intervals { get; set; }

    /// <summary>Total number of values in <see cref="Intervals" />; recorded so a reader can see the pool size.</summary>
    public long Cardinality { get; set; }

    /// <summary>True only when a single publishing source seeded this number.</summary>
    public bool Verifiable { get; set; }

    /// <summary>
    ///     True when this generation emitted entropy instead of an in-range value. <see cref="Value" /> stays 0 in that
    ///     case: the text the user saw is a deterministic function of the seed, but the text itself is not stored, so
    ///     any number written here could not be re-derived — a placeholder is the honest content.
    /// </summary>
    public bool AdvancedOutput { get; set; }

    /// <summary>
    ///     How many entropy entries one collection round contributed, which is what makes a concatenated output's seed
    ///     rebuildable. Zero on records written before this field existed; such a record can only be replayed when it
    ///     holds a single round.
    /// </summary>
    public int EntropyRoundSize { get; set; }

    /// <summary>
    ///     Recognises an entropy record, including one written before <see cref="AdvancedOutput" /> existed. Such a
    ///     record has no range, no explicit set, no interval space and no batch, so every numeric field is a placeholder.
    /// </summary>
    [JsonIgnore]
    public bool IsEntropyRecord =>
        AdvancedOutput
        || (LowerBound == 0 && UpperBound == 0 && Value == 0
            && Results is not { Count: > 0 } && Values is not { Count: > 0 } && Intervals is not { Count: > 0 });

    /// <summary>Every contribution, in derivation order.</summary>
    public List<RawNumberProofEntropy> Entropy { get; set; } = [];

    /// <summary>Lowercase hex SHA-256 of the derived seed, so a replay can compare seeds directly.</summary>
    public string SeedDigest { get; set; } = string.Empty;

    public long Index { get; set; }

    public string PrevHash { get; set; } = string.Empty;

    public string SelfHash { get; set; } = string.Empty;
}

/// <summary>One entropy contribution as recorded in a <see cref="RawNumberProof" />.</summary>
public sealed class RawNumberProofEntropy
{
    public string SourceId { get; set; } = string.Empty;

    /// <summary>The exact text the derivation hashed (hex for a publishing source, generated bytes as hex for local).</summary>
    public string OutputValue { get; set; } = string.Empty;

    public long PulseIndex { get; set; }

    public DateTimeOffset? TimeStamp { get; set; }

    public string? SignatureValue { get; set; }

    public bool? SignatureVerified { get; set; }
}

/// <summary>Stable identifiers for the raw-number record.</summary>
public static class RawNumberProofFormat
{
    public const string AlgorithmId = "verirandom-raw-number/v1";

    public const string Kind = "raw-number";

    /// <summary>Separator for the local CSPRNG pseudo-source; it has no catalog entry.</summary>
    public const string LocalSourceId = "local-csprng";
}

/// <summary>
///     The hash rules of the raw-number chain, kept next to the model so a verifier and the writer cannot drift.
/// </summary>
public static class RawNumberProofHashing
{
    private const string ChainDomainSeparator = "VeriRandomRawProof/v1";

    /// <summary>
    ///     Hash of everything the record claims, excluding the chain fields themselves and the id. Ids and timestamps
    ///     are included on purpose: they are part of the claim, not incidental metadata.
    /// </summary>
    public static string ComputePayloadHash(RawNumberProof proof)
    {
        ArgumentNullException.ThrowIfNull(proof);

        var builder = new StringBuilder();
        builder.Append(proof.AlgorithmId).Append('|')
            .Append(proof.Kind).Append('|')
            .Append(proof.ProofId).Append('|')
            .Append(proof.CreatedAt.ToUniversalTime().ToString("O")).Append('|')
            .Append(proof.LowerBound).Append('|')
            .Append(proof.UpperBound).Append('|')
            .Append(proof.Value).Append('|')
            .Append(proof.Verifiable ? '1' : '0').Append('|')
            .Append(proof.SeedDigest);

        // Appended only when a set was used, so a range record written before sets existed keeps its exact digest.
        if (proof.Values is { Count: > 0 } values)
            builder.Append("|set:").AppendJoin(',', values);

        // Same rule for batches: a single-value record keeps the digest it had before batches existed.
        if (proof.Results is { Count: > 1 } results)
            builder.Append("|results:").AppendJoin(',', results);

        // Interval space, appended only when present so every earlier record keeps its exact digest.
        if (proof.Intervals is { Count: > 0 } intervals)
            builder.Append("|space:").AppendJoin(';', intervals.Select(pair => $"{pair[0]}-{pair[1]}"));

        // Entropy mode and its round structure, both appended only when present so every earlier record keeps its
        // exact digest. The round size matters because a concatenated output derives one seed per round and hashes
        // their concatenation; without it a verifier cannot rebuild the seed at all.
        if (proof.AdvancedOutput)
            builder.Append("|advanced");

        if (proof.EntropyRoundSize > 0)
            builder.Append("|roundsize:").Append(proof.EntropyRoundSize);

        foreach (var entropy in proof.Entropy)
            builder.Append('|')
                .Append(entropy.SourceId).Append(':')
                .Append(entropy.PulseIndex).Append(':')
                .Append(entropy.OutputValue);

        return LowerHex(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    /// <summary>Chain node hash: position, the hash it extends, and the payload it points at.</summary>
    public static string ComputeSelfHash(long index, string prevHash, string payloadHash) =>
        LowerHex(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{ChainDomainSeparator}|{index}|{prevHash}|{payloadHash}")));

    private static string LowerHex(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant();
}
