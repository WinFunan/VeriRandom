using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using FluentAvalonia.UI.Controls;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Attributes;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Helpers.UI;
using SecRandom.Core.Icons;
using SecRandom.Core.Models.SubConfigs;
using SecRandom.Core.Services.Config;
using SecRandom.Core.Services.Security;
using SecRandom.Models;
using SecRandom.Services.Security;
using SecRandom.Services.Security.Fido2;
using SecRandom.ViewModels;
using SR = SecRandom.Langs.SettingsPages.Security.Resources;

namespace SecRandom.Views.SettingsPages.General;

[PageInfo("settings.general.security", FluentIcons.ShieldKeyholeFilled, "settings.general")]
public partial class SecuritySettingsPage : UserControl, INotifyPropertyChanged
{
    private readonly ISecurityService _securityService = IAppHost.GetService<ISecurityService>();

    /// <summary>
    ///     分离签名的文档读写。私钥只作为方法参数传入，不进入任何字段或配置，抽取机因此无法自行签发。
    /// </summary>
    private readonly ConfigPolicySignatureService _configPolicySignatureService =
        IAppHost.GetService<ConfigPolicySignatureService>();

    private readonly Fido2SecurityKeyService? _fido2Service =
        IAppHost.TryGetService<Fido2SecurityKeyService>();
    private bool _refreshing;
    private bool _isSettingsSubscribed;
    private event PropertyChangedEventHandler? NotifyPropertyChanged;

    public SecuritySettingsPage()
    {
        Settings = IAppHost.GetService<SecuritySettingsStore>().Data;
        SudoModeDurationValue = Settings.SudoModeDurationSeconds;
        FactorOptions =
        [
            new(SR.S_Password, () => Settings.PasswordEnabled, value => Settings.PasswordEnabled = value),
            new(SR.S_Totp, () => Settings.TotpEnabled, value => Settings.TotpEnabled = value),
            new(SR.S_Usb, () => Settings.UsbBindingEnabled, value => Settings.UsbBindingEnabled = value),
            new(SR.S_Fido2, () => Settings.Fido2Enabled, value => Settings.Fido2Enabled = value)
        ];
        SelectedFactorOptions =
            new AvaloniaList<MultiSelectSettingOption>(FactorOptions.Where(option => option.IsSelected));
        SelectedFactorOptions.CollectionChanged += SelectedFactorOptionsOnCollectionChanged;
        IntegrityActionOptions =
        [
            new(SettingsIntegrityAction.Confirm, SR.O_IntegrityAction_Confirm),
            new(SettingsIntegrityAction.AutoRestore, SR.O_IntegrityAction_AutoRestore)
        ];
        IntegrityRestoreSourceOptions =
        [
            new(SettingsIntegrityRestoreSource.Local, SR.O_IntegrityRestoreSource_Local),
            new(SettingsIntegrityRestoreSource.LocalThenCloud, SR.O_IntegrityRestoreSource_LocalThenCloud),
            new(SettingsIntegrityRestoreSource.CloudThenLocal, SR.O_IntegrityRestoreSource_CloudThenLocal)
        ];
        SignedPolicyModeOptions =
        [
            new(ConfigIntegrityMode.Off, SR.O_SignedPolicy_Off),
            new(ConfigIntegrityMode.LocalFingerprint, SR.O_SignedPolicy_LocalFingerprint),
            new(ConfigIntegrityMode.SignedPolicy, SR.O_SignedPolicy_Signed)
        ];
        DataContext = this;
        InitializeComponent();
        SubscribeSettings();
        RefreshSecurityState();
    }

