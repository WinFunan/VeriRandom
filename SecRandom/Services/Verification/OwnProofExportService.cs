using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using SecRandom.Shared;
using SecRandom.Shared.Models.Verification;

namespace SecRandom.Services.Verification;

/// <summary>
///     File naming for the fork's reference proof, which is always a sibling of the Up proof it refers to.
///     Keeping the pair adjacent means the existing retention and storage-limit cleanup removes them together.
/// </summary>
public static class OwnProofPaths
{
    public const string UpExtension = ".srproof.json";
    public const string Extension = ".ownproof.json";
    public const string SearchPattern = "*.ownproof.json";

    public static string FromUpPath(string upPath) =>
        upPath.EndsWith(UpExtension, StringComparison.OrdinalIgnoreCase)
            ? string.Concat(upPath.AsSpan(0, upPath.Length - UpExtension.Length), Extension)
            : upPath + Extension;
}

/// <summary>
///     Writes and reads the fork-owned reference proof. It never blocks a draw: a failure here only means the
///     reference declaration is missing, while the Up proof stays complete and independently checkable.
/// </summary>
public sealed class OwnProofExportService(
    OwnProofChainStore chainStore,
    ILogger<OwnProofExportService> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower) }
    };

    /// <summary>
    ///     Builds the reference node for an already-chained Up proof. The reference depends on the Up node's
    ///     chain hash, so it can never be re-pointed at a different draw afterwards.
    /// </summary>
    public OwnProof Create(DrawProof chainedUpProof, DrawProofBeacon beacon)
    {
        ArgumentNullException.ThrowIfNull(chainedUpProof);
        ArgumentNullException.ThrowIfNull(beacon);
        var upChain = chainedUpProof.Chain
            ?? throw new InvalidOperationException("The Up proof has no chain position to refer to.");

        return new OwnProof
        {
            ProofId = chainedUpProof.ProofId,
            CreatedAtUtc = chainedUpProof.CreatedAtUtc,
            Up = new OwnProofUpReference
            {
                ChainIndex = upChain.Index,
                ChainHash = upChain.SelfHash,
                ProofHash = WitnessClient.ComputeAttestedProofHash(chainedUpProof)
            },
            Beacon = beacon,
            Chain = chainStore.NextChain(upChain, WitnessClient.ComputeAttestedProofHash(chainedUpProof), beacon)
        };
    }

    public string Save(string upPath, OwnProof own)
    {
        var path = OwnProofPaths.FromUpPath(upPath);
        SaveAtPath(path, own);
        logger.LogInformation(
            "已导出自有参考证明：ProofId={ProofId}，参考链序={OwnIndex}，上游链序={UpIndex}，信标脉冲={PulseIndex}，序号={Sequence}，路径={Path}。",
            own.ProofId, own.Chain.Index, own.Up.ChainIndex, own.Beacon.PulseIndex, own.Beacon.Sequence, path);
        return path;
    }

    public void SaveAtPath(string path, OwnProof own)
    {
        var temporaryPath = path + $".{Guid.NewGuid():N}.tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(own, JsonOptions));
        File.Move(temporaryPath, path, true);
    }

    /// <summary>
    ///     Records one reference node the app removed together with its Up proof.
    /// </summary>
    public void RecordRemoved(long index) => chainStore.RecordRemovedIndices([index]);

    public IEnumerable<string> EnumeratePaths()
    {
        var root = Utils.GetDirectoryPath("proofs");
        return Directory.Exists(root)
            ? Directory.EnumerateFiles(root, OwnProofPaths.SearchPattern, SearchOption.AllDirectories)
            : [];
    }

    public bool TryRead(string path, out OwnProof? own)
    {
        try
        {
            own = JsonSerializer.Deserialize<OwnProof>(File.ReadAllText(path), JsonOptions);
            return own is not null;
        }
        catch (IOException)
        {
            own = null;
            return false;
        }
        catch (JsonException)
        {
            own = null;
            return false;
        }
    }

    /// <summary>
    ///     Removes reference nodes whose Up proof is gone. The reference chain is not worth keeping on its own,
    ///     and the removal is recorded so it is not reported as an unexplained gap.
    /// </summary>
    public void RemoveOrphans()
    {
        List<long> removed = [];
        foreach (var path in EnumeratePaths())
        {
            var upPath = path[..^OwnProofPaths.Extension.Length] + OwnProofPaths.UpExtension;
            if (File.Exists(upPath))
                continue;

            try
            {
                if (TryRead(path, out var own) && own is not null)
                    removed.Add(own.Chain.Index);
                File.Delete(path);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                logger.LogDebug(exception, "跳过正在使用的自有参考证明文件：{Path}。", path);
            }
        }

        chainStore.RecordRemovedIndices(removed);
    }
}
