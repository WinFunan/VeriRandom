using System;
using System.Formats.Cbor;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SecRandom.Core.Services.Security.Fido2;

namespace SecRandom.Core.Tests;

/// <summary>
///     Drives <see cref="WebAuthnVerifier" /> with a synthetic authenticator: an ECDsa P-256 key stands in
///     for the security key, so every accept and reject path is exercised without hardware. The two things
///     that must never regress are (1) a signature over a different payload is refused and (2) a counter that
///     does not advance is reported as a clone instead of being accepted.
/// </summary>
public class WebAuthnVerifierTests
{
    private const string RpId = "localhost";
    private const string Origin = "http://localhost:51234";
    private const string Challenge = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    [Fact]
    public void Registration_AcceptsAWellFormedEs256Credential()
    {
        RequireEc();
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var fixture = CreateRegistration(key, signCount: 0);

        var result = WebAuthnVerifier.VerifyRegistration(
            fixture.ClientDataJson, fixture.AttestationObject, Challenge, Origin, RpId);

        Assert.True(result.Success, result.Failure.ToString());
        Assert.NotNull(result.Credential);
        Assert.Equal(-7, result.Credential!.Algorithm);
        Assert.Equal(32, result.Credential.CredentialId.Length);
        Assert.Equal(0u, result.Credential.SignCount);
        Assert.True(result.Credential.UserVerified);
    }

    [Fact]
    public void Registration_RejectsAWrongChallenge()
    {
        RequireEc();
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var fixture = CreateRegistration(key, signCount: 0);

        var result = WebAuthnVerifier.VerifyRegistration(
            fixture.ClientDataJson, fixture.AttestationObject, "B" + Challenge[1..], Origin, RpId);

        Assert.False(result.Success);
        Assert.Equal(WebAuthnFailure.ChallengeMismatch, result.Failure);
    }

    [Fact]
    public void Registration_RejectsAForeignOrigin()
    {
        RequireEc();
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var fixture = CreateRegistration(key, signCount: 0);

        var result = WebAuthnVerifier.VerifyRegistration(
            fixture.ClientDataJson, fixture.AttestationObject, Challenge, "https://evil.example", RpId);

        Assert.False(result.Success);
        Assert.Equal(WebAuthnFailure.OriginMismatch, result.Failure);
    }

    [Fact]
    public void Registration_RejectsAMismatchedRpId()
    {
        RequireEc();
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var fixture = CreateRegistration(key, signCount: 0);

        var result = WebAuthnVerifier.VerifyRegistration(
            fixture.ClientDataJson, fixture.AttestationObject, Challenge, Origin, "example.com");

        Assert.False(result.Success);
        Assert.Equal(WebAuthnFailure.RpIdMismatch, result.Failure);
    }

    [Fact]
    public void Registration_RequiresTheUserPresenceFlag()
    {
        RequireEc();
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var fixture = CreateRegistration(key, signCount: 0, userPresent: false);

        var result = WebAuthnVerifier.VerifyRegistration(
            fixture.ClientDataJson, fixture.AttestationObject, Challenge, Origin, RpId);

        Assert.False(result.Success);
        Assert.Equal(WebAuthnFailure.UserPresenceRequired, result.Failure);
    }

    [Fact]
    public void Registration_RefusesAnUnsupportedAlgorithm()
    {
        RequireEc();
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var fixture = CreateRegistration(key, signCount: 0, algorithm: -8);

        var result = WebAuthnVerifier.VerifyRegistration(
            fixture.ClientDataJson, fixture.AttestationObject, Challenge, Origin, RpId);

        Assert.False(result.Success);
        Assert.Equal(WebAuthnFailure.UnsupportedAlgorithm, result.Failure);
    }

    [Fact]
    public void Assertion_AcceptsAnAdvancedCounter()
    {
        RequireEc();
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var cose = CreateCoseKey(key, -7);
        var clientData = CreateClientData(WebAuthnClientData.GetType);
        var authData = CreateAssertionAuthData(signCount: 7);
        var signature = Sign(key, authData, clientData);

        var result = WebAuthnVerifier.VerifyAssertion(
            clientData, authData, signature, cose, storedSignCount: 6, Challenge, Origin, RpId);

        Assert.True(result.Success, result.Failure.ToString());
        Assert.Equal(7u, result.SignCount);
    }