    public ViewModelBase ViewModel { get; } = IAppHost.GetService<ViewModelBase>();
    public SecuritySettingsConfig Settings { get; }
    public AvaloniaList<MultiSelectSettingOption> FactorOptions { get; }
    public AvaloniaList<MultiSelectSettingOption> SelectedFactorOptions { get; }
    public AvaloniaList<SettingOption<SettingsIntegrityAction>> IntegrityActionOptions { get; }
    public AvaloniaList<SettingOption<SettingsIntegrityRestoreSource>> IntegrityRestoreSourceOptions { get; }
    public SettingOption<SettingsIntegrityAction>? SelectedIntegrityActionOption { get; private set; }
    public SettingOption<SettingsIntegrityRestoreSource>? SelectedIntegrityRestoreSourceOption { get; private set; }
    public AvaloniaList<SettingOption<ConfigIntegrityMode>> SignedPolicyModeOptions { get; }
    public SettingOption<ConfigIntegrityMode>? SelectedSignedPolicyModeOption { get; private set; }
    public string SignedPolicyFingerprint { get; private set; } = string.Empty;
    public string SignedPolicyStatus { get; private set; } = string.Empty;

    public AvaloniaList<Fido2KeyOption> Fido2Keys { get; } = [];
    public bool HasFido2Keys => Fido2Keys.Count > 0;

