using System;
using SecRandom.Core.Services.Verification;

namespace SecRandom.Core.Tests;

/// <summary>
///     The class-progress ladder is the one place that decides what a draw degrades to. The order is what the
///     user is told in the settings page, so it is pinned here: 正式公证 → 信标 → random.org → 本地熵, with
///     random.org only when a key is configured and one second per stage.
/// </summary>
public class ClassProgressEntropyPolicyTests
{
    [Fact]
    public void StageBudget_IsOneSecond()
    {
        Assert.Equal(TimeSpan.FromSeconds(1), ClassProgressEntropyPolicy.StageBudget);
    }

    [Fact]
    public void FormalNotarization_FallsBackToBeaconThenRandomOrgThenLocal()
    {
        Assert.Equal(
            [ClassProgressEntropySource.Beacon, ClassProgressEntropySource.RandomOrg, ClassProgressEntropySource.LocalCsprng],
            ClassProgressEntropyPolicy.FallbackAfter(ClassProgressEntropySource.FormalNotarization, randomOrgAvailable: true));
    }

    [Fact]
    public void FormalNotarization_SkipsRandomOrgWhenNoKeyIsConfigured()
    {
        Assert.Equal(
            [ClassProgressEntropySource.Beacon, ClassProgressEntropySource.LocalCsprng],
            ClassProgressEntropyPolicy.FallbackAfter(ClassProgressEntropySource.FormalNotarization, randomOrgAvailable: false));
    }

    [Fact]
    public void Beacon_FallsBackToRandomOrgThenLocal()
    {
        Assert.Equal(
            [ClassProgressEntropySource.RandomOrg, ClassProgressEntropySource.LocalCsprng],
            ClassProgressEntropyPolicy.FallbackAfter(ClassProgressEntropySource.Beacon, randomOrgAvailable: true));
    }

    [Fact]
    public void Beacon_EndsAtLocalEntropyWithoutAKey()
    {
        Assert.Equal(
            [ClassProgressEntropySource.LocalCsprng],
            ClassProgressEntropyPolicy.FallbackAfter(ClassProgressEntropySource.Beacon, randomOrgAvailable: false));
    }

    [Fact]
    public void RandomOrg_IsNeverRetried()
    {
        Assert.Equal(
            [ClassProgressEntropySource.LocalCsprng],
            ClassProgressEntropyPolicy.FallbackAfter(ClassProgressEntropySource.RandomOrg, randomOrgAvailable: true));
    }

    [Fact]
    public void Fallback_ReportsStableSourceIds()
    {
        var fallback = new ClassProgressFallback(
            ClassProgressEntropySource.FormalNotarization, ClassProgressEntropySource.LocalCsprng);

        Assert.Equal("formal-notarization", fallback.AbandonedId);
        Assert.Equal("local-csprng", fallback.UsedId);
        Assert.Equal("beacon", ClassProgressEntropyPolicy.IdOf(ClassProgressEntropySource.Beacon));
        Assert.Equal("random-org", ClassProgressEntropyPolicy.IdOf(ClassProgressEntropySource.RandomOrg));
    }
}
