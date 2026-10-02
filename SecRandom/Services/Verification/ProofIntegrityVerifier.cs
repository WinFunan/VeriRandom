using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace SecRandom.Services.Verification;

/// <summary>
///     Rebuilds the local evidence chain from <c>data/proofs</c> and reports everything that cannot be
///     explained by the app's own retention cleanup. A local chain gives visibility, not protection:
///     an attacker who rewrites every file and the chain head can rebuild a self-consistent chain, so the
///     report must never be presented as proof that nothing was removed.
/// </summary>
public sealed class ProofIntegrityVerifier(
    DrawProofExportService proofExporter,
    ProofChainStore chainStore,
    OwnProofExportService ownProofExporter,
    OwnProofChainStore ownChainStore)
{
    private const int MaximumReportedIssues = 50;

    public ProofIntegrityReport Verify(CancellationToken cancellationToken = default)
    {
        var head = chainStore.Read();
        var issues = new List<ProofIntegrityIssue>();
        var truncated = false;
        var entries = new List<ChainedProof>();
        var unchained = 0;
        var unreadable = 0;

        foreach (var path in proofExporter.EnumerateProofPaths())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!proofExporter.TryRead(path, out var proof) || proof is null)
            {
                unreadable++;
                AddIssue(issues, ref truncated, ProofIntegrityIssueKind.Unreadable, path, "证明文件无法解析");
                continue;
            }

            if (proof.Chain is null)
            {
                unchained++;
                continue;
            }

            if (proof.Chain.FormatVersion != ProofChainStore.CurrentFormatVersion)
            {
                AddIssue(issues, ref truncated, ProofIntegrityIssueKind.UnsupportedVersion, path,
                    $"不支持的链格式版本 {proof.Chain.FormatVersion}");
                continue;
            }

            var expectedSelfHash = ProofChainStore.ComputeSelfHash(
                proof.Chain.Index,
                proof.Chain.PrevHash,
                WitnessClient.ComputeAttestedProofHash(proof));
            if (!string.Equals(expectedSelfHash, proof.Chain.SelfHash, StringComparison.Ordinal))
            {
                AddIssue(issues, ref truncated, ProofIntegrityIssueKind.Modified, path, "文件内容与链上记录不一致");
                continue;
            }

            entries.Add(new ChainedProof(path, proof.Chain.Index, proof.Chain.PrevHash, proof.Chain.SelfHash));
        }

        entries.Sort((left, right) => left.Index.CompareTo(right.Index));
        var gaps = 0;
        var expired = 0;
        var brokenLinks = 0;
        var beyondHead = 0;

        // Indices below the first surviving proof are missing too; only the recorded retention floor explains them.
        if (entries.Count > 0)
        {
            var expectedFirst = head.RetainedFromIndex + 1;
            if (entries[0].Index > expectedFirst)
            {
                gaps++;
                AddIssue(issues, ref truncated, ProofIntegrityIssueKind.Gap, entries[0].Path,
                    $"链序 {expectedFirst}-{entries[0].Index - 1} 缺少证明文件");
            }
        }

        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            if (entry.Index > head.HeadIndex)
            {
                beyondHead++;
                AddIssue(issues, ref truncated, ProofIntegrityIssueKind.BeyondHead, entry.Path,
                    $"链序 {entry.Index} 超过链头 {head.HeadIndex}");
            }

            if (index == 0)
                continue;

            var previous = entries[index - 1];
            if (!string.Equals(entry.PrevHash, previous.SelfHash, StringComparison.Ordinal))
            {
                brokenLinks++;
                AddIssue(issues, ref truncated, ProofIntegrityIssueKind.BrokenLink, entry.Path,
                    $"链序 {entry.Index} 的前一链哈希与链序 {previous.Index} 不衔接");
            }

            if (entry.Index == previous.Index + 1)
                continue;

            var missingFrom = previous.Index + 1;
            var missingTo = entry.Index - 1;
            if (missingTo <= head.RetainedFromIndex)
            {
                expired++;
                continue;
            }

            gaps++;
            AddIssue(issues, ref truncated, ProofIntegrityIssueKind.Gap, entry.Path,
                $"链序 {missingFrom}-{missingTo} 缺少证明文件");
        }

        var highestChained = entries.Count == 0 ? head.RetainedFromIndex : entries[^1].Index;
        var missingTail = (int)Math.Max(0, Math.Min(head.HeadIndex - highestChained, int.MaxValue));
        if (missingTail > 0)
        {
            AddIssue(issues, ref truncated, ProofIntegrityIssueKind.MissingTail, string.Empty,
                $"链序 {highestChained + 1}-{head.HeadIndex} 未落盘");
        }

        return new ProofIntegrityReport(
            entries.Count + unchained + unreadable,
            entries.Count,
            unchained,
            unreadable,
            expired,
            gaps,
            brokenLinks,
            issues.Count(issue => issue.Kind == ProofIntegrityIssueKind.Modified),
            beyondHead,
            missingTail,
            head.HeadIndex,
            head.RetainedFromIndex,
            truncated,
            issues,
            VerifyOwnReference(cancellationToken));
    }

    /// <summary>
    ///     Checks the fork's reference chain as a declaration, not as evidence: the node hash must still cover
    ///     the Up node it points at, and each pulse's seed increment factor must start at zero and rise by one.
    ///     These counters never make the report unhealthy — a reference node proves nothing on its own, so a
    ///     problem here is information for the reader rather than a failed verification.
    /// </summary>
    private OwnReferenceReport VerifyOwnReference(CancellationToken cancellationToken)
    {
        var head = ownChainStore.Read();
        var byPulse = new Dictionary<long, long>();
        var nodes = 0;
        var modified = 0;
        var sequenceViolations = 0;
        var timestamped = 0;
        var untimestamped = 0;

        foreach (var path in ownProofExporter.EnumeratePaths())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!ownProofExporter.TryRead(path, out var own) || own is null)
            {
                modified++;
                continue;
            }

            nodes++;
            var expected = OwnProofChainStore.ComputeSelfHash(
                own.Chain.Index,
                own.Chain.PrevHash,
                own.Up.ChainIndex,
                own.Up.ChainHash,
                own.Beacon.PulseIndex,
                own.Beacon.Sequence);
            if (!string.Equals(expected, own.Chain.SelfHash, StringComparison.Ordinal))
            {
                modified++;
                continue;
            }

            if (string.IsNullOrWhiteSpace(own.TimestampToken))
                untimestamped++;
            else
                timestamped++;

            // The preset: the first node of a pulse carries sequence 0 and every later node adds exactly one.
            if (!byPulse.TryGetValue(own.Beacon.PulseIndex, out var expectedSequence))
                expectedSequence = 0;
            if (own.Beacon.Sequence != expectedSequence)
                sequenceViolations++;
            byPulse[own.Beacon.PulseIndex] = own.Beacon.Sequence + 1;
        }

        return new OwnReferenceReport(nodes, modified, sequenceViolations, timestamped, untimestamped,
            head.HeadIndex, head.RetainedFromIndex);
    }

    private static void AddIssue(
        List<ProofIntegrityIssue> issues,
        ref bool truncated,
        ProofIntegrityIssueKind kind,
        string path,
        string detail)
    {
        if (issues.Count >= MaximumReportedIssues)
        {
            truncated = true;
            return;
        }

        issues.Add(new ProofIntegrityIssue(kind, path, detail));
    }

    private sealed record ChainedProof(string Path, long Index, string PrevHash, string SelfHash);
}

