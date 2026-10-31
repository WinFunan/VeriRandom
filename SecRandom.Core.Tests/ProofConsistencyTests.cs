using System;
using System.Linq;
using System.Text;
using SecRandom.Core.Models.Verification;
using SecRandom.Core.Services.Verification;
using SecRandom.Shared.Models.Verification;

namespace SecRandom.Core.Tests;

/// <summary>
///     These pin the three self-checks a proof file admits: the recorded input commitment is recomputed from the bytes,
///     the top-level result is compared with the payload's own response frame, and the audit counts are required to be
///     ordered. Replay alone never compared any of them, so an edited audit payload or a result field that names
///     different winners used to pass silently.
/// </summary>
public sealed class ProofConsistencyTests
{
    private static readonly byte[] Seed = Enumerable.Range(0, 32).Select(value => (byte)value).ToArray();

    private static VerificationDrawInput CreateInput(byte[] audit) => new()
    {
        Kind = VerificationDrawKind.Student,
        Count = 2,
        Candidates =
        [
            new VerificationCandidate(Guid.Parse("11111111-1111-1111-1111-111111111111"), 0, 1_000_000, false),
            new VerificationCandidate(Guid.Parse("22222222-2222-2222-2222-222222222222"), 0, 1_000_000, false),
            new VerificationCandidate(Guid.Parse("33333333-3333-3333-3333-333333333333"), 0, 1_000_000, false)
        ],
        AuditPayload = audit
    };

    private static byte[] CreateAudit(int before, int after) => Encoding.UTF8.GetBytes(
        "{\"format\":\"anonymized-audit/v1\",\"operation\":\"student\",\"requestedCount\":2,\"candidateCount\":3," +
        "\"fairness\":{\"fairDraw\":true,\"averageGapProtectionApplied\":true," +
        $"\"candidateCountBeforeAverageGapProtection\":{before}," +
        $"\"candidateCountAfterAverageGapProtection\":{after}," +
        "\"averageGapProtectionThreshold\":1,\"batchSize\":2}}");

    private static DrawProof CreateProof(byte[] audit, string engineVersion)
    {
        var input = CreateInput(audit);
        var drawn = new ManagedVerificationKernel().Draw(input, Seed);
        var payload = VerificationWireCodec.EncodeProofPayload(input, Seed, drawn.Winners);

        return new DrawProof
        {
            AlgorithmId = VerificationWireCodec.AlgorithmId,
            AlgorithmEngineVersion = engineVersion,
            InputHash = AnonymizedAuditPayload.EncodeBase64Url(VerificationWireCodec.ComputeInputHash(input)),
            Payload = AnonymizedAuditPayload.EncodeBase64Url(payload),
            AuditPayload = AnonymizedAuditPayload.EncodeBase64Url(audit),
            Result = new DrawProofResult
            {
                WinnerRecordIds = drawn.Winners.Select(winner => winner.RecordId).ToArray()
            }
        };
    }

    private static DrawProof CreateProof() =>
        CreateProof(CreateAudit(before: 3, after: 3), VerificationWireCodec.AlgorithmEngineVersion);

    [Fact]
    public void AnUntouchedProofPassesEverySelfCheck()
    {
        var report = ProofConsistency.Check(CreateProof());

        Assert.True(report.InputHashCheckable);
        Assert.True(report.InputHashVerified);
        Assert.True(report.ResultMatchesPayload);
        Assert.True(report.AuditCountsOrdered);
        Assert.True(report.IsConsistent);
        Assert.Empty(report.Issues);
    }

    [Fact]
    public void AResultThatNamesOtherWinnersIsCaught()
    {
        var proof = CreateProof();
        var swapped = proof with
        {
            Result = new DrawProofResult
            {
                WinnerRecordIds = [Guid.Parse("33333333-3333-3333-3333-333333333333")]
            }
        };

        var report = ProofConsistency.Check(swapped);

        Assert.False(report.ResultMatchesPayload);
        Assert.Contains(ProofConsistencyIssue.ResultDisagreesWithPayload, report.Issues);
        Assert.False(report.IsConsistent);
    }

    /// <summary>
    ///     The one edit replay cannot see: it never receives the audit payload, so swapping it while keeping the payload
    ///     used to be invisible. The input commitment covers both halves, which is what catches it here.
    /// </summary>
    [Fact]
    public void AnEditedAuditPayloadBreaksTheInputCommitment()
    {
        var proof = CreateProof();
        var edited = proof with
        {
            AuditPayload = AnonymizedAuditPayload.EncodeBase64Url(CreateAudit(before: 30, after: 3))
        };

        var report = ProofConsistency.Check(edited);

        Assert.False(report.InputHashVerified);
        Assert.Contains(ProofConsistencyIssue.InputHashMismatch, report.Issues);
    }

    [Fact]
    public void CountsThatCannotBeOrderedAreCaught()
    {
        // The gate may only ever remove candidates, so "after" larger than "before" cannot describe any real draw.
        var proof = CreateProof(CreateAudit(before: 3, after: 5), VerificationWireCodec.AlgorithmEngineVersion);

        var report = ProofConsistency.Check(proof);

        Assert.False(report.AuditCountsOrdered);
        Assert.Contains(ProofConsistencyIssue.AuditCountsImpossible, report.Issues);
        // The commitment is intact: only the claim itself is impossible, which is what the audit check exists for.
        Assert.True(report.InputHashVerified);
    }

    [Fact]
    public void AnotherEngineVersionIsReportedAsUncheckableRatherThanMismatched()
    {
        var proof = CreateProof(CreateAudit(before: 3, after: 3), "2.0.0");

        var report = ProofConsistency.Check(proof);

        Assert.False(report.InputHashCheckable);
        Assert.False(report.InputHashVerified);
        Assert.DoesNotContain(ProofConsistencyIssue.InputHashMismatch, report.Issues);
        Assert.True(report.IsConsistent);
    }

    [Fact]
    public void AGarbagePayloadIsReportedInsteadOfThrowing()
    {
        var report = ProofConsistency.Check(new DrawProof { Payload = "not-a-frame" });

        Assert.Contains(ProofConsistencyIssue.PayloadUndecodable, report.Issues);
        Assert.False(report.IsConsistent);
    }
}
