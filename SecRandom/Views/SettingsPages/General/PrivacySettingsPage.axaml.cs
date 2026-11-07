using System;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Attributes;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Helpers.UI;
using SecRandom.Core.Icons;
using SecRandom.Core.Models.SubConfigs.General;
using SecRandom.Core.Services.Config;
using SecRandom.Services.Consent;
using SecRandom.ViewModels;
using lsp = SecRandom.Langs.SettingsPages.General.Privacy;

namespace SecRandom.Views.SettingsPages.General;

[PageInfo("settings.general.privacy", FluentIcons.EyeFilled, "settings.general")]
public partial class PrivacySettingsPage : UserControl
{
    private bool _revertingChannel;
    private bool _initializingEgress;

    public PrivacySettingsPage()
    {
        Settings = ViewModel.Config.General.PrivacySettings;
        DataContext = this;
        InitializeComponent();
        // 隐私设置整体被策略覆盖
        ConfigPolicyLock.Apply(S_SentryTelemetry_Enabled, S_OnlineStatus_Mode);

        // 联网总开关是设备本地记录，不参与配置策略锁定：策略锁保护的是 settings.json 中的隐私项
        _initializingEgress = true;
        SectlEgressSwitchToggle.IsChecked = EgressSwitch?.IsDisabled == true;
        _initializingEgress = false;
        // 本版本把开关固定为禁用：控件只显示状态，不接受改动；解除强制后这一行会自动恢复可操作
        SectlEgressSwitchToggle.IsEnabled = EgressSwitch is not null && !EgressSwitch.IsForced;
        UpdateEgressStatus();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public ViewModelBase ViewModel { get; } = IAppHost.GetService<ViewModelBase>();
    public PrivacySettingsConfig Settings { get; }
    private MainConfigHandler ConfigHandler { get; } = IAppHost.GetService<MainConfigHandler>();
    private SectlEgressSwitch? EgressSwitch { get; } = IAppHost.TryGetService<SectlEgressSwitch>();

    private void OnLoaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        Settings.PropertyChanged += SettingsOnPropertyChanged;

    private void OnUnloaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        Settings.PropertyChanged -= SettingsOnPropertyChanged;

    /// <summary>
    ///     Lets an installation that skipped the cross-border notice during first-run setup sign it later.
    ///     Everything that could send data to SECTL stays disabled until this succeeds.
    /// </summary>
    private async void CrossBorderTransferAccept_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        // The master switch outranks the notice, so signing it here would promise nothing. Say why instead of
        // opening a dialog whose confirmation changes no behaviour.
        if (SectlTrafficPolicy.IsEgressSwitchOff())
        {
            this.ShowWarningToast(lsp.Resources.M_SectlEgressForced);
            return;
        }

        var owner = TopLevel.GetTopLevel(this);
        if (owner is not null)
            await SectlTrafficPolicy.EnsureTransferAcceptedAsync(ConfigHandler, owner);
    }

    /// <summary>
    ///     Enabling either online channel ships data to SecRandom/SECTL, so the fork-specific online-services
    ///     acknowledgement becomes mandatory at that moment. It is signed through the shared second-level
    ///     dialog and can never be applied silently; declining reverts the toggle that triggered it.
    /// </summary>
    private async void SettingsOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_revertingChannel)
            return;

        var enabledSentryTelemetry =
            e.PropertyName == nameof(PrivacySettingsConfig.SentryTelemetryEnabled) && Settings.SentryTelemetryEnabled;
        var enabledOnlineStatus =
            e.PropertyName == nameof(PrivacySettingsConfig.OnlineStatusMode) && Settings.OnlineStatusMode != OnlineStatusMode.Off;
        if (!enabledSentryTelemetry && !enabledOnlineStatus)
            return;

        var owner = TopLevel.GetTopLevel(this);
        if (owner is null || await SecRandomServicesConsent.EnsureAsync(ConfigHandler, owner))
            return;

        _revertingChannel = true;
        try
        {
            if (enabledSentryTelemetry)
                Settings.SentryTelemetryEnabled = false;
            else
                Settings.OnlineStatusMode = OnlineStatusMode.Off;
        }
        finally
        {
            _revertingChannel = false;
        }
    }

    /// <summary>
    ///     The master switch is the one privacy control that lives outside <c>settings.json</c>, so it is written
    ///     straight into its device-local store. A failed write is reverted in the UI instead of leaving a switch that
    ///     claims to be on while traffic is still allowed.
    /// </summary>
    private void SectlEgressSwitch_OnCheckedChanged(object? sender, RoutedEventArgs e)
    {
        if (_initializingEgress || EgressSwitch is null || EgressSwitch.IsForced)
            return;

        var disabled = SectlEgressSwitchToggle.IsChecked == true;
        try
        {
            EgressSwitch.SetDisabled(disabled);
        }
        catch (Exception exception)
        {
            _initializingEgress = true;
            SectlEgressSwitchToggle.IsChecked = EgressSwitch.IsDisabled;
            _initializingEgress = false;
            this.ShowErrorToast(lsp.Resources.S_SectlEgress_Switch, exception);
        }

        UpdateEgressStatus();
    }

    private void UpdateEgressStatus() =>
        SectlEgressStatus.Text = EgressSwitch?.IsForced == true
            ? lsp.Resources.M_SectlEgressForced
            : EgressSwitch?.IsDisabled == true
                ? lsp.Resources.M_SectlEgressOn
                : lsp.Resources.M_SectlEgressOff;
}
