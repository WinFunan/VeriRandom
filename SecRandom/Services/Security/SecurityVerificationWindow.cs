using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Controls;
using SecRandom.Core.Icons;
using SecRandom.Services.Security.Fido2;
using SR = SecRandom.Langs.SettingsPages.Security.Resources;

namespace SecRandom.Services.Security;

internal static class SecurityVerificationDialog
{
    public static async Task<SecurityVerificationResult> ShowAsync(
        TopLevel xamlRoot,
        SecurityVerificationRequest request,
        Func<SecurityVerificationResponse, CancellationToken, Task<SecurityVerificationResult>> verify,
        CancellationToken cancellationToken = default)
    {
        var password = new TextBox
        {
            PasswordChar = '●',
            PlaceholderText = SR.C_PasswordPlaceholder
        };
        InputMethod.SetIsInputMethodEnabled(password, false);
        var totp = new TextBox { PlaceholderText = SR.C_TotpPlaceholder, MaxLength = 6 };
        var usb = new TextBlock
        {
            Text = SR.M_UsbMissing,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        var usbRequired = request.RequiredFactors.Contains(SecurityFactor.Usb);
        var usbPresent = false;
        var usbProbeRunning = false;
        var usbDialogClosed = false;
        // 安全密钥的断言由对话框触发，VerifyAsync 只校验结果；所以这里必须把 proof 原样带回。
        var fido2Required = request.RequiredFactors.Contains(SecurityFactor.Fido2);
        Fido2AssertionProof? fido2Proof = null;
        var fido2Busy = false;
        var fido2Status = new TextBlock
        {
            Text = SR.M_Fido2NotVerified,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };
        var verifyAvailability = new VerificationAvailabilityCommand(() =>
            SecurityVerificationEligibility.CanSubmit(
                request, password.Text, totp.Text, usbPresent, fido2Proof is not null));

        password.TextChanged += (_, _) => verifyAvailability.RaiseCanExecuteChanged();
        totp.TextChanged += (_, _) => verifyAvailability.RaiseCanExecuteChanged();

        async Task RefreshUsbStatusAsync()
        {
            if (usbProbeRunning)
                return;

            usbProbeRunning = true;
            try
            {
                var bindings = await Task.Run(() =>
                    IAppHost.GetService<ISecurityService>()
                        .GetUsbBindingsAsync()
                        .GetAwaiter()
                        .GetResult());
                if (!usbDialogClosed)
                {
                    usbPresent = bindings.Any(binding => binding.IsPresent);
                    usb.Text = usbPresent
                        ? SR.M_UsbPresent
                        : SR.M_UsbMissing;
                    verifyAvailability.RaiseCanExecuteChanged();
                }
            }
            catch (Exception)
            {
                if (!usbDialogClosed)
                {
                    usbPresent = false;
                    usb.Text = SR.M_UsbMissing;
                    verifyAvailability.RaiseCanExecuteChanged();
                }
            }
            finally
            {
                usbProbeRunning = false;
            }
        }

        var errorText = new TextBlock
        {
            Foreground = Brushes.IndianRed,
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false
        };

        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(errorText);
        panel.Children.Add(new TextBlock
        {
            Text = request.LockoutRemaining is { } remaining
                ? string.Format(SR.M_VerificationLockedFormat, Math.Ceiling(remaining.TotalSeconds))
                : request.RequireAllSelectedFactors ? SR.M_VerificationAllFactors : SR.M_VerificationAnyFactor,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        });

        if (request.RequiredFactors.Contains(SecurityFactor.Password))
        {
            panel.Children.Add(new TextBlock { Text = SR.S_Password });
            panel.Children.Add(password);
        }

        if (request.RequiredFactors.Contains(SecurityFactor.Totp))
        {
            panel.Children.Add(new TextBlock { Text = SR.S_Totp });
            panel.Children.Add(totp);
            if (!request.RequireAllSelectedFactors && request.TotpStandaloneReady)
            {
                // 免密副本是可读的，所以「仅 TOTP」只挡界面上的误操作；每次单独使用都把这句放回眼前
                panel.Children.Add(new TextBlock
                {
                    Text = SR.M_TotpStandaloneNotice,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = Brushes.Orange,
                    Opacity = 0.9
                });
            }
            if (!request.RequireAllSelectedFactors && !request.TotpStandaloneReady)
            {
                // 免密副本只在主密码验证后被刷新，升级后的首次免密校验必然失败，
                // 这里提前说明原因而不是只显示「验证失败」
                panel.Children.Add(new TextBlock
                {
                    Text = SR.M_VerificationTotpStandalonePending,
                    TextWrapping = TextWrapping.Wrap,
                    Opacity = 0.7
                });
            }
        }

        if (usbRequired)
        {
            var usbStatusPanel = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                ColumnSpacing = 8
            };
            var refreshUsbButton = new Button
            {
                Content = new FluentIcon(FluentIcons.ArrowClockwiseFilled),
                Padding = new Thickness(4),
                MinWidth = 32,
                MinHeight = 32
            };
            ToolTip.SetTip(refreshUsbButton, SR.C_Refresh);
            refreshUsbButton.Click += async (_, _) => await RefreshUsbStatusAsync();
            Grid.SetColumn(refreshUsbButton, 1);
            usbStatusPanel.Children.Add(usb);
            usbStatusPanel.Children.Add(refreshUsbButton);
            panel.Children.Add(usbStatusPanel);
        }

        if (fido2Required)
        {
            var fido2Button = new Button { Content = SR.C_UseSecurityKey };
            fido2Button.Click += async (_, _) =>
            {
                if (fido2Busy)
                    return;

                var service = IAppHost.TryGetService<Fido2SecurityKeyService>();
                if (service is null || string.IsNullOrEmpty(request.Fido2Challenge))
                {
                    fido2Status.Text = SR.M_Fido2Unavailable;
                    return;
                }

                fido2Busy = true;
                fido2Button.IsEnabled = false;
                fido2Status.Text = SR.M_Fido2Waiting;
                try
                {
                    var outcome = await service.AssertAsync(null, request.Fido2Challenge, cancellationToken);
                    if (outcome.Status == Fido2OperationStatus.Succeeded && outcome.Proof is not null)
                    {
                        fido2Proof = outcome.Proof;
                        fido2Status.Text = SR.M_Fido2Verified;
                    }
                    else
                    {
                        fido2Proof = null;
                        fido2Status.Text = outcome.Status switch
                        {
                            Fido2OperationStatus.NotEnrolled => SR.M_Fido2NotEnrolled,
                            Fido2OperationStatus.Cancelled => SR.M_Fido2Cancelled,
                            Fido2OperationStatus.TimedOut => SR.M_Fido2TimedOut,
                            Fido2OperationStatus.VerificationFailed when outcome.CloneSuspected => SR.M_Fido2CloneSuspected,
                            Fido2OperationStatus.BrowserFailed => SR.M_Fido2Unavailable,
                            _ => SR.M_Fido2Failed
                        };
                    }
                }
                finally
                {
                    fido2Busy = false;
                    fido2Button.IsEnabled = true;
                    verifyAvailability.RaiseCanExecuteChanged();
                }
            };

            panel.Children.Add(new TextBlock { Text = SR.S_Fido2 });
            panel.Children.Add(new StackPanel { Spacing = 8, Children = { fido2Button, fido2Status } });
        }

        var dialog = new FATaskDialog
        {
            XamlRoot = xamlRoot,
            Title = SR.M_VerificationDialogTitle,
            Header = SR.M_VerificationDialogTitle,
            Content = panel
        };
        dialog.Buttons.Add(new FATaskDialogButton(SR.C_Cancel, "cancel"));
        if (request.AllowPreview)
            dialog.Buttons.Add(new FATaskDialogButton(SR.C_Preview, "preview"));
        dialog.Buttons.Add(new FATaskDialogButton(SR.C_Verify, "verify")
        {
            IsDefault = true,
            Command = verifyAvailability
        });

        // FluentAvalonia closes the dialog on any button click. Holding the
        // closing deferral keeps the dialog open while credentials are
        // verified, so a rejection can be shown inline and retried.
        SecurityVerificationResult? finalResult = null;
        dialog.Closing += async (_, args) =>
        {
            if (!Equals(args.Result, "verify"))
                return;

            var deferral = args.GetDeferral();
            try
            {
                var response = new SecurityVerificationResponse(
                    password.Text ?? string.Empty,
                    totp.Text ?? string.Empty,
                    UsbPresent: usbPresent,
                    Fido2: fido2Proof);
                var result = await Task.Run(() => verify(response, cancellationToken));
                if (result.IsAuthorized)
                {
                    finalResult = result;
                }
                else
                {
                    args.Cancel = true;
                    ShowError(result);
                }
            }
            catch (OperationCanceledException)
            {
                args.Cancel = true;
            }
            catch (Exception)
            {
                args.Cancel = true;
                ShowError(new SecurityVerificationResult(false, SecurityVerificationFailure.FactorUnavailable));
            }
            finally
            {
                deferral.Complete();
            }
        };

        if (usbRequired)
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            timer.Tick += async (_, _) => await RefreshUsbStatusAsync();
            dialog.Closing += (_, _) =>
            {
                usbDialogClosed = true;
                timer.Stop();
            };
            _ = RefreshUsbStatusAsync();
            timer.Start();
        }

        return await dialog.ShowAsync() switch
        {
            "preview" => new SecurityVerificationResult(false, SecurityVerificationFailure.PreviewRequested),
            "verify" => finalResult ?? new SecurityVerificationResult(false, SecurityVerificationFailure.Cancelled),
            _ => new SecurityVerificationResult(false, SecurityVerificationFailure.Cancelled)
        };

        void ShowError(SecurityVerificationResult result)
        {
            errorText.Text = result.Failure switch
            {
                SecurityVerificationFailure.LockedOut => result.LockoutRemaining is { } lockout
                    ? string.Format(SR.M_VerificationLockedFormat, Math.Ceiling(lockout.TotalSeconds))
                    : SR.M_VerificationInvalid,
                SecurityVerificationFailure.InvalidCredentials => SR.M_VerificationInvalid,
                _ => SR.M_VerificationUnavailable
            };
            errorText.IsVisible = true;
        }
    }

    private sealed class VerificationAvailabilityCommand(Func<bool> canExecute) : ICommand
    {
        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => canExecute();

        public void Execute(object? parameter)
        {
        }

        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
