using System;
using SecRandom.Core.Services.Config;

namespace SecRandom.Services.FirstRun;

public sealed class FirstRunOobeService(MainConfigHandler configHandler)
{
    public const int CurrentPrivacyPolicyVersion = 1;
    public const int CurrentGplVersion = 1;
    public const int CurrentVerificationNoticeVersion = 1;
    public const int CurrentSecRandomServicesVersion = 1;

    public bool IsRequired()
    {
        return IsPrivacyPolicyOnlyRequired() || !configHandler.Data.General.Basic.GuideCompleted;
    }

    public bool IsPrivacyPolicyOnlyRequired()
    {
        var basic = configHandler.Data.General.Basic;
        // The SECTL online-services acknowledgement is intentionally NOT part of this gate: it is optional
        // during first-run setup and only becomes mandatory at the point of use (formal notarization or the
        // telemetry/online-status channels), where SecRandomServicesConsent enforces it with its own dialog.
        return basic.GuideCompleted &&
                (Math.Max(basic.AcceptedGplVersion, basic.AcceptedEulaVersion) < CurrentGplVersion ||
                  basic.AcceptedPrivacyPolicyVersion < CurrentPrivacyPolicyVersion ||
                  basic.AcceptedVerificationNoticeVersion < CurrentVerificationNoticeVersion);
    }

    /// <summary>
    ///     Marks the guide complete. The SECTL online-services acknowledgement is optional here, so it is
    ///     only recorded when the user actually ticked it; otherwise it stays unaccepted and will be
    ///     demanded later, through the confirmation dialog, the first time an online feature is used.
    /// </summary>
    public void Complete(bool secRandomServicesAccepted)
    {
        var basic = configHandler.Data.General.Basic;
        basic.AcceptedEulaVersion = CurrentGplVersion;
        basic.AcceptedPrivacyPolicyVersion = CurrentPrivacyPolicyVersion;
        basic.AcceptedGplVersion = CurrentGplVersion;
        basic.AcceptedVerificationNoticeVersion = CurrentVerificationNoticeVersion;
        if (secRandomServicesAccepted)
            basic.AcceptedSecRandomServicesVersion = CurrentSecRandomServicesVersion;
        basic.GuideCompleted = true;
        configHandler.Save();
    }
}
