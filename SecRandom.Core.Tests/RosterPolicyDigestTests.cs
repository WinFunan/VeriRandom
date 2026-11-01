using SecRandom.Core.Services.Security;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.Core.Tests;

/// <summary>
///     名单覆盖（分离签名的可选部分）只覆盖身份映射。这里把那条边界钉住：改权重或库存不算改动，改名或
///     停用算改动。取舍的理由写在 <see cref="RosterPolicyDigest" /> 上——覆盖一个主持人日常会调整的抽取
///     输入，会让签名在第一次正常操作时就失效，那时这个功能就不是保护而是负担。
/// </summary>
public sealed class RosterPolicyDigestTests
{
    [Fact]
    public void PrizeWeightAndInventoryAreNotPartOfTheIdentityMapping()
    {
        var list = new PrizeList { Name = "默认奖池" };
        var prize = new Prize { Name = "一等奖" };
        list.Prizes.Add(prize);

        var baseline = RosterPolicyDigest.ComputeList(list);

        // 权重与库存是日常会被调整的抽取输入，不算改动。
        prize.Weight = 9;
        prize.Count = 42;
        Assert.Equal(baseline, RosterPolicyDigest.ComputeList(list));

        // 改名是身份映射的变化，必须算改动。
        prize.Name = "特等奖";
        var renamed = RosterPolicyDigest.ComputeList(list);
        Assert.NotEqual(baseline, renamed);

        // 停用也是。
        prize.Exists = false;
        Assert.NotEqual(renamed, RosterPolicyDigest.ComputeList(list));
    }

    [Fact]
    public void CombiningNoListsStillYieldsADigest()
    {
        // 覆盖开关打开但一台机器上还没有任何名单时，摘要必须是一个真实的值而不是空：是否覆盖名单是
        // 由「rosters 这个键在不在」记录的，返回 null 会让"开着但没名单"与"关着"看起来一样。
        var digest = RosterPolicyDigest.Combine([]);

        Assert.False(string.IsNullOrWhiteSpace(digest));
        Assert.Equal(64, digest.Length);
    }
}