    public Fido2KeyOption? SelectedFido2Key
    {
        get => _selectedFido2Key;
        set
        {
            if (ReferenceEquals(_selectedFido2Key, value))
                return;
            _selectedFido2Key = value;
            NotifyPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedFido2Key)));
        }
    }

    public Fido2KeyOption? SelectedSigningKey
    {
        get => _selectedSigningKey;
        set
        {
            if (ReferenceEquals(_selectedSigningKey, value))
                return;
            _selectedSigningKey = value;
            NotifyPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedSigningKey)));
        }
    }

    private Fido2KeyOption? _selectedFido2Key;
    private Fido2KeyOption? _selectedSigningKey;
    public bool IsAutoRestoreSelected => Settings.SettingsIntegrityAction == SettingsIntegrityAction.AutoRestore;
    public bool CanEnableSecurity { get; private set; }
    public bool IsSecurityEnabled { get; private set; }
    public bool HasPassword { get; private set; }
    public bool HasTotp { get; private set; }
    public bool CanSetPassword => !HasPassword;
    public bool CanConfigureAdditionalFactors { get; private set; }
    public bool CanEditFactorSelection { get; private set; }
    public bool CanEditProtectedOperations { get; private set; }
    public bool CanEditSudoModeDuration => CanEditProtectedOperations && Settings.SudoModeEnabled;
    public string TotpButtonText { get; private set; } = SR.C_SetTotp;
    public bool IsLockedOut { get; private set; }
    public string LockoutText { get; private set; } = string.Empty;
    public double SudoModeDurationValue { get; set; }

    event PropertyChangedEventHandler? INotifyPropertyChanged.PropertyChanged
    {
        add => NotifyPropertyChanged += value;
        remove => NotifyPropertyChanged -= value;
    }

    private SecuritySettingsStore SettingsStore { get; } = IAppHost.GetService<SecuritySettingsStore>();

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        SubscribeSettings();
        RefreshSecurityState();
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        if (!_isSettingsSubscribed)
            return;

        Settings.PropertyChanged -= SettingsOnPropertyChanged;
        _isSettingsSubscribed = false;
    }

    private async void SelectedFactorOptionsOnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_refreshing)
            return;

        var state = _securityService.GetUiState();
        var requestedOptions = FactorOptions
            .Where(option => SelectedFactorOptions.Contains(option) ||
                             (ReferenceEquals(option, FactorOptions[0]) && state.HasPassword))
            .ToArray();
        if (FactorOptions.All(option => option.IsSelected == requestedOptions.Contains(option)))
        {
            RefreshSecurityState();
            return;
        }

        if (TopLevel.GetTopLevel(this) is not { } xamlRoot)
        {
            RefreshSecurityState();
            return;
        }

        var wasStandaloneTotpCapable = IsStandaloneTotpCapable();
        await ApplySecuritySettingsUpdateAsync(
            xamlRoot,
            () =>
            {
                foreach (var option in FactorOptions)
                    option.SetSelected(requestedOptions.Contains(option));
            },
            SynchronizeSelectedFactorOptions);
        await AnnounceNewlyAllowedStandaloneTotpAsync(xamlRoot, wasStandaloneTotpCapable);
    }

    private void SubscribeSettings()
    {
        if (_isSettingsSubscribed)
            return;

        Settings.PropertyChanged += SettingsOnPropertyChanged;
        _isSettingsSubscribed = true;
    }

    private void SettingsOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_refreshing) return;
        SettingsStore.Save();
        RefreshSecurityState();
    }

    private void RefreshSecurityState()
    {
        _refreshing = true;
        try
        {
            var state = _securityService.GetUiState();
            CanEnableSecurity = state.HasPassword;
            IsSecurityEnabled = state.SecurityEnabled;
            HasPassword = state.HasPassword;
            HasTotp = state.HasTotp;
            CanConfigureAdditionalFactors = state.CanConfigureAdditionalFactors;
            CanEditFactorSelection = state.CanEditFactorSelection;
            CanEditProtectedOperations = state.CanEditProtectedOperations;
            TotpButtonText = state.HasTotp ? SR.C_ResetTotp : SR.C_SetTotp;
            IsLockedOut = state.LockoutRemaining is not null;
            LockoutText = state.LockoutRemaining is { } remaining
                ? string.Format(SR.M_LockoutFormat, Math.Ceiling(remaining.TotalSeconds))
                : string.Empty;
            SudoModeDurationValue = Settings.SudoModeDurationSeconds;
            // 配置可能被手工改写成未知枚举值，这里回退到默认项而不是让下拉框绑定抛异常
            SelectedIntegrityActionOption = IntegrityActionOptions
                .FirstOrDefault(option => option.Value == Settings.SettingsIntegrityAction) ?? IntegrityActionOptions[0];
            SelectedIntegrityRestoreSourceOption = IntegrityRestoreSourceOptions
                .FirstOrDefault(option => option.Value == Settings.SettingsIntegrityRestoreSource)
                ?? IntegrityRestoreSourceOptions[0];
            SelectedSignedPolicyModeOption = SignedPolicyModeOptions
                .FirstOrDefault(option => option.Value == Settings.ConfigIntegrityMode) ?? SignedPolicyModeOptions[0];
            RefreshSignedPolicyPresentation();
            RefreshFido2Keys();
            SynchronizeSelectedFactorOptions();
        }
        finally
        {
            _refreshing = false;
            foreach (var name in new[]
                     {
                          nameof(CanEnableSecurity), nameof(IsSecurityEnabled), nameof(HasPassword), nameof(CanSetPassword),
                          nameof(HasTotp),
                          nameof(CanConfigureAdditionalFactors), nameof(CanEditFactorSelection), nameof(CanEditProtectedOperations),
                          nameof(TotpButtonText), nameof(IsLockedOut), nameof(LockoutText),
                          nameof(SudoModeDurationValue), nameof(CanEditSudoModeDuration),
                          nameof(SelectedIntegrityActionOption), nameof(SelectedIntegrityRestoreSourceOption),
                          nameof(IsAutoRestoreSelected),
                          nameof(SelectedSignedPolicyModeOption), nameof(SignedPolicyFingerprint),
                          nameof(SignedPolicyStatus), nameof(HasFido2Keys), nameof(SelectedFido2Key), nameof(SelectedSigningKey)
                     })
                NotifyPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    private async void SecurityEnabled_OnIsCheckedChanged(object? sender, RoutedEventArgs e)
    {
        if (_refreshing || sender is not ToggleSwitch toggle || toggle.IsChecked is not { } requested ||
            requested == Settings.SecurityEnabled)
            return;

        if (TopLevel.GetTopLevel(this) is not { } xamlRoot)
        {
            RefreshSecurityState();
            return;
        }

        await ApplySecuritySettingsUpdateAsync(
            xamlRoot,
            () => Settings.SecurityEnabled = requested,
            () => toggle.IsChecked = Settings.SecurityEnabled);
    }

    private async void SecurityOption_OnIsCheckedChanged(object? sender, RoutedEventArgs e)
    {
        if (_refreshing || sender is not ToggleButton toggle || toggle.Tag is not string optionName ||
            toggle.IsChecked is not { } requested ||
            !TryGetSecurityOption(optionName, out var getValue, out var setValue))
            return;

        var current = getValue();
        if (requested == current)
            return;

        if (TopLevel.GetTopLevel(this) is not { } xamlRoot)
        {
            RefreshSecurityState();
            return;
        }

        var wasStandaloneTotpCapable = IsStandaloneTotpCapable();
        await ApplySecuritySettingsUpdateAsync(
            xamlRoot,
            () => setValue(requested),
            () => toggle.IsChecked = current);
        await AnnounceNewlyAllowedStandaloneTotpAsync(xamlRoot, wasStandaloneTotpCapable);
    }

    /// <summary>
    ///     篡改处理方式与恢复来源共用一套流程：先按旧值回退下拉框，再走安全密码授权后写入设置。
    /// </summary>
    private async void IntegrityAction_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_refreshing || sender is not ComboBox combo ||
            combo.SelectedItem is not SettingOption<SettingsIntegrityAction> option ||
            option.Value == Settings.SettingsIntegrityAction)
            return;

        if (TopLevel.GetTopLevel(this) is not { } xamlRoot)
        {
            RefreshSecurityState();
            return;
        }

        await ApplySecuritySettingsUpdateAsync(
            xamlRoot,
            () => Settings.SettingsIntegrityAction = option.Value,
            () => combo.SelectedItem = SelectedIntegrityActionOption);
    }

    private async void IntegrityRestoreSource_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_refreshing || sender is not ComboBox combo ||
            combo.SelectedItem is not SettingOption<SettingsIntegrityRestoreSource> option ||
            option.Value == Settings.SettingsIntegrityRestoreSource)
            return;

        if (TopLevel.GetTopLevel(this) is not { } xamlRoot)
        {
            RefreshSecurityState();
            return;
        }

        await ApplySecuritySettingsUpdateAsync(
            xamlRoot,
            () => Settings.SettingsIntegrityRestoreSource = option.Value,
            () => combo.SelectedItem = SelectedIntegrityRestoreSourceOption);
    }

    /// <summary>
    ///     分离签名模式与本地指纹共用同一条安全授权链路，避免绕过密码闸门直接落盘。
    /// </summary>
    private async void SignedPolicyMode_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_refreshing || sender is not ComboBox combo ||
            combo.SelectedItem is not SettingOption<ConfigIntegrityMode> option ||
            option.Value == Settings.ConfigIntegrityMode)
            return;

        if (TopLevel.GetTopLevel(this) is not { } xamlRoot)
        {
            RefreshSecurityState();
            return;
        }

        await ApplySecuritySettingsUpdateAsync(
            xamlRoot,
            () => Settings.ConfigIntegrityMode = option.Value,
            () => combo.SelectedItem = SelectedSignedPolicyModeOption);
    }

    /// <summary>
    ///     密钥对必须是随机的，绝不从密码派生：密码熵不足，派生密钥会让任何拿到公钥的人离线穷举密码，
    ///     而且改密码会让所有已部署的签名失效。私钥只经剪贴板交给用户带走，本机不落盘。
    /// </summary>
    private async void SignedPolicyGenerate_OnClick(object? sender, RoutedEventArgs e)
    {
        var (_, privateKey) = ConfigSignature.CreateKeyPair();
        if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)
        {
            // 拿不到剪贴板就宁可不生成：也不该造出一个用户无法带走的私钥
            this.ShowErrorToast(SR.M_SignedPolicy_ClipboardUnavailable);
            return;
        }

        await clipboard.SetTextAsync(privateKey);
        this.ShowSuccessToast(SR.M_SignedPolicy_KeyGenerated);
    }

    private async void SignedPolicySign_OnClick(object? sender, RoutedEventArgs e)
    {
        var privateKey = SignedPolicyPrivateKeyBox.Text;
        if (string.IsNullOrWhiteSpace(privateKey))
        {
            this.ShowWarningToast(SR.C_SignedPolicy_PrivateKey);
            return;
        }

        var signerLabel = SignedPolicySignerBox.Text;
        var authorized = await _securityService.AuthorizeAsync(
            SecurityOperation.ChangeSecuritySettings,
            () =>
            {
                SignPolicyDocument(privateKey, signerLabel);
                return Task.CompletedTask;
            });
        if (!authorized)
            return;

        SignedPolicyPrivateKeyBox.Text = string.Empty;
        RefreshSecurityState();
    }

    private void RefreshFido2Keys()
    {
        var previous = SelectedFido2Key?.Id;
        var previousSigning = SelectedSigningKey?.Id;
        Fido2Keys.Clear();
        foreach (var credential in _fido2Service?.ListCredentials() ?? [])
        {
            var algorithm = credential.Algorithm switch
            {
                -7 => "ES256",
                -257 => "RS256",
                _ => $"alg {credential.Algorithm}"
            };
            Fido2Keys.Add(new Fido2KeyOption(
                credential.Id,
                $"{credential.Label} · {algorithm} · {credential.CreatedAtUtc.ToLocalTime():yyyy-MM-dd}"));
        }

        _selectedFido2Key = Fido2Keys.FirstOrDefault(key => key.Id == previous) ?? Fido2Keys.FirstOrDefault();
        _selectedSigningKey = Fido2Keys.FirstOrDefault(key => key.Id == previousSigning) ?? Fido2Keys.FirstOrDefault();
        NotifyPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasFido2Keys)));
        NotifyPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedFido2Key)));
        NotifyPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedSigningKey)));
    }

    private async void Fido2Register_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_fido2Service is null)
            return;

        var label = string.Format(SR.M_Fido2DefaultLabel, Fido2Keys.Count + 1);
        Fido2RegistrationOutcome? outcome = null;
        var authorized = await _securityService.AuthorizeAsync(
            SecurityOperation.ChangeSecuritySettings,
            async () => outcome = await _fido2Service.RegisterAsync(label));
        if (!authorized || outcome is null)
            return;

        switch (outcome.Status)
        {
            case Fido2OperationStatus.Succeeded:
                this.ShowSuccessToast(SR.M_Fido2Registered);
                break;
            case Fido2OperationStatus.Cancelled:
                this.ShowWarningToast(SR.M_Fido2Cancelled);
                break;
            case Fido2OperationStatus.TimedOut:
                this.ShowWarningToast(SR.M_Fido2TimedOut);
                break;
            case Fido2OperationStatus.BrowserFailed:
                this.ShowErrorToast(SR.M_Fido2Unavailable);
                break;
            default:
                this.ShowErrorToast(string.Format(SR.M_Fido2RegisterFailed, outcome.Detail ?? outcome.Status.ToString()));
                break;
        }

        RefreshSecurityState();
    }

    private async void Fido2Remove_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_fido2Service is null || SelectedFido2Key is not { } selected)
        {
            this.ShowWarningToast(SR.C_Fido2_None);
            return;
        }

        var authorized = await _securityService.AuthorizeAsync(
            SecurityOperation.ChangeSecuritySettings,
            () =>
            {
                _fido2Service.Remove(selected.Id);
                if (_fido2Service.ListCredentials().Count == 0)
                {
                    // No key left: leaving the factor switched on would show a factor that can never pass.
                    Settings.Fido2Enabled = false;
                    SettingsStore.Save();
                }
                return Task.CompletedTask;
            });
        if (!authorized)
            return;

        this.ShowSuccessToast(SR.M_Fido2Removed);
        RefreshSecurityState();
    }

    /// <summary>
    ///     与私钥签发同一条授权链路，但本机不需要任何私钥：安全密钥直接对配置摘要签名，
    ///     所以在不受信任的机器上也能当地签发分离配置。
    /// </summary>
    private async void SignedPolicySignWithKey_OnClick(object? sender, RoutedEventArgs e)
    {
        if (SelectedSigningKey is not { } selected)
        {
            this.ShowWarningToast(SR.C_Fido2_None);
            return;
        }

        var signerLabel = SignedPolicySignerBox.Text;
        var authorized = await _securityService.AuthorizeAsync(
            SecurityOperation.ChangeSecuritySettings,
            async () =>
            {
                try
                {
                    var path = await _configPolicySignatureService.SignWithSecurityKeyAsync(selected.Id, signerLabel);
                    this.ShowSuccessToast(string.Format(SR.M_SignedPolicy_KeySigned, path));
                }
                catch (Exception exception) when (exception is InvalidOperationException or FormatException or CryptographicException)
                {
                    this.ShowErrorToast(string.Format(SR.M_SignedPolicy_KeySignFailed, exception.Message));
                }
            });
        if (!authorized)
            return;

        RefreshSecurityState();
    }

    private void SignPolicyDocument(string privateKey, string? signerLabel)
    {
        try
        {
            var path = _configPolicySignatureService.Sign(privateKey, signerLabel);
            this.ShowSuccessToast(string.Format(SR.M_SignedPolicy_Signed, path));
        }
        catch (Exception exception) when (exception is FormatException or CryptographicException or ArgumentException)
        {
            this.ShowErrorToast(string.Format(SR.M_SignedPolicy_SignFailed, exception.Message));
        }
    }

    /// <summary>
    ///     指纹是本机唯一的防替换锚点：本项不做持久化钉住，所以只在界面上显著展示，由用户与可信设备逐字核对。
    /// </summary>
    private void RefreshSignedPolicyPresentation()
    {
        var verification = _configPolicySignatureService.Verify();
        SignedPolicyFingerprint = verification.PublicKeyFingerprint ?? string.Empty;
        SignedPolicyStatus = verification.Status switch
        {
            ConfigPolicyVerificationStatus.Verified => string.Format(
                SR.M_SignedPolicy_Status_Verified,
                string.IsNullOrWhiteSpace(verification.SignerLabel) ? "-" : verification.SignerLabel,
                verification.SignedAtUtc?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "-"),
            ConfigPolicyVerificationStatus.DocumentMissing => SR.M_SignedPolicy_Status_DocumentMissing,
            ConfigPolicyVerificationStatus.DocumentMalformed => SR.M_SignedPolicy_Status_DocumentMalformed,
            ConfigPolicyVerificationStatus.SignatureInvalid => SR.M_SignedPolicy_Status_SignatureInvalid,
            _ => SR.M_SignedPolicy_Status_NotConfigured
        };
    }

    private async void SetPassword_OnClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not { } xamlRoot) return;
        var result = await SecuritySetupDialogs.ShowPasswordEditorAsync(xamlRoot, hasPassword: false);
        if (result is null) return;
        await SavePasswordAsync(result);
    }

    private async void ChangePassword_OnClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not { } xamlRoot) return;
        var result = await SecuritySetupDialogs.ShowPasswordEditorAsync(xamlRoot, hasPassword: true);
        if (result is null) return;
        await SavePasswordAsync(result);
    }

    private async void RemovePassword_OnClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not { } xamlRoot) return;
        var currentPassword = await SecuritySetupDialogs.ShowPasswordRemovalAsync(xamlRoot);
        if (currentPassword is null) return;

        _refreshing = true;
        var removed = await _securityService.RemovePasswordAsync(currentPassword);
        _refreshing = false;
        if (removed) this.ShowSuccessToast(SR.M_PasswordRemoved);
        else this.ShowErrorToast(SR.M_PasswordSaveFailed);
        RefreshSecurityState();
    }

    private async Task SavePasswordAsync(PasswordEditorResult result)
    {
        _refreshing = true;
        var saved = await _securityService.SetPasswordAsync(result.NewPassword, result.CurrentPassword);
        _refreshing = false;
        if (saved) this.ShowSuccessToast(SR.M_PasswordSaved);
        else this.ShowErrorToast(SR.M_PasswordSaveFailed);
        RefreshSecurityState();
    }

    private async void ManageTotp_OnClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not { } xamlRoot) return;
        var secret = await _securityService.BeginTotpSetupAsync(xamlRoot);
        if (secret is null)
        {
            if (!_securityService.GetUiState().HasPassword)
                this.ShowWarningToast(SR.M_SetPasswordFirst);
            return;
        }

        var code = await SecuritySetupDialogs.ShowTotpSetupAsync(xamlRoot, secret);
        var wasStandaloneTotpCapable = IsStandaloneTotpCapable();
        if (code is not null && await _securityService.ConfirmTotpAsync(secret, code))
        {
            this.ShowSuccessToast(SR.M_TotpSaved);
            // 在「任意已选验证方式」模式下新建 TOTP，等于这一刻起 TOTP 可以单独解锁
            await AnnounceNewlyAllowedStandaloneTotpAsync(xamlRoot, wasStandaloneTotpCapable);
        }
        else if (code is not null) this.ShowErrorToast(SR.M_TotpSaveFailed);
        else await _securityService.CancelTotpSetupAsync(secret);
        RefreshSecurityState();
    }

    private async void RemoveTotp_OnClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not { } xamlRoot) return;
        _refreshing = true;
        var removed = await _securityService.RemoveTotpAsync(xamlRoot);
        _refreshing = false;
        if (removed) this.ShowSuccessToast(SR.M_TotpRemoved);
        else this.ShowErrorToast(SR.M_TotpRemoveFailed);
        RefreshSecurityState();
    }

    private async void ManageUsb_OnClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not { } xamlRoot) return;
        var result = await SecuritySetupDialogs.ShowUsbBindingAsync(xamlRoot,
            await _securityService.GetUsbDevicesAsync());
        if (result is null) return;
        var success = result.UnbindId is not null
            ? await _securityService.UnbindUsbAsync(xamlRoot, result.UnbindId)
            : await _securityService.BindUsbAsync(xamlRoot, result.DeviceId!);
        if (success) this.ShowSuccessToast(SR.M_UsbUpdated);
        else this.ShowErrorToast(SR.M_UsbUpdateFailed);
        RefreshSecurityState();
    }

    private async void SudoModeDuration_OnValueChanged(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        if (_refreshing || e.NewValue is not { } newValue)
            return;

        var requested = (int)newValue;
        if (requested == Settings.SudoModeDurationSeconds)
            return;

        if (TopLevel.GetTopLevel(this) is not { } xamlRoot)
        {
            RefreshSecurityState();
            return;
        }

        // 放宽免验证时长属于放宽防护，和其余安全项一样走受验证保护的写入边界
        await ApplySecuritySettingsUpdateAsync(
            xamlRoot,
            () => Settings.SudoModeDurationSeconds = requested,
            () =>
            {
                SudoModeDurationValue = Settings.SudoModeDurationSeconds;
                NotifyPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SudoModeDurationValue)));
            });
    }

    private async Task ApplySecuritySettingsUpdateAsync(TopLevel xamlRoot, Action update, Action restoreView)
    {
        _refreshing = true;
        try
        {
            restoreView();
            await _securityService.UpdateSecuritySettingsAsync(xamlRoot, update);
        }
        finally
        {
            _refreshing = false;
            RefreshSecurityState();
        }
    }

    /// <summary>
    ///     「任意已选验证方式」模式下 TOTP 能单独解锁，靠的是与凭据文件同目录的可读种子副本。它只约束
    ///     界面：能读到本机文件的人可以直接算出验证码。因此这种组合一旦成立就明确说一次，而不是把代价
    ///     只写在设置项描述里；只在刚变成可用的那次提示，避免每开一次设置页都弹。
    /// </summary>
    private async Task AnnounceNewlyAllowedStandaloneTotpAsync(TopLevel xamlRoot, bool wasCapable)
    {
        if (wasCapable || !IsStandaloneTotpCapable())
            return;

        var dialog = new FATaskDialog
        {
            XamlRoot = xamlRoot,
            Title = SR.M_TotpStandaloneNoticeTitle,
            Header = SR.M_TotpStandaloneNoticeTitle,
            Content = new TextBlock
            {
                Text = SR.M_TotpStandaloneNotice,
                TextWrapping = TextWrapping.Wrap
            }
        };
        dialog.Buttons.Add(new FATaskDialogButton(SR.C_Acknowledge, "acknowledge") { IsDefault = true });
        await dialog.ShowAsync();
    }

    /// <summary>模式、因素与已配置的种子三者同时成立时，TOTP 才能脱离主密码单独解锁。</summary>
    private bool IsStandaloneTotpCapable() =>
        !Settings.RequireAllSelectedFactors
        && Settings.TotpEnabled
        && _securityService.GetUiState().HasTotp;

    private void SynchronizeSelectedFactorOptions()
    {
        var selectedOptions = FactorOptions.Where(option => option.IsSelected).ToArray();
        if (SelectedFactorOptions.SequenceEqual(selectedOptions))
            return;

        SelectedFactorOptions.Clear();
        foreach (var option in selectedOptions)
            SelectedFactorOptions.Add(option);
    }

    private bool TryGetSecurityOption(string optionName, out Func<bool> getValue, out Action<bool> setValue)
    {
        switch (optionName)
        {
            case nameof(SecuritySettingsConfig.SudoModeEnabled):
                getValue = () => Settings.SudoModeEnabled;
                setValue = value => Settings.SudoModeEnabled = value;
                return true;
            case nameof(SecuritySettingsConfig.RequireAllSelectedFactors):
                getValue = () => Settings.RequireAllSelectedFactors;
                setValue = value => Settings.RequireAllSelectedFactors = value;
                return true;
            case nameof(SecuritySettingsConfig.AllowSettingsPreview):
                getValue = () => Settings.AllowSettingsPreview;
                setValue = value => Settings.AllowSettingsPreview = value;
                return true;
            case nameof(SecuritySettingsConfig.SettingsIntegrityCheckEnabled):
                getValue = () => Settings.SettingsIntegrityCheckEnabled;
                setValue = value => Settings.SettingsIntegrityCheckEnabled = value;
                return true;
            case nameof(SecuritySettingsConfig.ProtectOpenSettings):
                getValue = () => Settings.ProtectOpenSettings;
                setValue = value => Settings.ProtectOpenSettings = value;
                return true;
            case nameof(SecuritySettingsConfig.ProtectToggleMainWindow):
                getValue = () => Settings.ProtectToggleMainWindow;
                setValue = value => Settings.ProtectToggleMainWindow = value;
                return true;
            case nameof(SecuritySettingsConfig.ProtectToggleFloatingWindow):
                getValue = () => Settings.ProtectToggleFloatingWindow;
                setValue = value => Settings.ProtectToggleFloatingWindow = value;
                return true;
            case nameof(SecuritySettingsConfig.ProtectRestart):
                getValue = () => Settings.ProtectRestart;
                setValue = value => Settings.ProtectRestart = value;
                return true;
            case nameof(SecuritySettingsConfig.ProtectExit):
                getValue = () => Settings.ProtectExit;
                setValue = value => Settings.ProtectExit = value;
                return true;
            case nameof(SecuritySettingsConfig.ProtectRawRandomSettings):
                getValue = () => Settings.ProtectRawRandomSettings;
                setValue = value => Settings.ProtectRawRandomSettings = value;
                return true;
            case nameof(SecuritySettingsConfig.ProtectRollCallStart):
                getValue = () => Settings.ProtectRollCallStart;
                setValue = value => Settings.ProtectRollCallStart = value;
                return true;
            case nameof(SecuritySettingsConfig.ProtectRollCallReset):
                getValue = () => Settings.ProtectRollCallReset;
                setValue = value => Settings.ProtectRollCallReset = value;
                return true;
            case nameof(SecuritySettingsConfig.ProtectQuickDrawStart):
                getValue = () => Settings.ProtectQuickDrawStart;
                setValue = value => Settings.ProtectQuickDrawStart = value;
                return true;
            case nameof(SecuritySettingsConfig.ProtectQuickDrawReset):
                getValue = () => Settings.ProtectQuickDrawReset;
                setValue = value => Settings.ProtectQuickDrawReset = value;
                return true;
            case nameof(SecuritySettingsConfig.ProtectLotteryStart):
                getValue = () => Settings.ProtectLotteryStart;
                setValue = value => Settings.ProtectLotteryStart = value;
                return true;
            case nameof(SecuritySettingsConfig.ProtectLotteryReset):
                getValue = () => Settings.ProtectLotteryReset;
                setValue = value => Settings.ProtectLotteryReset = value;
                return true;
            default:
                getValue = null!;
                setValue = null!;
                return false;
        }
    }
}

/// <summary>One row in the security page's enrolled-key list and signing picker.</summary>
public sealed class Fido2KeyOption(string id, string display)
{
    public string Id { get; } = id;
    public string Display { get; } = display;
    public override string ToString() => Display;
}