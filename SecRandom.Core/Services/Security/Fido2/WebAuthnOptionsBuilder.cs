using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SecRandom.Core.Services.Security.Fido2;

/// <summary>
///     Builds the JSON handed to <c>navigator.credentials.create/get</c>. Field names are part of the
///     WebAuthn wire format, so they are written literally (camelCase) instead of being produced by a naming
///     policy — a serializer option change must never be able to rename them.
/// </summary>
public static class WebAuthnOptionsBuilder
{
    public const int DefaultTimeoutMilliseconds = 120_000;
    public const int ChallengeLength = 32;
    public const int UserHandleLength = 32;

    // A null transports member is not a valid WebAuthn sequence; omit the property instead of sending null.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string CreateChallenge() => Base64Url.Encode(RandomNumberGenerator.GetBytes(ChallengeLength));

    public static string CreateUserHandle() => Base64Url.Encode(RandomNumberGenerator.GetBytes(UserHandleLength));

    public static string BuildCreationOptions(
        string challenge,
        string rpId,
        string rpName,
        string userIdBase64Url,
        string userName,
        string displayName,
        IReadOnlyList<WebAuthnCredentialDescriptor>? excludeCredentials = null,
        int timeoutMilliseconds = DefaultTimeoutMilliseconds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(challenge);
        ArgumentException.ThrowIfNullOrWhiteSpace(rpId);
        ArgumentException.ThrowIfNullOrWhiteSpace(userIdBase64Url);

        var payload = new
        {
            publicKey = new
            {
                rp = new { name = rpName, id = rpId },
                user = new { id = userIdBase64Url, name = userName, displayName },
                challenge,
                pubKeyCredParams = new object[]
                {
                    new { type = "public-key", alg = -7 },
                    new { type = "public-key", alg = -257 }
                },
                timeout = timeoutMilliseconds,
                attestation = "none",
                authenticatorSelection = new
                {
                    authenticatorAttachment = "cross-platform",
                    residentKey = "discouraged",
                    userVerification = "preferred"
                },
                excludeCredentials = ToDescriptors(excludeCredentials)
            }
        };

        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    public static string BuildRequestOptions(
        string challenge,
        string rpId,
        IReadOnlyList<WebAuthnCredentialDescriptor>? allowCredentials = null,
        int timeoutMilliseconds = DefaultTimeoutMilliseconds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(challenge);
        ArgumentException.ThrowIfNullOrWhiteSpace(rpId);

        var payload = new
        {
            publicKey = new
            {
                challenge,
                rpId,
                timeout = timeoutMilliseconds,
                userVerification = "preferred",
                allowCredentials = ToDescriptors(allowCredentials)
            }
        };

        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    private static object[] ToDescriptors(IReadOnlyList<WebAuthnCredentialDescriptor>? descriptors) =>
        descriptors is null
            ? []
            : descriptors
                .Where(descriptor => !string.IsNullOrWhiteSpace(descriptor.Id))
                .Select(descriptor => (object)new
                {
                    type = "public-key",
                    id = descriptor.Id,
                    transports = descriptor.Transports is { Count: > 0 } transports ? transports : null
                })
                .ToArray();
}
