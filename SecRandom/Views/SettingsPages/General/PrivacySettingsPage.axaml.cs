using System.ComponentModel;
using Avalonia.Controls;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Attributes;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Icons;
using SecRandom.Core.Models.SubConfigs.General;
using SecRandom.Core.Services.Config;
using SecRandom.Services.Consent;
using SecRandom.ViewModels;

namespace SecRandom.Views.SettingsPages.General;

[PageInfo("settings.general.privacy", FluentIcons.EyeFilled, "settings.general")]
public partial class PrivacySettingsPage : UserControl
{
    private bool _revertingChannel;

    public PrivacySettingsPage()
    {
        Settings = ViewModel.Config.General.PrivacySettings;
        DataContext = this;
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public ViewModelBase ViewModel { get; } = IAppHost.GetService<ViewModelBase>();
    public PrivacySettingsConfig Settings { get; }
    private MainConfigHandler ConfigHandler { get; } = IAppHost.GetService<MainConfigHandler>();

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
}
