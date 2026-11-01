using System;
using System.Text;
using SecRandom.Core.Services.Security;

namespace SecRandom.Core.Tests;

/// <summary>
///     The configuration signature exists so a draw machine can verify a policy it cannot itself author:
///     it holds only the public key. These tests pin the properties that make that meaningful — a signature
///     verifies only for the exact digest it was made over, a different key pair cannot pass, and a damaged
///     document is refused rather than treated as valid.
/// </summary>
public sealed class ConfigSignatureTests
{
    private static byte[] DigestOf(string text) => ConfigSignature.ComputeDigest(Encoding.UTF8.GetBytes(text));

    [Fact]
    public void ASignatureVerifiesForTheDigestItWasMadeOver()
    {
        var (publicKey, privateKey) = ConfigSignature.CreateKeyPair();
        var digest = DigestOf("policy-v1");

        var signature = ConfigSignature.Sign(digest, privateKey);

        Assert.True(ConfigSignature.Verify(digest, signature, publicKey));
    }

    [Fact]
    public void ATamperedDigestIsRejected()
    {
        var (publicKey, privateKey) = ConfigSignature.CreateKeyPair();
        var signature = ConfigSignature.Sign(DigestOf("policy-v1"), privateKey);

        Assert.False(ConfigSignature.Verify(DigestOf("policy-v1-with-one-setting-changed"), signature, publicKey));
    }

    [Fact]
    public void AnotherKeyPairCannotPassAsTheSigner()
    {
        var (_, privateKey) = ConfigSignature.CreateKeyPair();
        var (otherPublicKey, _) = ConfigSignature.CreateKeyPair();
        var digest = DigestOf("policy-v1");

        var signature = ConfigSignature.Sign(digest, privateKey);

        // This is the whole point: the draw machine can verify but never re-sign, so a key it does not
        // recognise must not be accepted.
        Assert.False(ConfigSignature.Verify(digest, signature, otherPublicKey));
    }

    [Fact]
    public void AFlippedSignatureBitIsRejected()
    {
        var (publicKey, privateKey) = ConfigSignature.CreateKeyPair();
        var digest = DigestOf("policy-v1");
        var bytes = Convert.FromBase64String(ConfigSignature.Sign(digest, privateKey));
        bytes[^1] ^= 0x01;

        Assert.False(ConfigSignature.Verify(digest, Convert.ToBase64String(bytes), publicKey));
    }

    [Theory]
    [InlineData("", "not-a-key")]
    [InlineData("not-base64!", "not-a-key")]
    [InlineData("AAAA", "not-base64!")]
    public void MalformedInputIsRejectedInsteadOfThrowing(string signature, string publicKey)
    {
        Assert.False(ConfigSignature.Verify(DigestOf("policy-v1"), signature, publicKey));
    }

    [Fact]
    public void TheFingerprintIsStableAndKeySpecific()
    {
        var (publicKey, _) = ConfigSignature.CreateKeyPair();
        var (otherPublicKey, _) = ConfigSignature.CreateKeyPair();

        Assert.Equal(ConfigSignature.Fingerprint(publicKey), ConfigSignature.Fingerprint(publicKey));
        Assert.NotEqual(ConfigSignature.Fingerprint(publicKey), ConfigSignature.Fingerprint(otherPublicKey));
        Assert.Equal(32, ConfigSignature.Fingerprint(publicKey).Length);
    }

    [Fact]
    public void AWellFormedDocumentIsAcceptedAndAForeignAlgorithmIsNot()
    {
        var (publicKey, privateKey) = ConfigSignature.CreateKeyPair();
        var digest = DigestOf("policy-v1");
        var document = new ConfigSignatureDocument
        {
            Scope = "verirandom-config-policy/v1",
            PublicKey = publicKey,
            Digest = Convert.ToBase64String(digest),
            Signature = ConfigSignature.Sign(digest, privateKey)
        };

        Assert.True(ConfigSignature.IsDocumentWellFormed(document));
        Assert.False(ConfigSignature.IsDocumentWellFormed(document with { Algorithm = "ed25519" }));
        Assert.False(ConfigSignature.IsDocumentWellFormed(document with { FormatVersion = 99 }));
        Assert.False(ConfigSignature.IsDocumentWellFormed(document with { Scope = "" }));
    }

    [Fact]
    public void ThePrivateKeyIsNeverDerivedFromAUserPassword()
    {
        // Guards the design decision: two key pairs must be independent even when the same operator runs
        // the generation twice, because the key is random and the password only ever protects it at rest.
        var (firstPublic, firstPrivate) = ConfigSignature.CreateKeyPair();
        var (secondPublic, secondPrivate) = ConfigSignature.CreateKeyPair();

        Assert.NotEqual(firstPublic, secondPublic);
        Assert.NotEqual(firstPrivate, secondPrivate);
    }
}
