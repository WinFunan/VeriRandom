using System;
using System.Linq;
using SecRandom.Core.Models.Verification;
using SecRandom.Core.Services.Verification;
using SecRandom.Shared.Models.Verification;

namespace SecRandom.Core.Tests;

/// <summary>
///     A replay is only worth offering if it is exact: same pool, same weights, same seed, same algorithm. These
///     tests pin the round trip and the two ways it must refuse (a tampered result, and a mode whose seed is not
///     in the file).
/// </summary>
public sealed class ProofReplayTests
{
    private static readonly byte[] Seed = Enumerable.Range(0, 32).Select(value => (byte)value).ToArray();

    private static VerificationDrawInput CreateInput()
    {
        return new VerificationDrawInput
        {
            Kind = VerificationDrawKind.Student,
            Count = 2,
            Candidates =
            [
                new VerificationCandidate(Guid.Parse("11111111-1111-1111-1111-111111111111"), 0, 1_000_000, false),
                new VerificationCandidate(Guid.Parse("22222222-2222-2222-2222-222222222222"), 0, 1_000_000, false),
                new VerificationCandidate(Guid.Parse("33333333-3333-3333-3333-333333333333"), 0, 1_000_000, false)
            ]
        };
    }

    [Fact]
    public void AnUntouchedProofReplaysToItsOwnRecordedWinners()
    {
        var input = CreateInput();
        var drawn = new ManagedVerificationKernel().Draw(input, Seed);
        var payload = VerificationWireCodec.EncodeProofPayload(input, Seed, drawn.Winners);

        var report = ProofReplayService.Replay(payload, VerificationProofMode.OfflineReproducible,
            VerificationWireCodec.AlgorithmEngineVersion);

        Assert.Equal(ProofReplayStatus.Reproduced, report.Status);
        Assert.True(report.IsReproduced);
        Assert.Equal(drawn.Winners.Count, report.ReplayedWinners.Count);
        // The pool came back from the payload, not from the arguments.
        Assert.Equal(input.Count, report.ReplayedWinners.Count);
    }

    [Fact]
    public void ChangingTheRecordedWinnersIsReportedAsAMismatch()
    {
        var input = CreateInput();
        var drawn = new ManagedVerificationKernel().Draw(input, Seed);
        var payload = VerificationWireCodec.EncodeProofPayload(input, Seed, drawn.Winners);

        // Flip the last byte of the response frame: the last winner's occurrence index.
        var tampered = payload.ToArray();
        tampered[^1] = (byte)(tampered[^1] + 1);

        var report = ProofReplayService.Replay(tampered, VerificationProofMode.OfflineReproducible,
            VerificationWireCodec.AlgorithmEngineVersion);

        Assert.Equal(ProofReplayStatus.Mismatch, report.Status);
        Assert.Equal(drawn.Winners.Count, report.ReplayedWinners.Count);
    }

    [Fact]
    public void ChangingARecordedWeightChangesTheReplayedResult()
    {
        var input = CreateInput();
        var drawn = new ManagedVerificationKernel().Draw(input, Seed);
        var payload = VerificationWireCodec.EncodeProofPayload(input, Seed, drawn.Winners);

        // The first candidate's weight sits before the response frame; zeroing it removes that member's chance.
        // This must stay a decode success and a replay mismatch, never an exception.
        var tampered = payload.ToArray();
        var lastWeightByte = 49 + 0 * 45 + 40 + 7;
        for (var index = 0; index < 8; index++)
            tampered[lastWeightByte - index] = 0;

        var report = ProofReplayService.Replay(tampered, VerificationProofMode.OfflineReproducible,
            VerificationWireCodec.AlgorithmEngineVersion);

        Assert.Equal(ProofReplayStatus.Mismatch, report.Status);
        Assert.False(report.IsReproduced);
    }

    [Fact]
    public void FormalNotarizationCannotBeReplayedFromItsFile()
    {
        var input = CreateInput();
        var drawn = new ManagedVerificationKernel().Draw(input, Seed);
        var payload = VerificationWireCodec.EncodeProofPayload(input, Seed, drawn.Winners);

        var report = ProofReplayService.Replay(payload, VerificationProofMode.OnlineWitnessed,
            VerificationWireCodec.AlgorithmEngineVersion);

        Assert.Equal(ProofReplayStatus.NoLocalSeed, report.Status);
        Assert.False(report.IsReproduced);
    }

    [Fact]
    public void AnotherEngineVersionIsRefusedInsteadOfReplayed()
    {
        var input = CreateInput();
        var drawn = new ManagedVerificationKernel().Draw(input, Seed);
        var payload = VerificationWireCodec.EncodeProofPayload(input, Seed, drawn.Winners);

        var report = ProofReplayService.Replay(payload, VerificationProofMode.OfflineReproducible, "2.0.0");

        Assert.Equal(ProofReplayStatus.UnsupportedFormat, report.Status);
    }

    [Fact]
    public void GarbagePayloadIsRefusedInsteadOfThrowing()
    {
        var report = ProofReplayService.Replay([1, 2, 3, 4, 5], VerificationProofMode.OfflineReproducible,
            VerificationWireCodec.AlgorithmEngineVersion);

        Assert.Equal(ProofReplayStatus.UnsupportedFormat, report.Status);
    }
}
