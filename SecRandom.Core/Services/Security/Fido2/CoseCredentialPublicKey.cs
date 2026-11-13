using System;
using System.Collections.Generic;
using System.Formats.Cbor;
using System.Security.Cryptography;

namespace SecRandom.Core.Services.Security.Fido2;

/// <summary>
///     One COSE_Key carried in the attested credential data. Attestation statements are deliberately not
///     verified here: this app only needs proof of possession, so the credential is accepted with whatever
///     format the key emitted and every later assertion is checked against this stored key instead.
///     Supported algorithms are the ones a roaming FIDO2 key actually ships: ES256/384/512, RS256/384/512
///     and PS256/384/512. EdDSA (COSE -8) has no BCL implementation on every target, so it is refused
///     explicitly rather than silently treated as valid.
/// </summary>
public sealed class CoseCredentialPublicKey
{
    private const int KtyEc2 = 2;
    private const int KtyRsa = 3;

    private readonly int _kind;
    private readonly ECCurve? _curve;
    private readonly byte[]? _x;
    private readonly byte[]? _y;
    private readonly byte[]? _modulus;
    private readonly byte[]? _exponent;

    private CoseCredentialPublicKey(int algorithm, int kind, ECCurve? curve, byte[]? x, byte[]? y, byte[]? modulus, byte[]? exponent)
    {
        Algorithm = algorithm;
        _kind = kind;
        _curve = curve;
        _x = x;
        _y = y;
        _modulus = modulus;
        _exponent = exponent;
    }

    public int Algorithm { get; }

    public static bool TryParse(byte[] coseKey, out CoseCredentialPublicKey? key, out WebAuthnFailure failure)
    {
        key = null;
        failure = WebAuthnFailure.MalformedPublicKey;

        if (coseKey is null || coseKey.Length == 0)
            return false;

        var integers = new Dictionary<int, int>();
        var byteStrings = new Dictionary<int, byte[]>();
        try
        {
            var reader = new CborReader(coseKey, CborConformanceMode.Lax);
            if (reader.ReadStartMap() is null)
                return false;

            while (reader.PeekState() != CborReaderState.EndMap)
            {
                if (reader.PeekState() is not (CborReaderState.UnsignedInteger or CborReaderState.NegativeInteger))
                    return false;
                var label = reader.ReadInt32();

                switch (reader.PeekState())
                {
                    case CborReaderState.UnsignedInteger:
                    case CborReaderState.NegativeInteger:
                        integers[label] = reader.ReadInt32();
                        break;
                    case CborReaderState.ByteString:
                        byteStrings[label] = reader.ReadByteString();
                        break;
                    default:
                        return false;
                }
            }

            reader.ReadEndMap();
        }
        catch (CborContentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }

        if (!integers.TryGetValue(1, out var kty) || !integers.TryGetValue(3, out var algorithm))
            return false;

        switch (kty)
        {
            case KtyEc2:
            {
                if (!integers.TryGetValue(-1, out var crv) ||
                    !byteStrings.TryGetValue(-2, out var x) ||
                    !byteStrings.TryGetValue(-3, out var y))
                    return false;

                var curve = crv switch
                {
                    1 => ECCurve.NamedCurves.nistP256,
                    2 => ECCurve.NamedCurves.nistP384,
                    3 => ECCurve.NamedCurves.nistP521,
                    _ => (ECCurve?)null
                };
                if (curve is null || !IsEllipticAlgorithm(algorithm))
                {
                    failure = WebAuthnFailure.UnsupportedAlgorithm;
                    return false;
                }

                key = new CoseCredentialPublicKey(algorithm, KtyEc2, curve, x, y, null, null);
                failure = WebAuthnFailure.None;
                return true;
            }

            case KtyRsa:
            {
                if (!byteStrings.TryGetValue(-1, out var modulus) || !byteStrings.TryGetValue(-2, out var exponent) ||
                    !IsRsaAlgorithm(algorithm))
                {
                    failure = WebAuthnFailure.UnsupportedAlgorithm;
                    return false;
                }

                key = new CoseCredentialPublicKey(algorithm, KtyRsa, null, null, null, modulus, exponent);
                failure = WebAuthnFailure.None;
                return true;
            }

            default:
                failure = WebAuthnFailure.UnsupportedAlgorithm;
                return false;
        }
    }

    public bool TryVerify(byte[] data, byte[] signature)
    {
        try
        {
            return _kind switch
            {
                KtyEc2 => VerifyElliptic(data, signature),
                KtyRsa => VerifyRsa(data, signature),
                _ => false
            };
        }
        catch (CryptographicException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private bool VerifyElliptic(byte[] data, byte[] signature)
    {
        if (_curve is not { } curve || _x is null || _y is null)
            return false;

        using var ecdsa = ECDsa.Create(new ECParameters
        {
            Curve = curve,
            Q = new ECPoint { X = _x, Y = _y }
        });
        var hash = Algorithm switch
        {
            -7 => HashAlgorithmName.SHA256,
            -35 => HashAlgorithmName.SHA384,
            -36 => HashAlgorithmName.SHA512,
            _ => default
        };
        if (hash == default)
            return false;

        return ecdsa.VerifyData(data, signature, hash, DSASignatureFormat.Rfc3279DerSequence);
    }

    private bool VerifyRsa(byte[] data, byte[] signature)
    {
        if (_modulus is null || _exponent is null)
            return false;

        using var rsa = RSA.Create(new RSAParameters { Modulus = _modulus, Exponent = _exponent });
        var (hash, padding) = Algorithm switch
        {
            -257 => (HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1),
            -258 => (HashAlgorithmName.SHA384, RSASignaturePadding.Pkcs1),
            -259 => (HashAlgorithmName.SHA512, RSASignaturePadding.Pkcs1),
            -37 => (HashAlgorithmName.SHA256, RSASignaturePadding.Pss),
            -38 => (HashAlgorithmName.SHA384, RSASignaturePadding.Pss),
            -39 => (HashAlgorithmName.SHA512, RSASignaturePadding.Pss),
            _ => (default(HashAlgorithmName), RSASignaturePadding.Pkcs1)
        };
        return hash != default && rsa.VerifyData(data, signature, hash, padding);
    }

    private static bool IsEllipticAlgorithm(int algorithm) => algorithm is -7 or -35 or -36;

    private static bool IsRsaAlgorithm(int algorithm) => algorithm is -257 or -258 or -259 or -37 or -38 or -39;
}

