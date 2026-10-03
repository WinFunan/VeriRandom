using System;
using System.Collections.Generic;
using System.Linq;

namespace SecRandom.Core.Services.Verification;

/// <summary>
///     The evidence a user is willing to rely on when judging an ordinary draw proof. The assessment starts
///     from full marks and subtracts only what the user's own choices cannot be backed by evidence, so the
///     result describes evidence coverage rather than a cryptographic strength claim.
/// </summary>
public enum ProofTrustSource
{
    /// <summary>The external beacon that supplied the draw's entropy.</summary>
    BeaconProvider,

    /// <summary>The RFC 3161 authority that stamped the proof.</summary>
    TimestampAuthority,

    /// <summary>The SecRandom server that locked and replayed the draw.</summary>
    SectlServer,

    /// <summary>A person who remembers roughly when the draw happened.</summary>
    SocialWitness
}

/// <summary>How closely the remembered draw time is supposed to match.</summary>
public enum WitnessTimeConfidence
{
    High,
    Medium,
    Low
}

/// <summary>How the proof's time stamp lines up with the pulse it was derived from.</summary>
public enum ProofTrustPulseTier
{
    /// <summary>Stamped inside the pulse's own period.</summary>
    SamePeriod,

    /// <summary>Stamped in the next period: the pulse is one period old, which is tolerated but weakened.</summary>
    PreviousPeriod,

    /// <summary>Beyond the tolerated age, so the seed-to-pulse binding is too weak to keep the score.</summary>
    BeyondTolerance
}

/// <summary>How one assessed factor turned out. The caller localizes this; Core owns no user-facing text.</summary>
public enum ProofTrustFactorState
{
    /// <summary>The evidence for this factor is present and within tolerance.</summary>
    Satisfied,

    /// <summary>The evidence for this factor is absent.</summary>
    Missing,

    /// <summary>The factor has evidence, but it is only partly convincing (social witness beyond tolerance).</summary>
    PartiallySatisfied,

    /// <summary>The factor was not selected, so it neither adds nor subtracts anything.</summary>
    NotAssessed
}

public sealed record ProofTrustInput(
    bool ChainIntact,
    bool HasBeaconEvidence,
    bool HasTimestampToken,
    bool HasServerReceipt,
    DateTimeOffset? PulsePublishedAtUtc,
    DateTimeOffset? TimestampedAtUtc,
    DateTimeOffset? DrawCreatedAtUtc,
    IReadOnlyCollection<ProofTrustSource> TrustedSources,
    DateTimeOffset? WitnessedAtUtc,
    WitnessTimeConfidence WitnessConfidence);

public sealed record ProofTrustFactor(
    ProofTrustSource Source,
    double Weight,
    double Awarded,
    ProofTrustFactorState State,
    double? ErrorSeconds = null,
    double? ToleranceSeconds = null);

/// <summary>
///     The outcome of one assessment. <see cref="Score" /> is a percentage: full marks when every selected
///     factor is backed by evidence, zero when the proof cannot even vouch for itself.
/// </summary>
public sealed record ProofTrustReport(
    int Score,
    bool ChainIntact,
    ProofTrustPulseTier PulseTier,
    IReadOnlyList<ProofTrustFactor> Factors)
{
    /// <summary>True when the assessment was zeroed because the chain itself no longer verifies.</summary>
    public bool IsZeroed => !ChainIntact;

    /// <summary>True when the pulse age cost the score part of its marks.</summary>
    public bool PulsePenalized => PulseTier != ProofTrustPulseTier.SamePeriod;
}

/// <summary>
///     Scores how well an ordinary-mode proof is backed by the evidence a user chose to trust.
///     Rules, in order:
///     <list type="number">
///         <item>
///             A proof whose own chain no longer verifies scores zero regardless of everything else — a
///             record that cannot prove itself cannot be graded.
///         </item>
///         <item>
///             Each selected factor is worth <see cref="WeightPerSource" />; the score is the share of the
///             selected weight that is actually backed by evidence.
///         </item>
///         <item>
///             A pulse more than one beacon period after the stamp is still tolerated — a draw can start just
///             before a period boundary — but it halves the score; once the pulse is older than
///             <see cref="MaximumPulseAge" /> the seed-to-pulse binding is too weak and three quarters are
///             deducted. The score never goes negative.
///         </item>
///         <item>
///             A social witness scores the full weight while the remembered time is inside the window its
///             confidence asserts, and loses that weight proportionally once the error exceeds it.
///         </item>
///     </list>
/// </summary>
public static class ProofTrustScorer
{
    /// <summary>
    ///     Equal weights on purpose: the score reports which evidence is present and deliberately does not
    ///     rank a cryptographic anchor above a human memory as though that were a cryptographic claim.
    /// </summary>
    public const double WeightPerSource = 25d;

    /// <summary>The NIST beacon period. A stamp inside the pulse's own period needs no adjustment.</summary>
    public static readonly TimeSpan PulsePeriod = TimeSpan.FromSeconds(60);

