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

    private static GateEvidence Evidence(IReadOnlyList<GateEvidenceMember> removed) => new()    {
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

    // ---------------------------------------------------------------- rule replay

    private static GateEvidenceMember At(int index, int count) => new()
    {
        RecordId = Guid.Parse($"00000000-0000-0000-0000-{index:D12}"),
        DrawCount = count
    };

    /// <summary>
    ///     The eligible count is the average, tightened to <c>min + threshold</c> only while the spread actually exceeds
    ///     the threshold. Here the spread (9) does, so the tightened value wins over the average.
    /// </summary>
    [Fact]
    public void TheEligibleCountIsTightenedOnlyWhenTheSpreadExceedsTheThreshold()
    {
        var pool = new[] { At(0, 0), At(1, 0), At(2, 5), At(3, 5), At(4, 9) };

        var removed = GateReplay.ComputeRemoved(pool, drawCount: 1, threshold: 1);

        Assert.Equal(3, removed.Count);
        Assert.DoesNotContain(pool[0].RecordId, removed);
        Assert.DoesNotContain(pool[1].RecordId, removed);
    }

    [Fact]
    public void AnEvenPoolRemovesNobody()
    {
        var pool = new[] { At(0, 0), At(1, 0), At(2, 0) };

        Assert.Empty(GateReplay.ComputeRemoved(pool, drawCount: 2, threshold: 1));
    }

    /// <summary>
    ///     The top-up adds whole count levels, never single members: that is what makes the outcome independent of
    ///     roster order, and therefore reproducible by a verifier.
    /// </summary>
    [Fact]
    public void TheTopUpAddsWholeLevels()
    {
        // Average 7.86 is tightened to 1, so the three 5s and both 20s are out; the kept pool of 2 is short of the 3
        // requested draws, the whole 5-level comes back, and only the 20s stay removed.
        var pool = new[] { At(0, 0), At(1, 0), At(2, 5), At(3, 5), At(4, 5), At(5, 20), At(6, 20) };

        var removed = GateReplay.ComputeRemoved(pool, drawCount: 3, threshold: 1);

        Assert.Equal(2, removed.Count);
        Assert.Contains(pool[5].RecordId, removed);
        Assert.Contains(pool[6].RecordId, removed);
    }

    [Fact]
    public void ARecordThatMatchesTheRuleReplaysCleanly()
    {
        var pool = new[] { At(0, 0), At(1, 0), At(2, 5), At(3, 5), At(4, 9) };
        var evidence = Evidence([At(2, 5), At(3, 5), At(4, 9)]) with { Pool = pool };

        var replay = GateReplay.Replay(evidence, drawCount: 1);

        Assert.True(replay.Judged);
        Assert.True(replay.Matches);
        Assert.Empty(replay.UnexpectedRemoved);
        Assert.Empty(replay.MissingRemoved);
    }

    [Fact]
    public void ARemovalTheRuleWouldNotMakeIsReportedAsUnjustified()
    {
        var pool = new[] { At(0, 0), At(1, 0), At(2, 5), At(3, 9) };
        // The 0-count member was removed although the rule keeps it.
        var evidence = Evidence([At(0, 0), At(2, 5), At(3, 9)]) with { Pool = pool };

        var replay = GateReplay.Replay(evidence, drawCount: 1);

        Assert.Single(replay.UnexpectedRemoved);
        Assert.Equal(pool[0].RecordId, replay.UnexpectedRemoved[0]);
        Assert.False(replay.Matches);
    }

    [Fact]
    public void AKeptMemberTheRuleWouldRemoveIsReportedAsIncomplete()
    {
        var pool = new[] { At(0, 0), At(1, 0), At(2, 5), At(3, 9) };
        // The record removed only one of the two members above the eligible count.
        var evidence = Evidence([At(3, 9)]) with { Pool = pool };

        var replay = GateReplay.Replay(evidence, drawCount: 1);

        Assert.Single(replay.MissingRemoved);
        Assert.Equal(pool[2].RecordId, replay.MissingRemoved[0]);
    }

    /// <summary>A record written before the pool was stored cannot be judged — and must not be read as a pass.</summary>
    [Fact]
    public void ARecordWithoutAPoolIsNotJudged()
    {
        var replay = GateReplay.Replay(Evidence([At(2, 5)]), drawCount: 1);

        Assert.False(replay.Judged);
        Assert.False(replay.Matches);
    }
}
