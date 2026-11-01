using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Services.Config;
using SecRandom.Core.Services.Security;
using SecRandom.Shared;

namespace SecRandom.Services.Security;

/// <summary>Outcome of verifying the deployed signed policy.</summary>
public enum ConfigPolicyVerificationStatus
{
    /// <summary>The installation is not using signed policy mode.</summary>
    NotConfigured,

    /// <summary>The deployed signature matches the policy this machine is running.</summary>
    Verified,

    /// <summary>Signed policy mode is on, but no signature document was deployed.</summary>
    DocumentMissing,

    /// <summary>The document is unreadable or claims a different scope/algorithm/version.</summary>
    DocumentMalformed,

    /// <summary>The signature does not match this machine's policy, so the policy was changed after signing.</summary>
    SignatureInvalid
}

public sealed record ConfigPolicyVerification(
    ConfigPolicyVerificationStatus Status,
    string? SignerLabel,
    DateTimeOffset? SignedAtUtc,
    string? PublicKeyFingerprint)
{
    public bool IsVerified => Status == ConfigPolicyVerificationStatus.Verified;
    public bool IsConfigured => Status != ConfigPolicyVerificationStatus.NotConfigured;
}

/// <summary>
///     Reads and writes the detached policy signature.
///     The document is deliberately public: it carries the public key and the signature only, so it is safe
///     to copy from the trusted device to the draw machine and safe to include in backups. The private key
///     never reaches this class — signing takes it as a parameter, which is what lets the draw machine
///     verify a policy it could not have authored.
/// </summary>
internal sealed class ConfigPolicySignatureService(
    MainConfigHandler configHandler,
    SecuritySettingsStore settingsStore,
    SecRandom.Core.Abstraction.Services.IProfileCatalogManager profileCatalogManager,
    ILogger<ConfigPolicySignatureService> logger)
{
    private EventHandler? _policyChanged;

    /// <summary>
    ///     已部署的策略可能变了：安全设置的保存（完整性模式就住在那里）或一份新的签名文档落盘。
    ///     抽签界面比"操作员去签发"早得多就建好了，所以它订这个事件而不是轮询策略文件。
    ///     存储的事件只在第一个订阅者到来时挂上，最后一个订阅者走掉后摘掉。
    /// </summary>
    public event EventHandler? PolicyChanged
    {
        add
        {
            if (_policyChanged is null)
                settingsStore.Saved += OnSecuritySettingsSaved;
            _policyChanged += value;
        }
        remove
        {
            _policyChanged -= value;
            if (_policyChanged is null)
                settingsStore.Saved -= OnSecuritySettingsSaved;
        }
    }

    private void OnSecuritySettingsSaved(object? sender, EventArgs e) => _policyChanged?.Invoke(this, EventArgs.Empty);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string DocumentFileName => "config-policy.json";

    private static string DocumentPath => Utils.GetFilePath("config", DocumentFileName);

    /// <summary>
    ///     名单覆盖是可选项（<c>ConfigIntegrityIncludeRosters</c>），默认关闭：覆盖名单意味着任何一次
    ///     增删改名都会让签名失效，那是必须显式选择的代价。覆盖的是身份映射（学生的学号/姓名/分组/性别/
    ///     停用，奖品的编号与名称），不含奖品权重与库存——它们是主持人日常会调整的抽取输入，理由见
    ///     <see cref="RosterPolicyDigest" />。没有任何名单时 <c>Combine</c> 仍返回真实摘要，所以
    ///     「开着但还没有名单」与「关着」不会长得一样。
    /// </summary>
    private string? ComputeRosterDigest()
    {
        if (!settingsStore.Data.ConfigIntegrityIncludeRosters)
            return null;

        var digests = new System.Collections.Generic.List<string>();
        foreach (var name in profileCatalogManager.GetStudentListNames())
        {
            if (profileCatalogManager.LoadStudentList(name) is { } list)
                digests.Add(RosterPolicyDigest.ComputeList(list));
        }

        foreach (var name in profileCatalogManager.GetPrizeListNames())
        {
            if (profileCatalogManager.LoadPrizeList(name) is { } list)
                digests.Add(RosterPolicyDigest.ComputeList(list));
        }

        return RosterPolicyDigest.Combine(digests);
    }

    public ConfigPolicyVerification Verify()
    {
        if (settingsStore.Data.ConfigIntegrityMode != ConfigIntegrityMode.SignedPolicy)
            return new ConfigPolicyVerification(ConfigPolicyVerificationStatus.NotConfigured, null, null, null);

        var path = DocumentPath;
        if (!File.Exists(path))
        {
            logger.LogWarning("Signed policy mode is on but no signature document was deployed.");
            return new ConfigPolicyVerification(ConfigPolicyVerificationStatus.DocumentMissing, null, null, null);
        }

        ConfigSignatureDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<ConfigSignatureDocument>(File.ReadAllText(path), JsonOptions);
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            logger.LogWarning(exception, "The policy signature document could not be read.");
            return new ConfigPolicyVerification(ConfigPolicyVerificationStatus.DocumentMalformed, null, null, null);
        }

        if (document is null || !ConfigSignature.IsDocumentWellFormed(document)
            || !string.Equals(document.Scope, ConfigPolicyDigest.ScopeId, StringComparison.Ordinal))
        {
            logger.LogWarning("The policy signature document is not usable for this build.");
            return new ConfigPolicyVerification(ConfigPolicyVerificationStatus.DocumentMalformed, null, null, null);
        }

        var fingerprint = ConfigSignature.Fingerprint(document.PublicKey);
        var digest = ConfigPolicyDigest.Compute(configHandler.Data, settingsStore.Data, ComputeRosterDigest());
        if (!ConfigSignature.Verify(digest, document.Signature, document.PublicKey))
        {
            logger.LogWarning(
                "The signed policy does not match this machine's settings. Signer={Signer}, Fingerprint={Fingerprint}",
                document.SignerLabel, fingerprint);
            return new ConfigPolicyVerification(
                ConfigPolicyVerificationStatus.SignatureInvalid, document.SignerLabel, document.SignedAtUtc, fingerprint);
        }

        return new ConfigPolicyVerification(
            ConfigPolicyVerificationStatus.Verified, document.SignerLabel, document.SignedAtUtc, fingerprint);
    }

    /// <summary>
    ///     True while a deployed signed policy is in force. Its covered settings must then be locked in the UI:
    ///     any edit is exactly what invalidates this signature, so the operator is not allowed to make one by
    ///     accident. The integrity fields themselves stay editable because they are deliberately outside the
    ///     covered set — switching the mode off, changing the policy, re-signing, and switching it back on is
    ///     the intended way to change a covered setting.
    /// </summary>
    public bool IsPolicyLockEnforced => Verify().IsVerified;

    /// <summary>
    ///     Signs the policy this machine is currently running and writes the document atomically, so a failed
    ///     write can never leave a half-written signature on disk. Intended to run on the trusted device.
    /// </summary>
    public string Sign(string privateKeyPkcs8Base64, string? signerLabel)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(privateKeyPkcs8Base64);

        var publicKey = ConfigSignature.PublicKeyFromPrivateKey(privateKeyPkcs8Base64);
        var digest = ConfigPolicyDigest.Compute(configHandler.Data, settingsStore.Data, ComputeRosterDigest());
        var document = new ConfigSignatureDocument
        {
            Scope = ConfigPolicyDigest.ScopeId,
            PublicKey = publicKey,
            Digest = Convert.ToBase64String(digest),
            Signature = ConfigSignature.Sign(digest, privateKeyPkcs8Base64),
            SignedAtUtc = DateTimeOffset.UtcNow,
            SignerLabel = string.IsNullOrWhiteSpace(signerLabel) ? null : signerLabel.Trim()
        };

        var path = DocumentPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = path + $".{Guid.NewGuid():N}.tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(document, JsonOptions));
        File.Move(temporaryPath, path, true);

        logger.LogInformation(
            "已签发配置策略。签名者={Signer}，指纹={Fingerprint}，范围={Scope}",
            document.SignerLabel, ConfigSignature.Fingerprint(publicKey), document.Scope);
        // 签发会改变指纹、签名者和签发时间，所以与设置保存走同一条通知。
        _policyChanged?.Invoke(this, EventArgs.Empty);

        return path;
    }

    public ConfigSignatureDocument? ReadDocument()
    {
        try
        {
            var path = DocumentPath;
            return File.Exists(path)
                ? JsonSerializer.Deserialize<ConfigSignatureDocument>(File.ReadAllText(path), JsonOptions)
                : null;
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            logger.LogDebug(exception, "无法读取策略签名文档。");
            return null;
        }
    }
}