    /// <summary>
    ///     The oldest tolerated pulse age. A pulse from the previous period is normal — a draw can start just
    ///     before a period boundary — but beyond this the pulse is at least two periods old and the
    ///     seed-to-pulse binding is too weak to keep most of the score.
    /// </summary>
    public static readonly TimeSpan MaximumPulseAge = TimeSpan.FromSeconds(110);

    /// <summary>Share of the score kept when the pulse is one period old (a half is deducted).</summary>
    public const double PreviousPeriodFactor = 0.5d;

    /// <summary>Share kept once the pulse is older than <see cref="MaximumPulseAge" />: three quarters deducted.</summary>
    public const double BeyondToleranceFactor = 0.25d;

    /// <summary>The window each confidence level asserts for a remembered draw time.</summary>
    public static TimeSpan WindowFor(WitnessTimeConfidence confidence) => confidence switch
    {
        WitnessTimeConfidence.High => TimeSpan.FromMinutes(5),
        WitnessTimeConfidence.Medium => TimeSpan.FromMinutes(30),
        _ => TimeSpan.FromHours(2)
    };

    public static ProofTrustReport Evaluate(ProofTrustInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var pulseTier = ResolvePulseTier(input);

        if (!input.ChainIntact)
        {
            var broken = Enum.GetValues<ProofTrustSource>()
                .Select(source => new ProofTrustFactor(source, WeightPerSource, 0d, ProofTrustFactorState.Missing))
                .ToArray();
            return new ProofTrustReport(0, false, pulseTier, broken);
        }

        var selected = input.TrustedSources.Distinct().ToArray();
        var totalWeight = selected.Length * WeightPerSource;
        if (totalWeight <= 0d)
            return new ProofTrustReport(0, true, pulseTier, []);

        var factors = new List<ProofTrustFactor>(selected.Length);
        foreach (var source in selected)
        {
            factors.Add(source switch
            {
                ProofTrustSource.BeaconProvider => Simple(source, input.HasBeaconEvidence),
                ProofTrustSource.TimestampAuthority => Simple(source, input.HasTimestampToken),
                ProofTrustSource.SectlServer => Simple(source, input.HasServerReceipt),
                ProofTrustSource.SocialWitness => ScoreWitness(input),
                _ => Simple(source, false)
            });
        }

        var awarded = factors.Sum(factor => factor.Awarded);
        var score = 100d * awarded / totalWeight;

        score *= pulseTier switch
        {
            ProofTrustPulseTier.PreviousPeriod => PreviousPeriodFactor,
            ProofTrustPulseTier.BeyondTolerance => BeyondToleranceFactor,
            _ => 1d
        };

        var rounded = (int)Math.Round(Math.Clamp(score, 0d, 100d), MidpointRounding.AwayFromZero);
        return new ProofTrustReport(rounded, true, pulseTier, factors);
    }

    /// <summary>
    ///     Grades the distance between the pulse and the stamp. Crossing one period is tolerated; the pulse
    ///     simply may not be older than <see cref="MaximumPulseAge" />.
    /// </summary>
    private static ProofTrustPulseTier ResolvePulseTier(ProofTrustInput input)
    {
        if (input.TimestampedAtUtc is not { } stamped || input.PulsePublishedAtUtc is not { } published)
            return ProofTrustPulseTier.SamePeriod;

        var age = stamped - published;
        if (age <= PulsePeriod)
            return ProofTrustPulseTier.SamePeriod;

        return age <= MaximumPulseAge
            ? ProofTrustPulseTier.PreviousPeriod
            : ProofTrustPulseTier.BeyondTolerance;
    }

    private static ProofTrustFactor Simple(ProofTrustSource source, bool present) =>
        new(source, WeightPerSource, present ? WeightPerSource : 0d,
            present ? ProofTrustFactorState.Satisfied : ProofTrustFactorState.Missing);

    private static ProofTrustFactor ScoreWitness(ProofTrustInput input)
    {
        var tolerance = WindowFor(input.WitnessConfidence);
        if (input.WitnessedAtUtc is not { } witnessed || input.DrawCreatedAtUtc is not { } created)
            return new ProofTrustFactor(ProofTrustSource.SocialWitness, WeightPerSource, 0d,
                ProofTrustFactorState.Missing, null, tolerance.TotalSeconds);

        var error = (witnessed - created).Duration();

        // Inside the asserted window the memory corroborates the record outright; beyond it the factor fades
        // out over one further window, so a wildly wrong time simply earns nothing.
        if (error <= tolerance)
        {
            return new ProofTrustFactor(ProofTrustSource.SocialWitness, WeightPerSource, WeightPerSource,
                ProofTrustFactorState.Satisfied, error.TotalSeconds, tolerance.TotalSeconds);
        }

        var overshoot = error - tolerance;
        var fade = Math.Clamp(overshoot / tolerance, 0d, 1d);
        var awarded = WeightPerSource * (1d - fade);
        return new ProofTrustFactor(ProofTrustSource.SocialWitness, WeightPerSource, awarded,
            awarded <= 0d ? ProofTrustFactorState.Missing : ProofTrustFactorState.PartiallySatisfied,
            error.TotalSeconds, tolerance.TotalSeconds);
    }
}
