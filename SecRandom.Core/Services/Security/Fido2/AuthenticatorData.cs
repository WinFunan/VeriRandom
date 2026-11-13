using System;
using System.Buffers.Binary;
using System.Formats.Cbor;

namespace SecRandom.Core.Services.Security.Fido2;

/// <summary>
///     The authenticator data structure shared by registration and assertion (WebAuthn §6.1). Only the
///     fields this device actually needs are surfaced: the RP id hash, the two flags that gate a decision,
///     the signature counter, and — for registration — the credential id and its COSE public key.
/// </summary>
public sealed class AuthenticatorData
{
    public const byte FlagUserPresent = 0x01;
    public const byte FlagUserVerified = 0x04;
    public const byte FlagBackupEligible = 0x08;
    public const byte FlagBackupState = 0x10;
    public const byte FlagAttestedCredentialData = 0x40;

    private const int FixedLength = 37;

    private AuthenticatorData(
        byte[] rpIdHash,
        byte flags,
        uint signCount,
        byte[]? credentialId,
        byte[]? publicKeyCose,
        byte[]? aaguid)
    {
        RpIdHash = rpIdHash;
        Flags = flags;
        SignCount = signCount;
        CredentialId = credentialId;
        PublicKeyCose = publicKeyCose;
        Aaguid = aaguid;
    }

    public byte[] RpIdHash { get; }
    public byte Flags { get; }
    public uint SignCount { get; }
    public byte[]? CredentialId { get; }
    public byte[]? PublicKeyCose { get; }
    public byte[]? Aaguid { get; }

    public bool UserPresent => (Flags & FlagUserPresent) != 0;
    public bool UserVerified => (Flags & FlagUserVerified) != 0;
    public bool HasAttestedCredentialData => (Flags & FlagAttestedCredentialData) != 0;

    public static bool TryParse(byte[] data, out AuthenticatorData? parsed, out WebAuthnFailure failure)
    {
        parsed = null;
        failure = WebAuthnFailure.MalformedAuthenticatorData;

        if (data is null || data.Length < FixedLength)
            return false;

        var rpIdHash = data[..32];
        var flags = data[32];
        var signCount = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(33, 4));

        if ((flags & FlagAttestedCredentialData) == 0)
        {
            // Assertions may carry extension data after the counter; it is not authenticated by us.
            parsed = new AuthenticatorData(rpIdHash, flags, signCount, null, null, null);
            failure = WebAuthnFailure.None;
            return true;
        }

        // attestedCredentialData: aaguid(16) credIdLen(2) credId(n) credentialPublicKey(COSE)
        const int attestedHeaderLength = 16 + 2;
        if (data.Length < FixedLength + attestedHeaderLength)
            return false;

        var aaguid = data.AsSpan(FixedLength, 16).ToArray();
        var credentialIdLength = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(FixedLength + 16, 2));
        const int credentialIdOffset = FixedLength + attestedHeaderLength;
        if (credentialIdLength == 0 || data.Length <= credentialIdOffset + credentialIdLength)
            return false;

        var credentialId = data.AsSpan(credentialIdOffset, credentialIdLength).ToArray();
        var keyOffset = credentialIdOffset + credentialIdLength;

        byte[] publicKeyCose;
        try
        {
            var remaining = data.AsMemory(keyOffset);
            var reader = new CborReader(remaining, CborConformanceMode.Lax);
            reader.SkipValue();
            var consumed = remaining.Length - reader.BytesRemaining;
            if (consumed <= 0)
                return false;
            publicKeyCose = remaining[..consumed].ToArray();
        }
        catch (CborContentException)
        {
            return false;
        }

        parsed = new AuthenticatorData(rpIdHash, flags, signCount, credentialId, publicKeyCose, aaguid);
        failure = WebAuthnFailure.None;
        return true;
    }
}
