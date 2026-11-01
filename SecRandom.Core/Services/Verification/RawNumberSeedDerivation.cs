using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace SecRandom.Core.Services.Verification;

/// <summary>
///     One source's contribution to a raw-number seed: the source id plus the exact value it published (or, for the
///     local CSPRNG, the exact bytes it produced, as hex).
/// </summary>
public sealed record RawNumberEntropyInput(string SourceId, string OutputValue);

/// <summary>
///     Derives the seed for a raw random number from the entropy sources the user selected.
/// </summary>
/// <remarks>
///     <para>
///         This is deliberately <b>not</b> <c>BeaconSeedDerivation</c>. A draw binds its seed to exactly one published
///         pulse, which is what lets a verifier re-derive it from that pulse alone. The raw-number page lets the user
///         combine several sources, so its seed is a hash over every contribution instead — and the price is stated in
///         the UI: with more than one source, or with any source that publishes no timeline, there is no single
///         authority artifact to re-check later.
///     </para>
///     <para>
///         Values are hashed as the <b>ASCII of the hex string</b> a source published, never as decoded bytes: NIST
///         publishes 128 hex characters and drand 64, so a decoder would have to guess a width per source, and a proof
///         replay must be able to reproduce the same seed years later from the recorded text alone.
///     </para>
///     <para>
///         Order matters and is the order the sources are listed in the proof. Reordering them is a different
///         derivation; that is why the proof records the list rather than a set.
///     </para>
/// </remarks>
public static class RawNumberSeedDerivation
{
    private const string DomainSeparator = "VeriRandomRaw/v1";

    /// <summary>Derives the 32-byte seed the sampler consumes.</summary>
    public static byte[] Derive(IReadOnlyList<RawNumberEntropyInput> inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        if (inputs.Count == 0)
            throw new ArgumentException("At least one entropy input is required.", nameof(inputs));

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.ASCII.GetBytes(DomainSeparator));

        foreach (var input in inputs)
        {
            if (string.IsNullOrWhiteSpace(input.OutputValue))
                throw new ArgumentException($"Entropy input '{input.SourceId}' carries no value.", nameof(inputs));

            // Lengths are prefixed so that ("ab", "c") and ("a", "bc") cannot collide at the join point.
            AppendLengthPrefixed(hash, input.SourceId);
            AppendLengthPrefixed(hash, input.OutputValue);
        }

        return hash.GetHashAndReset();
    }

    /// <summary>Lowercase hex digest of the derived seed, which is what the proof records.</summary>
    public static string Digest(byte[] seed) => Convert.ToHexString(seed).ToLowerInvariant();

    private static void AppendLengthPrefixed(IncrementalHash hash, string value)
    {
        var bytes = Encoding.ASCII.GetBytes(value);
        hash.AppendData(BitConverter.GetBytes(bytes.Length));
        hash.AppendData(bytes);
    }
}
