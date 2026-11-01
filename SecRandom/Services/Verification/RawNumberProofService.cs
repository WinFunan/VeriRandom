using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Models.Verification;
using SecRandom.Core.Services.Draw;
using SecRandom.Core.Services.Verification;
using SecRandom.Shared;

namespace SecRandom.Services.Verification;

/// <summary>
///     Writes raw-number records to <c>data/proofs/raw</c> and checks them again from the file.
/// </summary>
/// <remarks>
///     <para>
///         The record is a hash chain of its own (<c>chain-head.json</c> beside the nodes), mirroring the draw proof
///         chain but kept apart from it: <c>ProofIntegrityVerifier</c> judges the draw chain by replaying the draw
///         kernel, and feeding it files it cannot decode would turn every raw number into a reported gap.
///     </para>
///     <para>
///         <see cref="SaveAsync" /> verifies what it just wrote before returning. A record that cannot be recomputed
///         from its own contents is worthless as evidence, and discovering that at write time is far cheaper than
///         discovering it when someone tries to check it.
///     </para>
/// </remarks>
public sealed class RawNumberProofService(ILogger<RawNumberProofService> logger)
{
    private const string HeadFileName = "chain-head.json";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>Writes the record, links it to the chain head, and returns the file path.</summary>
    public async Task<string> SaveAsync(RawNumberProof proof, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(proof);

        var head = await LoadHeadAsync(cancellationToken).ConfigureAwait(false);

        proof.Index = head.Index + 1;
        proof.PrevHash = head.SelfHash ?? string.Empty;
        proof.CreatedAt = proof.CreatedAt == default ? DateTimeOffset.UtcNow : proof.CreatedAt;
        proof.ProofId = string.IsNullOrWhiteSpace(proof.ProofId) ? Guid.NewGuid().ToString("N") : proof.ProofId;
        proof.SelfHash = RawNumberProofHashing.ComputeSelfHash(
            proof.Index,
            proof.PrevHash,
            RawNumberProofHashing.ComputePayloadHash(proof));

        var directory = ProofDirectory;
        Directory.CreateDirectory(directory);

        var fileName = string.Create(
            CultureInfo.InvariantCulture,
            $"raw-{proof.Index:D6}-{proof.CreatedAt:yyyyMMddHHmmss}.rawproof.json");
        var path = Path.Combine(directory, fileName);

        await WriteAtomicAsync(path, JsonSerializer.Serialize(proof, SerializerOptions), cancellationToken)
            .ConfigureAwait(false);

        // The head moves only after the node is on disk: a crash leaves an unreferenced file, never a head pointing at
        // something that does not exist.
        var headPath = Path.Combine(directory, HeadFileName);
        await WriteAtomicAsync(
                headPath,
                JsonSerializer.Serialize(new RawNumberChainHead(proof.Index, proof.SelfHash), SerializerOptions),
                cancellationToken)
            .ConfigureAwait(false);

        var verification = Verify(proof);
        if (!verification.Succeeded)
            logger.LogWarning("裸随机数记录写入后自校验未通过：种子 {Seed} 结果 {Value} 链哈希 {Chain}",
                verification.SeedMatches, verification.ValueMatches, verification.ChainHashMatches);

        return path;
    }

    /// <summary>
    ///     Recomputes the seed and the value from what the record itself carries, and re-derives its chain hash.
    ///     This is the only check a raw-number record can pass on its own: it does not and cannot re-fetch the source.
    /// </summary>
    public RawNumberVerification Verify(RawNumberProof proof)
    {
        ArgumentNullException.ThrowIfNull(proof);

        if (proof.IsEntropyRecord)
        {
            var entropySeed = DeriveEntropySeed(proof);
            var entropySeedMatches = entropySeed is not null && string.Equals(
                RawNumberSeedDerivation.Digest(entropySeed),
                proof.SeedDigest,
                StringComparison.Ordinal);

            // No value is compared: an entropy record deliberately stores none, because the text on screen is a
            // presentation of the seed rather than a result to recompute. `ValueApplicable` is what stops `Succeeded`
            // from reading that placeholder as a failed check.
            return new RawNumberVerification(entropySeedMatches, false, ChainHashMatches(proof), ValueApplicable: false);
        }

        var inputs = proof.Entropy
            .Select(entropy => new RawNumberEntropyInput(entropy.SourceId, entropy.OutputValue))
            .ToList();

        if (inputs.Count == 0)
            return new RawNumberVerification(false, false, false);

        var seed = RawNumberSeedDerivation.Derive(inputs);
        var seedMatches = string.Equals(
            RawNumberSeedDerivation.Digest(seed),
            proof.SeedDigest,
            StringComparison.Ordinal);

        var valueMatches = seedMatches && MatchesRecordedValues(proof, seed);

        return new RawNumberVerification(seedMatches, valueMatches, ChainHashMatches(proof));
    }

