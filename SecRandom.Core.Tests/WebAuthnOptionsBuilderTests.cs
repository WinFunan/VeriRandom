using System.Linq;
using System.Text.Json;
using SecRandom.Core.Services.Security.Fido2;

namespace SecRandom.Core.Tests;

/// <summary>
///     The option JSON is the browser's contract: WebAuthn field names are literal, and a binary field that is
///     not base64url is rejected by the browser before the key is ever touched. These tests pin both.
/// </summary>
public class WebAuthnOptionsBuilderTests
{
    [Fact]
    public void CreateChallenge_IsUrlSafeAndDecodesToThirtyTwoBytes()
    {
        var challenge = WebAuthnOptionsBuilder.CreateChallenge();

        Assert.DoesNotContain('+', challenge);
        Assert.DoesNotContain('/', challenge);
        Assert.DoesNotContain('=', challenge);
        Assert.Equal(WebAuthnOptionsBuilder.ChallengeLength, Base64Url.Decode(challenge).Length);
    }

    [Fact]
    public void BuildCreationOptions_EmitsTheWebAuthnFieldNamesTheBrowserRequires()
    {
        var json = WebAuthnOptionsBuilder.BuildCreationOptions(
            "Y2hhbGxlbmdl", "localhost", "VeriRandom", "dXNlcg", "verirandom", "My key");

        using var document = JsonDocument.Parse(json);
        var publicKey = document.RootElement.GetProperty("publicKey");

        Assert.Equal("localhost", publicKey.GetProperty("rp").GetProperty("id").GetString());
        Assert.Equal("Y2hhbGxlbmdl", publicKey.GetProperty("challenge").GetString());
        Assert.Equal("dXNlcg", publicKey.GetProperty("user").GetProperty("id").GetString());
        Assert.Equal("none", publicKey.GetProperty("attestation").GetString());
        Assert.Equal("cross-platform",
            publicKey.GetProperty("authenticatorSelection").GetProperty("authenticatorAttachment").GetString());

        var algorithms = publicKey.GetProperty("pubKeyCredParams")
            .EnumerateArray()
            .Select(item => item.GetProperty("alg").GetInt32())
            .ToArray();
        Assert.Contains(-7, algorithms);
        Assert.Contains(-257, algorithms);

        Assert.Equal(JsonValueKind.Array, publicKey.GetProperty("excludeCredentials").ValueKind);
        Assert.Empty(publicKey.GetProperty("excludeCredentials").EnumerateArray());
    }

    [Fact]
    public void BuildCreationOptions_ListsEnrolledCredentialsAsBase64UrlIds()
    {
        var json = WebAuthnOptionsBuilder.BuildCreationOptions(
            "Y2hhbGxlbmdl", "localhost", "VeriRandom", "dXNlcg", "verirandom", "My key",
            [new WebAuthnCredentialDescriptor("Y3JlZC0x", ["usb", "nfc"])]);

        using var document = JsonDocument.Parse(json);
        var excluded = document.RootElement.GetProperty("publicKey").GetProperty("excludeCredentials");
        var entry = Assert.Single(excluded.EnumerateArray());

        Assert.Equal("public-key", entry.GetProperty("type").GetString());
        Assert.Equal("Y3JlZC0x", entry.GetProperty("id").GetString());
        Assert.Equal(["usb", "nfc"], entry.GetProperty("transports").EnumerateArray().Select(t => t.GetString()));
    }

    [Fact]
    public void BuildRequestOptions_CarriesTheRelyingPartyAndAllowedCredential()
    {
        var json = WebAuthnOptionsBuilder.BuildRequestOptions(
            "Y2hhbGxlbmdl", "localhost", [new WebAuthnCredentialDescriptor("Y3JlZC0x")]);

        using var document = JsonDocument.Parse(json);
        var publicKey = document.RootElement.GetProperty("publicKey");

        Assert.Equal("Y2hhbGxlbmdl", publicKey.GetProperty("challenge").GetString());
        Assert.Equal("localhost", publicKey.GetProperty("rpId").GetString());

        var allowed = Assert.Single(publicKey.GetProperty("allowCredentials").EnumerateArray());
        Assert.Equal("Y3JlZC0x", allowed.GetProperty("id").GetString());
    }
}
