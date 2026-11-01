using SecRandom.Services.Updates;

namespace SecRandom.Core.Tests;

/// <summary>
///     Signed update-manifest versions are release tags such as v3.0.0-beta.1, which System.Version cannot parse.
///     Desktop and mobile update checks must share one rule so a pre-release train still sees newer releases.
/// </summary>
public sealed class UpdateVersionComparerTests
{
    [Theory]
    [InlineData("3.1.0", "v3.0.0-beta.1", true)]
    [InlineData("3.0.1", "3.0.0", true)]
    [InlineData("v4.0.0", "v3.0.0-beta.1", true)]
    [InlineData("3.0.0", "v3.0.0-beta.1", false)]
    [InlineData("3.0.0-beta.2", "v3.0.0-beta.1", false)]
    [InlineData("3.0.0-beta.1", "v3.0.0-beta.1", false)]
    [InlineData("3.0.0+build.7", "3.0.0", false)]
    [InlineData("v3.1.5 Mu 2", "v3.1.5 Mu 1", true)]
    [InlineData("v3.1.5 Mu 1", "v3.1.5", true)]
    [InlineData("v3.1.5", "v3.1.5 Mu 1", false)]
    [InlineData("v3.1.6", "v3.1.5 Mu 9", true)]
    [InlineData("v3.1.5-mu.1", "v3.1.5 Mu 1", false)]
    [InlineData("", "3.0.0", false)]
    [InlineData("3.0.0", "", false)]
    public void IsNewerComparesTheNumericCoreOfReleaseTags(string candidate, string current, bool expected) =>
        Assert.Equal(expected, UpdateVersionComparer.IsNewer(candidate, current));

    [Theory]
    [InlineData("v3.0.0-beta.1", "3.0.0")]
    [InlineData("3.0.0+build.7", "3.0.0")]
    [InlineData("  v3.1.2  ", "3.1.2")]
    [InlineData("v3.1.5 Mu 3", "3.1.5")]
    public void TryParseDropsThePrefixAndSuffix(string text, string expected)
    {
        Assert.True(UpdateVersionComparer.TryParse(text, out var version));
        Assert.Equal(Version.Parse(expected), version);
    }
}
