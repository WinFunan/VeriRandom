using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using SecRandom.Shared;
using SecRandom.Shared.Models.Verification;

namespace SecRandom.Services.Verification;

/// <summary>
///     Owns the fork's reference-only evidence chain. It mirrors the Up chain one node at a time, and each
///     node hashes the Up node it depends on plus the beacon pulse index and the seed's increment factor
///     inside that pulse.
///     This chain is a supplementary source, not extra cryptographic assurance: it is used to declare that
///     a seed can be checked against an external beacon, never to decide whether a draw was fair.
/// </summary>
public sealed class OwnProofChainStore(ILogger<OwnProofChainStore> logger)
{
    public const int CurrentFormatVersion = 1;
    public const string HeadFileName = "own-chain-head.json";

    private static readonly byte[] DomainSeparator = Encoding.ASCII.GetBytes("SecRandomProof/v3/own-chain");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower) }
    };

    private readonly object _gate = new();
    private OwnChainHeadDocument? _head;
    private bool _headLoaded;

    /// <summary>
    ///     The preset a verifier applies: the node hash covers the Up node it depends on and the beacon
    ///     position, so re-pointing a reference node at another draw or another pulse breaks it.
    /// </summary>
    public static string ComputeSelfHash(
        long index,
        string previousHash,
        long upChainIndex,
        string upChainHash,
        long pulseIndex,
        long sequence)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(DomainSeparator);
        AppendInt64(hash, index);
        AppendString(hash, previousHash);
        AppendInt64(hash, upChainIndex);
        AppendString(hash, upChainHash);
        AppendInt64(hash, pulseIndex);
        AppendInt64(hash, sequence);
        return WitnessClient.ToBase64Url(hash.GetHashAndReset());
    }

    /// <summary>
    ///     Reserves the next reference position and advances the persisted head. As with the Up chain, the
    ///     head is written before the node, so a crash can leave at most one missing tail entry.
    /// </summary>
    public OwnProofChain NextChain(DrawProofChain upChain, string upProofHash, DrawProofBeacon beacon)
    {
        ArgumentNullException.ThrowIfNull(upChain);
        ArgumentNullException.ThrowIfNull(beacon);

        lock (_gate)
        {
            var head = EnsureHeadLocked();
            var index = head.HeadIndex + 1;
            var previousHash = head.HeadHash;
            var selfHash = ComputeSelfHash(
                index, previousHash, upChain.Index, upChain.SelfHash, beacon.PulseIndex, beacon.Sequence);
            head.HeadIndex = index;
            head.HeadHash = selfHash;
            PersistLocked();
            return new OwnProofChain
            {
                FormatVersion = CurrentFormatVersion,
                Index = index,
                PrevHash = previousHash,
                SelfHash = selfHash
            };
        }
    }

    /// <summary>
    ///     Records reference nodes the app itself removed together with their Up proof, so an absence the
    ///     app caused is not reported as tampering.
    /// </summary>
    public void RecordRemovedIndices(IEnumerable<long> indices)
    {
        ArgumentNullException.ThrowIfNull(indices);

        var removed = indices.Where(index => index > 0).ToArray();
        if (removed.Length == 0)
            return;

        lock (_gate)
        {
            var head = EnsureHeadLocked();
            var highest = removed.Max();
            if (highest <= head.RetainedFromIndex)
                return;

            head.RetainedFromIndex = highest;
            PersistLocked();
        }
    }

    public OwnChainHead Read()
    {
        lock (_gate)
        {
            var head = EnsureHeadLocked();
            return new OwnChainHead(head.FormatVersion, head.HeadIndex, head.HeadHash, head.RetainedFromIndex);
        }
    }

    private OwnChainHeadDocument EnsureHeadLocked()
    {
        if (_headLoaded && _head is not null)
            return _head;

        _headLoaded = true;
        _head = new OwnChainHeadDocument();
        var path = TryResolveHeadPath();
        if (path is null || !File.Exists(path))
            return _head;

        try
        {
            var loaded = JsonSerializer.Deserialize<OwnChainHeadDocument>(File.ReadAllText(path), JsonOptions);
            if (loaded is not null)
                _head = loaded;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            logger.LogWarning(exception, "自有参考链头读取失败，已从空链头继续。");
        }

        return _head;
    }

    private void PersistLocked()
    {
        var path = TryResolveHeadPath();
        if (path is null || _head is null)
            return;

        try
        {
            _head.FormatVersion = CurrentFormatVersion;
            var temporaryPath = path + $".{Guid.NewGuid():N}.tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(_head, JsonOptions));
            File.Move(temporaryPath, path, true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            logger.LogWarning(exception, "自有参考链头写入失败，参考证明文件仍会照常保存。");
        }
    }

    private string? TryResolveHeadPath()
    {
        try
        {
            return Utils.GetFilePath("proofs", HeadFileName);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            logger.LogDebug(exception, "无法解析自有参考链头路径。");
            return null;
        }
    }

    private static void AppendString(IncrementalHash hash, string? value)
    {
        var bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
        AppendInt32(hash, bytes.Length);
        hash.AppendData(bytes);
    }

    private static void AppendInt32(IncrementalHash hash, int value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(int)];
        BitConverter.TryWriteBytes(buffer, value);
        hash.AppendData(buffer);
    }

    private static void AppendInt64(IncrementalHash hash, long value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(long)];
        BitConverter.TryWriteBytes(buffer, value);
        hash.AppendData(buffer);
    }
}

public sealed record OwnChainHead(
    int FormatVersion,
    long HeadIndex,
    string HeadHash,
    long RetainedFromIndex);

internal sealed class OwnChainHeadDocument
{
    public int FormatVersion { get; set; } = OwnProofChainStore.CurrentFormatVersion;

    public long HeadIndex { get; set; }

    public string HeadHash { get; set; } = string.Empty;

    public long RetainedFromIndex { get; set; }
}