    [Fact]
    public void Assertion_RejectsATamperedSignature()
    {
        RequireEc();
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var cose = CreateCoseKey(key, -7);
        var clientData = CreateClientData(WebAuthnClientData.GetType);
        var authData = CreateAssertionAuthData(signCount: 7);
        var signature = Sign(key, authData, clientData);
        signature[^1] ^= 0xFF;

        var result = WebAuthnVerifier.VerifyAssertion(
            clientData, authData, signature, cose, storedSignCount: 6, Challenge, Origin, RpId);

        Assert.False(result.Success);
        Assert.Equal(WebAuthnFailure.InvalidSignature, result.Failure);
    }

    [Fact]
    public void Assertion_ReportsACounterRegressionAsAClone()
    {
        RequireEc();
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var cose = CreateCoseKey(key, -7);
        var clientData = CreateClientData(WebAuthnClientData.GetType);
        var authData = CreateAssertionAuthData(signCount: 5);
        var signature = Sign(key, authData, clientData);

        var result = WebAuthnVerifier.VerifyAssertion(
            clientData, authData, signature, cose, storedSignCount: 9, Challenge, Origin, RpId);

        Assert.False(result.Success);
        Assert.Equal(WebAuthnFailure.CounterRegression, result.Failure);
    }

    [Fact]
    public void Assertion_TreatsAZeroCounterAsNoCounterRatherThanAClone()
    {
        RequireEc();
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var cose = CreateCoseKey(key, -7);
        var clientData = CreateClientData(WebAuthnClientData.GetType);
        var authData = CreateAssertionAuthData(signCount: 0);
        var signature = Sign(key, authData, clientData);

        var result = WebAuthnVerifier.VerifyAssertion(
            clientData, authData, signature, cose, storedSignCount: 4, Challenge, Origin, RpId);

        Assert.True(result.Success, result.Failure.ToString());
        Assert.True(result.CounterReset);
    }

    [Fact]
    public void Assertion_RejectsARegistrationClientData()
    {
        RequireEc();
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var cose = CreateCoseKey(key, -7);
        var clientData = CreateClientData(WebAuthnClientData.CreateType);
        var authData = CreateAssertionAuthData(signCount: 1);
        var signature = Sign(key, authData, clientData);

        var result = WebAuthnVerifier.VerifyAssertion(
            clientData, authData, signature, cose, storedSignCount: 0, Challenge, Origin, RpId);

        Assert.False(result.Success);
        Assert.Equal(WebAuthnFailure.UnexpectedClientDataType, result.Failure);
    }

