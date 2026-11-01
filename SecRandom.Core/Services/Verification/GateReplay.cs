using System;
using System.Collections.Generic;
using System.Linq;
using SecRandom.Shared.Models.Verification;

namespace SecRandom.Core.Services.Verification;

/// <summary>
///     Outcome of re-running the average-gap rule over a recorded pool.
/// </summary>
/// <param name="Judged">
///     False when the record carries no pool (written before the pool was recorded), so no verdict is possible. A caller
///     must show that as "cannot be judged", never as a pass.
/// </param>
/// <param name="UnexpectedRemoved">Members the record removed although the rule would have kept them.</param>
/// <param name="MissingRemoved">Members the rule would have removed although the record kept them.</param>
public sealed record GateReplayResult(
    bool Judged,
    IReadOnlyList<Guid> ExpectedRemoved,
    IReadOnlyList<Guid> UnexpectedRemoved,
    IReadOnlyList<Guid> MissingRemoved)
{
    public bool Matches => Judged && UnexpectedRemoved.Count == 0 && MissingRemoved.Count == 0;
}

/// <summary>
///     Re-runs the average-gap gate over the pool a draw recorded, so "which candidates this rule removes" stops being a
///     claim and becomes a computation.
/// </summary>
/// <remarks>
///     <para>
///         The rule is replicated here, not called from the draw engine: the engine works on <c>Student</c> objects and
///         live history, while a verifier only has record ids and counts. Keeping the copy pure is what lets it be tested
///         against the engine's behaviour and read by a reviewer — but it also means the two must be kept in step. Any
///         change to <c>DrawEngine.ApplyAverageGapProtection</c> has to change this method in the same commit.
///     </para>
///     <para>
///         It answers both questions at once, which is why recording the whole pool was worth its size: a member in
///         <see cref="GateReplayResult.ExpectedRemoved" /> that the record kept is a <em>missing</em> removal, and a
///         member the record removed that the rule would have kept is an <em>unjustified</em> one.
///     </para>
/// </remarks>
public static class GateReplay
{
    public static GateReplayResult Replay(GateEvidence evidence, int drawCount)
    {
        ArgumentNullException.ThrowIfNull(evidence);

        if (evidence.Pool.Count == 0)
            return new GateReplayResult(false, [], [], []);

        var expected = ComputeRemoved(evidence.Pool, drawCount, evidence.Threshold);

        var expectedSet = expected.ToHashSet();
        var recordedSet = evidence.Removed.Select(member => member.RecordId).ToHashSet();

        return new GateReplayResult(
            true,
            expected,
            recordedSet.Where(id => !expectedSet.Contains(id)).OrderBy(id => id).ToArray(),
            expectedSet.Where(id => !recordedSet.Contains(id)).OrderBy(id => id).ToArray());
    }

    /// <summary>
    ///     The members the gate removes from a pool of the given sizes: everyone above the eligible count, then the
    ///     lowest levels added back whole until the pool can satisfy the draw.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Mirrors <c>DrawEngine.ApplyAverageGapProtection</c> exactly, including the two details that make the
    ///         result reproducible: the eligible count is the average, tightened to <c>min + threshold</c> only while the
    ///         spread exceeds the threshold, and the top-up adds <b>whole count levels</b> rather than single members —
    ///         which is why the outcome never depends on the order the roster happened to be in.
    ///     </para>
    ///     <para>
    ///         Entries are treated as separate candidates even when two share a record id, because the engine sees two
    ///         <c>Student</c> objects in that case; collapsing them here would silently change the count.
    ///     </para>
    /// </remarks>
    public static IReadOnlyList<Guid> ComputeRemoved(
        IReadOnlyList<GateEvidenceMember> pool,
        int drawCount,
        int threshold)
    {
        ArgumentNullException.ThrowIfNull(pool);

        if (pool.Count == 0)
            return [];

        var counts = pool.Select(member => member.DrawCount).ToList();
        var average = counts.Average();
        var min = counts.Min();
        var max = counts.Max();

        var maxEligible = average;
        if (max - min > threshold)
            maxEligible = Math.Min(average, min + threshold);

        var kept = new List<int>(pool.Count);
        var keptIndexes = new HashSet<int>();
        for (var index = 0; index < pool.Count; index++)
        {
            if (pool[index].DrawCount > maxEligible)
                continue;

            kept.Add(index);
            keptIndexes.Add(index);
        }

        if (drawCount > 0 && kept.Count < drawCount)
        {
            foreach (var level in Enumerable.Range(0, pool.Count)
                         .Where(index => !keptIndexes.Contains(index))
                         .GroupBy(index => pool[index].DrawCount)
                         .OrderBy(group => group.Key))
            {
                // Whole levels: a partially added level would make the outcome depend on roster order, and then no
                // verifier could ever reproduce it.
                foreach (var index in level)
                {
                    kept.Add(index);
                    keptIndexes.Add(index);
                }

                if (kept.Count >= drawCount)
                    break;
            }
        }

        return Enumerable.Range(0, pool.Count)
            .Where(index => !keptIndexes.Contains(index))
            .Select(index => pool[index].RecordId)
            .ToArray();
    }
}
