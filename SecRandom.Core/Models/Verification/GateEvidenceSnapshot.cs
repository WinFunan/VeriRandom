using System;
using System.Collections.Generic;
using System.Linq;
using SecRandom.Shared.Models.Verification;

namespace SecRandom.Core.Models.Verification;

/// <summary>
///     The gate facts captured at draw time, before they are bound to a proof.
/// </summary>
/// <remarks>
///     It travels on <see cref="VerificationDrawInput" /> but is deliberately <b>not</b> part of the wire frame: the
///     attestation service decodes that frame with a fixed layout, so the facts are written to a fork-local sibling file
///     instead (see <c>GateEvidence</c>).
/// </remarks>
public sealed record GateEvidenceSnapshot(
    string GateRuleId,
    int Threshold,
    int CandidateCountBefore,
    int CandidateCountAfter,
    IReadOnlyList<GateEvidenceMember> Removed,
    IReadOnlyList<GateEvidenceMember> Pool);

/// <summary>
///     Turns "who was in the pool before the gate" plus "who survived it" into the evidence snapshot.
/// </summary>
public static class GateEvidenceDiff
{
    /// <summary>
    ///     Diff of the pre-gate pool against the survivors.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The survivor set is the frozen pool's <em>record ids</em>, not object references: a list can legitimately
    ///         hold two entries for one person (a duplicated record id is repaired elsewhere, but not guaranteed here),
    ///         and comparing ids keeps the diff from reporting the same person twice.
    ///     </para>
    ///     <para>
    ///         Returns <c>null</c> when nobody was removed. That is what makes the sibling file's absence meaningful:
    ///         a draw with nothing to remove writes nothing, so an existing file always describes a real removal.
    ///     </para>
    /// </remarks>
    public static GateEvidenceSnapshot? FromSurvivors(
        IReadOnlyList<GateEvidenceMember> preGateMembers,
        IReadOnlySet<Guid> survivors,
        int threshold,
        string gateRuleId = GateEvidence.AverageGapRuleId)
    {
        ArgumentNullException.ThrowIfNull(preGateMembers);
        ArgumentNullException.ThrowIfNull(survivors);

        var removed = preGateMembers
            .Where(member => !survivors.Contains(member.RecordId))
            .ToArray();

        if (removed.Length == 0)
            return null;

        return new GateEvidenceSnapshot(
            gateRuleId,
            threshold,
            preGateMembers.Count,
            survivors.Count,
            removed,
            preGateMembers);
    }
}
