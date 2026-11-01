using System;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Models;
using SecRandom.Core.Models.SubConfigs;
using SecRandom.Core.Services.Security;

namespace SecRandom.Core.Tests;

/// <summary>
///     A signed policy is only usable if it covers exactly the settings an operator deploys. These tests pin
///     both halves of that: a covered policy change must change the digest, and anything the draw machine
///     rewrites during ordinary use must NOT — otherwise the signature would fail on the first window
///     resize or draw and the feature would be worthless.
///     <para>
///         The security settings are passed alongside the model rather than read from it: they now live in the
///         encrypted security-settings store (<c>data/config/security/settings.json</c>), so
///         <c>MainConfigModel</c> no longer carries them at all.
///     </para>
/// </summary>
public sealed class ConfigPolicyDigestTests
{
    [Fact]
    public void TheSameConfigurationAlwaysYieldsTheSameDigest()
    {
        var first = ConfigPolicyDigest.Compute(new MainConfigModel(), new SecuritySettingsConfig());
        var second = ConfigPolicyDigest.Compute(new MainConfigModel(), new SecuritySettingsConfig());

        Assert.Equal(Convert.ToBase64String(first), Convert.ToBase64String(second));
        Assert.Equal(32, first.Length);
    }

    [Theory]
    [InlineData("verification")]
    [InlineData("privacy")]
    [InlineData("linkage")]
    [InlineData("fairDraw")]
    [InlineData("lottery")]
    [InlineData("securityPolicy")]
    public void AChangeToACoveredSettingChangesTheDigest(string mutation)
    {
        var baseline = ConfigPolicyDigest.Compute(new MainConfigModel(), new SecuritySettingsConfig());
        var model = new MainConfigModel();
        var security = new SecuritySettingsConfig();

        switch (mutation)
        {
            case "verification":
                model.General.Verification.BeaconEntropyEnabled = true;
                break;
            case "privacy":
                model.General.PrivacySettings.SentryTelemetryEnabled = true;
                break;
            case "linkage":
                model.LinkageSettings.VerificationRequired = true;
                break;
            case "fairDraw":
                model.FairDrawSettings.ShieldEnabled = true;
                break;
            case "lottery":
                model.MoreSettings.LotteryEnabled = true;
                break;
            case "securityPolicy":
                security.ProtectExit = true;
                break;
        }

        Assert.NotEqual(
            Convert.ToBase64String(baseline),
            Convert.ToBase64String(ConfigPolicyDigest.Compute(model, security)));
    }

    [Fact]
    public void RuntimeStateOutsideTheScopeDoesNotChangeTheDigest()
    {
        var baseline = ConfigPolicyDigest.Compute(new MainConfigModel(), new SecuritySettingsConfig());
        var model = new MainConfigModel();

        // Everything here is rewritten by the application during normal use.
        model.General.Basic.MainWindowWidth = 1919;
        model.General.Basic.MainWindowHeight = 1079;
        model.General.Basic.MainWindowMaximized = true;
        model.General.Basic.SettingsWindowWidth = 1234;
        model.RecentTimerPresetSeconds = [30, 60, 120];
        model.RollCallSettings.DefaultClass = "默认名单";
        model.Appearance.ThemeColorMode = ThemeColorMode.Custom;

        Assert.Equal(
            Convert.ToBase64String(baseline),
            Convert.ToBase64String(ConfigPolicyDigest.Compute(model, new SecuritySettingsConfig())));
    }

    [Fact]
    public void TheIntegrityCheckDoesNotCoverItself()
    {
        var baseline = ConfigPolicyDigest.Compute(new MainConfigModel(), new SecuritySettingsConfig());
        var model = new MainConfigModel();
        var security = new SecuritySettingsConfig();

        // Otherwise switching the check on, or changing how a mismatch is handled, would invalidate the
        // signature that verifies it.
        security.ConfigIntegrityMode = ConfigIntegrityMode.SignedPolicy;
        security.SettingsIntegrityCheckEnabled = true;
        security.SettingsIntegrityAction = SettingsIntegrityAction.AutoRestore;
        security.SettingsIntegrityRestoreSource = SettingsIntegrityRestoreSource.CloudThenLocal;

        Assert.Equal(
            Convert.ToBase64String(baseline),
            Convert.ToBase64String(ConfigPolicyDigest.Compute(model, security)));
    }

    [Fact]
    public void ADigestMadeOnTheHostVerifiesAgainstTheDeployedCopy()
    {
        // The whole flow in miniature: the trusted device signs, the draw machine rebuilds the same digest
        // from its own copy and verifies with the public key alone.
        var (publicKey, privateKey) = ConfigSignature.CreateKeyPair();
        var model = new MainConfigModel();
        var security = new SecuritySettingsConfig();
        model.General.Verification.BeaconEntropyEnabled = true;

        var digest = ConfigPolicyDigest.Compute(model, security);
        var signature = ConfigSignature.Sign(digest, privateKey);

        Assert.True(ConfigSignature.Verify(ConfigPolicyDigest.Compute(model, security), signature, publicKey));

        // A policy edit on the draw machine — which the operator did not sign — must fail.
        model.General.Verification.BeaconEntropyEnabled = false;
        Assert.False(ConfigSignature.Verify(ConfigPolicyDigest.Compute(model, security), signature, publicKey));
    }
}
