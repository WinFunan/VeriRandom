using System;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using SecRandom.Core.Enums.Configs;

namespace SecRandom.Core.Services.Security;

/// <summary>
///     The detached signature deployed next to the configuration. It is deliberately a separate document:
///     the configuration file keeps its own format, and a missing or stale document is a verification
///     failure rather than a parse error.
/// </summary>
public sealed record ConfigSignatureDocument
{
    public const string AlgorithmId = "ecdsa-p256-sha256";

    /// <summary>Bumped when the canonical projection changes, so an old signature is refused instead of
    /// silently verifying a different set of fields.</summary>
    [JsonPropertyName("formatVersion")]
    public int FormatVersion { get; init; } = CurrentFormatVersion;

    [JsonPropertyName("algorithm")]
    public string Algorithm { get; init; } = AlgorithmId;

    /// <summary>Declared scope of the digest. Changing which settings are covered changes this id.</summary>
    [JsonPropertyName("scope")]
    public string Scope { get; init; } = string.Empty;

    /// <summary>SubjectPublicKeyInfo of the signing key, Base64.</summary>
    [JsonPropertyName("publicKey")]
    public string PublicKey { get; init; } = string.Empty;

    /// <summary>SHA-256 over the canonical projection of the covered settings, Base64.</summary>
    [JsonPropertyName("digest")]
    public string Digest { get; init; } = string.Empty;

    /// <summary>ECDSA P-256 signature over <see cref="Digest" />, DER, Base64.</summary>
    [JsonPropertyName("signature")]
    public string Signature { get; init; } = string.Empty;

    [JsonPropertyName("signedAtUtc")]
    public DateTimeOffset SignedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Free-form host label, so an operator can tell which trusted device signed this.</summary>
    [JsonPropertyName("signerLabel")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SignerLabel { get; init; }

    public const int CurrentFormatVersion = 1;
}

/// <summary>
///     Detached-signature primitives for the configuration file.
///     The key pair is always generated randomly and never derived from a user password: a password is
///     low-entropy, so a deterministic key pair would let anyone holding a public key plus one signature
///     verify password guesses offline, and changing the password would invalidate every deployed
///     signature. A password may only protect the private key at rest.
/// </summary>
public static class ConfigSignature
{
    public static readonly HashAlgorithmName HashAlgorithm = HashAlgorithmName.SHA256;

    /// <summary>Creates a fresh P-256 key pair. The private key is PKCS#8, which is what a trusted device stores.</summary>
    public static (string PublicKey, string PrivateKey) CreateKeyPair()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return (Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()),
            Convert.ToBase64String(key.ExportPkcs8PrivateKey()));
    }

    /// <summary>SHA-256 of the canonical projection. The projection itself belongs to the caller.</summary>
    public static byte[] ComputeDigest(ReadOnlySpan<byte> canonicalPayload) => SHA256.HashData(canonicalPayload);

    /// <summary>
    ///     Derives the matching public key from a PKCS#8 private key, so an operator who already holds a
    ///     private key can publish its public half without regenerating the pair.
    /// </summary>
    public static string PublicKeyFromPrivateKey(string privateKeyPkcs8Base64)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(privateKeyPkcs8Base64);
        using var key = ECDsa.Create();
        key.ImportPkcs8PrivateKey(Convert.FromBase64String(privateKeyPkcs8Base64), out _);
        return Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());
    }

    /// <summary>Signs a precomputed digest. The digest is signed as-is, never hashed again.</summary>
    public static string Sign(ReadOnlySpan<byte> digest, string privateKeyPkcs8Base64)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(privateKeyPkcs8Base64);
        using var key = ECDsa.Create();
        key.ImportPkcs8PrivateKey(Convert.FromBase64String(privateKeyPkcs8Base64), out _);
        return Convert.ToBase64String(key.SignHash(digest.ToArray(), DSASignatureFormat.Rfc3279DerSequence));
    }

    public static bool Verify(ReadOnlySpan<byte> digest, string signatureBase64, string publicKeyBase64)
    {
        if (string.IsNullOrWhiteSpace(signatureBase64) || string.IsNullOrWhiteSpace(publicKeyBase64))
            return false;

        try
        {
            using var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKeyBase64), out _);
            return key.VerifyHash(digest, Convert.FromBase64String(signatureBase64),
                DSASignatureFormat.Rfc3279DerSequence);
        }
        catch (Exception exception) when (exception is FormatException or CryptographicException)
        {
            return false;
        }
    }

    /// <summary>
    ///     Short, human-comparable fingerprint of a public key, for the one-time out-of-band check that the
    ///     key on the draw machine is really the host's. Without that check an attacker could simply replace
    ///     the public key together with the signature.
    /// </summary>
    public static string Fingerprint(string publicKeyBase64)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publicKeyBase64);
        var hash = SHA256.HashData(Convert.FromBase64String(publicKeyBase64));
        return Convert.ToHexString(hash)[..32];
    }

    /// <summary>Validates a deployed document without deciding anything about the covered settings.</summary>
    public static bool IsDocumentWellFormed(ConfigSignatureDocument document) =>
        document.FormatVersion == ConfigSignatureDocument.CurrentFormatVersion
        && string.Equals(document.Algorithm, ConfigSignatureDocument.AlgorithmId, StringComparison.Ordinal)
        && !string.IsNullOrWhiteSpace(document.Scope)
        && !string.IsNullOrWhiteSpace(document.PublicKey)
        && !string.IsNullOrWhiteSpace(document.Digest)
        && !string.IsNullOrWhiteSpace(document.Signature);
}
