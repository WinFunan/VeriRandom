using System;
using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace SecRandom.Core.Services.Draw;

/// <summary>
///     Maps a seed onto one number inside a closed range, with rejection sampling instead of modulo.
/// </summary>
/// <remarks>
///     <para>
///         A raw random number has no candidate pool, so the draw kernel does not apply: the range can span a billion
///         values, and materialising that as candidates would be both wasteful and meaningless. What the number does
///         need is to be <em>reproducible</em>, because it is recorded in a proof: derive the seed (a beacon pulse, or a
///         mixed several-source seed), run this function, and the same number comes back — exactly the property a draw
///         replay relies on.
///     </para>
///     <para>
///         Rejection, not modulo: taking <c>value % span</c> biases the low end whenever <c>span</c> does not divide
///         <c>2^64</c>, and the bias is measurable for the small ranges a classroom actually uses. Bytes are expanded
///         from the seed with SHA-256 in counter mode, and a candidate is discarded when it falls in the final partial
///         block, which is the standard unbiased construction.
///     </para>
///     <para>
///         This proves nothing about fairness towards people: there is no roster, so there is no roster digest tying a
///         result to a name. All it can establish is that the recorded input produces the recorded number.
///     </para>
/// </remarks>
public static class RejectionSampler
{
    private static readonly byte[] DomainSeparator = Encoding.ASCII.GetBytes("VeriRandomRaw/v1");

    private static readonly byte[] BatchDomainSeparator = Encoding.ASCII.GetBytes("VeriRandomRaw/v1/batch");

    /// <summary>How many candidates a single seed may be asked for before the caller is told the range is unusable.</summary>
    private const int MaximumAttempts = 1024;

    /// <summary>
    ///     One number in [<paramref name="lower" />, <paramref name="upper" />], derived only from
    ///     <paramref name="seed" />. Throws when the range is empty or inverted, so a bad input never silently
    ///     produces a number.
    /// </summary>
    public static long Sample(long lower, long upper, ReadOnlySpan<byte> seed)
    {
        if (upper < lower)
            throw new ArgumentOutOfRangeException(nameof(upper), upper, "Upper bound must not be below the lower bound.");
        if (seed.IsEmpty)
            throw new ArgumentException("A seed is required.", nameof(seed));
        if (lower == upper)
            return lower;

        var span = (ulong)(upper - lower) + 1UL;

        // Largest multiple of span that fits in the sampled width; candidates at or above it are rejected, because
        // reducing them modulo span would over-represent the low values.
        var limit = ulong.MaxValue - (ulong.MaxValue % span);

        for (var attempt = 0; attempt < MaximumAttempts; attempt++)
        {
            Span<byte> block = stackalloc byte[sizeof(ulong)];
            Expand(seed, attempt, block);
            var candidate = BinaryPrimitives.ReadUInt64BigEndian(block);

            // Compare against the reject threshold: `limit` is the first value that must be discarded.
            if (candidate >= limit && limit != 0)
                continue;

            return lower + (long)(candidate % span);
        }

        throw new InvalidOperationException(
            "Rejection sampling did not settle within the attempt budget; the range is too close to the sample width.");
    }

    /// <summary>
    ///     One element of an explicit candidate set, chosen uniformly. This is the same rejection construction as
    ///     <see cref="Sample" />, applied to the set's index rather than to the numeric range — which is what makes
    ///     "draw from 5 or 10 only" exact instead of approximated by a range.
    /// </summary>
    public static long SampleFromSet(IReadOnlyList<long> values, ReadOnlySpan<byte> seed)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count == 0)
            throw new ArgumentException("The candidate set is empty.", nameof(values));
        if (seed.IsEmpty)
            throw new ArgumentException("A seed is required.", nameof(seed));
        if (values.Count == 1)
            return values[0];

        var span = (ulong)values.Count;
        var limit = ulong.MaxValue - (ulong.MaxValue % span);

        for (var attempt = 0; attempt < MaximumAttempts; attempt++)
        {
            Span<byte> block = stackalloc byte[sizeof(ulong)];
            Expand(seed, attempt, block);
            var candidate = BinaryPrimitives.ReadUInt64BigEndian(block);

            if (candidate >= limit && limit != 0)
                continue;

            return values[(int)(candidate % span)];
        }

        throw new InvalidOperationException(
            "Rejection sampling did not settle within the attempt budget; the set is too large for the sample width.");
    }

    /// <summary>
    ///     Several independent values from one range. Each index gets its own derived seed, so the batch is
    ///     reproducible as a whole: a verifier recomputes every value from the master seed and the index, and a
    ///     repeated value inside the batch is a legitimate outcome rather than a duplicated draw.
    /// </summary>
    public static long[] SampleMany(long lower, long upper, ReadOnlySpan<byte> seed, int count)
    {
        if (count <= 0)
            return [];

        var results = new long[count];
        Span<byte> indexSeed = stackalloc byte[32];

        for (var index = 0; index < count; index++)
        {
            DeriveIndexSeed(seed, index, indexSeed);
            results[index] = Sample(lower, upper, indexSeed);
        }

        return results;
    }

    /// <summary>Batch form of <see cref="SampleFromSet" />, with the same per-index seed derivation.</summary>
    public static long[] SampleManyFromSet(IReadOnlyList<long> values, ReadOnlySpan<byte> seed, int count)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (count <= 0)
            return [];

        var results = new long[count];
        Span<byte> indexSeed = stackalloc byte[32];

        for (var index = 0; index < count; index++)
        {
            DeriveIndexSeed(seed, index, indexSeed);
            results[index] = SampleFromSet(values, indexSeed);
        }

        return results;
    }

    /// <summary>
    ///     Seed for one position in a batch. Derived rather than consuming a stream, so value <c>i</c> depends only on
    ///     the master seed and <c>i</c> — which is what makes "recompute the third value" possible without replaying
    ///     the first two.
    /// </summary>
    private static void DeriveIndexSeed(ReadOnlySpan<byte> seed, int index, Span<byte> destination)
    {
        Span<byte> material = stackalloc byte[BatchDomainSeparator.Length + sizeof(int)];
        BatchDomainSeparator.CopyTo(material);
        BinaryPrimitives.WriteInt32BigEndian(material[BatchDomainSeparator.Length..], index);

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(material);
        hash.AppendData(seed);
        hash.GetHashAndReset(destination);
    }

    /// <summary>SHA-256 counter mode over the seed, so one seed yields an unlimited stream of independent candidates.</summary>
    private static void Expand(ReadOnlySpan<byte> seed, int counter, Span<byte> destination)
    {
        Span<byte> material = stackalloc byte[DomainSeparator.Length + sizeof(long) + sizeof(int)];
        var offset = 0;
        DomainSeparator.CopyTo(material);
        offset += DomainSeparator.Length;
        BinaryPrimitives.WriteInt64BigEndian(material[offset..], seed.Length);
        offset += sizeof(long);
        BinaryPrimitives.WriteInt32BigEndian(material[offset..], counter);

        Span<byte> digest = stackalloc byte[32];
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(material);
        hash.AppendData(seed);
        hash.GetHashAndReset(digest);

        digest[..destination.Length].CopyTo(destination);
    }
}
