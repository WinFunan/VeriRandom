using System;
using System.Collections.Generic;
using System.Linq;
using SecRandom.Core.Models.Verification;
using SecRandom.Shared.Models.Verification;

namespace SecRandom.Core.Services.Verification;

/// <summary>One way a proof file can disagree with itself.</summary>
public enum ProofConsistencyIssue
{
    /// <summary>The payload is not a frame this build can decode, so nothing else can be judged.</summary>
    PayloadUndecodable,

    /// <summary>Payload and audit bytes no longer reproduce the recorded input commitment.</summary>
    InputHashMismatch,

    /// <summary>The top-level result names different winners than the payload's own response frame.</summary>
    ResultDisagreesWithPayload,

    /// <summary>The audit payload's candidate counts cannot be ordered before ≥ after ≥ frozen pool.</summary>
    AuditCountsImpossible
}

/// <summary>
///     The self-checks a proof file admits without any outside material.
/// </summary>
/// <param name="InputHashCheckable">
///     False when the proof was written by another engine version. The commitment is recomputed with <em>this</em>
///     build's codec, so an older frame legitimately hashes differently; calling that a mismatch would accuse a proof
///     for being old.
/// </param>
/// <param name="AuditCountsOrdered">
///     Only an ordering is knowable. The gate and the repeat rules can drop candidates, and freezing for the request
///     can drop more (zero-probability rules), so <c>after</c> does not equal the payload's candidate count in general
///     — an equality check would fire on every draw that carries an internal rule.
/// </param>
public sealed record ProofConsistencyReport(
    bool InputHashCheckable,
    bool InputHashVerified,
    bool ResultMatchesPayload,
    bool AuditCountsOrdered,
    IReadOnlyList<ProofConsistencyIssue> Issues)
{
    /// <summary>True only when every check that could run ran and passed.</summary>
    public bool IsConsistent => Issues.Count == 0;
}

/// <summary>
///     Cross-checks a proof file against itself: replay used to accept the recorded input commitment, the top-level
///     result and the audit claims on faith, because nothing compared them with the bytes they claim to describe.
/// </summary>
/// <remarks>
///     <para>
///         What this closes: an <c>InputHash</c> that every local reader used to trust without ever recomputing it, a
///         <c>Result.WinnerRecordIds</c> that could name different winners than the payload's response frame without any
///         surface noticing, and audit claims that could contradict the pool they describe.
///     </para>
///     <para>
///         What it does <b>not</b> close: the audit payload's gap-protection claim is still only a claim. Verifying it
///         would need the pre-gate candidate set and every candidate's draw count, neither of which the proof carries;
///         keeping the counts merely ordered is the strongest statement the current format supports.
///     </para>
/// </remarks>
public static class ProofConsistency
{
    public static ProofConsistencyReport Check(DrawProof proof)
    {
        ArgumentNullException.ThrowIfNull(proof);

        if (!TryDecode(proof, out var input, out var recorded, out var auditBytes))
            return new ProofConsistencyReport(false, false, false, false, [ProofConsistencyIssue.PayloadUndecodable]);

        var issues = new List<ProofConsistencyIssue>();

        // The commitment was written as base64url of the input hash; recompute it from the bytes the file actually
        // carries rather than trusting the stored string.
        var checkable = string.Equals(
            proof.AlgorithmEngineVersion,
            VerificationWireCodec.AlgorithmEngineVersion,
            StringComparison.Ordinal);

        var inputHashVerified = false;
        if (checkable)
        {
            var expected = RecomputeInputHash(input, auditBytes);
            inputHashVerified = expected is not null
                                && string.Equals(expected, proof.InputHash, StringComparison.Ordinal);

            if (!inputHashVerified)
                issues.Add(ProofConsistencyIssue.InputHashMismatch);
        }

        var resultMatches = MatchesRecordedFrame(proof, recorded);
        if (!resultMatches)
            issues.Add(ProofConsistencyIssue.ResultDisagreesWithPayload);

        var countsOrdered = true;
        if (AnonymizedAuditPayload.TryParse(auditBytes)?.Fairness is { } fairness)
        {
            var before = fairness.CandidateCountBeforeAverageGapProtection;
            var after = fairness.CandidateCountAfterAverageGapProtection;

            // Prize draws carry no fairness block, and a record written before those counts existed reports zero; in
            // both cases there is nothing to order, so the check abstains instead of inventing a verdict.
            if (before > 0)
            {
                countsOrdered = after > 0
                                && after <= before
                                && input.Candidates.Count <= after;

                if (!countsOrdered)
                    issues.Add(ProofConsistencyIssue.AuditCountsImpossible);
            }
        }

        return new ProofConsistencyReport(checkable, inputHashVerified, resultMatches, countsOrdered, issues);
    }

    private static bool TryDecode(
        DrawProof proof,
        out VerificationDrawInput input,
        out VerificationKernelResult recorded,
        out byte[] auditBytes)
    {
        input = null!;
        recorded = new VerificationKernelResult();

        // An audit payload that is not valid base64url cannot have been the one that produced the commitment, so the
        // hash check below fails on its own; decoding to empty here keeps that failure path uniform.
        auditBytes = TryDecodeAudit(proof.AuditPayload);

        if (string.IsNullOrWhiteSpace(proof.Payload))
            return false;

        byte[] payloadBytes;
        try
        {
            payloadBytes = AnonymizedAuditPayload.DecodeBase64Url(proof.Payload);
        }
        catch (FormatException)
        {
            return false;
        }

        return VerificationWireCodec.TryDecodeProofPayload(payloadBytes, out input, out _, out recorded);
    }

    private static byte[] TryDecodeAudit(string? auditPayload)
    {
        if (string.IsNullOrWhiteSpace(auditPayload))
            return [];

        try
        {
            return AnonymizedAuditPayload.DecodeBase64Url(auditPayload);
        }
        catch (FormatException)
        {
            return [];
        }
    }

    private static string? RecomputeInputHash(VerificationDrawInput input, byte[] auditBytes)
    {
        try
        {
            // EncodeInput re-validates the profile/mode pairing and canonicalizes the candidates, so a proof whose frame
            // is internally impossible lands here rather than producing a meaningless hash.
            var recomputed = VerificationWireCodec.ComputeInputHash(new VerificationDrawInput
            {
                Kind = input.Kind,
                SamplingMode = input.SamplingMode,
                AlgorithmProfile = input.AlgorithmProfile,
                Count = input.Count,
                Candidates = input.Candidates,
                AuditPayload = auditBytes
            });

            return AnonymizedAuditPayload.EncodeBase64Url(recomputed);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    ///     The proof keeps the winners twice: once in the payload's response frame (which is what replay compares) and
    ///     once in the top-level result (which is what the window displays). A file where those disagree would show one
    ///     set of winners and grade another.
    /// </summary>
    private static bool MatchesRecordedFrame(DrawProof proof, VerificationKernelResult recorded)
    {
        var recordedIds = proof.Result.WinnerRecordIds;
        if (recordedIds.Count != recorded.Winners.Count)
            return false;

        return !recordedIds.Where((recordId, index) => recordId != recorded.Winners[index].RecordId).Any();
    }
}
