using System;
using System.Security.Cryptography;
using System.Text;

namespace SecRandom.Core.Models.SlideTrigger;

/// <summary>
///     One marker a teacher can place in a deck: a short id (what the image carries) plus the parameters (what the
///     app does when that id is seen).
/// </summary>
/// <remarks>
///     Consumption is recorded per session rather than by a one-shot flag, because a deck is reused all term: slide 12
///     should fire once per lesson, not once for ever. Rolling the session (a new lesson, or an explicit
///     "start a new session" action) is what makes the same marker usable again.
/// </remarks>
public sealed class SlideTriggerDefinition
{
    public string Id { get; set; } = string.Empty;

    /// <summary>Teacher-facing name, so a list of markers is not just ids.</summary>
    public string Name { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public SlideTriggerParameters Parameters { get; set; } = new();

    /// <summary>
    ///     The exact text that was drawn as a QR code for this marker. Kept so the settings page can re-export the PNG
    ///     without re-encoding, and so "what is actually in my deck" has one authoritative answer. It is the marker,
    ///     not the parameters, that the app fires on — editing the parameters here must therefore also regenerate this.
    /// </summary>
    public string MarkerText { get; set; } = string.Empty;

    /// <summary>Date after which the marker is refused; null means it never expires.</summary>
    public DateTimeOffset? ExpiresAt { get; set; }

    /// <summary>Session in which this marker last fired; null means it has never fired.</summary>
    public string? LastConsumedSession { get; set; }

    public DateTimeOffset? LastConsumedAt { get; set; }

    /// <summary>True when this marker already fired in <paramref name="session" /> and must be ignored.</summary>
    public bool IsConsumedIn(string session) =>
        !string.IsNullOrEmpty(LastConsumedSession)
        && string.Equals(LastConsumedSession, session, StringComparison.Ordinal);

    public void MarkConsumed(string session, DateTimeOffset at)
    {
        LastConsumedSession = session;
        LastConsumedAt = at;
    }
}

/// <summary>
///     The id written into a marker image.
/// </summary>
/// <remarks>
///     Eight random bytes rendered as thirteen Crockford-style characters: no <c>I</c>/<c>L</c>/<c>O</c>/<c>U</c>, so a
///     misread is impossible rather than merely unlikely once the text has been through a QR round trip. Eight bytes is
///     what the packed payload reserves for it, and thirteen characters is exactly how 64 bits fit in base 32.
/// </remarks>
public static class SlideTriggerId
{
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    private const int IdLength = 13;
    private const int ByteCount = 8;

    public static string New() => FromBytes(RandomNumberGenerator.GetBytes(ByteCount));

    public static string FromBytes(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length != ByteCount)
            throw new ArgumentException($"A slide trigger id needs exactly {ByteCount} bytes.", nameof(bytes));

        ulong value = 0;
        foreach (var item in bytes)
            value = (value << 8) | item;

        var builder = new StringBuilder(IdLength);

        // 13 characters cover 4 + 12 × 5 = 64 bits exactly, so the first character carries the high nibble.
        builder.Append(Alphabet[(int)((value >> 60) & 0xF)]);
        for (var shift = 55; shift >= 0; shift -= 5)
            builder.Append(Alphabet[(int)((value >> shift) & 0x1F)]);

        return builder.ToString();
    }

    public static byte[] ToBytes(string id)
    {
        if (!IsValid(id))
            throw new ArgumentException($"'{id}' is not a slide trigger id.", nameof(id));

        var normalized = id.ToUpperInvariant();
        ulong value = (ulong)Alphabet.IndexOf(normalized[0]);

        for (var index = 1; index < normalized.Length; index++)
            value = (value << 5) | (uint)Alphabet.IndexOf(normalized[index]);

        var bytes = new byte[ByteCount];
        for (var index = ByteCount - 1; index >= 0; index--)
        {
            bytes[index] = (byte)(value & 0xFF);
            value >>= 8;
        }

        return bytes;
    }

    public static bool IsValid(string? value)
    {
        if (value is not { Length: IdLength })
            return false;

        foreach (var character in value)
            if (Alphabet.IndexOf(char.ToUpperInvariant(character)) < 0)
                return false;

        return true;
    }
}
