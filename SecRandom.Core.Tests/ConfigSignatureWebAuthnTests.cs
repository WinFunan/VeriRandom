using System;
using System.Buffers.Binary;
using System.Formats.Cbor;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SecRandom.Core.Services.Security;
using SecRandom.Core.Services.Security.Fido2;

namespace SecRandom.Core.Tests;

/// <summary>
///     A security key can author the detached configuration document, which is what lets an operator sign one on a
///     machine that never holds the ECDSA private key. These tests pin the binding that makes that safe: the
///     document's assertion must cover the configuration digest through its challenge, so a signature cannot be
///     moved to another configuration.
/// </summary>
public class ConfigSignatureWebAuthnTests
{
    private const string Origin = "http://localhost:51234";

    [Fact]
    public void CreateConfigChallenge_DependsOnTheDigestAndIsDomainSeparated()
    {
        var first = ConfigSignature.CreateConfigChallenge(SHA256.HashData(Encoding.UTF8.GetBytes("policy-a")));
        var again = ConfigSignature.CreateConfigChallenge(SHA256.HashData(Encoding.UTF8.GetBytes("policy-a")));
        var second = ConfigSignature.CreateConfigChallenge(SHA256.HashData(Encoding.UTF8.GetBytes("policy-b")));

        Assert.Equal(first, again);
        Assert.NotEqual(first, second);
        Assert.NotEqual(Base64Url.Encode(SHA256.HashData(Encoding.UTF8.GetBytes("policy-a"))), first);
    }

    [Fact]
    public void VerifyWebAuthn_AcceptsASignatureOverTheConfigurationDigest()
    {
        using var key = RSA.Create(2048);
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes("policy"));
        var document = CreateDocument(key, digest);

        Assert.True(ConfigSignature.IsDocumentWellFormed(document));
        Assert.True(ConfigSignature.VerifyWebAuthn(document, digest));
    }

    [Fact]
    public void VerifyWebAuthn_RejectsASignatureMovedToAnotherConfiguration()
    {
        using var key = RSA.Create(2048);
        var document = CreateDocument(key, SHA256.HashData(Encoding.UTF8.GetBytes("policy")));
        var otherDigest = SHA256.HashData(Encoding.UTF8.GetBytes("another-policy"));

        Assert.False(ConfigSignature.VerifyWebAuthn(document, otherDigest));
    }

    [Fact]
    public void VerifyWebAuthn_RejectsANonLoopbackOrigin()
    {
        using var key = RSA.Create(2048);
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes("policy"));
        var document = CreateDocument(key, digest) with { Origin = "https://example.com" };

        Assert.False(ConfigSignature.VerifyWebAuthn(document, digest));
    }

    [Fact]
    public void VerifyWebAuthn_RejectsATamperedSignature()
    {
        using var key = RSA.Create(2048);
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes("policy"));
        var document = CreateDocument(key, digest);

        var signature = Base64Url.Decode(document.Signature);
        signature[^1] ^= 0xFF;
        var tampered = document with { Signature = Base64Url.Encode(signature) };

        Assert.False(ConfigSignature.VerifyWebAuthn(tampered, digest));
    }

    [Fact]
    public void IsDocumentWellFormed_RequiresTheWebAuthnFieldsForThatAlgorithm()
    {
        using var key = RSA.Create(2048);
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes("policy"));
        var document = CreateDocument(key, digest);

        Assert.True(ConfigSignature.IsDocumentWellFormed(document));
        Assert.False(ConfigSignature.IsDocumentWellFormed(document with { ClientDataJson = null }));
        Assert.False(ConfigSignature.IsDocumentWellFormed(document with { CredentialId = null }));
        Assert.False(ConfigSignature.IsDocumentWellFormed(document with { Algorithm = "unknown" }));
    }

    private static ConfigSignatureDocument CreateDocument(RSA key, byte[] digest)
    {
        var challenge = ConfigSignature.CreateConfigChallenge(digest);
        var clientDataJson = JsonSerializer.SerializeToUtf8Bytes(new
        {
            type = "webauthn.get",
            challenge,
            origin = Origin,
            crossOrigin = false
        });

        var authenticatorData = CreateAuthenticatorData(signCount: 7);
        var signedData = Concat(authenticatorData, SHA256.HashData(clientDataJson));
        var signature = key.SignData(signedData, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        return new ConfigSignatureDocument
        {
            Algorithm = ConfigSignatureDocument.WebAuthnAlgorithmId,
            Scope = "test-scope",
            PublicKey = Base64Url.Encode(CreateCoseRsaKey(key)),
            Digest = Convert.ToBase64String(digest),
            Signature = Base64Url.Encode(signature),
            CredentialId = "credential-1",
            ClientDataJson = Base64Url.Encode(clientDataJson),
            AuthenticatorData = Base64Url.Encode(authenticatorData),
            Origin = Origin,
            SignCount = 7,
            SignerLabel = "test"
        };
    }

    private static byte[] CreateAuthenticatorData(uint signCount)
    {
        using var stream = new System.IO.MemoryStream();
        stream.Write(SHA256.HashData(Encoding.UTF8.GetBytes("localhost")));
        stream.WriteByte(0x01 | 0x04);
        var counter = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(counter, signCount);
        stream.Write(counter);
        return stream.ToArray();
    }

    private static byte[] CreateCoseRsaKey(RSA key)
    {
        var parameters = key.ExportParameters(false);
        var writer = new CborWriter(CborConformanceMode.Lax);
        writer.WriteStartMap(4);
        writer.WriteInt32(1);
        writer.WriteInt32(3);
        writer.WriteInt32(3);
        writer.WriteInt32(-257);
        writer.WriteInt32(-1);
        writer.WriteByteString(parameters.Modulus ?? []);
        writer.WriteInt32(-2);
        writer.WriteByteString(parameters.Exponent ?? []);
        writer.WriteEndMap();
        return writer.Encode();
    }

    private static byte[] Concat(byte[] first, byte[] second)
    {
        var result = new byte[first.Length + second.Length];
        Buffer.BlockCopy(first, 0, result, 0, first.Length);
        Buffer.BlockCopy(second, 0, result, first.Length, second.Length);
        return result;
    }
}
