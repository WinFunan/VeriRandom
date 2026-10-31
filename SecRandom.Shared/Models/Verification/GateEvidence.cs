using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace SecRandom.Shared.Models.Verification;

/// <summary>
///     What the average-gap gate removed from one draw's candidate pool.
/// </summary>
/// <remarks>
///     <para>
///         This is a fork-local <b>sibling file</b>, not a proof field. Both the wire payload and the anonymous audit
///         payload are consumed by the attestation service against a fixed field list and a fixed frame encoding, so a
///         new field there would make every ordinary submission fail. It never enters
///         <c>WitnessClient.ComputeAttestedProofHash</c>.
///     </para>
///     <para>
///         What it establishes: given the pre-gate draw counts it records, the gate's rule reproduces exactly the set
///         that disappeared — so "the highest-count students were the ones removed" becomes checkable rather than
///         implied by a candidate count. What it does <b>not</b> establish: that those counts were true. Nothing commits
///         the history files, so the counts stay self-reported; this narrows the claim, it does not remove the
///         assumption.
///     </para>
///     <para>
///         It is written only when the gate actually removed somebody. A missing file therefore means "the gate had
///         nothing to remove" <em>or</em> "this proof predates the evidence", and both readings must be offered rather
///         than silently scored as a pass.
///     </para>
/// </remarks>
public sealed record class GateEvidence
{
    public const string CurrentFormat = "verirandom-gate-evidence/v1";

    /// <summary>
    ///     Identifier of the candidate-gate rule the record belongs to. It is stored in every record rather than assumed
    ///     by the verifier, so an old record is judged by the rule that actually ran instead of by today's.
    /// </summary>
    public const string AverageGapRuleId = "verirandom-average-gap/v1";

    [JsonPropertyName("format")]
    public string Format { get; init; } = CurrentFormat;

    /// <summary>The Up proof this evidence belongs to.</summary>
    [JsonPropertyName("proofId")]
    public Guid ProofId { get; init; }

    /// <summary>Chain position of the Up proof, recorded so the binding is readable without opening it.</summary>
    [JsonPropertyName("upChainIndex")]
    public long UpChainIndex { get; init; }

    /// <summary>
    ///     The Up proof's <c>chain.selfHash</c>. Since that hash is anchored in the Up chain, evidence can never be
    ///     re-pointed at a different draw afterwards.
    /// </summary>
    [JsonPropertyName("upChainHash")]
    public string UpChainHash { get; init; } = string.Empty;

    /// <summary>Stable identifier of the rule that produced the removal, so an old record is judged by its own rule.</summary>
    [JsonPropertyName("gateRuleId")]
    public string GateRuleId { get; init; } = string.Empty;

    [JsonPropertyName("threshold")]
    public int Threshold { get; init; }

    [JsonPropertyName("candidateCountBefore")]
    public int CandidateCountBefore { get; init; }

    [JsonPropertyName("candidateCountAfter")]
    public int CandidateCountAfter { get; init; }

    /// <summary>Everyone the gate removed, with the count that removed them.</summary>
    [JsonPropertyName("removed")]
    public IReadOnlyList<GateEvidenceMember> Removed { get; init; } = [];

    /// <summary>
    ///     Every candidate that entered the gate, with the count the gate saw.
    /// </summary>
    /// <remarks>
    ///     Without this the removal set cannot be judged: the rule's threshold is a function of <em>all</em> counts, so
    ///     knowing only the removed members' counts can never say whether somebody who should have been removed was
    ///     left in the pool. Recording the whole pool is what makes the rule replayable per draw and therefore closes
    ///     both "was the removal justified" and "was it complete". It costs about 40 bytes per candidate and discloses
    ///     the draw-count distribution of that pool — the price of being able to check rather than be told.
    ///     Absent on records written before this field existed; a replay then reports "cannot be judged".
    /// </remarks>
    [JsonPropertyName("pool")]
    public IReadOnlyList<GateEvidenceMember> Pool { get; init; } = [];

    [JsonPropertyName("createdAtUtc")]
    public DateTimeOffset CreatedAtUtc { get; init; }

    /// <summary>Hash over every field above. It excludes itself, so it cannot be self-referential.</summary>
    [JsonPropertyName("selfHash")]
    public string SelfHash { get; init; } = string.Empty;
}

/// <summary>One candidate the gate removed, and the history count that put them over the threshold.</summary>
public sealed record class GateEvidenceMember
{
    [JsonPropertyName("recordId")]
    public Guid RecordId { get; init; }

    [JsonPropertyName("drawCount")]
    public int DrawCount { get; init; }
}

/// <summary>
///     The self-hash rule of a gate-evidence node, kept beside the model so writer and verifier cannot drift.
/// </summary>
public static class GateEvidenceHashing
{
    private const string DomainSeparator = "VeriRandomGateEvidence/v1";

    /// <summary>
    ///     Hashes the whole record except <see cref="GateEvidence.SelfHash" />. Members are ordered by record id so the
    ///     same evidence written by a different enumeration order still hashes identically.
    /// </summary>
    public static string ComputeSelfHash(GateEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);

        var builder = new StringBuilder();
        builder.Append(DomainSeparator).Append('|')
            .Append(evidence.Format).Append('|')
            .Append(evidence.ProofId.ToString("N")).Append('|')
            .Append(evidence.UpChainIndex).Append('|')
            .Append(evidence.UpChainHash).Append('|')
            .Append(evidence.GateRuleId).Append('|')
            .Append(evidence.Threshold).Append('|')
            .Append(evidence.CandidateCountBefore).Append('|')
            .Append(evidence.CandidateCountAfter).Append('|')
            .Append(evidence.CreatedAtUtc.ToUniversalTime().ToString("O"));

        foreach (var member in evidence.Removed.OrderBy(member => member.RecordId.ToString("N"), StringComparer.Ordinal))
            builder.Append('|').Append(member.RecordId.ToString("N")).Append(':').Append(member.DrawCount);

        // Appended only when present, so records written before the pool was recorded keep their exact hash.
        foreach (var member in evidence.Pool.OrderBy(member => member.RecordId.ToString("N"), StringComparer.Ordinal))
            builder.Append("|pool:").Append(member.RecordId.ToString("N")).Append(':').Append(member.DrawCount);

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()))).ToLowerInvariant();
    }
}
