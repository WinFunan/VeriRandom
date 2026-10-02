using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using SecRandom.Core.Models.Verification;

namespace SecRandom.Core.Services.Verification;

/// <summary>
///     Derives the 32-byte verification seed from a beacon pulse and the per-pulse sequence number.
///     The derivation is intentionally pure: no local random material, wall clock, or caller-supplied
///     value enters the seed, so the published pulse (plus its sequence number) is the only independent
///     variable that determines the result for a fixed algorithm and candidate pool.
/// </summary>
public static class BeaconSeedDerivation
{
    public const string DerivationId = "secrandom-beacon-1";

    private static readonly byte[] DomainSeparator = Encoding.ASCII.GetBytes("SecRandomBeacon/v1");

    public static byte[] Derive(BeaconPulse pulse, long sequence)
    {
        ArgumentNullException.ThrowIfNull(pulse);
        if (sequence < 0)
            throw new ArgumentOutOfRangeException(nameof(sequence), sequence, "Beacon sequence must not be negative.");

        var output = DecodeOutputValue(pulse.OutputValue);

        var material = new byte[DomainSeparator.Length + sizeof(long) + sizeof(long) + output.Length + sizeof(long)];
        var offset = 0;
        DomainSeparator.CopyTo(material, offset);
        offset += DomainSeparator.Length;
        BinaryPrimitives.WriteInt64BigEndian(material.AsSpan(offset), pulse.PulseIndex);
        offset += sizeof(long);
        BinaryPrimitives.WriteInt64BigEndian(material.AsSpan(offset), pulse.ChainIndex);
        offset += sizeof(long);
        output.CopyTo(material, offset);
        offset += output.Length;
        BinaryPrimitives.WriteInt64BigEndian(material.AsSpan(offset), sequence);

        return SHA256.HashData(material);
    }

    /// <summary>
    ///     Decodes a NIST Beacon output value. The v2 pulse publishes 512 bits as 128 lowercase hex
    ///     characters; anything shorter is rejected instead of being padded or truncated into a seed.
    /// </summary>
    public static byte[] DecodeOutputValue(string outputValue)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputValue);
        var trimmed = outputValue.Trim();
        if (trimmed.Length != 128 || !IsHex(trimmed))
            throw new FormatException("Beacon output value must be 128 hexadecimal characters.");

        return Convert.FromHexString(trimmed);
    }

    private static bool IsHex(string value)
    {
        foreach (var character in value)
        {
            if (!Uri.IsHexDigit(character))
                return false;
        }

        return true;
    }
}
