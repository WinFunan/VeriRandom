using System;
using System.Linq;
using SecRandom.Core.Services.Verification;

namespace SecRandom.Core.Tests;

/// <summary>
///     The trust score answers "how well is this proof backed by the evidence I chose to trust", so these
///     tests pin the four rules that make it meaningful: a broken chain zeroes it, missing factors subtract,
///     a previous-period pulse halves it without going negative, and a remembered draw time only helps
///     while it is inside the window its confidence asserts.
/// </summary>
public sealed class ProofTrustScorerTests
{
    private static readonly DateTimeOffset DrawTime = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);

    private static readonly ProofTrustSource[] AllSources =
    [
        ProofTrustSource.BeaconProvider,
        ProofTrustSource.TimestampAuthority,
        ProofTrustSource.SectlServer,
        ProofTrustSource.SocialWitness
    ];

    [Fact]
    public void EveryFactorBackedByEvidenceScoresFullMarks()
    {
        var report = ProofTrustScorer.Evaluate(Input());

        Assert.Equal(100, report.Score);
        Assert.True(report.ChainIntact);
        Assert.Equal(ProofTrustPulseTier.SamePeriod, report.PulseTier);
        Assert.False(report.PulsePenalized);
        Assert.All(report.Factors, factor => Assert.Equal(ProofTrustFactorState.Satisfied, factor.State));
    }

    [Fact]
    public void ABrokenChainIsScoredZeroRegardlessOfOtherEvidence()
    {
        var report = ProofTrustScorer.Evaluate(Input(chainIntact: false));

        Assert.Equal(0, report.Score);
        Assert.True(report.IsZeroed);
    }

    [Fact]
    public void MissingEvidenceSubtractsItsShareOfTheSelectedWeight()
    {
        var report = ProofTrustScorer.Evaluate(Input(hasBeaconEvidence: false));

        Assert.Equal(75, report.Score);
        Assert.Equal(ProofTrustFactorState.Missing, Factor(report, ProofTrustSource.BeaconProvider).State);
    }

    [Fact]
    public void OnlySelectedFactorsCount()
    {
        // One selected factor that is backed by evidence is still full marks: the score is coverage of the
        // user's own selection, not a universal grade.
        var report = ProofTrustScorer.Evaluate(Input(
            trusted: [ProofTrustSource.TimestampAuthority]));

        Assert.Equal(100, report.Score);

        var report2 = ProofTrustScorer.Evaluate(Input(
            hasTimestampToken: false,
            trusted: [ProofTrustSource.TimestampAuthority]));

        Assert.Equal(0, report2.Score);
    }

    [Fact]
    public void NoSelectedFactorScoresZero()
    {
        var report = ProofTrustScorer.Evaluate(Input(trusted: []));

        Assert.Equal(0, report.Score);
        Assert.Empty(report.Factors);
    }

    [Fact]
    public void APreviousPeriodPulseIsToleratedButHalvesTheScore()
    {
        // Crossing one period is normal, so 90 s costs half rather than everything.
        var report = ProofTrustScorer.Evaluate(Input(
            pulsePublishedAt: DrawTime - TimeSpan.FromSeconds(90),
            timestampedAt: DrawTime));

        Assert.Equal(ProofTrustPulseTier.PreviousPeriod, report.PulseTier);
        Assert.True(report.PulsePenalized);
        Assert.Equal(50, report.Score);
    }

    [Fact]
    public void APulseBeyondTheToleratedAgeDeductsThreeQuarters()
    {
        // 110 s is the hard bound; past it the pulse is at least two periods old.
        var report = ProofTrustScorer.Evaluate(Input(
            pulsePublishedAt: DrawTime - TimeSpan.FromSeconds(150),
            timestampedAt: DrawTime));

        Assert.Equal(ProofTrustPulseTier.BeyondTolerance, report.PulseTier);
        Assert.Equal(25, report.Score);
    }

    [Fact]
    public void ThePulseAgeBoundaryItselfIsStillTolerated()
    {
        var report = ProofTrustScorer.Evaluate(Input(
            pulsePublishedAt: DrawTime - TimeSpan.FromSeconds(110),
            timestampedAt: DrawTime));

        Assert.Equal(ProofTrustPulseTier.PreviousPeriod, report.PulseTier);
        Assert.Equal(50, report.Score);
    }

    [Fact]
    public void ThePulsePenaltyCanNeverProduceANegativeScore()
    {
        var report = ProofTrustScorer.Evaluate(Input(
            hasBeaconEvidence: false,
            hasTimestampToken: false,
            hasServerReceipt: false,
            witnessedAt: DrawTime.AddHours(10),
            pulsePublishedAt: DrawTime - TimeSpan.FromSeconds(150),
            timestampedAt: DrawTime));

        Assert.Equal(ProofTrustPulseTier.BeyondTolerance, report.PulseTier);
        Assert.True(report.Score >= 0);
        Assert.Equal(0, report.Score);
    }

    [Fact]
    public void APulseInsideTheSamePeriodIsNotPenalised()
    {
        var report = ProofTrustScorer.Evaluate(Input(
            pulsePublishedAt: DrawTime - TimeSpan.FromSeconds(60),
            timestampedAt: DrawTime));

        Assert.Equal(ProofTrustPulseTier.SamePeriod, report.PulseTier);
        Assert.Equal(100, report.Score);
    }

    [Fact]
    public void ARememberedTimeInsideItsWindowIsNotDeducted()
    {
        var report = ProofTrustScorer.Evaluate(Input(
            witnessedAt: DrawTime.AddMinutes(4),
            confidence: WitnessTimeConfidence.High));

        Assert.Equal(100, report.Score);
        Assert.Equal(ProofTrustFactorState.Satisfied, Factor(report, ProofTrustSource.SocialWitness).State);
    }

    [Fact]
    public void ARememberedTimeBeyondItsWindowFadesOutOverOneFurtherWindow()
    {
        // High confidence asserts five minutes; being 450 s out overshoots by half of that window.
        var report = ProofTrustScorer.Evaluate(Input(
            witnessedAt: DrawTime.AddSeconds(450),
            confidence: WitnessTimeConfidence.High));

        var witness = Factor(report, ProofTrustSource.SocialWitness);
        Assert.Equal(ProofTrustFactorState.PartiallySatisfied, witness.State);
        Assert.Equal(12.5d, witness.Awarded, 3);
        Assert.Equal(88, report.Score);
    }

    [Fact]
    public void AWildlyWrongRememberedTimeEarnsNothing()
    {
        var report = ProofTrustScorer.Evaluate(Input(
            witnessedAt: DrawTime.AddHours(10),
            confidence: WitnessTimeConfidence.Low));

        Assert.Equal(ProofTrustFactorState.Missing, Factor(report, ProofTrustSource.SocialWitness).State);
        Assert.Equal(75, report.Score);
    }

    [Fact]
    public void AMissingRememberedTimeIsAMissingFactor()
    {
        var report = ProofTrustScorer.Evaluate(Input(witnessedAt: null));

        Assert.Equal(ProofTrustFactorState.Missing, Factor(report, ProofTrustSource.SocialWitness).State);
        Assert.Equal(75, report.Score);
    }

    private static ProofTrustFactor Factor(ProofTrustReport report, ProofTrustSource source) =>
        report.Factors.Single(factor => factor.Source == source);

    private static ProofTrustInput Input(
        bool chainIntact = true,
        bool hasBeaconEvidence = true,
        bool hasTimestampToken = true,
        bool hasServerReceipt = true,
        DateTimeOffset? pulsePublishedAt = null,
        DateTimeOffset? timestampedAt = null,
        DateTimeOffset? witnessedAt = null,
        WitnessTimeConfidence confidence = WitnessTimeConfidence.High,
        ProofTrustSource[]? trusted = null) =>
        new(
            chainIntact,
            hasBeaconEvidence,
            hasTimestampToken,
            hasServerReceipt,
            pulsePublishedAt ?? DrawTime - TimeSpan.FromSeconds(10),
            timestampedAt ?? DrawTime,
            DrawTime,
            trusted ?? AllSources,
            witnessedAt ?? DrawTime,
            confidence);
}
