using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using SecRandom.Core.Attributes;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Icons;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Services.Config;
using SecRandom.Core.Services.Verification;
using SecRandom.Helpers;
using SecRandom.Services.Consent;
using SecRandom.Services.Desktop;
using SecRandom.Services.Verification;
using SecRandom.Shared;
using SecRandom.Shared.Models.Verification;
using LR = SecRandom.Langs.SettingsPages.General.Verification.Resources;

namespace SecRandom.Views.SettingsPages.General;

[PageInfo("settings.general.verification", FluentIcons.DocumentCheckmarkFilled, "settings.general")]
public partial class VerificationSettingsPage : UserControl
{
    private static readonly int[] RetentionOptions = [0, 7, 15, 30, 60, 90, 365];
    private static readonly long[] StorageOptions = [0, 16L * 1024 * 1024, 32L * 1024 * 1024, 64L * 1024 * 1024, 128L * 1024 * 1024, 256L * 1024 * 1024, 512L * 1024 * 1024, 1024L * 1024 * 1024];
    private MainConfigHandler ConfigHandler { get; } = IAppHost.GetService<MainConfigHandler>();
    private DrawProofAttestationService AttestationService { get; } = IAppHost.GetService<DrawProofAttestationService>();    private ProofIntegrityVerifier IntegrityVerifier { get; } = IAppHost.GetService<ProofIntegrityVerifier>();
    private IExternalLauncher ExternalLauncher { get; } = IAppHost.GetService<IExternalLauncher>();
    private INistBeaconClient BeaconClient { get; } = IAppHost.GetService<INistBeaconClient>();
    private BeaconEntropyProvider BeaconEntropy { get; } = IAppHost.GetService<BeaconEntropyProvider>();
    private DrawProofExportService ProofExporter { get; } = IAppHost.GetService<DrawProofExportService>();
    private OwnProofExportService OwnProofExporter { get; } = IAppHost.GetService<OwnProofExportService>();
    private bool _verificationModeSelectionReady;
    private bool _restoringVerificationModeSelection;
    private bool _restoringTimestampAuthority;
    private bool _restoringAttestationUpload;

    /// <summary>
    ///     Projects the stored ordinary-draw upload choice onto the settings radio pair. The group is
    ///     disabled while the cross-border notice has not been accepted, because every SECTL path —
    ///     this upload included — stays blocked until then.
    /// </summary>
    private void RefreshAttestationUpload()
    {
        _restoringAttestationUpload = true;
        try
        {
            var mode = ConfigHandler.Data.General.Verification.AttestationUpload;
            AttestationUploadOnRadio.IsChecked = mode == AttestationUploadMode.Enabled;
            AttestationUploadOffRadio.IsChecked = mode == AttestationUploadMode.Disabled;
            var egressAllowed = SectlTrafficPolicy.IsEgressAllowed(ConfigHandler);
            AttestationUploadGroup.IsEnabled = egressAllowed;
            // 未同意出境须知时就地给出补签入口：既告诉用户去哪里补，也省掉「补签后还要重进本页」
            EgressConsentPrompt.IsVisible = !egressAllowed;
        }
        finally
        {
            _restoringAttestationUpload = false;
        }
    }

