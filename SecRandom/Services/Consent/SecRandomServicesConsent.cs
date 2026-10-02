using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using FluentAvalonia.UI.Controls;
using SecRandom.Core.Services.Config;
using SecRandom.Helpers;
using SecRandom.Services.FirstRun;
using LR = SecRandom.Langs.FirstRunOobe.Resources;

namespace SecRandom.Services.Consent;

/// <summary>
///     The fork-specific SECTL online-services acknowledgement. It is optional during first-run setup and
///     becomes mandatory the first time a feature actually ships data to SecRandom/SECTL: switching to
///     formal notarization, or enabling the Sentry-telemetry / online-status channels. Every late sign-off
///     goes through the same second-level dialog, so it can never be applied silently by flipping a switch.
/// </summary>
public static class SecRandomServicesConsent
{
    public static bool IsRequired(MainConfigHandler configHandler) =>
        configHandler.Data.General.Basic.AcceptedSecRandomServicesVersion
        < FirstRunOobeService.CurrentSecRandomServicesVersion;

    public static void MarkAccepted(MainConfigHandler configHandler)
    {
        configHandler.Data.General.Basic.AcceptedSecRandomServicesVersion =
            FirstRunOobeService.CurrentSecRandomServicesVersion;
        configHandler.Save();
    }

    /// <summary>
    ///     Appends the SECTL disclosure text to a dialog panel and returns the acknowledgement checkbox
    ///     the caller must enforce before accepting the dialog.
    /// </summary>
    public static CheckBox AppendDisclosure(Panel panel)
    {
        panel.Children.Add(new TextBlock
        {
            Text = LR.C_SecRandomServicesTitle,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 12, 0, 0)
        });
        panel.Children.Add(new TextBlock
        {
            Text = LR.C_SecRandomServicesClause,
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(new TextBlock
        {
            Text = LR.C_SecRandomServicesDisclaimer,
            TextWrapping = TextWrapping.Wrap
        });

        var acknowledgement = new CheckBox
        {
            Content = LR.C_SecRandomServicesAccept,
            Margin = new Thickness(0, 4, 0, 0)
        };
        panel.Children.Add(acknowledgement);
        return acknowledgement;
    }

    /// <summary>
    ///     Shows the second-level SECTL disclosure dialog and persists the acknowledgement when accepted.
    ///     Returns true when consent is not required, or when the user read and accepted it; returns false
    ///     when the user closed the dialog without acknowledging, so the caller must abandon the action.
    /// </summary>
    public static async Task<bool> EnsureAsync(MainConfigHandler configHandler, TopLevel owner)
    {
        if (!IsRequired(configHandler))
            return true;

        var panel = new StackPanel { Spacing = 4 };
        var acknowledgement = AppendDisclosure(panel);
        var dialog = new FAContentDialog
        {
            Title = LR.C_SecRandomServicesTitle,
            Content = panel,
            PrimaryButtonText = LR.C_SecRandomServicesAccept,
            CloseButtonText = LR.C_Cancel,
            DefaultButton = FAContentDialogButton.Close
        };
        ConfirmDialogGate.Arm(dialog, acknowledgement);

        var result = await dialog.ShowAsync(owner);
        if (result != FAContentDialogResult.Primary || acknowledgement.IsChecked != true)
            return false;

        MarkAccepted(configHandler);
        return true;
    }
}