    [Fact]
    public void Assertion_AcceptsAnRs256Credential()
    {
        using var rsa = RSA.Create(2048);
        var cose = CreateCoseRsaKey(rsa);
        var clientData = CreateClientData(WebAuthnClientData.GetType);
        var authData = CreateAssertionAuthData(signCount: 3);
        var signed = Concat(authData, SHA256.HashData(clientData));
        var signature = rsa.SignData(signed, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        var result = WebAuthnVerifier.VerifyAssertion(
            clientData, authData, signature, cose, storedSignCount: 2, Challenge, Origin, RpId);

        Assert.True(result.Success, result.Failure.ToString());
    }

    /// <summary>
    ///     Some locked-down Windows hosts cannot create software EC keys through CNG; the verifier itself is
    ///     unaffected, so those tests skip instead of reporting a false failure.
    /// </summary>
    private static readonly bool EcAvailable = ProbeEc();

    private static bool ProbeEc()
    {
        try
        {
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    private static void RequireEc()
    {
        if (!EcAvailable)
            Assert.Skip("This machine's CNG provider cannot create EC keys.");
    }

    // ---- synthetic authenticator -------------------------------------------------------------

    private sealed record RegistrationFixture(byte[] ClientDataJson, byte[] AttestationObject);

    private static RegistrationFixture CreateRegistration(
        ECDsa key,
        uint signCount,
        bool userPresent = true,
        int algorithm = -7)
    {
        var cose = CreateCoseKey(key, algorithm);
        var credentialId = RandomNumberGenerator.GetBytes(32);
        var clientData = CreateClientData(WebAuthnClientData.CreateType);
        var authData = CreateRegistrationAuthData(credentialId, cose, signCount, userPresent);

        var writer = new CborWriter(CborConformanceMode.Lax);
        writer.WriteStartMap(3);
        writer.WriteTextString("fmt");
        writer.WriteTextString("none");
        writer.WriteTextString("attStmt");
        writer.WriteStartMap(0);
        writer.WriteEndMap();
        writer.WriteTextString("authData");
        writer.WriteByteString(authData);
        writer.WriteEndMap();

        return new RegistrationFixture(clientData, writer.Encode());
    }

    private static byte[] CreateClientData(string type) =>
        JsonSerializer.SerializeToUtf8Bytes(new
        {
            type,
            challenge = Challenge,
            origin = Origin,
            crossOrigin = false
        });

    private static byte[] CreateRegistrationAuthData(byte[] credentialId, byte[] cose, uint signCount, bool userPresent)
    {
        using var stream = new System.IO.MemoryStream();
        stream.Write(SHA256.HashData(Encoding.UTF8.GetBytes(RpId)));
        stream.WriteByte((byte)((userPresent ? 0x01 : 0x00) | 0x04 | 0x40));
        var counter = new byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(counter, signCount);
        stream.Write(counter);
        stream.Write(new byte[16]); // AAGUID
        var idLength = new byte[2];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(idLength, (ushort)credentialId.Length);
        stream.Write(idLength);
        stream.Write(credentialId);
        stream.Write(cose);
        return stream.ToArray();
    }

    private static byte[] CreateAssertionAuthData(uint signCount)
    {
        using var stream = new System.IO.MemoryStream();
        stream.Write(SHA256.HashData(Encoding.UTF8.GetBytes(RpId)));
        stream.WriteByte(0x01 | 0x04);
        var counter = new byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(counter, signCount);
        stream.Write(counter);
        return stream.ToArray();
    }

    private static byte[] Sign(ECDsa key, byte[] authData, byte[] clientData) =>
        key.SignData(Concat(authData, SHA256.HashData(clientData)), HashAlgorithmName.SHA256,
            DSASignatureFormat.Rfc3279DerSequence);

    private static byte[] Concat(byte[] first, byte[] second)
    {
        var result = new byte[first.Length + second.Length];
        Buffer.BlockCopy(first, 0, result, 0, first.Length);
        Buffer.BlockCopy(second, 0, result, first.Length, second.Length);
        return result;
    }

    private static byte[] CreateCoseKey(ECDsa key, int algorithm)
    {
        var parameters = key.ExportParameters(false);
        var writer = new CborWriter(CborConformanceMode.Lax);
        writer.WriteStartMap(5);
        writer.WriteInt32(1);
        writer.WriteInt32(2); // kty: EC2
        writer.WriteInt32(3);
        writer.WriteInt32(algorithm);
        writer.WriteInt32(-1);
        writer.WriteInt32(1); // crv: P-256
        writer.WriteInt32(-2);
        writer.WriteByteString(parameters.Q.X ?? []);
        writer.WriteInt32(-3);
        writer.WriteByteString(parameters.Q.Y ?? []);
        writer.WriteEndMap();
        return writer.Encode();
    }

    private static byte[] CreateCoseRsaKey(RSA key)
    {
        var parameters = key.ExportParameters(false);
        var writer = new CborWriter(CborConformanceMode.Lax);
        writer.WriteStartMap(4);
        writer.WriteInt32(1);
        writer.WriteInt32(3); // kty: RSA
        writer.WriteInt32(3);
        writer.WriteInt32(-257);
        writer.WriteInt32(-1);
        writer.WriteByteString(parameters.Modulus ?? []);
        writer.WriteInt32(-2);
        writer.WriteByteString(parameters.Exponent ?? []);
        writer.WriteEndMap();
        return writer.Encode();
    }
}
