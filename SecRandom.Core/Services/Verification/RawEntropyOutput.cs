using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace SecRandom.Core.Services.Verification;

/// <summary>How a raw-number generation presents its result.</summary>
public enum RawOutputFormat
{
    /// <summary>The plain integer(s) inside the chosen range or set.</summary>
    Value,

    /// <summary>Raw bits as uppercase hex.</summary>
    Hex,

    /// <summary>Raw bits rendered as printable ASCII characters.</summary>
    AsciiPrintable
}

/// <summary>What the generated text actually carries, as opposed to how many bits were asked for.</summary>
/// <param name="SymbolCount">Number of output symbols (bytes or characters).</param>
/// <param name="BitsPerSymbol">Measured Shannon entropy of the output, in bits per symbol.</param>
/// <param name="TotalBits">Bits per symbol times symbol count.</param>
/// <param name="AvailableSourceBits">Entropy the seed can actually supply.</param>
public sealed record RawEntropyReport(
    int SymbolCount,
    double BitsPerSymbol,
    double TotalBits,
    int AvailableSourceBits);

/// <summary>
///     Turns a seed into bits or printable text, and measures what came out.
/// </summary>
/// <remarks>
///     <para>
///         <b>A wider output is not more entropy.</b> Asking for 1280 bits from a 256-bit seed yields 1280 <em>bits</em>
///         but at most 256 bits of entropy: everything past the seed is a deterministic expansion of it. That is why
///         <see cref="Analyze" /> reports both the requested width and the source's real capacity, and why the UI must
///         show both numbers next to each other instead of the flattering one alone.
///     </para>
///     <para>
///         Printable ASCII uses rejection over 188 = 2 × 94, so every character of the 0x21–0x7E range is equally
///         likely. Taking <c>byte % 94</c> directly would over-represent the low end by a measurable amount.
///     </para>
/// </remarks>
public static class RawEntropyOutput
{
    private const int SourceBytes = 32;
    private const int PrintableRange = 94;
    private const int PrintableBase = 0x21;
    private const int AcceptedBytes = PrintableRange * 2;

    private static readonly byte[] DomainSeparator = Encoding.ASCII.GetBytes("VeriRandomRaw/v1/bits");

    /// <summary>Expands the seed to <paramref name="bitCount" /> bits, or fewer bytes when the count is not a multiple of 8.</summary>
    public static byte[] Expand(ReadOnlySpan<byte> seed, int bitCount)
    {
        if (bitCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(bitCount), bitCount, "Bit count must be positive.");
        if (seed.IsEmpty)
            throw new ArgumentException("A seed is required.", nameof(seed));

        var byteCount = (bitCount + 7) / 8;
        var result = new byte[byteCount];
        var produced = 0;

        for (var counter = 0; produced < byteCount; counter++)
        {
            var block = Block(seed, counter);
            var take = Math.Min(SourceBytes, byteCount - produced);
            block.AsSpan(0, take).CopyTo(result.AsSpan(produced));
            produced += take;
        }

        return result;
    }

    /// <summary>Uppercase hex, which is what an operator comparing against another tool expects.</summary>
    public static string ToHex(byte[] bytes) => Convert.ToHexString(bytes);

    /// <summary>Uniform printable ASCII of <paramref name="count" /> characters.</summary>
    public static string ToPrintableAscii(ReadOnlySpan<byte> seed, int count)
    {
        if (count <= 0)
            return string.Empty;

        // Two bytes per character is generous: rejection throws away at most 68 of 256 values, so the supply runs out
        // only in pathological cases — expand again rather than loop forever.
        var buffer = Expand(seed, Math.Max(count * 2, 64) * 8);
        var builder = new StringBuilder(count);
        var index = 0;

        while (builder.Length < count)
        {
            if (index >= buffer.Length)
            {
                buffer = Expand(SeedWithCounter(seed, buffer.Length), Math.Max(count * 2, 64) * 8);
                index = 0;
            }

            var value = buffer[index++];
            if (value >= AcceptedBytes)
                continue;

            builder.Append((char)(PrintableBase + (value % PrintableRange)));
        }

        return builder.ToString();
    }

    /// <summary>
    ///     Measures the output. Deliberately measured, not assumed: a generator that claims 1280 bits while emitting
    ///     twelve distinct characters should be caught by this number, not by a footnote.
    /// </summary>
    public static RawEntropyReport Analyze(string output, int availableSourceBits)
    {
        if (string.IsNullOrEmpty(output))
            return new RawEntropyReport(0, 0, 0, availableSourceBits);

        var counts = new Dictionary<char, int>(output.Length);
        foreach (var character in output)
            counts[character] = counts.GetValueOrDefault(character) + 1;

        var bitsPerSymbol = 0d;
        foreach (var count in counts.Values)
        {
            var probability = (double)count / output.Length;
            bitsPerSymbol -= probability * Math.Log2(probability);
        }

        return new RawEntropyReport(output.Length, bitsPerSymbol, bitsPerSymbol * output.Length, availableSourceBits);
    }

    private static byte[] Block(ReadOnlySpan<byte> seed, int counter)
    {
        Span<byte> counterBytes = stackalloc byte[sizeof(int)];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(counterBytes, counter);

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(DomainSeparator);
        hash.AppendData(counterBytes);
        hash.AppendData(seed);
        return hash.GetHashAndReset();
    }

    private static byte[] SeedWithCounter(ReadOnlySpan<byte> seed, int counter)
    {
        var material = new byte[seed.Length + sizeof(int)];
        seed.CopyTo(material);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(material.AsSpan(seed.Length), counter);
        return SHA256.HashData(material);
    }
}
