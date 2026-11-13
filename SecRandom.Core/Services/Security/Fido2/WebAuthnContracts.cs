using System;
using System.Collections.Generic;

namespace SecRandom.Core.Services.Security.Fido2;

/// <summary>
///     Why a WebAuthn registration or assertion was refused. The names are deliberately machine-readable:
///     the caller decides how to present them, and a later factor wiring must be able to tell "your key
///     said no" (<see cref="InvalidSignature" />) apart from "this build cannot talk to that key"
///     (<see cref="UnsupportedAlgorithm" />) and from "the key was cloned" (<see cref="CounterRegression" />).
/// </summary>
public enum WebAuthnFailure
{
    None,
    MalformedClientData,
    UnexpectedClientDataType,
    ChallengeMismatch,
    OriginMismatch,
    CrossOriginNotAllowed,
    MalformedAttestationObject,
    MalformedAuthenticatorData,
    RpIdMismatch,
    UserPresenceRequired,
    MissingCredentialData,
    MalformedPublicKey,
    UnsupportedAlgorithm,
    InvalidSignature,
    CounterRegression
}

/// <summary>
///     A credential the authenticator just registered. <see cref="PublicKeyCose" /> is the raw COSE_Key
///     CBOR item, stored verbatim so a later assertion is verified against exactly what the key emitted.
/// </summary>
public sealed record WebAuthnRegisteredCredential(
    byte[] CredentialId,
    byte[] PublicKeyCose,
    int Algorithm,
    uint SignCount,
    byte[] Aaguid,
    string AttestationFormat,
    bool UserVerified);

public sealed record WebAuthnRegistrationResult(
    bool Success,
    WebAuthnFailure Failure,
    WebAuthnRegisteredCredential? Credential)
{
    public static WebAuthnRegistrationResult Ok(WebAuthnRegisteredCredential credential) =>
        new(true, WebAuthnFailure.None, credential);

    public static WebAuthnRegistrationResult Fail(WebAuthnFailure failure) => new(false, failure, null);
}

/// <summary>
///     Outcome of one assertion. <see cref="CounterReset" /> reports "both counters were zero", which is a
///     key without a signature counter and is <b>not</b> an error; a real regression fails with
///     <see cref="WebAuthnFailure.CounterRegression" /> instead.
/// </summary>
public sealed record WebAuthnAssertionResult(
    bool Success,
    WebAuthnFailure Failure,
    uint SignCount,
    bool UserVerified,
    bool CounterReset)
{
    public static WebAuthnAssertionResult Ok(uint signCount, bool userVerified, bool counterReset) =>
        new(true, WebAuthnFailure.None, signCount, userVerified, counterReset);

    public static WebAuthnAssertionResult Fail(WebAuthnFailure failure) =>
        new(false, failure, 0, false, false);
}

/// <summary>One entry of <c>allowCredentials</c>/<c>excludeCredentials</c>; the id is base64url.</summary>
public sealed record WebAuthnCredentialDescriptor(string Id, IReadOnlyList<string>? Transports = null);
