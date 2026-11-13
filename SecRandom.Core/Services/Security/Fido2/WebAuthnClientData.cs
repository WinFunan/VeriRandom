using System;
using System.Text.Json;

namespace SecRandom.Core.Services.Security.Fido2;

/// <summary>
///     The browser-produced <c>clientDataJSON</c>. Its JSON is produced by the browser, not by us, so it is
///     parsed defensively: a missing or non-string field is a failure, never an exception escaping into a
///     caller that is in the middle of a security decision.
/// </summary>
public static class WebAuthnClientData
{
    public const string CreateType = "webauthn.create";
    public const string GetType = "webauthn.get";

    public static bool TryParse(
        byte[] clientDataJson,
        string expectedType,
        string expectedChallenge,
        string expectedOrigin,
        out WebAuthnFailure failure)
    {
        failure = WebAuthnFailure.None;
        if (clientDataJson is null || clientDataJson.Length == 0)
        {
            failure = WebAuthnFailure.MalformedClientData;
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(clientDataJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                failure = WebAuthnFailure.MalformedClientData;
                return false;
            }

            if (!TryReadString(root, "type", out var type) ||
                !TryReadString(root, "challenge", out var challenge) ||
                !TryReadString(root, "origin", out var origin))
            {
                failure = WebAuthnFailure.MalformedClientData;
                return false;
            }

            if (!string.Equals(type, expectedType, StringComparison.Ordinal))
            {
                failure = WebAuthnFailure.UnexpectedClientDataType;
                return false;
            }

            if (!ChallengeEquals(challenge, expectedChallenge))
            {
                failure = WebAuthnFailure.ChallengeMismatch;
                return false;
            }

            if (!string.Equals(origin, expectedOrigin, StringComparison.OrdinalIgnoreCase))
            {
                failure = WebAuthnFailure.OriginMismatch;
                return false;
            }

            if (root.TryGetProperty("crossOrigin", out var crossOrigin) &&
                crossOrigin.ValueKind == JsonValueKind.True)
            {
                failure = WebAuthnFailure.CrossOriginNotAllowed;
                return false;
            }

            return true;
        }
        catch (JsonException)
        {
            failure = WebAuthnFailure.MalformedClientData;
            return false;
        }
    }

    private static bool TryReadString(JsonElement root, string name, out string value)
    {
        value = string.Empty;
        if (!root.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.String)
            return false;
        value = element.GetString() ?? string.Empty;
        return value.Length > 0;
    }

    // Compare decoded bytes so padding differences and the +/- vs -/_ alphabet cannot fail a valid echo.
    private static bool ChallengeEquals(string actual, string expected)
    {
        if (string.Equals(actual, expected, StringComparison.Ordinal))
            return true;
        return Base64Url.TryDecode(actual, out var actualBytes)
               && Base64Url.TryDecode(expected, out var expectedBytes)
               && actualBytes.AsSpan().SequenceEqual(expectedBytes);
    }
}
