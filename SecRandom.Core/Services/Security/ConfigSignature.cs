using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Services.Security.Fido2;

namespace SecRandom.Core.Services.Security;

/// <summary>
///     The detached signature deployed next to the configuration. It is deliberately a separate document:
///     the configuration file keeps its own format, and a missing or stale document is a verification
///     failure rather than a parse error.
/// </summary>
public sealed record ConfigSignatureDocument
{
    public const string AlgorithmId = "ecdsa-p256-sha256";

    /// <summary>
    ///     Signing key held by a FIDO2 authenticator instead of a local ECDSA private key. The document carries
    ///     the whole WebAuthn assertion (client data, authenticator data, signature) plus the credential's COSE
    ///     public key, so any machine can verify it later without the key and without a private key file.
    /// </summary>
    public const string WebAuthnAlgorithmId = "webauthn-es256-v1";

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

    // WebAuthn fields. Only meaningful for WebAuthnAlgorithmId; they stay null for a local ECDSA signature.
    [JsonPropertyName("credentialId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CredentialId { get; init; }

    [JsonPropertyName("clientDataJson")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ClientDataJson { get; init; }

    [JsonPropertyName("authenticatorData")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AuthenticatorData { get; init; }

    [JsonPropertyName("origin")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Origin { get; init; }

    [JsonPropertyName("signCount")]
    public uint SignCount { get; init; }

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

    /// <summary>
    ///     The deterministic challenge a security-key signature must cover. Domain separated so an assertion
    ///     produced for a configuration digest can never be replayed as a verification prompt, and vice versa.
    /// </summary>
    public static string CreateConfigChallenge(ReadOnlySpan<byte> digest)
    {
        var prefix = Encoding.UTF8.GetBytes("VeriRandom/ConfigSignature/webauthn-v1\0");
        var buffer = new byte[prefix.Length + digest.Length];
        Buffer.BlockCopy(prefix, 0, buffer, 0, prefix.Length);
        digest.CopyTo(buffer.AsSpan(prefix.Length));
        try
        {
            return Base64Url.Encode(SHA256.HashData(buffer));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(buffer);
        }
    }

    /// <summary>
    ///     Verifies a document produced by <c>webauthn-es256-v1</c>. The signed challenge binds the configuration
    ///     digest, so a signature from one configuration cannot be moved to another; the origin must still be a
    ///     loopback page, which is the only place this app ever runs a ceremony.
    /// </summary>
    public static bool VerifyWebAuthn(ConfigSignatureDocument document, ReadOnlySpan<byte> digest)
    {
        if (document.Algorithm != ConfigSignatureDocument.WebAuthnAlgorithmId
            || !WebAuthnOriginPolicy.IsLoopback(document.Origin)
            || string.IsNullOrWhiteSpace(document.ClientDataJson)
            || string.IsNullOrWhiteSpace(document.AuthenticatorData)
            || string.IsNullOrWhiteSpace(document.Signature)
            || string.IsNullOrWhiteSpace(document.PublicKey))
            return false;

        if (!Base64Url.TryDecode(document.ClientDataJson, out var clientDataJson)
            || !Base64Url.TryDecode(document.AuthenticatorData, out var authenticatorData)
            || !Base64Url.TryDecode(document.Signature, out var signature)
            || !Base64Url.TryDecode(document.PublicKey, out var publicKeyCose))
            return false;

        // storedSignCount: 0 on purpose — this document is the only assertion we hold, so there is no previous
        // counter to compare against, and the one-time challenge is what proves freshness here.
        var verification = WebAuthnVerifier.VerifyAssertion(
            clientDataJson,
            authenticatorData,
            signature,
            publicKeyCose,
            0,
            CreateConfigChallenge(digest),
            document.Origin!,
            Fido2BrowserTransportRelyingPartyId);
        return verification.Success;
    }

    /// <summary>
    ///     The relying-party id the app's browser ceremony runs under. Kept as a constant here so a verifier can
    ///     check a document without depending on the app layer.
    /// </summary>
    public const string Fido2BrowserTransportRelyingPartyId = "localhost";

    /// <summary>Validates a deployed document without deciding anything about the covered settings.</summary>
    public static bool IsDocumentWellFormed(ConfigSignatureDocument document) =>
        document.FormatVersion == ConfigSignatureDocument.CurrentFormatVersion
        && !string.IsNullOrWhiteSpace(document.Scope)
        && !string.IsNullOrWhiteSpace(document.PublicKey)
        && !string.IsNullOrWhiteSpace(document.Digest)
        && !string.IsNullOrWhiteSpace(document.Signature)
        && (string.Equals(document.Algorithm, ConfigSignatureDocument.AlgorithmId, StringComparison.Ordinal)
            || (string.Equals(document.Algorithm, ConfigSignatureDocument.WebAuthnAlgorithmId, StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(document.CredentialId)
                && !string.IsNullOrWhiteSpace(document.ClientDataJson)
                && !string.IsNullOrWhiteSpace(document.AuthenticatorData)
                && !string.IsNullOrWhiteSpace(document.Origin)));
}
