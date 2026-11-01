using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using SecRandom.Core.Models.SlideTrigger;

namespace SecRandom.Core.Services.SlideTrigger;

/// <summary>Why a marker could not be used.</summary>
public enum SlideTriggerPayloadError
{
    None,
    /// <summary>The text is not a marker at all — the normal case for a screenshot with nothing on it.</summary>
    NotAMarker,
    /// <summary>A marker from a format this build does not understand; retyping it will not help.</summary>
    UnsupportedVersion,
    /// <summary>Truncated or corrupt bytes.</summary>
    Malformed,
    /// <summary>Correctly formed, but the deck's marker has outlived its validity window.</summary>
    Expired
}

/// <summary>One decoded marker.</summary>
public sealed record SlideTriggerPayloadResult(
    bool Success,
    SlideTriggerPayloadError Error,
    string Id,
    DateTimeOffset? ExpiresAt,
    SlideTriggerParameters? Parameters)
{
    public static SlideTriggerPayloadResult Fail(SlideTriggerPayloadError error) =>
        new(false, error, string.Empty, null, null);
}

/// <summary>
///     The self-contained marker that goes into a courseware slide.
/// </summary>
/// <remarks>
///     <para>
///         <b>Why the parameters travel inside the image instead of staying in the app.</b> Courseware is prepared on
///         the teacher's own laptop, not on the classroom machine, and often not even on the machine that will present
///         it. An id-only marker would therefore do nothing on the very computer it was made for — the id would simply
///         not exist there. Everything the marker needs to act therefore lives in the payload.
///     </para>
///     <para>
///         <b>Why binary instead of JSON.</b> A JSON payload would be 300–400 characters, which pushes the QR matrix
///         far past what a projected-and-screenshotted code can carry reliably. The packed form is 18 bytes plus the
///         optional filter strings, so a marker with a list and a group is still around 50 bytes — a small, dense,
///         easily decoded matrix.
///     </para>
///     <para>
///         <b>Byte layout</b> (all integers big-endian):
///         <c>version(1) flags(1) count(1) delayTenths(1) inputs(1) armedSeconds(1) expiresUnixDays(4) id(8)</c>,
///         followed by three length-prefixed UTF-8 strings — list name, gender, group — where length 0 means "not
///         restricted". Every decoded value is re-clamped through <see cref="SlideTriggerParameters.Normalize" />,
///         because a marker is data off a screen, not something this process wrote.
///     </para>
///     <para>
///         <b>No signature in v1.</b> A marker is unsigned, so anyone who can put that picture on the projector can
///         trigger a draw. The mitigations are the per-session single use, the expiry date, and showing on screen which
///         marker fired; a signature would additionally require pairing each classroom machine with the teacher's
///         laptop key, which is a heavier feature than this one. Treat "a student photographs the slide" as accepted
///         risk until that pairing exists.
///     </para>
/// </remarks>
public static class SlideTriggerPayload
{
    /// <summary>Marker text prefix. Also the format magic inside the bytes.</summary>
    public const string Prefix = "VRT1";

    private const byte CurrentVersion = 1;

    private const int HeaderLength = 18;
    private const int IdBytes = 8;
    private const int MaximumListNameBytes = 48;
    private const int MaximumGenderBytes = 12;
    private const int MaximumGroupBytes = 24;

    private const byte FlagAllowRepeat = 1 << 0;
    private const byte FlagIgnoreHistoryWeights = 1 << 1;
    private const byte FlagShowAnimation = 1 << 2;
    private const byte FlagVoice = 1 << 3;
    private const byte FlagWaitForInputs = 1 << 4;

    /// <summary>Packs a marker into the text that will be drawn as a QR code.</summary>
    public static string Encode(SlideTriggerParameters parameters, string id, DateTimeOffset? expiresAt = null)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        if (!SlideTriggerId.IsValid(id))
            throw new ArgumentException($"'{id}' is not a slide trigger id.", nameof(id));

        var normalized = parameters.Normalize();
        var bytes = new List<byte>(64);

        bytes.Add(CurrentVersion);
        bytes.Add(BuildFlags(normalized));
        bytes.Add((byte)normalized.Count);
        bytes.Add((byte)Math.Round(normalized.DelaySeconds * 10));
        bytes.Add((byte)normalized.TriggerAfterInputs);
        bytes.Add((byte)Math.Round(normalized.ArmedTimeoutSeconds));

        WriteUInt32(bytes, expiresAt is { } expiry ? DaysFromUnixEpoch(expiry) : 0u);

        bytes.AddRange(SlideTriggerId.ToBytes(id));

        WriteString(bytes, normalized.StudentListName, MaximumListNameBytes, nameof(parameters.StudentListName));
        WriteString(bytes, normalized.Gender, MaximumGenderBytes, nameof(parameters.Gender));
        WriteString(bytes, normalized.Group, MaximumGroupBytes, nameof(parameters.Group));

