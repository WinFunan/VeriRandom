using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using FluentAvalonia.UI.Controls;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Services.Config;
using SecRandom.Helpers;
using SecRandom.Services.FirstRun;
using LR = SecRandom.Langs.FirstRunOobe.Resources;

namespace SecRandom.Services.Consent;

/// <summary>
///     The one gate every path that could send data to SECTL must pass.
///     SECTL's servers sit outside mainland China, so the fork asks for a separate cross-border transfer
///     acknowledgement. It is optional during first-run setup — refusing it simply disables every
///     SECTL-sending feature — and it can be signed later, through <see cref="EnsureTransferAcceptedAsync" />,
///     the first time a feature would actually send something.
///     Never gate SECTL traffic on <c>OnlineStatusMode</c> alone: the version-usage report is deliberately
///     not privacy-gated upstream, so it must be checked against this policy explicitly.
/// </summary>
public static class SectlTrafficPolicy
{
    public static bool IsTransferNoticeAccepted(MainConfigHandler configHandler) =>
        configHandler.Data.General.Basic.AcceptedCrossBorderTransferVersion
        >= FirstRunOobeService.CurrentCrossBorderTransferVersion;

    /// <summary>
    ///     Upstream's SECTL online-services privacy policy acknowledgement.
    /// </summary>
    public static bool IsServicesPolicyAccepted(MainConfigHandler configHandler) =>
        configHandler.Data.General.Basic.AcceptedSecRandomServicesVersion
        >= FirstRunOobeService.CurrentSecRandomServicesVersion;

    /// <summary>
    ///     The device's own refusal, set through the privacy page. It outranks both acknowledgements: a user who once
    ///     agreed to the transfer can still forbid every SECTL connection afterwards without that agreement being
    ///     erased.
    /// </summary>
    public static bool IsEgressSwitchOff() =>
        IAppHost.TryGetService<SectlEgressSwitch>()?.IsDisabled == true;

    /// <summary>
    ///     The single answer to "may anything leave this device for SECTL?". It requires **both**
    ///     acknowledgements — upstream's online-services policy and the fork's cross-border notice — and the device
    ///     switch to be off, so a user who declined either one (or flipped the switch) keeps every SECTL path closed:
    ///     sign-in, heartbeat, cloud backup, online status, usage counters, the version report, announcements, and proof
    ///     attestation.
    /// </summary>
    public static bool IsEgressAllowed(MainConfigHandler configHandler) =>
        !IsEgressSwitchOff()
        && IsTransferNoticeAccepted(configHandler)
        && IsServicesPolicyAccepted(configHandler);

    /// <summary>
    ///     Ordinary-draw replay attestation is an additional, explicit choice on top of the egress consent.
    ///     <see cref="AttestationUploadMode.Unset" /> means the user has not been asked yet and must not upload.
    /// </summary>
    public static bool IsAttestationUploadAllowed(MainConfigHandler configHandler) =>
        IsEgressAllowed(configHandler)
        && configHandler.Data.General.Verification.AttestationUpload == AttestationUploadMode.Enabled;

    public static void MarkTransferNoticeAccepted(MainConfigHandler configHandler)
    {
        configHandler.Data.General.Basic.AcceptedCrossBorderTransferVersion =
            FirstRunOobeService.CurrentCrossBorderTransferVersion;
        configHandler.Save();
    }

    /// <summary>
    ///     Late sign-off for the cross-border notice. It goes through the same deliberate-consent pattern as
    ///     the other SECTL acknowledgement: a dialog with a required checkbox whose confirm button stays
    ///     disabled for a minimum reading period. Returns true when consent already exists or was just given.
    /// </summary>
    public static async Task<bool> EnsureTransferAcceptedAsync(MainConfigHandler configHandler, TopLevel owner)
    {
        // Nothing may leave the device, so asking the user to sign a transfer notice would only promise a feature
        // that stays closed. Refuse before raising any dialog.
        if (IsEgressSwitchOff())
            return false;

        // Both acknowledgements gate SECTL traffic, so the late sign-off collects them in order:
        // upstream's online-services policy first, then the fork's cross-border notice. Each shows its own
        // dialog, and declining either one leaves the gate closed.
        if (!await SecRandomServicesConsent.EnsureAsync(configHandler, owner))
            return false;

        if (IsTransferNoticeAccepted(configHandler))
            return true;

        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(new TextBlock
        {
            Text = LR.C_CrossBorderTitle,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(new TextBlock
        {
            Text = LR.C_CrossBorderBody,
            TextWrapping = TextWrapping.Wrap
        });
        var acknowledgement = new CheckBox
        {
            Content = LR.C_CrossBorderAcceptRequired,
            Margin = new Thickness(0, 12, 0, 0)
        };
        panel.Children.Add(acknowledgement);

        var dialog = new FAContentDialog
        {
            Title = LR.C_CrossBorderTitle,
            Content = panel,
            PrimaryButtonText = LR.C_CrossBorderConfirm,
            CloseButtonText = LR.C_Cancel,
            DefaultButton = FAContentDialogButton.Close
        };
        ConfirmDialogGate.Arm(dialog, acknowledgement);

        var result = await dialog.ShowAsync(owner);
        if (result != FAContentDialogResult.Primary || acknowledgement.IsChecked != true)
            return false;

        MarkTransferNoticeAccepted(configHandler);
        return true;
    }
}
