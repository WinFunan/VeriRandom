using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Models.Verification;
using SecRandom.Core.Services.Config;
using SecRandom.Core.Services.Verification;
using SecRandom.Shared;
using SecRandom.Shared.Models.Verification;

namespace SecRandom.Services.Verification;

public sealed class DrawProofExportService(
    MainConfigHandler configHandler,
    ProofChainStore chainStore,
    OwnProofExportService ownProofExporter,
    GateEvidenceExportService gateEvidenceExporter,
    ILogger<DrawProofExportService> logger)
{
    private const int MaximumFileNameLength = 240;
    private static readonly TimeZoneInfo ChinaStandardTime = GetChinaStandardTime();
    private static readonly HashSet<char> InvalidFileNameCharacters = "<>:\"/\\|?*"
        .Concat(Path.GetInvalidFileNameChars())
        .ToHashSet();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower) }
    };

    /// <summary>
    ///     Writes the Up proof, the gate evidence when the average-gap gate removed somebody, and — when the draw took
    ///     its seed from a beacon pulse — the reference proof that sits beside it. The Up proof never carries the beacon:
    ///     the reference node is the only place the pulse and the seed's increment factor are recorded.
    /// </summary>
    public DrawProofExportResult Save(
        DrawProof proof,
        DrawProofExportContext context,
        DrawProofBeacon? beacon = null,
        GateEvidenceSnapshot? gateEvidence = null)
    {
        RemoveExpiredProofs(configHandler.Data.General.ProofRetention.RetentionDays);
        var chained = proof with { Chain = chainStore.NextChain(proof), Beacon = null };
        var timestamp = TimeZoneInfo.ConvertTime(chained.CreatedAtUtc, ChinaStandardTime);
        var path = Utils.GetFilePath(
            "proofs",
            timestamp.ToString("yyyy-MM"),
            timestamp.ToString("yyyy-MM-dd"),
            CreateFileName(chained, context));
        SaveAtPath(path, chained);
        string? ownPath = null;
        if (beacon is not null)
        {
            try
            {
                ownPath = ownProofExporter.Save(path, ownProofExporter.Create(chained, beacon));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or NotSupportedException or InvalidOperationException)
            {
                // The reference declaration is supplementary: losing it must never invalidate a completed draw.
                logger.LogWarning(exception, "自有参考证明导出失败，上游证明不受影响。ProofId={ProofId}", chained.ProofId);
            }
        }

        // Written after the Up file and before any cleanup: nothing can delete the pair in between, so an evidence file
        // can never end up describing a proof that does not exist.
        string? gatePath = null;
        if (gateEvidence is not null)
        {
            try
            {
                gatePath = gateEvidenceExporter.Save(path, gateEvidenceExporter.Create(chained, gateEvidence));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or NotSupportedException or InvalidOperationException)
            {
                // Same rule as the reference node: the draw already succeeded, so its evidence missing must not fail it.
                logger.LogWarning(exception, "门槛证据导出失败，上游证明不受影响。ProofId={ProofId}", chained.ProofId);
            }
        }

        // Upstream now protects the file this save just wrote from the storage-limit cleanup.
        RemoveProofsOverStorageLimit(configHandler.Data.General.ProofRetention.MaximumStorageBytes, path);
        logger.LogInformation(
            "已导出抽取证明：ProofId={ProofId}，模式={Mode}，链序={ChainIndex}，路径={Path}。",
            chained.ProofId, chained.Mode, chained.Chain?.Index, path);
        return new DrawProofExportResult(path, chained, ownPath, gatePath);
    }

    public void SaveAtPath(string path, DrawProof proof)
    {
        var temporaryPath = path + $".{Guid.NewGuid():N}.tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(proof, JsonOptions));
        File.Move(temporaryPath, path, true);
    }

    public IEnumerable<string> EnumerateProofPaths()
    {
        var root = Utils.GetDirectoryPath("proofs");
        return Directory.Exists(root)
            ? Directory.EnumerateFiles(root, "*.srproof.json", SearchOption.AllDirectories)
            : [];
    }

    public bool TryRead(string path, out DrawProof? proof)
    {
        try
        {
            proof = JsonSerializer.Deserialize<DrawProof>(File.ReadAllText(path), JsonOptions);
            return proof is not null;
        }
        catch (IOException)
        {
            proof = null;
            return false;
        }
        catch (JsonException)
        {
            proof = null;
            return false;
        }
    }

    public static string CreateFileName(DrawProof proof, DrawProofExportContext context)
    {
        var timestamp = TimeZoneInfo.ConvertTime(proof.CreatedAtUtc, ChinaStandardTime);
        var listName = SanitizeFilePart(context.ListName, "未命名名单");
        var filters = context.FilterLabels
            .Where(filter => !string.IsNullOrWhiteSpace(filter))
            .Where(filter => !filter.StartsWith("状态=", StringComparison.Ordinal))
            .Select(filter => SanitizeFilePart(filter, string.Empty))
            .Where(filter => !string.IsNullOrWhiteSpace(filter))
            .ToArray();
        if (VerificationWireCodec.TryGetAlgorithmLabel(proof.AlgorithmId, out var algorithmLabel))
            filters = [algorithmLabel, .. filters];
        var filterText = SanitizeFilePart(
            filters.Length == 0 ? "全部" : string.Join("、", filters),
            "全部");
        var fixedLength = timestamp.ToString("yyyyMMdd_HHmmss_fff").Length
            + proof.ProofId.ToString("N")[..8].Length
            + ".srproof.json".Length
            + 3;
        var availableLength = MaximumFileNameLength - fixedLength;
        listName = Truncate(listName, Math.Max(1, availableLength / 2));
        filterText = Truncate(filterText, Math.Max(1, availableLength - listName.Length));

        return $"{timestamp:yyyyMMdd_HHmmss_fff}_{listName}_{filterText}_{proof.ProofId.ToString("N")[..8]}.srproof.json";
    }

    private void RemoveExpiredProofs(int retentionDays)
    {
        if (retentionDays <= 0)
            return;

        var root = Utils.GetDirectoryPath("proofs");
        if (!Directory.Exists(root))
            return;

        var cutoff = DateTime.UtcNow.AddDays(-retentionDays);
        List<long> removed = [];
        foreach (var path in Directory.EnumerateFiles(root, "*.srproof.json", SearchOption.AllDirectories))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(path) < cutoff)
                {
                    if (TryReadChainIndex(path, out var chainIndex))
                        removed.Add(chainIndex);
                    File.Delete(path);
                    DeleteSiblings(path);
                }
            }
            catch (IOException exception)
            {
                logger.LogDebug(exception, "跳过正在使用的过期证明文件：{Path}。", path);
            }
            catch (UnauthorizedAccessException exception)
            {
                logger.LogDebug(exception, "无权删除过期证明文件：{Path}。", path);
            }
        }

        chainStore.RecordRemovedIndices(removed, ProofChainEvent.RetentionCleanup);
    }

    private void RemoveProofsOverStorageLimit(long maximumStorageBytes, string protectedPath)
    {
        if (maximumStorageBytes <= 0)
            return;

        var root = Utils.GetDirectoryPath("proofs");
        if (!Directory.Exists(root))
            return;

        var files = Directory.EnumerateFiles(root, "*.srproof.json", SearchOption.AllDirectories)
            .Select(path => new FileInfo(path))
            .OrderBy(file => file.LastWriteTimeUtc)
            .ThenBy(file => file.FullName, StringComparer.Ordinal)
            .ToList();
        var totalBytes = files.Sum(file => file.Length);
        List<long> removed = [];
        foreach (var file in files)
        {
            if (totalBytes <= maximumStorageBytes)
                break;

            // The proof saved by this very call must survive its own cleanup: deleting it would leave the
            // chain head pointing at a file that never landed (a self-inflicted missing tail) and hand the
            // attestation queue a path that no longer exists.
            if (string.Equals(file.FullName, protectedPath, StringComparison.Ordinal))
                continue;

            try
            {
                var length = file.Length;
                if (TryReadChainIndex(file.FullName, out var chainIndex))
                    removed.Add(chainIndex);
                file.Delete();
                DeleteSiblings(file.FullName);
                totalBytes -= length;
            }
            catch (IOException exception)
            {
                logger.LogDebug(exception, "跳过正在使用的超限证明文件：{Path}。", file.FullName);
            }
            catch (UnauthorizedAccessException exception)
            {
                logger.LogDebug(exception, "无权删除超限证明文件：{Path}。", file.FullName);
            }
        }

        chainStore.RecordRemovedIndices(removed, ProofChainEvent.StorageLimitCleanup);
    }

    private bool TryReadChainIndex(string path, out long chainIndex)
    {
        chainIndex = 0;
        if (!TryRead(path, out var proof) || proof?.Chain is null)
            return false;

        chainIndex = proof.Chain.Index;
        return true;
    }

    /// <summary>
    ///     Removes every sibling a proof owns, from both cleanup paths. Kept as one call so a future sibling cannot be
    ///     forgotten in one of the two sites; the Own reference already had a single home and each new sibling adds a
    ///     second delete that is easy to miss.
    /// </summary>
    private void DeleteSiblings(string upPath)
    {
        DeleteOwnSibling(upPath);
        DeleteGateEvidenceSibling(upPath);
    }

    private void DeleteGateEvidenceSibling(string upPath)
    {
        try
        {
            File.Delete(GateEvidencePaths.FromUpPath(upPath));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The gate evidence has no chain of its own, so nothing has to be recorded when it disappears.
            logger.LogDebug(exception, "跳过正在使用的门槛证据文件：{Path}。", upPath);
        }
    }

    private void DeleteOwnSibling(string upPath)
    {
        try
        {
            var ownPath = OwnProofPaths.FromUpPath(upPath);
            if (File.Exists(ownPath) && ownProofExporter.TryRead(ownPath, out var own) && own is not null)
                ownProofExporter.RecordRemoved(own.Chain.Index);
            File.Delete(ownPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogDebug(exception, "跳过正在使用的自有参考证明文件：{Path}。", upPath);
        }
    }

    private static TimeZoneInfo GetChinaStandardTime()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai");
        }
    }

    private static string SanitizeFilePart(string value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;

        StringBuilder builder = new();
        foreach (var character in value.Trim())
        {
            builder.Append(character switch
            {
                ':' or '：' or ',' or '，' or '、' => '_',
                _ when char.IsControl(character) || InvalidFileNameCharacters.Contains(character) => '_',
                _ => character
            });
        }

        var sanitized = builder.ToString().Trim(' ', '.');
        if (string.IsNullOrWhiteSpace(sanitized))
            return fallback;

        return Truncate(sanitized, 80);
    }

    private static string Truncate(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : value[..maximumLength];

}

public sealed record DrawProofExportResult(
    string Path,
    DrawProof Proof,
    string? OwnPath = null,
    string? GateEvidencePath = null);

public sealed record DrawProofExportContext(string ListName, IReadOnlyList<string> FilterLabels)
{
    public static DrawProofExportContext ForStudents(string listName, string group = "", string gender = "", string courseName = "")
    {
        List<string> filters = [];
        if (!string.IsNullOrWhiteSpace(group))
            filters.Add($"组别={group}");
        if (!string.IsNullOrWhiteSpace(gender))
            filters.Add($"性别={gender}");
        if (!string.IsNullOrWhiteSpace(courseName))
            filters.Add($"课程={courseName}");
        return new DrawProofExportContext(listName, filters);
    }

    public static DrawProofExportContext ForPrizes(string listName) =>
        new(listName, []);
}