        return $"{Prefix}:{Base64Url(bytes.ToArray())}";
    }

    /// <summary>
    ///     Reads a marker back. Failures are reported by reason rather than by throwing: a screenshot with no marker
    ///     on it is the ordinary case and must be cheap to reject.
    /// </summary>
    public static SlideTriggerPayloadResult Decode(string? text, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(text))
            return SlideTriggerPayloadResult.Fail(SlideTriggerPayloadError.NotAMarker);

        var compact = Compact(text);
        if (!compact.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
            return SlideTriggerPayloadResult.Fail(SlideTriggerPayloadError.NotAMarker);

        compact = compact[Prefix.Length..].TrimStart(':', '.', '-', '_');

        byte[] bytes;
        try
        {
            bytes = FromBase64Url(compact);
        }
        catch (FormatException)
        {
            return SlideTriggerPayloadResult.Fail(SlideTriggerPayloadError.Malformed);
        }

        if (bytes.Length < HeaderLength)
            return SlideTriggerPayloadResult.Fail(SlideTriggerPayloadError.Malformed);

        if (bytes[0] != CurrentVersion)
            return SlideTriggerPayloadResult.Fail(SlideTriggerPayloadError.UnsupportedVersion);

        var flags = bytes[1];
        var expiresDays = ReadUInt32(bytes, 6);
        var expiresAt = expiresDays == 0 ? (DateTimeOffset?)null : UnixEpoch.AddDays(expiresDays);
        var id = SlideTriggerId.FromBytes(bytes.AsSpan(10, IdBytes).ToArray());

        var offset = HeaderLength;
        if (!TryReadString(bytes, ref offset, out var listName)
            || !TryReadString(bytes, ref offset, out var gender)
            || !TryReadString(bytes, ref offset, out var group))
            return SlideTriggerPayloadResult.Fail(SlideTriggerPayloadError.Malformed);

        if (expiresAt is { } expiry && now > expiry)
            return new SlideTriggerPayloadResult(false, SlideTriggerPayloadError.Expired, id, expiresAt, null);

        var parameters = new SlideTriggerParameters
        {
            StudentListName = listName,
            Count = bytes[2],
            DelaySeconds = bytes[3] / 10.0,
            TriggerAfterInputs = bytes[4],
            ArmedTimeoutSeconds = bytes[5],
            AllowRepeat = (flags & FlagAllowRepeat) != 0,
            IgnoreHistoryWeights = (flags & FlagIgnoreHistoryWeights) != 0,
            ShowAnimation = (flags & FlagShowAnimation) != 0,
            VoiceEnabled = (flags & FlagVoice) != 0,
            Timing = (flags & FlagWaitForInputs) != 0
                ? SlideTriggerTiming.AfterInputs
                : SlideTriggerTiming.AfterDelay,
            Gender = gender,
            Group = group
        };

        return new SlideTriggerPayloadResult(
            true,
            SlideTriggerPayloadError.None,
            id,
            expiresAt,
            parameters.Normalize());
    }

    public static bool LooksLikeMarker(string? text) => Decode(text, DateTimeOffset.UtcNow).Success;

    private static byte BuildFlags(SlideTriggerParameters parameters)
    {
        byte flags = 0;

        if (parameters.AllowRepeat) flags |= FlagAllowRepeat;
        if (parameters.IgnoreHistoryWeights) flags |= FlagIgnoreHistoryWeights;
        if (parameters.ShowAnimation) flags |= FlagShowAnimation;
        if (parameters.VoiceEnabled) flags |= FlagVoice;
        if (parameters.Timing == SlideTriggerTiming.AfterInputs) flags |= FlagWaitForInputs;

        return flags;
    }

    private static void WriteString(List<byte> bytes, string? value, int maximumBytes, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            bytes.Add(0);
            return;
        }

        var encoded = Encoding.UTF8.GetBytes(value.Trim());
        if (encoded.Length > maximumBytes)
            throw new ArgumentException($"{fieldName} exceeds {maximumBytes} UTF-8 bytes.", fieldName);

        bytes.Add((byte)encoded.Length);
        bytes.AddRange(encoded);
    }

    private static bool TryReadString(byte[] bytes, ref int offset, out string? value)
    {
        value = null;
        if (offset >= bytes.Length)
            return false;

        var length = bytes[offset++];
        if (length == 0)
            return true;

        if (offset + length > bytes.Length)
            return false;

        value = Encoding.UTF8.GetString(bytes, offset, length);
        offset += length;
        return true;
    }

    private static void WriteUInt32(List<byte> bytes, uint value)
    {
        bytes.Add((byte)(value >> 24));
        bytes.Add((byte)(value >> 16));
        bytes.Add((byte)(value >> 8));
        bytes.Add((byte)value);
    }

    private static uint ReadUInt32(byte[] bytes, int offset) =>
        ((uint)bytes[offset] << 24)
        | ((uint)bytes[offset + 1] << 16)
        | ((uint)bytes[offset + 2] << 8)
        | bytes[offset + 3];

    private static uint DaysFromUnixEpoch(DateTimeOffset value) =>
        (uint)Math.Clamp(Math.Floor((value.ToUniversalTime() - UnixEpoch).TotalDays), 1, uint.MaxValue);

    private static DateTimeOffset UnixEpoch => DateTimeOffset.UnixEpoch;

    private static string Compact(string text)
    {
        var builder = new StringBuilder(text.Length);

        foreach (var character in text)
            if (!char.IsWhiteSpace(character))
                builder.Append(character);

        return builder.ToString();
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string text)
    {
        var padded = text.Replace('-', '+').Replace('_', '/');
        padded += (padded.Length % 4) switch
        {
            2 => "==",
            3 => "=",
            0 => string.Empty,
            _ => "="
        };

        return Convert.FromBase64String(padded);
    }
}
