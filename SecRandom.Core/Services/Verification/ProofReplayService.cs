using System;
using System.Collections.Generic;
using System.Linq;
using SecRandom.Core.Models.Verification;
using SecRandom.Shared.Models.Verification;

namespace SecRandom.Core.Services.Verification;

/// <summary>What happened when a recorded proof was replayed through the kernel that produced it.</summary>
public enum ProofReplayStatus
{
    /// <summary>The kernel produced exactly the winners the proof recorded.</summary>
    Reproduced,

    /// <summary>The kernel produced a different result, so the proof does not match its own inputs.</summary>
    Mismatch,

    /// <summary>Formal notarization: the entropy came from the server's signed lock, which the file does not carry.</summary>
    NoLocalSeed,

    /// <summary>The payload could not be decoded, or the proof records another engine version.</summary>
    UnsupportedFormat
}

/// <summary>Outcome of one replay, with both winner sets so a caller can show what differed.</summary>
public sealed record ProofReplayReport(
    ProofReplayStatus Status,
    IReadOnlyList<VerificationWinner> RecordedWinners,
    IReadOnlyList<VerificationWinner> ReplayedWinners,
    string? Detail)
{
    /// <summary>True only when the replay re-derived the recorded result.</summary>
    public bool IsReproduced => Status == ProofReplayStatus.Reproduced;
}

/// <summary>
///     Replays one recorded proof: decode the payload back into the draw input, run the same kernel with the same
///     seed, and compare the result with what the proof recorded.
/// </summary>
/// <remarks>
///     <para>
///         This deliberately does <em>not</em> rebuild the conditions. The candidate pool, the per-candidate weights,
///         the sampling mode and the count are taken from the payload exactly as recorded — recomputing any of them
///         from today's roster or history would replay a different draw than the one the proof describes.
///     </para>
///     <para>
///         What a success proves is that the recorded inputs produce the recorded result: self-consistency. It does
///         not show that the kernel is unbiased (that is the fairness audit's job), nor that the seed was honestly
///         random, nor that the roster was the real classroom list.
///     </para>
///     <para>
///         File access stays out of Core: the caller reads the proof, decodes its base64url payload and passes the
///         bytes together with the mode and the engine version the proof recorded.
///     </para>
/// </remarks>
public static class ProofReplayService
{
    public static ProofReplayReport Replay(
        ReadOnlySpan<byte> payload,
        VerificationProofMode mode,
        string engineVersion)
    {
        // Replaying with a different kernel than the one that drew would compare two different algorithms.
        if (!string.Equals(engineVersion, VerificationWireCodec.AlgorithmEngineVersion, StringComparison.Ordinal))
        {
            return new ProofReplayReport(
                ProofReplayStatus.UnsupportedFormat, [], [],
                $"engine version {engineVersion} != {VerificationWireCodec.AlgorithmEngineVersion}");
        }

        if (!VerificationWireCodec.TryDecodeProofPayload(payload, out var input, out var seed, out var recorded))
            return new ProofReplayReport(ProofReplayStatus.UnsupportedFormat, [], [], "payload could not be decoded");

        if (mode == VerificationProofMode.OnlineWitnessed)
            return new ProofReplayReport(ProofReplayStatus.NoLocalSeed, recorded.Winners, [], null);

        try
        {
            var replayed = new ManagedVerificationKernel().Draw(input, seed);
            var same = SameWinners(recorded.Winners, replayed.Winners);
            return new ProofReplayReport(
                same ? ProofReplayStatus.Reproduced : ProofReplayStatus.Mismatch,
                recorded.Winners, replayed.Winners, null);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            // An undrawable input is a damaged or foreign proof, not a crash for the caller to handle.
            return new ProofReplayReport(ProofReplayStatus.UnsupportedFormat, recorded.Winners, [], exception.Message);
        }
    }

    private static bool SameWinners(
        IReadOnlyList<VerificationWinner> recorded,
        IReadOnlyList<VerificationWinner> replayed)
    {
        if (recorded.Count != replayed.Count)
            return false;

        // The response frame records the order the kernel reported, so compare positionally as well as by value.
        return !recorded.Where((winner, index) => winner != replayed[index]).Any();
    }
}
