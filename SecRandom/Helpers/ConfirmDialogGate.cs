using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;

namespace SecRandom.Helpers;

/// <summary>
///     Forced-reading gate for deliberate-consent dialogs: the primary button stays disabled for a minimum
///     period, so a high-stakes confirmation cannot be produced by an instantaneous reflex click. Dialogs
///     that move data to SecRandom/SECTL (formal notarization, the online-services acknowledgement) must
///     use it, and a caller must never shorten <see cref="MinimumDisplay" />.
/// </summary>
public static class ConfirmDialogGate
{
    public static readonly TimeSpan MinimumDisplay = TimeSpan.FromSeconds(5);

    /// <summary>
    ///     Disables the dialog's primary button until both the minimum display period has elapsed and every
    ///     supplied acknowledgement checkbox is ticked.
    /// </summary>
    public static void Arm(FAContentDialog dialog, params CheckBox[] acknowledgements)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        ArgumentNullException.ThrowIfNull(acknowledgements);

        var elapsed = false;
        dialog.IsPrimaryButtonEnabled = false;

        void Refresh() => dialog.IsPrimaryButtonEnabled =
            elapsed && acknowledgements.All(box => box.IsChecked == true);

        foreach (var box in acknowledgements)
            box.IsCheckedChanged += (_, _) => Refresh();

        var timer = new DispatcherTimer { Interval = MinimumDisplay };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            elapsed = true;
            Refresh();
        };
        dialog.Closing += (_, _) => timer.Stop();
        timer.Start();
    }
}
