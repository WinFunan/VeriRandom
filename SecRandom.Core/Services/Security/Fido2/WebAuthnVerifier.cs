using System;
using System.Formats.Cbor;
using System.Security.Cryptography;
using System.Text;

namespace SecRandom.Core.Services.Security.Fido2;

/// <summary>
///     Verifies the two WebAuthn ceremonies this app needs: one registration (the key is enrolled) and one
///     assertion (the key proves it is still the same device).
///     Deliberate limits: attestation statements are not trusted (any <c>fmt</c> is accepted and
///     <c>attStmt</c> is ignored), because the goal is proof of possession, not a key vendor allow-list.
///     The user-presence flag is required; user verification is reported but not required, since the
///     browser prompt already covers the "touch the key" step and a PIN policy is a caller decision.
/// </summary>
public static class WebAuthnVerifier
{
    public static WebAuthnRegistrationResult VerifyRegistration(
        byte[] clientDataJson,
        byte[] attestationObject,
        string expectedChallenge,
        string expectedOrigin,
        string rpId)
    {
        if (!WebAuthnClientData.TryParse(clientDataJson, WebAuthnClientData.CreateType,
                expectedChallenge, expectedOrigin, out var failure))
            return WebAuthnRegistrationResult.Fail(failure);

        if (!TryReadAttestationObject(attestationObject, out var format, out var authenticatorDataBytes) ||
            authenticatorDataBytes is null)
            return WebAuthnRegistrationResult.Fail(WebAuthnFailure.MalformedAttestationObject);

        if (!AuthenticatorData.TryParse(authenticatorDataBytes, out var authenticatorData, out failure) ||
            authenticatorData is null)
            return WebAuthnRegistrationResult.Fail(failure);

        if (!RpIdMatches(authenticatorData, rpId))
            return WebAuthnRegistrationResult.Fail(WebAuthnFailure.RpIdMismatch);

        if (!authenticatorData.UserPresent)
            return WebAuthnRegistrationResult.Fail(WebAuthnFailure.UserPresenceRequired);

        if (!authenticatorData.HasAttestedCredentialData ||
            authenticatorData.CredentialId is not { Length: > 0 } credentialId ||
            authenticatorData.PublicKeyCose is not { Length: > 0 } publicKeyCose)
            return WebAuthnRegistrationResult.Fail(WebAuthnFailure.MissingCredentialData);

        if (!CoseCredentialPublicKey.TryParse(publicKeyCose, out var key, out failure) || key is null)
            return WebAuthnRegistrationResult.Fail(failure);

        return WebAuthnRegistrationResult.Ok(new WebAuthnRegisteredCredential(
            credentialId,
            publicKeyCose,
            key.Algorithm,
            authenticatorData.SignCount,
            authenticatorData.Aaguid ?? [],
            format,
            authenticatorData.UserVerified));
    }

    public static WebAuthnAssertionResult VerifyAssertion(
        byte[] clientDataJson,
        byte[] authenticatorData,
        byte[] signature,
        byte[] publicKeyCose,
        int storedSignCount,
        string expectedChallenge,
        string expectedOrigin,
        string rpId)
    {
        if (!WebAuthnClientData.TryParse(clientDataJson, WebAuthnClientData.GetType,
                expectedChallenge, expectedOrigin, out var failure))
            return WebAuthnAssertionResult.Fail(failure);

        if (!AuthenticatorData.TryParse(authenticatorData, out var parsed, out failure) || parsed is null)
            return WebAuthnAssertionResult.Fail(failure);

        if (!RpIdMatches(parsed, rpId))
            return WebAuthnAssertionResult.Fail(WebAuthnFailure.RpIdMismatch);

        if (!parsed.UserPresent)
            return WebAuthnAssertionResult.Fail(WebAuthnFailure.UserPresenceRequired);

        if (signature is not { Length: > 0 })
            return WebAuthnAssertionResult.Fail(WebAuthnFailure.InvalidSignature);

        if (!CoseCredentialPublicKey.TryParse(publicKeyCose, out var key, out failure) || key is null)
            return WebAuthnAssertionResult.Fail(failure);

        // WebAuthn signs authenticatorData || SHA-256(clientDataJSON).
        var clientDataHash = SHA256.HashData(clientDataJson);
        var signedData = new byte[authenticatorData.Length + clientDataHash.Length];
        Buffer.BlockCopy(authenticatorData, 0, signedData, 0, authenticatorData.Length);
        Buffer.BlockCopy(clientDataHash, 0, signedData, authenticatorData.Length, clientDataHash.Length);
        try
        {
            if (!key.TryVerify(signedData, signature))
                return WebAuthnAssertionResult.Fail(WebAuthnFailure.InvalidSignature);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(signedData);
        }

        // A counter that moves backwards, or repeats, is the documented signature of a cloned authenticator.
        // Zero on either side means "this key does not maintain a counter" and must not be read as cloning.
        if (storedSignCount > 0 && parsed.SignCount > 0 && parsed.SignCount <= (uint)storedSignCount)
            return WebAuthnAssertionResult.Fail(WebAuthnFailure.CounterRegression);

        var counterReset = parsed.SignCount == 0;
        return WebAuthnAssertionResult.Ok(parsed.SignCount, parsed.UserVerified, counterReset);
    }

    private static bool RpIdMatches(AuthenticatorData data, string rpId)
    {
        if (string.IsNullOrWhiteSpace(rpId))
            return false;
        var expected = SHA256.HashData(Encoding.UTF8.GetBytes(rpId));
        return data.RpIdHash.AsSpan().SequenceEqual(expected);
    }

    private static bool TryReadAttestationObject(byte[] attestationObject, out string format, out byte[]? authData)
    {
        format = string.Empty;
        authData = null;
        if (attestationObject is null || attestationObject.Length == 0)
            return false;

        try
        {
            var reader = new CborReader(attestationObject, CborConformanceMode.Lax);
            if (reader.ReadStartMap() is null)
                return false;

            while (reader.PeekState() != CborReaderState.EndMap)
            {
                var name = reader.ReadTextString();
                switch (name)
                {
                    case "fmt":
                        format = reader.ReadTextString();
                        break;
                    case "authData":
                        authData = reader.ReadByteString();
                        break;
                    default:
                        reader.SkipValue();
                        break;
                }
            }

            reader.ReadEndMap();
            return authData is { Length: > 0 };
        }
        catch (CborContentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}
