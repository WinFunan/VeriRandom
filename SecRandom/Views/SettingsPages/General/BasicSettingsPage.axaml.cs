using System;
using System.ComponentModel;
using System.Globalization;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using FluentAvalonia.UI.Controls;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Attributes;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Helpers.UI;
using SecRandom.Core.Icons;
using SecRandom.Core.Models.SubConfigs.General;
using SecRandom.Core.Services.Config;
using SecRandom.Helpers;
using SecRandom.Services.Desktop;
using SecRandom.ViewModels;
using LR = SecRandom.Langs.SettingsPages.General.Basic.Resources;

namespace SecRandom.Views.SettingsPages.General;

[PageInfo("settings.general.basic", FluentIcons.WrenchSettingsFilled, "settings.general")]
public partial class BasicSettingsPage : UserControl
{
    private bool _isApplyingProgrammaticChange;
    private bool _isSubscribed;

    public BasicSettingsPage()
    {
        Settings = ViewModel.Config.Basic;
        DataContext = this;
        InitializeComponent();
    }

    public ViewModelBase ViewModel { get; } = IAppHost.GetService<ViewModelBase>();
    public BasicSettingsConfig Settings { get; }
    public CrashRecoverySettingsConfig CrashRecoverySettings => ViewModel.Config.General.CrashRecovery;
    public bool IsUiAccessSupported => OperatingSystem.IsWindows();
    public string MainWindowTopmostModeDescription => IsUiAccessSupported
        ? LR.S_Behavior_MainWindowTopmostMode_D
        : LR.S_Behavior_MainWindowTopmostMode_NonWindows_D;
    private MainConfigHandler ConfigHandler { get; } = IAppHost.GetService<MainConfigHandler>();
    private DesktopIntegrationService DesktopIntegration { get; } = IAppHost.GetService<DesktopIntegrationService>();
    public bool IsDesktop => App.IsDesktop;

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (_isSubscribed)
            return;

        Settings.PropertyChanged += SettingsOnPropertyChanged;
        CrashRecoverySettings.PropertyChanged += CrashRecoverySettingsOnPropertyChanged;
        _isSubscribed = true;
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        if (!_isSubscribed)
            return;

        Settings.PropertyChanged -= SettingsOnPropertyChanged;
        CrashRecoverySettings.PropertyChanged -= CrashRecoverySettingsOnPropertyChanged;
        _isSubscribed = false;
    }

    private void SettingsOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isApplyingProgrammaticChange)
            return;

        if (e.PropertyName == nameof(Settings.Language))
        {
            // 先设置语言，省的 Needs Restarting 显示中文等情况发生
            var culture = Settings.Language switch
            {
                LanguageMode.ChineseSimplified => @"zh-Hans",
                LanguageMode.English => @"en-US",
                LanguageMode.Japanese => @"ja-JP",
                _ => @"zh-Hans"
            };
            App.InitializeLanguages(new CultureInfo(culture));
            SettingsView.Current?.RequestRestartApp();
        }

        if (e.PropertyName == nameof(Settings.Autostart)
            && !DesktopIntegration.TrySetAutostart(Settings.Autostart, out var autostartError))
        {
            RevertDesktopIntegration(
                nameof(Settings.Autostart),
                !Settings.Autostart,
                LR.S_Behavior_Autostart,
                autostartError);
            return;
        }

        if (e.PropertyName == nameof(Settings.UrlProtocol)
            && !DesktopIntegration.TrySetUrlProtocol(Settings.UrlProtocol, out var protocolError))
        {
            RevertDesktopIntegration(
                nameof(Settings.UrlProtocol),
                !Settings.UrlProtocol,
                LR.S_Behavior_UrlProtocol,
                protocolError);
            return;
        }

        if (e.PropertyName == nameof(Settings.LegacyUrlProtocol))
        {
            if (Settings.LegacyUrlProtocol)
            {
                // Nothing is registered until the warning is acknowledged, so the setting can sit at true
                // while the dialog is open and be reverted if the user declines.
                _ = ConfirmLegacyUrlProtocolAsync();
                return;
            }

            ApplyUrlProtocolRegistration();
        }

        ConfigHandler.Save();

        if (e.PropertyName == nameof(Settings.MainWindowTopmostMode)
            && Settings.MainWindowTopmostMode == TopmostMode.UiAccess
            && !DesktopIntegration.IsUiAccessAvailable())
            SettingsView.Current?.RequestRestartApp();
    }

    /// <summary>
    ///     Claiming upstream's <c>secrandom://</c> scheme can conflict with an installed upstream SecRandom,
    ///     because a URL scheme has a single owner. The opt-in is therefore spelled out and must be
    ///     acknowledged before anything is registered; declining reverts the switch.
    /// </summary>
    private async Task ConfirmLegacyUrlProtocolAsync()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
        {
            SetLegacyUrlProtocol(false);
            return;
        }

        var acknowledgement = new CheckBox
        {
            Content = LR.C_LegacyUrlProtocol_Confirm,
            Margin = new Avalonia.Thickness(0, 12, 0, 0)
        };
        var content = new StackPanel { Spacing = 4 };
        content.Children.Add(new TextBlock
        {
            Text = LR.C_LegacyUrlProtocol_Body,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        });
        content.Children.Add(acknowledgement);
        var dialog = new FAContentDialog
        {
            Title = LR.C_LegacyUrlProtocol_Title,
            Content = content,
            PrimaryButtonText = LR.C_LegacyUrlProtocol_Confirm,
            DefaultButton = FAContentDialogButton.None
        };
        ConfirmDialogGate.Arm(dialog, acknowledgement);

        var result = await dialog.ShowAsync(topLevel);
        if (result != FAContentDialogResult.Primary || acknowledgement.IsChecked != true)
        {
            SetLegacyUrlProtocol(false);
            return;
        }

        ApplyUrlProtocolRegistration();
    }

    private void SetLegacyUrlProtocol(bool value)
    {
        _isApplyingProgrammaticChange = true;
        Settings.LegacyUrlProtocol = value;
        _isApplyingProgrammaticChange = false;
        ConfigHandler.Save();
    }

    private void ApplyUrlProtocolRegistration()
    {
        if (DesktopIntegration.TrySetUrlProtocol(Settings.UrlProtocol, out var error))
        {
            ConfigHandler.Save();
            return;
        }

        if (Settings.LegacyUrlProtocol)
        {
            // The failure belongs to the extra scheme, so only that opt-in is rolled back.
            SetLegacyUrlProtocol(false);
            this.ShowErrorToast(string.Format(
                CultureInfo.CurrentCulture, LR.M_DesktopIntegrationFailed, LR.S_Behavior_LegacyUrlProtocol, error));
            return;
        }

        RevertDesktopIntegration(nameof(Settings.UrlProtocol), !Settings.UrlProtocol, LR.S_Behavior_UrlProtocol, error);
    }

    private void RevertDesktopIntegration(string propertyName, bool value, string title, string error)
    {
        _isApplyingProgrammaticChange = true;
        if (propertyName == nameof(Settings.Autostart))
            Settings.Autostart = value;
        else
            Settings.UrlProtocol = value;
        _isApplyingProgrammaticChange = false;

        ConfigHandler.Save();
        this.ShowErrorToast(string.Format(CultureInfo.CurrentCulture, LR.M_DesktopIntegrationFailed, title, error));
    }

    private void CrashRecoverySettingsOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        ConfigHandler.Save();
    }

}
