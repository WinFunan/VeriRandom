using System;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Services.Config;

namespace SecRandom.Services.FirstRun;

public sealed class FirstRunOobeService(MainConfigHandler configHandler)
{
    public const int CurrentPrivacyPolicyVersion = 1;
    public const int CurrentGplVersion = 1;
    public const int CurrentVerificationNoticeVersion = 1;
    public const int CurrentSecRandomServicesVersion = 1;

    // Cross-border data transfer notice: SECTL's servers are outside mainland China, so a separate
    // acknowledgement gates every SECTL egress path. Versioned on its own so changing the notice re-asks.
    public const int CurrentCrossBorderTransferVersion = 1;

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
    ///     Marks the guide complete. Both SECTL acknowledgements are optional here, so each is only recorded
    ///     when the user actually accepted it; otherwise it stays unaccepted and is demanded later, through
    ///     its own confirmation dialog, the first time a feature would actually send data to SECTL.
    ///     <paramref name="attestationUpload" /> is the mandatory no-default ordinary-draw choice and is
    ///     always written, including <see cref="AttestationUploadMode.Unset" /> when the user somehow
    ///     reached completion without choosing.
    /// </summary>
    public void Complete(
        bool secRandomServicesAccepted,
        bool crossBorderTransferAccepted,
        AttestationUploadMode attestationUpload)
    {
        var basic = configHandler.Data.General.Basic;
        basic.AcceptedEulaVersion = CurrentGplVersion;
        basic.AcceptedPrivacyPolicyVersion = CurrentPrivacyPolicyVersion;
        basic.AcceptedGplVersion = CurrentGplVersion;
        basic.AcceptedVerificationNoticeVersion = CurrentVerificationNoticeVersion;
        if (secRandomServicesAccepted)
            basic.AcceptedSecRandomServicesVersion = CurrentSecRandomServicesVersion;
        if (crossBorderTransferAccepted)
            basic.AcceptedCrossBorderTransferVersion = CurrentCrossBorderTransferVersion;
        configHandler.Data.General.Verification.AttestationUpload = attestationUpload;
        basic.GuideCompleted = true;
        configHandler.Save();
    }
}