    private static bool ChainHashMatches(RawNumberProof proof) => string.Equals(
        RawNumberProofHashing.ComputeSelfHash(
            proof.Index,
            proof.PrevHash,
            RawNumberProofHashing.ComputePayloadHash(proof)),
        proof.SelfHash,
        StringComparison.Ordinal);

    /// <summary>
    ///     Every raw-number record on disk, ordered by chain position. They live in their own folder and their own
    ///     chain, because a raw number has no candidate pool for the draw replay to work on.
    /// </summary>
    public IReadOnlyList<RawNumberProofFile> LoadAll()
    {
        var directory = ProofDirectory;
        if (!Directory.Exists(directory))
            return [];

        var files = new List<RawNumberProofFile>();
        foreach (var path in Directory.EnumerateFiles(directory, "*.rawproof.json", SearchOption.AllDirectories))
        {
            try
            {
                var proof = JsonSerializer.Deserialize<RawNumberProof>(
                    File.ReadAllText(path),
                    SerializerOptions);
                if (proof is not null)
                    files.Add(new RawNumberProofFile(path, proof));
            }
            catch (Exception exception) when (exception is IOException or JsonException)
            {
                logger.LogWarning(exception, "无法读取裸随机数记录 {Path}", path);
            }
        }

        return files.OrderBy(file => file.Proof.Index).ToArray();
    }

    /// <summary>
    ///     Recomputes every record from its own contents. This is the strongest check a raw-number record admits:
    ///     it proves the file still describes the number it claims, not that the entropy source behaved.
    /// </summary>
    public RawNumberBatchVerification VerifyAll()
    {
        var files = LoadAll();
        var failed = new List<string>();

        foreach (var file in files)
            if (!Verify(file.Proof).Succeeded)
            {
                failed.Add(Path.GetFileName(file.Path));
                logger.LogWarning("裸随机数记录自校验失败：{Path}", file.Path);
            }

        return new RawNumberBatchVerification(
            files.Count,
            failed,
            files.Count == 0 ? 0 : files[^1].Proof.Index);
    }

    /// <summary>
    ///     Recomputes the value(s) a record should have produced. Separate from <see cref="Verify" /> because replay is
    ///     a question about the <em>result</em> — "what does this seed actually give" — while verification is about the
    ///     file, and a reviewer wants to see both numbers rather than only the verdict.
    /// </summary>
    public RawNumberReplay Replay(RawNumberProof proof)
    {
        ArgumentNullException.ThrowIfNull(proof);

        var inputs = proof.Entropy
            .Select(entropy => new RawNumberEntropyInput(entropy.SourceId, entropy.OutputValue))
            .ToList();

        if (inputs.Count == 0)
            return new RawNumberReplay([], false);

        var seed = RawNumberSeedDerivation.Derive(inputs);
        var recorded = proof.Results is { Count: > 0 } results ? results : [proof.Value];
        var count = recorded.Count;

        var recomputed = Recompute(proof, seed, count);

        return new RawNumberReplay(recomputed, recomputed.SequenceEqual(recorded));
    }

    /// <summary>
    ///     Recomputes the seed digest an entropy record claims. This is the only check such a record admits: it never
    ///     stored a value to compare, so "what number did it produce" cannot be answered from the file at all.
    /// </summary>
    public RawNumberDigestReplay ReplayEntropy(RawNumberProof proof)
    {
        ArgumentNullException.ThrowIfNull(proof);

        var seed = DeriveEntropySeed(proof);
        var recomputed = seed is null ? string.Empty : RawNumberSeedDerivation.Digest(seed);

        var verdict = recomputed.Length == 0
            ? RawDigestVerdict.Unverifiable
            : string.Equals(recomputed, proof.SeedDigest, StringComparison.Ordinal)
                ? RawDigestVerdict.Matches
                // A record written before the round size existed can hold several rounds in one flat list, and no
                // verifier can regroup that. Calling it a mismatch would accuse a record for being old.
                : proof.AdvancedOutput
                    ? RawDigestVerdict.Mismatch
                    : RawDigestVerdict.Unverifiable;

        return new RawNumberDigestReplay(verdict, proof.SeedDigest, recomputed);
    }

    /// <summary>
    ///     Rebuilds the seed an entropy record was made from: one derivation per collection round, concatenated in
    ///     round order. A record without a recorded round size is treated as one round, which is exactly how single-round
    ///     records were written.
    /// </summary>
    private static byte[]? DeriveEntropySeed(RawNumberProof proof)
    {
        var entries = proof.Entropy;
        if (entries.Count == 0)
            return null;

        var roundSize = proof.EntropyRoundSize > 0
            ? Math.Min(proof.EntropyRoundSize, entries.Count)
            : entries.Count;

        var material = new List<byte>(entries.Count * 32);

        for (var start = 0; start < entries.Count; start += roundSize)
        {
            var round = entries
                .Skip(start)
                .Take(roundSize)
                .Select(entry => new RawNumberEntropyInput(entry.SourceId, entry.OutputValue))
                .ToList();

            material.AddRange(RawNumberSeedDerivation.Derive(round));
        }

        return material.ToArray();
    }

