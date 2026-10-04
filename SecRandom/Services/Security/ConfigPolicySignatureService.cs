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
public sealed class ConfigPolicySignatureService(
    MainConfigHandler configHandler,
    ILogger<ConfigPolicySignatureService> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string DocumentFileName => "config-policy.json";

    private static string DocumentPath => Utils.GetFilePath("config", DocumentFileName);

    public ConfigPolicyVerification Verify()
    {
        if (configHandler.Data.SecuritySettings.ConfigIntegrityMode != ConfigIntegrityMode.SignedPolicy)
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
        var digest = ConfigPolicyDigest.Compute(configHandler.Data);
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
    ///     Signs the policy this machine is currently running and writes the document atomically, so a failed
    ///     write can never leave a half-written signature on disk. Intended to run on the trusted device.
    /// </summary>
    public string Sign(string privateKeyPkcs8Base64, string? signerLabel)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(privateKeyPkcs8Base64);

        var publicKey = ConfigSignature.PublicKeyFromPrivateKey(privateKeyPkcs8Base64);
        var digest = ConfigPolicyDigest.Compute(configHandler.Data);
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
