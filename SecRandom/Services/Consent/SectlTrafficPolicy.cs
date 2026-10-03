using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using FluentAvalonia.UI.Controls;
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
    ///     The single answer to "may anything leave this device for SECTL?".
    /// </summary>
    public static bool IsEgressAllowed(MainConfigHandler configHandler) =>
        IsTransferNoticeAccepted(configHandler);

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
