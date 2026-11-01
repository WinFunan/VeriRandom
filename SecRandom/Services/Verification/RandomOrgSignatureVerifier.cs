using System;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Avalonia.Platform;
using Microsoft.Extensions.Logging;

namespace SecRandom.Services.Verification;

/// <summary>Outcome of checking a random.org signature. False is "could not vouch", never "is forged".</summary>
/// <param name="IsValid">The RSA signature verified against the pinned public key.</param>
/// <param name="CertificateExpired">The published verification certificate is past its NotAfter.</param>
/// <param name="CertificateNotAfter">That NotAfter, for the user-facing note.</param>
public sealed record RandomOrgSignatureCheck(
    bool IsValid,
    bool CertificateExpired,
    DateTimeOffset? CertificateNotAfter);

/// <summary>
///     Verifies random.org's signature over a signed response, offline.
/// </summary>
/// <remarks>
///     <para>
///         Per random.org's Signed API documentation, the signature is an RSA signature over the SHA-512 digest of the
///         JSON encoding of the <c>random</c> object, verifiable with the public key published at
///         <c>https://api.random.org/server.crt</c>. The caller must pass the <em>received</em> JSON text of that
///         object: re-serializing it can reorder members or respace them, and the signature covers bytes.
///     </para>
///     <para>
///         The certificate ships as a loose asset on desktop, which an attacker with local write access could replace,
///         so its SHA-256 fingerprint is pinned here as well. A mismatch means "this build cannot vouch for that key",
///         which is reported as unverified rather than as forgery — the pin exists to stop a swapped file from
///         silently vouching for a swapped key, not to judge the data.
///     </para>
///     <para>
///         The published certificate expired on 2025-11-15 and random.org has not replaced it. RSA verification does
///         not care, but the chain claim does, so expiry is reported separately instead of being folded into
///         <see cref="RandomOrgSignatureCheck.IsValid" />.
///     </para>
/// </remarks>
public sealed class RandomOrgSignatureVerifier(ILogger<RandomOrgSignatureVerifier> logger)
{
    private const string CertificateUri = "avares://SecRandom/Assets/Verification/randomorg-server.crt";

    /// <summary>SHA-256 of the certificate this build trusts. Refresh both together, never one of them alone.</summary>
    private const string PinnedCertificateSha256 = "ovIWQvHn8gUpUyZfRtw9qoJhPRWsMo7OpGhu7bcgM1E=";

    public RandomOrgSignatureCheck Verify(string rawRandomJson, string signatureBase64)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawRandomJson);
        ArgumentException.ThrowIfNullOrWhiteSpace(signatureBase64);

        var certificate = TryLoadPinnedCertificate(out var expired, out var notAfter);
        if (certificate is null)
            return new RandomOrgSignatureCheck(false, expired, notAfter);

        using (certificate)
        {
            try
            {
                using var rsa = certificate.GetRSAPublicKey();
                if (rsa is null)
                {
                    logger.LogWarning("random.org 验签证书里没有 RSA 公钥，本次按未验证处理。");
                    return new RandomOrgSignatureCheck(false, expired, notAfter);
                }

                var signature = Convert.FromBase64String(signatureBase64);
                var isValid = rsa.VerifyData(
                    System.Text.Encoding.UTF8.GetBytes(rawRandomJson),
                    signature,
                    HashAlgorithmName.SHA512,
                    RSASignaturePadding.Pkcs1);

                return new RandomOrgSignatureCheck(isValid, expired, notAfter);
            }
            catch (Exception exception) when (exception is CryptographicException or FormatException)
            {
                logger.LogWarning(exception, "random.org 签名校验失败（按未验证处理）。");
                return new RandomOrgSignatureCheck(false, expired, notAfter);
            }
        }
    }

    private X509Certificate2? TryLoadPinnedCertificate(out bool expired, out DateTimeOffset? notAfter)
    {
        expired = false;
        notAfter = null;

        try
        {
            using var stream = AssetLoader.Open(new Uri(CertificateUri));
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            var raw = buffer.ToArray();

            var certificate = X509CertificateLoader.LoadCertificate(raw);
            notAfter = new DateTimeOffset(certificate.NotAfter.ToUniversalTime());
            expired = certificate.NotAfter.ToUniversalTime() < DateTime.UtcNow;

            var fingerprint = Convert.ToBase64String(certificate.GetCertHash(HashAlgorithmName.SHA256));
            if (!string.Equals(fingerprint, PinnedCertificateSha256, StringComparison.Ordinal))
            {
                logger.LogWarning(
                    "random.org 验签证书与内置指纹不一致（实际 {Actual}），本次按未验证处理。", fingerprint);
                certificate.Dispose();
                return null;
            }

            return certificate;
        }
        catch (Exception exception) when (exception is IOException or CryptographicException or UriFormatException
                                              or FileNotFoundException)
        {
            logger.LogWarning(exception, "random.org 验签证书不可用，本次按未验证处理。");
            return null;
        }
    }
}