public enum ProofIntegrityIssueKind
{
    Unreadable,
    Modified,
    UnsupportedVersion,
    BrokenLink,
    Gap,
    BeyondHead,
    MissingTail
}

public sealed record ProofIntegrityIssue(ProofIntegrityIssueKind Kind, string Path, string Detail);

public sealed record ProofIntegrityReport(
    int Total,
    int Chained,
    int Unchained,
    int Unreadable,
    int Expired,
    int Gaps,
    int BrokenLinks,
    int Modified,
    int BeyondHead,
    int MissingTail,
    long HeadIndex,
    long RetainedFromIndex,
    bool IssuesTruncated,
    IReadOnlyList<ProofIntegrityIssue> Issues,
    OwnReferenceReport OwnReference)
{
    /// <summary>
    ///     Missing-tail entries are expected after a crash between the chain-head write and the proof write,
    ///     so they are reported but do not mark the chain unhealthy. Everything else is unexplained.
    /// </summary>
    public bool IsHealthy => Gaps == 0 && BrokenLinks == 0 && Modified == 0 && BeyondHead == 0;
}

/// <summary>
///     Status of the fork's reference chain. It is reported separately from the Up chain on purpose: this
///     chain is a supplementary declaration that a seed can be checked against an external beacon, so its
///     counts never turn the Up chain unhealthy.
/// </summary>
public sealed record OwnReferenceReport(
    int Nodes,
    int Modified,
    int SequenceViolations,
    int Timestamped,
    int Untimestamped,
    long HeadIndex,
    long RetainedFromIndex)
{
    public bool IsIntact => Modified == 0 && SequenceViolations == 0;
}
