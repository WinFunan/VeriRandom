using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Models.Verification;
using SecRandom.Shared;
using SecRandom.Shared.Models.Verification;

namespace SecRandom.Services.Verification;

/// <summary>
///     File naming for the fork's gate evidence, which is always a sibling of the Up proof it describes.
///     Keeping the pair adjacent is what lets the existing retention and storage-limit cleanup remove them together.
/// </summary>
public static class GateEvidencePaths
{
    public const string UpExtension = ".srproof.json";
    public const string Extension = ".gateevidence.json";
    public const string SearchPattern = "*.gateevidence.json";

    public static string FromUpPath(string upPath) =>
        upPath.EndsWith(UpExtension, StringComparison.OrdinalIgnoreCase)
            ? string.Concat(upPath.AsSpan(0, upPath.Length - UpExtension.Length), Extension)
            : upPath + Extension;

    /// <summary>Inverse of <see cref="FromUpPath" />, so an orphan sweep never has to slice strings itself.</summary>
    public static string ToUpPath(string gatePath) =>
        gatePath.EndsWith(Extension, StringComparison.OrdinalIgnoreCase)
            ? string.Concat(gatePath.AsSpan(0, gatePath.Length - Extension.Length), UpExtension)
            : gatePath;
}

/// <summary>
///     Writes and reads the fork-owned gate evidence. It can never block a draw: a failure here only costs the record
///     that says which students the gate removed, while the Up proof stays complete and independently checkable.
/// </summary>
public sealed class GateEvidenceExportService(ILogger<GateEvidenceExportService> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    /// <summary>
    ///     Binds the captured facts to an already-chained Up proof. The binding is the Up node's chain hash, which is
    ///     anchored in the Up chain, so this record can never be re-pointed at a different draw afterwards.
    /// </summary>
    public GateEvidence Create(DrawProof chainedUpProof, GateEvidenceSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(chainedUpProof);
        ArgumentNullException.ThrowIfNull(snapshot);

        var upChain = chainedUpProof.Chain
                      ?? throw new InvalidOperationException("The Up proof has no chain position to refer to.");

        // Sorted by record id so two writers that enumerated the roster differently still produce the same hashed bytes.
        var removed = snapshot.Removed
            .OrderBy(member => member.RecordId.ToString("N"), StringComparer.Ordinal)
            .ToArray();

        var evidence = new GateEvidence
        {
            ProofId = chainedUpProof.ProofId,
            UpChainIndex = upChain.Index,
            UpChainHash = upChain.SelfHash,
            GateRuleId = snapshot.GateRuleId,
            Threshold = snapshot.Threshold,
            CandidateCountBefore = snapshot.CandidateCountBefore,
            CandidateCountAfter = snapshot.CandidateCountAfter,
            Removed = removed,
            CreatedAtUtc = chainedUpProof.CreatedAtUtc
        };

        return evidence with { SelfHash = GateEvidenceHashing.ComputeSelfHash(evidence) };
    }

    public string Save(string upPath, GateEvidence evidence)
    {
        var path = GateEvidencePaths.FromUpPath(upPath);
        SaveAtPath(path, evidence);
        logger.LogInformation(
            "已导出平均差距门槛证据：ProofId={ProofId}，上游链序={UpIndex}，门槛={Threshold}，移除={Removed} 人，路径={Path}。",
            evidence.ProofId, evidence.UpChainIndex, evidence.Threshold, evidence.Removed.Count, path);
        return path;
    }

    public void SaveAtPath(string path, GateEvidence evidence)
    {
        var temporaryPath = path + $".{Guid.NewGuid():N}.tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(evidence, JsonOptions));
        File.Move(temporaryPath, path, true);
    }

    public IEnumerable<string> EnumeratePaths()
    {
        var root = Utils.GetDirectoryPath("proofs");
        return Directory.Exists(root)
            ? Directory.EnumerateFiles(root, GateEvidencePaths.SearchPattern, SearchOption.AllDirectories)
            : [];
    }

    /// <summary>
    ///     Absence and corruption both mean "no gate evidence available"; neither is an error a draw or a window has to
    ///     handle, and neither may be reported as a pass.
    /// </summary>
    public bool TryRead(string path, out GateEvidence? evidence)
    {
        try
        {
            evidence = JsonSerializer.Deserialize<GateEvidence>(File.ReadAllText(path), JsonOptions);
            return evidence is not null;
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            evidence = null;
            return false;
        }
    }

    /// <summary>Reads the evidence that belongs to one Up proof, or null when it has none.</summary>
    public GateEvidence? TryReadForUpPath(string upPath) =>
        TryRead(GateEvidencePaths.FromUpPath(upPath), out var evidence) ? evidence : null;
}