    /// <summary>
    ///     Recomputes the recorded value(s) from the seed. A batch is recomputed as a batch — every index from the same
    ///     master seed — so a record whose middle entry was edited fails instead of passing on its first value alone.
    /// </summary>
    private static bool MatchesRecordedValues(RawNumberProof proof, byte[] seed)
    {
        var recorded = proof.Results is { Count: > 0 } results ? results : [proof.Value];
        return Recompute(proof, seed, recorded.Count).SequenceEqual(recorded);
    }

    /// <summary>
    ///     Recomputation order: a recorded interval space wins, then a plain explicit set, then the range. The space
    ///     case is what makes "1-1000000 excluding 7" replayable without storing a million values — the intervals are
    ///     the space, exactly as they were resolved at draw time.
    /// </summary>
    private static long[] Recompute(RawNumberProof proof, byte[] seed, int count)
    {
        if (proof.Intervals is { Count: > 0 } intervals)
        {
            var space = RawCandidateSpec.FromIntervals(intervals.Select(pair => (pair[0], pair[1])));
            return space.SampleMany(seed, count);
        }

        return proof.Values is { Count: > 0 } set
            ? RejectionSampler.SampleManyFromSet(set, seed, count)
            : RejectionSampler.SampleMany(proof.LowerBound, proof.UpperBound, seed, count);
    }

    private static string ProofDirectory => Utils.GetFilePath("proofs", "raw");

    private static async Task<RawNumberChainHead> LoadHeadAsync(CancellationToken cancellationToken)
    {
        var path = Path.Combine(ProofDirectory, HeadFileName);
        if (!File.Exists(path))
            return new RawNumberChainHead(0, null);

        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var head = await JsonSerializer
                .DeserializeAsync<RawNumberChainHead>(stream, SerializerOptions, cancellationToken)
                .ConfigureAwait(false);

            return head ?? new RawNumberChainHead(0, null);
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            // A damaged head must not let two records claim the same position: start above everything already named,
            // so a rebuild continues the visible numbering instead of overwriting it.
            return new RawNumberChainHead(HighestExistingIndex(), null);
        }
    }

    private static long HighestExistingIndex()
    {
        if (!Directory.Exists(ProofDirectory))
            return 0;

        long highest = 0;
        foreach (var file in Directory.EnumerateFiles(ProofDirectory, "raw-*.rawproof.json"))
        {
            var name = Path.GetFileName(file);
            var parts = name.Split('-');
            if (parts.Length > 1 && long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var index))
                highest = Math.Max(highest, index);
        }

        return highest;
    }

    private static async Task WriteAtomicAsync(string path, string content, CancellationToken cancellationToken)
    {
        var temporary = path + ".tmp";
        await File.WriteAllTextAsync(temporary, content, cancellationToken).ConfigureAwait(false);
        File.Move(temporary, path, true);
    }

    private sealed record RawNumberChainHead(long Index, string? SelfHash);
}

/// <summary>Outcome of recomputing a record from its own contents.</summary>
/// <param name="ValueApplicable">
///     False for an entropy record, which stores no value to compare. <see cref="Succeeded" /> must then judge the
///     record on its seed and chain alone instead of reading the placeholder as a failed check.
/// </param>
public sealed record RawNumberVerification(
    bool SeedMatches,
    bool ValueMatches,
    bool ChainHashMatches,
    bool ValueApplicable = true)
{
    public bool Succeeded => SeedMatches && ChainHashMatches && (!ValueApplicable || ValueMatches);
}

/// <summary>How an entropy record's seed digest compares with the digest stored in it.</summary>
public enum RawDigestVerdict
{
    /// <summary>Rebuilding from the recorded entropy reproduces the recorded digest.</summary>
    Matches,

    /// <summary>The record is an entropy record and its digest does not rebuild.</summary>
    Mismatch,

    /// <summary>No rebuild is possible: no entropy recorded, or a record older than the round structure.</summary>
    Unverifiable
}

/// <summary>Result of checking an entropy record's seed digest.</summary>
public sealed record RawNumberDigestReplay(
    RawDigestVerdict Verdict,
    string RecordedDigest,
    string RecomputedDigest);

/// <summary>One record as read back from disk.</summary>
public sealed record RawNumberProofFile(string Path, RawNumberProof Proof);

/// <summary>Result of checking every record on disk at once.</summary>
public sealed record RawNumberBatchVerification(
    int Total,
    IReadOnlyList<string> FailedFileNames,
    long LastIndex);

/// <summary>What a raw-number record's seed actually produces.</summary>
public sealed record RawNumberReplay(IReadOnlyList<long> Recomputed, bool Matches);
