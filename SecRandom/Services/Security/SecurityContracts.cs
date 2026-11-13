using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using SecRandom.Core.Enums.Configs;

namespace SecRandom.Services.Security;

public enum SecurityVerificationFailure
{
    None,
    NotRequired,
    NotConfigured,
    LockedOut,
    InvalidCredentials,
    FactorUnavailable,
    Cancelled,
    PreviewRequested
}

/// <summary>
/// 安全验证对话框的输入要求。<see cref="TotpStandaloneReady"/> 由服务层在
/// 「任意已选验证方式」模式下按免密 TOTP 副本是否存在填写：为 false 时对话框
/// 会提示用户先用主密码验证一次，而不是笼统报验证失败。
/// </summary>
public sealed record SecurityVerificationRequest(
    IReadOnlyList<SecurityFactor> RequiredFactors,
    bool RequireAllSelectedFactors,
    TimeSpan? LockoutRemaining,
    bool AllowPreview = false,
    bool TotpStandaloneReady = true,
    string? Fido2Challenge = null);

/// <summary>
///     一次安全密钥断言的原始结果，由对话框在浏览器里完成登记/断言后带回。
///     <see cref="SecurityService" /> 只验证它，不再触发第二次浏览器操作——验证必须可重入、可测试，
///     而且同一个 challenge 只能被使用一次。
/// </summary>
public sealed record Fido2AssertionProof(
    string CredentialId,
    string ClientDataJson,
    string AuthenticatorData,
    string Signature,
    string Origin);

public sealed record SecurityVerificationResponse(
    string Password,
    string TotpCode,
    bool UsbPresent,
    bool Cancelled = false,
    bool PreviewRequested = false,
    Fido2AssertionProof? Fido2 = null);

public sealed record SecurityVerificationResult(
    bool IsAuthorized,
    SecurityVerificationFailure Failure,
    TimeSpan? LockoutRemaining = null)
{
    public static SecurityVerificationResult Allowed { get; } = new(true, SecurityVerificationFailure.None);
}

public sealed record SecurityAuthorizationResult(bool IsAuthorized, bool PreviewOpened = false);

public sealed record SecuritySettingsUiState(
    bool HasPassword,
    bool HasTotp,
    bool HasUsbBinding,
    bool SecurityEnabled,
    bool CanConfigureAdditionalFactors,
    bool CanEditFactorSelection,
    bool CanEditProtectedOperations,
    TimeSpan? LockoutRemaining,
    bool HasFido2Key = false);

public enum SecurityFactor
{
    Password,
    Totp,
    Usb,
    Fido2
}

public interface ISecurityVerificationPrompt
{
    Task<SecurityVerificationResult> RequestAsync(
        TopLevel xamlRoot,
        SecurityVerificationRequest request,
        Func<SecurityVerificationResponse, CancellationToken, Task<SecurityVerificationResult>> verify,
        CancellationToken cancellationToken = default);
}

public interface ISecurityService
{
    SecuritySettingsUiState GetUiState();
    bool RequiresVerification(SecurityOperation operation);
    Task<SecurityVerificationResult> VerifyAsync(SecurityVerificationResponse response, CancellationToken cancellationToken = default);
    Task<bool> AuthorizeAsync(SecurityOperation operation, Func<Task> action, CancellationToken cancellationToken = default);
    Task<bool> AuthorizeAsync(IReadOnlyCollection<SecurityOperation> operations, Func<Task> action, CancellationToken cancellationToken = default);
    Task<bool> AuthorizePasswordAsync(TopLevel xamlRoot, Func<Task> action, CancellationToken cancellationToken = default);
    Task<SecurityAuthorizationResult> AuthorizeSettingsAsync(
        Func<Task> action,
        Func<Task> previewAction,
        CancellationToken cancellationToken = default);
    Task<bool> UpdateSecuritySettingsAsync(TopLevel xamlRoot, Action update, CancellationToken cancellationToken = default);
    Task<bool> SetPasswordAsync(string password, string? currentPassword = null, CancellationToken cancellationToken = default);
    Task<bool> RemovePasswordAsync(string currentPassword, CancellationToken cancellationToken = default);
    Task<string?> BeginTotpSetupAsync(CancellationToken cancellationToken = default);
    Task<string?> BeginTotpSetupAsync(TopLevel xamlRoot, CancellationToken cancellationToken = default);
    Task CancelTotpSetupAsync(string secret, CancellationToken cancellationToken = default);
    Task<bool> ConfirmTotpAsync(string secret, string code, CancellationToken cancellationToken = default);

    /// <summary>
    ///     移除已配置的 TOTP。种子从凭据信封中清除、免密副本一并删除、该验证方式关闭，
    ///     并且和其余凭据操作一样始终要求重新输入一次安全密码（不接受任何 Sudo 状态）。
    /// </summary>
    Task<bool> RemoveTotpAsync(TopLevel xamlRoot, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<UsbBindingInfo>> GetUsbBindingsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<UsbDeviceInfo>> GetUsbDevicesAsync(CancellationToken cancellationToken = default);
    Task<bool> BindUsbAsync(string deviceId, CancellationToken cancellationToken = default);
    Task<bool> BindUsbAsync(TopLevel xamlRoot, string deviceId, CancellationToken cancellationToken = default);
    Task<bool> UnbindUsbAsync(string bindingId, CancellationToken cancellationToken = default);
    Task<bool> UnbindUsbAsync(TopLevel xamlRoot, string bindingId, CancellationToken cancellationToken = default);
    bool TryUpdateSettings(Action update);

    /// <summary>
    ///     导入或恢复一份备份前的授权检查。安全设置已经不随归档走（它们住在加密的
    ///     <c>data/config/security/settings.json</c>，任何归档都够不到），所以这里不再比较候选配置，
    ///     而是把"导入"本身当作受保护操作：当前有保护且有可验证凭据时要求重新输入一次密码，
    ///     任何 Sudo 状态都不生效；没有保护时直接放行。
    /// </summary>
    Task<bool> AuthorizeArchiveImportAsync(CancellationToken cancellationToken = default);
    bool IsSudoModeActive();
    bool IsGlobalSudoModeActive();
    void DeactivateGlobalSudoMode();
    void DeactivateSudoMode();
    void DeactivateSettingsSudoMode();
    event Action? SudoModeChanged;
}

public sealed record UsbBindingInfo(string Id, string DisplayName, bool IsPresent);

public sealed record UsbDeviceInfo(
    string DriveLetter,
    string DisplayName,
    string DeviceId,
    bool IsBound,
    string? BindingId,
    bool IsPresent)
{
    public string? HardwareName { get; init; }
}
