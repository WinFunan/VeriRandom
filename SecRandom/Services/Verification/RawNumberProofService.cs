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

        var chainMatches = string.Equals(
            RawNumberProofHashing.ComputeSelfHash(
                proof.Index,
                proof.PrevHash,
                RawNumberProofHashing.ComputePayloadHash(proof)),
            proof.SelfHash,
            StringComparison.Ordinal);

        return new RawNumberVerification(seedMatches, valueMatches, chainMatches);
    }

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
    ///     Recomputes the recorded value(s) from the seed. A batch is recomputed as a batch — every index from the same
    ///     master seed — so a record whose middle entry was edited fails instead of passing on its first value alone.
    /// </summary>
    private static bool MatchesRecordedValues(RawNumberProof proof, byte[] seed)
    {
        if (proof.Results is { Count: > 1 } results)
        {
            var recomputed = proof.Values is { Count: > 0 } set
                ? RejectionSampler.SampleManyFromSet(set, seed, results.Count)
                : RejectionSampler.SampleMany(proof.LowerBound, proof.UpperBound, seed, results.Count);

            return recomputed.SequenceEqual(results);
        }

        var single = proof.Values is { Count: > 0 } singleSet
            ? RejectionSampler.SampleFromSet(singleSet, seed)
            : RejectionSampler.Sample(proof.LowerBound, proof.UpperBound, seed);

        return single == proof.Value;
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
public sealed record RawNumberVerification(bool SeedMatches, bool ValueMatches, bool ChainHashMatches)
{
    public bool Succeeded => SeedMatches && ValueMatches && ChainHashMatches;
}

/// <summary>One record as read back from disk.</summary>
public sealed record RawNumberProofFile(string Path, RawNumberProof Proof);

/// <summary>Result of checking every record on disk at once.</summary>
public sealed record RawNumberBatchVerification(
    int Total,
    IReadOnlyList<string> FailedFileNames,
    long LastIndex);
