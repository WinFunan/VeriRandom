using System;
using System.Collections.Generic;
using System.Linq;
using SecRandom.Core.Models.Verification;
using SecRandom.Shared.Models.Verification;

namespace SecRandom.Core.Tests;

/// <summary>
///     Pins the two things the gate evidence must get right: it reports exactly the candidates that disappeared, and its
///     hash does not depend on enumeration order (two writers over the same roster must produce the same record).
/// </summary>
public sealed class GateEvidenceTests
{
    private static readonly Guid Alice = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Bob = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Carol = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static GateEvidenceMember Member(Guid id, int count) => new() { RecordId = id, DrawCount = count };

    [Fact]
    public void OnlyTheCandidatesMissingFromTheSurvivorsAreReported()
    {
        var preGate = new[] { Member(Alice, 5), Member(Bob, 4), Member(Carol, 1) };

        var snapshot = GateEvidenceDiff.FromSurvivors(preGate, new HashSet<Guid> { Carol }, threshold: 1);

        Assert.NotNull(snapshot);
        Assert.Equal(3, snapshot!.CandidateCountBefore);
        Assert.Equal(1, snapshot.CandidateCountAfter);
        Assert.Equal(1, snapshot.Threshold);
        Assert.Equal(GateEvidence.AverageGapRuleId, snapshot.GateRuleId);
        Assert.Equal(new[] { Alice, Bob }, snapshot.Removed.Select(member => member.RecordId).OrderBy(id => id));
        // The recorded count is the one that put the member over the threshold, not a recomputed guess.
        Assert.Equal(5, snapshot.Removed.Single(member => member.RecordId == Alice).DrawCount);
    }

    /// <summary>
    ///     A draw whose gate removed nobody must produce nothing: absence of the file is then information, and a
    ///     zero-removal file would make "no evidence" and "nothing removed" indistinguishable.
    /// </summary>
    [Fact]
    public void NobodyRemovedProducesNoEvidenceAtAll()
    {
        var preGate = new[] { Member(Alice, 2), Member(Bob, 2) };

        var snapshot = GateEvidenceDiff.FromSurvivors(preGate, new HashSet<Guid> { Alice, Bob }, threshold: 1);

        Assert.Null(snapshot);
    }

    [Fact]
    public void SelfHashDoesNotDependOnMemberOrder()
    {
        var forward = Evidence([Member(Alice, 5), Member(Bob, 4)]);
        var backward = Evidence([Member(Bob, 4), Member(Alice, 5)]);

        Assert.Equal(GateEvidenceHashing.ComputeSelfHash(forward), GateEvidenceHashing.ComputeSelfHash(backward));
    }

    [Fact]
    public void SelfHashChangesWhenADrawCountChanges()
    {
        var original = Evidence([Member(Alice, 5)]);
        var edited = Evidence([Member(Alice, 4)]);

        Assert.NotEqual(GateEvidenceHashing.ComputeSelfHash(original), GateEvidenceHashing.ComputeSelfHash(edited));
    }

    /// <summary>
    ///     The Up binding is part of the hash: evidence cannot be repointed at another draw and keep a valid self-hash.
    /// </summary>
    [Fact]
    public void SelfHashCoversTheUpBinding()
    {
        var original = Evidence([Member(Alice, 5)]);
        var repointed = Evidence([Member(Alice, 5)]) with { UpChainHash = "another-chain-hash" };

        Assert.NotEqual(GateEvidenceHashing.ComputeSelfHash(original), GateEvidenceHashing.ComputeSelfHash(repointed));
    }

    private static GateEvidence Evidence(IReadOnlyList<GateEvidenceMember> removed) => new()
    {
        ProofId = Guid.Parse("44444444-4444-4444-4444-444444444444"),
        UpChainIndex = 7,
        UpChainHash = "up-chain-hash",
        GateRuleId = GateEvidence.AverageGapRuleId,
        Threshold = 1,
        CandidateCountBefore = 3,
        CandidateCountAfter = 1,
        Removed = removed,
        CreatedAtUtc = DateTimeOffset.Parse("2026-10-31T08:00:00Z")
    };
}
