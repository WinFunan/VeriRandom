using System;
using System.Collections.Generic;

namespace SecRandom.Core.Services.Verification;

/// <summary>
///     Where a draw's seed came from, ordered from strongest to weakest. The order is the fallback ladder of
///     the "优先保障课堂进行" switch.
/// </summary>
public enum ClassProgressEntropySource
{
    /// <summary>A server-locked formal notarization result (the strongest evidence).</summary>
    FormalNotarization,

    /// <summary>A published beacon pulse (NIST or drand).</summary>
    Beacon,

    /// <summary>random.org's atmospheric-noise bytes; there is no timeline or signature to re-check.</summary>
    RandomOrg,

    /// <summary>The local CSPRNG: reproducible, but the operator could have substituted it.</summary>
    LocalCsprng
}

/// <summary>
///     Recorded when a draw did not get the entropy source its mode asks for. A caller can show this to the
///     user, and the proof itself stays honest because it only ever records the source that was really used.
/// </summary>
public sealed record ClassProgressFallback(ClassProgressEntropySource Abandoned, ClassProgressEntropySource Used)
{
    public string AbandonedId => ClassProgressEntropyPolicy.IdOf(Abandoned);
    public string UsedId => ClassProgressEntropyPolicy.IdOf(Used);
}

/// <summary>
///     The one-second ladder behind <c>VerificationSettingsConfig.PrioritizeClassProgress</c>.
///     It is a pure policy object on purpose: the coordinator only executes these stages, so the order and the
///     budget can be pinned by tests without a network, a proof store, or a browser.
/// </summary>
public static class ClassProgressEntropyPolicy
{
    /// <summary>How long any one stage may take before the next one is tried.</summary>
    public static readonly TimeSpan StageBudget = TimeSpan.FromSeconds(1);

    /// <summary>
    ///     Stages to try, in order, after <paramref name="start" /> became unavailable. random.org is only
    ///     offered when a key is configured, because that service refuses anonymous requests; the local CSPRNG
    ///     is always last and never fails.
    /// </summary>
    public static IReadOnlyList<ClassProgressEntropySource> FallbackAfter(
        ClassProgressEntropySource start,
        bool randomOrgAvailable)
    {
        var stages = new List<ClassProgressEntropySource>(3);
        if (start == ClassProgressEntropySource.FormalNotarization)
            stages.Add(ClassProgressEntropySource.Beacon);
        if (start != ClassProgressEntropySource.RandomOrg && randomOrgAvailable)
            stages.Add(ClassProgressEntropySource.RandomOrg);
        stages.Add(ClassProgressEntropySource.LocalCsprng);
        return stages;
    }

    /// <summary>Stable id for logs and for a future proof/export field.</summary>
    public static string IdOf(ClassProgressEntropySource source) => source switch
    {
        ClassProgressEntropySource.FormalNotarization => "formal-notarization",
        ClassProgressEntropySource.Beacon => "beacon",
        ClassProgressEntropySource.RandomOrg => "random-org",
        _ => "local-csprng"
    };
}