    private void AttestationUpload_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_restoringAttestationUpload)
            return;

        var mode = AttestationUploadOnRadio.IsChecked == true
            ? AttestationUploadMode.Enabled
            : AttestationUploadMode.Disabled;
        if (ConfigHandler.Data.General.Verification.AttestationUpload == mode)
            return;

        ConfigHandler.Data.General.Verification.AttestationUpload = mode;
        ConfigHandler.Save();
    }

    /// <summary>
    ///     出境需要**两个**同意——上游的在线服务须知与本分支的跨境须知，缺一个都不放行。这里按顺序补齐
    ///     缺失的那个（各自的对话框由策略自己负责），成功后**就地刷新**，单选组立即解锁。
    /// </summary>
    private async void GrantEgressConsent_OnClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not { } xamlRoot)
            return;

        if (!SectlTrafficPolicy.IsServicesPolicyAccepted(ConfigHandler)
            && !await SecRandomServicesConsent.EnsureAsync(ConfigHandler, xamlRoot))
            return;

        if (!SectlTrafficPolicy.IsTransferNoticeAccepted(ConfigHandler)
            && !await SectlTrafficPolicy.EnsureTransferAcceptedAsync(ConfigHandler, xamlRoot))
            return;

        RefreshAttestationUpload();
    }

    public VerificationSettingsPage()
    {
        DataContext = this;
        InitializeComponent();
        // 有签名在生效时，抽取验证设置整体属于被覆盖范围：改动它等于让签名失效，故禁用这些输入
        ConfigPolicyLock.Apply(
            S_VerificationMode, S_BeaconEntropy, S_TimestampAuthority, S_BeaconEndpoint, S_AttestationUpload);
        _restoringTimestampAuthority = true;
        TimestampAuthorityToggle.IsChecked = ConfigHandler.Data.General.Verification.TimestampAuthorityEnabled;
        _restoringTimestampAuthority = false;
        TimestampAuthorityToggle.IsCheckedChanged += TimestampAuthority_OnIsCheckedChanged;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    /// <summary>
    ///     Time stamping is on by default, but the user may switch it off. Turning it off also removes the
    ///     period anchor the beacon match is normally checked against, so the coupling is spelled out and
    ///     must be confirmed before the change is persisted.
    /// </summary>
    private async void TimestampAuthority_OnIsCheckedChanged(object? sender, RoutedEventArgs e)
    {
        if (_restoringTimestampAuthority || sender is not ToggleSwitch toggle)
            return;

        var requested = toggle.IsChecked == true;
        if (requested == ConfigHandler.Data.General.Verification.TimestampAuthorityEnabled)
            return;

        if (!requested)
        {
            var acknowledgement = new CheckBox
            {
                Content = LR.C_TimestampDisableConfirm,
                Margin = new Avalonia.Thickness(0, 12, 0, 0)
            };
            var content = new StackPanel { Spacing = 4 };
            content.Children.Add(CreateDialogText(LR.C_TimestampDisableBody));
            content.Children.Add(acknowledgement);
            var dialog = new FAContentDialog
            {
                Title = LR.C_TimestampDisableTitle,
                Content = content,
                PrimaryButtonText = LR.C_ModeConfirmSwitch,
                CloseButtonText = LR.C_Cancel,
                DefaultButton = FAContentDialogButton.Close
            };
            ConfirmDialogGate.Arm(dialog, acknowledgement);

            var result = await dialog.ShowAsync(TopLevel.GetTopLevel(this));
            if (result != FAContentDialogResult.Primary || acknowledgement.IsChecked != true)
            {
                _restoringTimestampAuthority = true;
                toggle.IsChecked = true;
                _restoringTimestampAuthority = false;
                return;
            }
        }

        ConfigHandler.Data.General.Verification.TimestampAuthorityEnabled = requested;
        ConfigHandler.Save();
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        _verificationModeSelectionReady = true;
        AttestationService.StatusChanged += AttestationStatus_OnChanged;
        RefreshProofQueueStatus();
        RefreshAttestationUpload();
        if (string.IsNullOrEmpty(ProofIntegrityStatusText.Text))
            ProofIntegrityStatusText.Text = LR.M_ProofIntegrityIdle;
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        AttestationService.StatusChanged -= AttestationStatus_OnChanged;
    }

    private void AttestationStatus_OnChanged(object? sender, EventArgs e)
    {
        // Submission runs on a background worker, so the refresh must be marshaled to the UI thread.
        if (Dispatcher.UIThread.CheckAccess())
            RefreshProofQueueStatus();
        else
            Dispatcher.UIThread.Post(RefreshProofQueueStatus);
    }

    private void RefreshProofQueueStatus() => ApplyProofQueueStatus(AttestationService.GetStatus());

    private void ApplyProofQueueStatus(ProofAttestationStatus status)
    {
        List<string> parts = [];
        if (status.PendingCount > 0)
            parts.Add(string.Format(CultureInfo.CurrentCulture, LR.M_ProofQueuePending, status.PendingCount));
        if (status.ReceiptFailedCount > 0)
            parts.Add(string.Format(CultureInfo.CurrentCulture, LR.M_ProofQueueFailed, status.ReceiptFailedCount));
        if (status.TimestampFailedCount > 0)
            parts.Add(string.Format(CultureInfo.CurrentCulture, LR.M_ProofQueueTimestampFailed, status.TimestampFailedCount));
        if (status.ConflictCount > 0)
            parts.Add(string.Format(CultureInfo.CurrentCulture, LR.M_ProofQueueConflict, status.ConflictCount));

        ProofQueueStatusText.Text = parts.Count == 0 ? LR.M_ProofQueueIdle : string.Join(" · ", parts);
        RetryProofQueueButton.IsEnabled = status.HasOutstandingWork;

        // 链告警单独醒目提示：它是「服务端记的链头比本地高」的唯一信号，混在计数摘要里太容易被忽略
        ProofChainAlertText.Text = status.ChainAlertCount > 0
            ? string.Format(CultureInfo.CurrentCulture, LR.M_ProofQueueChainAlert, status.ChainAlertCount)
            : string.Empty;
        ProofChainAlertText.IsVisible = status.ChainAlertCount > 0;
    }

    private async void RetryProofQueue_OnClick(object? sender, RoutedEventArgs e)
    {
        RetryProofQueueButton.IsEnabled = false;
        try
        {
            ApplyProofQueueStatus(await AttestationService.RetryNowAsync());
        }
        finally
        {
            RetryProofQueueButton.IsEnabled = AttestationService.GetStatus().HasOutstandingWork;
        }
    }

    public int SelectedVerificationModeIndex => (int)ConfigHandler.Data.General.Verification.Mode;

    private static TextBlock CreateDialogText(string text, bool bold = false, double topMargin = 0) => new()
    {
        Text = text,
        TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        FontWeight = bold ? Avalonia.Media.FontWeight.SemiBold : Avalonia.Media.FontWeight.Normal,
        Margin = new Avalonia.Thickness(0, topMargin, 0, 0)
    };

    public bool BeaconEntropyEnabled
    {
        get => ConfigHandler.Data.General.Verification.BeaconEntropyEnabled;
        set
        {
            if (ConfigHandler.Data.General.Verification.BeaconEntropyEnabled == value)
                return;

            ConfigHandler.Data.General.Verification.BeaconEntropyEnabled = value;
            ConfigHandler.Save();
        }
    }

    public bool BeaconPrefetchEnabled
    {
        get => ConfigHandler.Data.General.Verification.BeaconPrefetchEnabled;
        set
        {
            if (ConfigHandler.Data.General.Verification.BeaconPrefetchEnabled == value)
                return;

            ConfigHandler.Data.General.Verification.BeaconPrefetchEnabled = value;
            ConfigHandler.Save();
        }
    }

    public bool BeaconAutoPrefetchEnabled
    {
        get => ConfigHandler.Data.General.Verification.BeaconAutoPrefetchEnabled;
        set
        {
            if (ConfigHandler.Data.General.Verification.BeaconAutoPrefetchEnabled == value)
                return;

            ConfigHandler.Data.General.Verification.BeaconAutoPrefetchEnabled = value;
            ConfigHandler.Save();
        }
    }

    /// <summary>Index of the configured source in <see cref="BeaconSourceCatalog" />; unknown ids read as NIST.</summary>
    public int SelectedBeaconSourceIndex
    {
        get
        {
            var source = ResolvedBeaconSource();
            for (var index = 0; index < BeaconSourceCatalog.All.Count; index++)
            {
                if (string.Equals(BeaconSourceCatalog.All[index].Id, source.Id, StringComparison.Ordinal))
                    return index;
            }

            return 0;
        }
    }

    private BeaconSource ResolvedBeaconSource() =>
        BeaconSourceCatalog.Find(ConfigHandler.Data.General.Verification.BeaconSourceId)
        ?? BeaconSourceCatalog.Default;

    private void BeaconSource_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var index = BeaconSourceCombo.SelectedIndex;
        if (index < 0 || index >= BeaconSourceCatalog.All.Count)
            return;

        var source = BeaconSourceCatalog.All[index];

        // Driven from code rather than bound to IsRandomOrgSourceSelected: the page raises no change notifications
        // for derived properties, so a binding keeps whatever it evaluated at load and the button would never appear
        // when the user switches to random.org afterwards.
        RandomOrgSetupButton.IsVisible =
            string.Equals(source.Id, BeaconSourceCatalog.RandomOrg.Id, StringComparison.Ordinal);

        if (string.Equals(ConfigHandler.Data.General.Verification.BeaconSourceId, source.Id, StringComparison.Ordinal))
            return;

        // Switching the source invalidates a cached pulse from the previous one only because the provider keys its
        // cache by source id as well; nothing else has to be reset here.
        ConfigHandler.Data.General.Verification.BeaconSourceId = source.Id;
        ConfigHandler.Save();
    }

    /// <summary>True while random.org is the selected source, which is when its key setup row makes sense.</summary>
    public bool IsRandomOrgSourceSelected =>
        string.Equals(ResolvedBeaconSource().Id, BeaconSourceCatalog.RandomOrg.Id, StringComparison.Ordinal);

    /// <summary>
    ///     Walks the user through random.org's own sign-up and API-key pages.
    /// </summary>
    /// <remarks>
    ///     The account, the application form and the key are all handled in the user's real browser: registering an
    ///     account on their behalf (or collecting their mailbox confirmation) would mean acting as them against
    ///     another service's terms, and an embedded browser would put their session inside this process. This dialog
    ///     only opens the right pages, shows the usage text to paste, and stores what they bring back.
    /// </remarks>
    private async void ConfigureRandomOrgSigning_OnClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not { } topLevel)
            return;

        var guide = CreateDialogText(LR.M_RandomOrgSetupGuide);
        var accountButton = new Button { Content = LR.C_RandomOrgOpenAccount };
        accountButton.Click += (_, _) => ExternalLauncher.TryOpenUri("https://accounts.random.org/");

        var apiKeyButton = new Button { Content = LR.C_RandomOrgOpenApiKeys };
        apiKeyButton.Click += (_, _) => ExternalLauncher.TryOpenUri("https://api.random.org/dashboard");

        var keyBox = new TextBox
        {
            PlaceholderText = LR.C_RandomOrgKeyPlaceholder,
            PasswordChar = '•',
            Text = IAppHost.GetService<RandomOrgCredentialStore>().Load() ?? string.Empty
        };

        var status = CreateDialogText(string.Empty, topMargin: 4);
        var validateButton = new Button { Content = LR.C_RandomOrgValidateKey };
        validateButton.Click += async (_, _) =>
        {
            validateButton.IsEnabled = false;
            status.Text = LR.M_BeaconFetching;
            try
            {
                var usage = await IAppHost.GetService<RandomOrgApiClient>()
                    .GetUsageAsync(keyBox.Text ?? string.Empty, CancellationToken.None);
                status.Text = string.Format(CultureInfo.CurrentCulture, LR.M_RandomOrgKeyValid,
                    usage.BitsLeft, usage.RequestsLeft);
            }
            catch (Exception exception)
            {
                status.Text = string.Format(CultureInfo.CurrentCulture, LR.M_RandomOrgKeyInvalid, exception.Message);
            }
            finally
            {
                validateButton.IsEnabled = true;
            }
        };

        var content = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                guide,
                new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    Spacing = 8,
                    Children = { accountButton, apiKeyButton }
                },
                keyBox,
                validateButton,
                status
            }
        };

        var dialog = new FAContentDialog
        {
            Title = LR.C_RandomOrgSetup,
            Content = content,
            PrimaryButtonText = LR.C_RandomOrgSaveKey,
            CloseButtonText = LR.C_ProofReplay_Cancel,
            DefaultButton = FAContentDialogButton.Close
        };

        if (await dialog.ShowAsync(topLevel) != FAContentDialogResult.Primary)
            return;

        try
        {
            IAppHost.GetService<RandomOrgCredentialStore>().Save(keyBox.Text ?? string.Empty);
            BeaconStatusText.Text = LR.M_RandomOrgKeySaved;
        }
        catch (Exception exception)
        {
            BeaconStatusText.Text = exception.Message;
        }
    }

    public string BeaconEndpoint
    {
        get => ConfigHandler.Data.General.Verification.BeaconEndpoint;
        set
        {
            var normalized = value ?? string.Empty;
            if (string.Equals(ConfigHandler.Data.General.Verification.BeaconEndpoint, normalized, StringComparison.Ordinal))
                return;

            ConfigHandler.Data.General.Verification.BeaconEndpoint = normalized;
            ConfigHandler.Save();
        }
    }

    private async void FetchBeacon_OnClick(object? sender, RoutedEventArgs e)
    {
        FetchBeaconButton.IsEnabled = false;
        BeaconStatusText.Text = LR.M_BeaconFetching;
        try
        {
            var pulse = await BeaconEntropy.FetchForTestAsync(CancellationToken.None);
            BeaconStatusText.Text = string.Format(
                CultureInfo.CurrentCulture,
                LR.M_BeaconFetched,
                pulse.PulseIndex,
                pulse.ChainIndex,
                pulse.TimeStamp.ToLocalTime());
        }
        catch (Exception exception)
        {
            // The message names the source, the host and the HTTP status, so a blocked or throttled source is
            // distinguishable from a wrong address. It is appended rather than formatted into the resource, so the
            // three languages keep their existing sentence and no translation has to guess at a placeholder.
            BeaconStatusText.Text = $"{LR.M_BeaconFailed}{Environment.NewLine}{exception.Message}";
        }
        finally
        {
            FetchBeaconButton.IsEnabled = true;
        }
    }

    public int SelectedRetentionIndex
    {
        get => Array.IndexOf(RetentionOptions, ConfigHandler.Data.General.ProofRetention.RetentionDays) is var index && index >= 0
            ? index
            : Array.IndexOf(RetentionOptions, 0);
        set
        {
            if (value < 0 || value >= RetentionOptions.Length)
                return;

            ConfigHandler.Data.General.ProofRetention.RetentionDays = RetentionOptions[value];
            ConfigHandler.Save();
        }
    }

    private void VerifyProofs_OnClick(object? sender, RoutedEventArgs e)
    {
        VerifyProofsButton.IsEnabled = false;
        try
        {
            ProofIntegrityStatusText.Text = FormatIntegrityReport(IntegrityVerifier.Verify());
        }
        finally
        {
            VerifyProofsButton.IsEnabled = true;
        }
    }

    private static string FormatIntegrityReport(ProofIntegrityReport report)
    {
        var counts = string.Format(
            CultureInfo.CurrentCulture, LR.M_ProofIntegrityCounts, report.Total, report.Chained, report.Unchained);
        var own = report.OwnReference;
        List<string> lines =
        [
            counts,
            string.Format(
                CultureInfo.CurrentCulture,
                LR.M_ProofIntegrityOwnReference,
                own.Nodes,
                own.Timestamped,
                own.HeadIndex,
                own.RetainedFromIndex),
            LR.M_ProofIntegrityLimits
        ];

        // 参考链（Own）的问题单独报：它刻意不参与 IsHealthy，所以不能因为 Up 链健康就被吞掉
        if (!own.IsIntact)
            lines.Add(string.Format(
                CultureInfo.CurrentCulture,
                LR.M_ProofIntegrityOwnReferenceProblems,
                own.Modified,
                own.SequenceViolations));

        if (report.IsHealthy && report.MissingTail == 0)
        {
            if (own.IsIntact)
                lines.Add(LR.M_ProofIntegrityHealthy);
            return string.Join(Environment.NewLine, lines);
        }

        lines.Add(string.Format(
            CultureInfo.CurrentCulture,
            LR.M_ProofIntegrityProblems,
            report.Gaps,
            report.BrokenLinks,
            report.Modified,
            report.BeyondHead,
            report.MissingTail,
            report.Expired));
        lines.AddRange(report.Issues
            .Take(5)
            .Select(issue => $"{IssueLabel(issue.Kind)}：{Path.GetFileName(issue.Path)}"));
        return string.Join(Environment.NewLine, lines);
    }

    private static string IssueLabel(ProofIntegrityIssueKind kind) => kind switch
    {
        ProofIntegrityIssueKind.Unreadable => LR.M_ProofIssue_Unreadable,
        ProofIntegrityIssueKind.Modified => LR.M_ProofIssue_Modified,
        ProofIntegrityIssueKind.UnsupportedVersion => LR.M_ProofIssue_UnsupportedVersion,
        ProofIntegrityIssueKind.BrokenLink => LR.M_ProofIssue_BrokenLink,
        ProofIntegrityIssueKind.Gap => LR.M_ProofIssue_Gap,
        ProofIntegrityIssueKind.BeyondHead => LR.M_ProofIssue_BeyondHead,
        _ => LR.M_ProofIssue_MissingTail
    };

    private void OpenProofFolder_OnClick(object? sender, RoutedEventArgs e)
    {
        var directory = Utils.GetDirectoryPath("proofs");
        ExternalLauncher.TryOpenPath(directory);
    }

    private void OpenVerificationWebsite_OnClick(object? sender, RoutedEventArgs e)
    {
        ExternalLauncher.TryOpenUri("https://fair.sectl.cn/");
    }

    private void OpenProofReplay_OnClick(object? sender, RoutedEventArgs e)
    {
        // 重放窗口是独立的模态视图，它打开时自己重新读取证明链，所以这里只负责打开，不预先加载数据。
        _ = IAppHost.GetService<ProofReplayViewService>().ShowAsync();
    }

    public int SelectedStorageIndex
    {
        get => Array.IndexOf(StorageOptions, ConfigHandler.Data.General.ProofRetention.MaximumStorageBytes) is var index && index >= 0
            ? index
            : Array.IndexOf(StorageOptions, 0);
        set
        {
            if (value < 0 || value >= StorageOptions.Length)
                return;

            ConfigHandler.Data.General.ProofRetention.MaximumStorageBytes = StorageOptions[value];
            ConfigHandler.Save();
        }
    }

    private async void VerificationMode_OnChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_verificationModeSelectionReady || _restoringVerificationModeSelection || sender is not ComboBox { SelectedIndex: >= 0 } selector)
            return;

        var requestedMode = (VerificationMode)selector.SelectedIndex;
        var currentMode = ConfigHandler.Data.General.Verification.Mode;
        if (requestedMode == currentMode)
            return;

        var dialogContent = new StackPanel { Spacing = 4 };
        if (requestedMode == VerificationMode.Ordinary)
        {
            dialogContent.Children.Add(CreateDialogText(LR.M_ModeConfirmLocal));
        }
        else
        {
            dialogContent.Children.Add(CreateDialogText(LR.M_ModeConfirmFormal));
            dialogContent.Children.Add(CreateDialogText(LR.M_ModeConfirmFormalNoticeTitle, bold: true, topMargin: 8));
            dialogContent.Children.Add(CreateDialogText(LR.M_ModeConfirmFormalNotice));
            dialogContent.Children.Add(CreateDialogText(LR.M_ModeConfirmFormalWarning, bold: true));
        }

        var acknowledgement = new CheckBox
        {
            Content = LR.C_ModeReadConfirm,
            Margin = new Avalonia.Thickness(0, 12, 0, 0)
        };
        dialogContent.Children.Add(acknowledgement);

        // Switching to formal notarization ships data to SECTL, so the fork-specific online-services
        // acknowledgement becomes mandatory here and must be signed inside this same dialog.
        CheckBox? servicesAcknowledgement = null;
        if (requestedMode == VerificationMode.FormalNotarized && SecRandomServicesConsent.IsRequired(ConfigHandler))
            servicesAcknowledgement = SecRandomServicesConsent.AppendDisclosure(dialogContent);

        var dialog = new FAContentDialog
        {
            Title = LR.C_ModeConfirmTitle,
            Content = dialogContent,
            PrimaryButtonText = LR.C_ModeConfirmSwitch,
            CloseButtonText = LR.C_Cancel,
            DefaultButton = FAContentDialogButton.Close
        };

        // The confirm button additionally stays disabled for a forced minimum reading period, so a mode that
        // sends data to SECTL cannot be entered by a reflex click.
        CheckBox[] acknowledgements = servicesAcknowledgement is null
            ? [acknowledgement]
            : [acknowledgement, servicesAcknowledgement];
        ConfirmDialogGate.Arm(dialog, acknowledgements);

        var result = await dialog.ShowAsync(TopLevel.GetTopLevel(this));

        if (result == FAContentDialogResult.Primary && acknowledgement.IsChecked == true
            && (servicesAcknowledgement is null || servicesAcknowledgement.IsChecked == true))
        {
            if (servicesAcknowledgement is not null)
                SecRandomServicesConsent.MarkAccepted(ConfigHandler);

            ConfigHandler.Data.General.Verification.Mode = requestedMode;
            ConfigHandler.Save();
        }

        _restoringVerificationModeSelection = true;
        selector.SelectedIndex = (int)ConfigHandler.Data.General.Verification.Mode;
        _restoringVerificationModeSelection = false;
    }
}
