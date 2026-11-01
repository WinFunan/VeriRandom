using System;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SecRandom.Core.Models.Verification;

/// <summary>
///     Read-only projection of the anonymous audit payload embedded in a proof.
/// </summary>
/// <remarks>
///     <para>
///         These values are <b>what the proof claims</b>, not something independently checked: the audit payload is the
///         draw engine's own record of how it prepared the candidate pool. Only replaying the recorded seed through the
///         same candidate pool turns a claim into evidence, which is why any surface that shows them must say so.
///     </para>
///     <para>
///         Only the fields a verifier reads are projected. The per-candidate array is deliberately left out: it is
///         anonymous by construction, but it is also large, and no part of the details view needs it.
///     </para>
/// </remarks>
public sealed class AnonymizedAuditPayload
{
    [JsonPropertyName("format")]
    public string Format { get; init; } = string.Empty;

    [JsonPropertyName("operation")]
    public string Operation { get; init; } = string.Empty;

    [JsonPropertyName("requestedCount")]
    public int RequestedCount { get; init; }

    [JsonPropertyName("candidateCount")]
    public int CandidateCount { get; init; }

    [JsonPropertyName("fairness")]
    public AnonymizedAuditFairness? Fairness { get; init; }

    [JsonPropertyName("rosterDigest")]
    public string? RosterDigest { get; init; }

    /// <summary>Parses the audit bytes. Returns null for anything this build cannot read rather than throwing.</summary>
    public static AnonymizedAuditPayload? TryParse(ReadOnlySpan<byte> utf8Json)
    {
        if (utf8Json.IsEmpty)
            return null;

        try
        {
            return JsonSerializer.Deserialize<AnonymizedAuditPayload>(utf8Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Parses the base64url form a proof file stores.</summary>
    public static AnonymizedAuditPayload? TryParseBase64Url(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        try
        {
            return TryParse(DecodeBase64Url(text));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>Base64url decode helper shared with the replay service.</summary>
    public static byte[] DecodeBase64Url(string text)
    {
        var padded = text.Trim().Replace('-', '+').Replace('_', '/');
        padded += (padded.Length % 4) switch
        {
            2 => "==",
            3 => "=",
            0 => string.Empty,
            _ => "="
        };

        return Convert.FromBase64String(padded);
    }

    /// <summary>Base64url encode without padding, matching the shape proofs are written in.</summary>
    public static string EncodeBase64Url(ReadOnlySpan<byte> bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

/// <summary>The <c>fairness</c> block of an anonymous audit payload.</summary>
public sealed class AnonymizedAuditFairness
{
    [JsonPropertyName("fairDraw")]
    public bool FairDraw { get; init; }

    [JsonPropertyName("algorithmProfile")]
    public string? AlgorithmProfile { get; init; }

    [JsonPropertyName("repeatMode")]
    public string? RepeatMode { get; init; }

    [JsonPropertyName("halfRepeatLimit")]
    public int? HalfRepeatLimit { get; init; }

    [JsonPropertyName("averageGapProtectionApplied")]
    public bool AverageGapProtectionApplied { get; init; }

    [JsonPropertyName("candidateCountBeforeAverageGapProtection")]
    public int CandidateCountBeforeAverageGapProtection { get; init; }

    [JsonPropertyName("candidateCountAfterAverageGapProtection")]
    public int CandidateCountAfterAverageGapProtection { get; init; }

    [JsonPropertyName("averageGapProtectionThreshold")]
    public int AverageGapProtectionThreshold { get; init; }

    [JsonPropertyName("batchSize")]
    public int BatchSize { get; init; }

    [JsonPropertyName("shareDebtPersonalHorizonRounds")]
    public double ShareDebtPersonalHorizonRounds { get; init; }

    [JsonPropertyName("shareDebtDimensionHorizonPerPick")]
    public double ShareDebtDimensionHorizonPerPick { get; init; }

    [JsonPropertyName("shareDebtRandomFloor")]
    public double ShareDebtRandomFloor { get; init; }
}
