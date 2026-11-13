using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Services.Security.Fido2;

namespace SecRandom.Services.Security.Fido2;

public enum Fido2OperationStatus
{
    Succeeded,
    Unsupported,
    NotEnrolled,
    Cancelled,
    TimedOut,
    BrowserFailed,
    VerificationFailed,
    StorageFailed
}

/// <summary>A credential projected for a caller that must not see the stored public key.</summary>
public sealed record Fido2CredentialInfo(
    string Id,
    string CredentialId,
    string Label,
    int Algorithm,
    uint SignCount,
    bool UserVerified,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset LastUsedAtUtc);

public sealed record Fido2RegistrationOutcome(
    Fido2OperationStatus Status,
    Fido2CredentialInfo? Credential,
    WebAuthnFailure Failure,
    string? Detail);

public sealed record Fido2AssertionOutcome(
    Fido2OperationStatus Status,
    WebAuthnFailure Failure,
    bool CloneSuspected,
    uint SignCount,
    string? Detail,
    Fido2AssertionProof? Proof = null,
    string? Origin = null);

/// <summary>
///     The one entry point for hardware security keys. It owns enrollment, assertion and removal, and it is the
///     only place that ever sees the browser response; the verification itself stays in Core.
/// </summary>
/// <remarks>
///     <para>
///         Nothing here is wired into <c>ISecurityService</c> yet: which verification factors a user must present is
///         still a settings decision, and this service deliberately answers only "did this key prove it is the
///         enrolled key". A future factor adds an <see cref="AssertAsync" /> call at that decision point instead of
///         duplicating the ceremony.
///     </para>
///     <para>
///         The relying party is the loopback <c>localhost</c> origin the transport serves, so a credential is bound
///         to this application's browser page rather than to a public domain. That is what makes the ceremony work
///         without a hosted site, and it also means a credential cannot be replayed against another RP id.
///     </para>
/// </remarks>
public sealed class Fido2SecurityKeyService(
    Fido2CredentialStore store,
    Fido2BrowserTransport transport,
    ILogger<Fido2SecurityKeyService> logger)
{
    public const string RelyingPartyId = Fido2BrowserTransport.RelyingPartyId;
    public const string RelyingPartyName = "VeriRandom";

    private static readonly TimeSpan CeremonyTimeout = TimeSpan.FromSeconds(120);
    private readonly SemaphoreSlim _ceremonyGate = new(1, 1);

    /// <summary>
    ///     iOS has no usable loopback <see cref="System.Net.HttpListener" /> path for an in-app browser ceremony, so
    ///     the mobile path stays unsupported until the heads provide a native <c>ASAuthorization</c> seam.
    /// </summary>
    public bool IsSupported => !OperatingSystem.IsIOS();

    public event EventHandler? Changed
    {
        add => store.Changed += value;
        remove => store.Changed -= value;
    }

    public IReadOnlyList<Fido2CredentialInfo> ListCredentials() =>
        store.List().Select(ToInfo).ToList();

    public async Task<Fido2RegistrationOutcome> RegisterAsync(
        string label,
        string? userName = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        if (!IsSupported)
            return new Fido2RegistrationOutcome(Fido2OperationStatus.Unsupported, null, WebAuthnFailure.None,
                "platform_unsupported");

        await _ceremonyGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var existing = store.List();
            // Reuse the first handle so a second key for the same person is the same WebAuthn user.
            var userHandle = existing.FirstOrDefault()?.UserHandle;
            if (string.IsNullOrWhiteSpace(userHandle))
                userHandle = WebAuthnOptionsBuilder.CreateUserHandle();

            var challenge = WebAuthnOptionsBuilder.CreateChallenge();
            var options = WebAuthnOptionsBuilder.BuildCreationOptions(
                challenge,
                RelyingPartyId,
                RelyingPartyName,
                userHandle,
                string.IsNullOrWhiteSpace(userName) ? "verirandom" : userName,
                label,
                existing.Select(record => new WebAuthnCredentialDescriptor(record.CredentialId, record.Transports))
                    .ToList());

            var response = await transport.RunAsync(Fido2BrowserOperation.Create, options, CeremonyTimeout, cancellationToken)
                .ConfigureAwait(false);
            if (!response.Completed)
                return new Fido2RegistrationOutcome(MapBrowserFailure(response.Error), null, WebAuthnFailure.None,
                    response.Error);

            if (!Base64Url.TryDecode(response.ClientDataJson, out var clientDataJson) ||
                !Base64Url.TryDecode(response.AttestationObject, out var attestationObject))
                return new Fido2RegistrationOutcome(Fido2OperationStatus.BrowserFailed, null,
                    WebAuthnFailure.MalformedAttestationObject, "response_incomplete");

            var verification = WebAuthnVerifier.VerifyRegistration(
                clientDataJson, attestationObject, challenge, response.Origin, RelyingPartyId);
            if (!verification.Success || verification.Credential is null)
            {
                logger.LogWarning("FIDO2 登记校验失败：{Failure}", verification.Failure);
                return new Fido2RegistrationOutcome(Fido2OperationStatus.VerificationFailed, null,
                    verification.Failure, verification.Failure.ToString());
            }

            var credential = verification.Credential;
            var record = new Fido2CredentialRecord
            {
                Id = Guid.NewGuid().ToString("N"),
                CredentialId = Base64Url.Encode(credential.CredentialId),
                PublicKeyCose = Base64Url.Encode(credential.PublicKeyCose),
                Algorithm = credential.Algorithm,
                SignCount = credential.SignCount,
                RpId = RelyingPartyId,
                UserHandle = userHandle,
                Label = label,
                Transports = response.Transports?.ToList() ?? [],
                UserVerified = credential.UserVerified,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                LastUsedAtUtc = DateTimeOffset.UtcNow
            };

            try
            {
                store.AddOrReplace(record);
            }
            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
            {
                logger.LogError(ex, "FIDO2 凭据保存失败");
                return new Fido2RegistrationOutcome(Fido2OperationStatus.StorageFailed, null,
                    WebAuthnFailure.None, "storage_failed");
            }

            logger.LogInformation("FIDO2 安全密钥已登记：{Algorithm}", record.Algorithm);
            return new Fido2RegistrationOutcome(Fido2OperationStatus.Succeeded, ToInfo(record),
                WebAuthnFailure.None, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new Fido2RegistrationOutcome(Fido2OperationStatus.Cancelled, null, WebAuthnFailure.None, "cancelled");
        }
        finally
        {
            _ceremonyGate.Release();
        }
    }

    /// <summary>
    ///     Asks one enrolled key to sign a fresh challenge. <see cref="Fido2AssertionOutcome.CloneSuspected" /> is the
    ///     signal a caller must treat as a hard refusal: the key presented a counter that did not advance, which is
    ///     what a copied authenticator looks like.
    /// </summary>
    public Task<Fido2AssertionOutcome> AssertAsync(
        string? idOrCredentialId = null,
        CancellationToken cancellationToken = default) =>
        AssertCoreAsync(idOrCredentialId, WebAuthnOptionsBuilder.CreateChallenge(), cancellationToken);

    /// <summary>
    ///     用调用方给定的 challenge 断言。分离配置签名必须走这条重载：签名要覆盖的是配置摘要，
    ///     而摘要只能通过一个确定的 challenge 绑进断言里。
    /// </summary>
    public Task<Fido2AssertionOutcome> AssertAsync(
        string? idOrCredentialId,
        string challenge,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(challenge);
        return AssertCoreAsync(idOrCredentialId, challenge, cancellationToken);
    }

    private async Task<Fido2AssertionOutcome> AssertCoreAsync(
        string? idOrCredentialId,
        string challenge,
        CancellationToken cancellationToken)
    {
        if (!IsSupported)
            return new Fido2AssertionOutcome(Fido2OperationStatus.Unsupported, WebAuthnFailure.None, false, 0,
                "platform_unsupported");

        var record = string.IsNullOrWhiteSpace(idOrCredentialId)
            ? store.List().FirstOrDefault()
            : store.Find(idOrCredentialId);
        if (record is null)
            return new Fido2AssertionOutcome(Fido2OperationStatus.NotEnrolled, WebAuthnFailure.None, false, 0,
                "not_enrolled");

        await _ceremonyGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var options = WebAuthnOptionsBuilder.BuildRequestOptions(
                challenge,
                RelyingPartyId,
                [new WebAuthnCredentialDescriptor(record.CredentialId, record.Transports)]);

            var response = await transport.RunAsync(Fido2BrowserOperation.Get, options, CeremonyTimeout, cancellationToken)
                .ConfigureAwait(false);
            if (!response.Completed)
                return new Fido2AssertionOutcome(MapBrowserFailure(response.Error), WebAuthnFailure.None, false, 0,
                    response.Error);

            if (!Base64Url.TryDecode(response.ClientDataJson, out var clientDataJson) ||
                !Base64Url.TryDecode(response.AuthenticatorData, out var authenticatorData) ||
                !Base64Url.TryDecode(response.Signature, out var signature) ||
                !Base64Url.TryDecode(record.PublicKeyCose, out var publicKeyCose))
                return new Fido2AssertionOutcome(Fido2OperationStatus.BrowserFailed,
                    WebAuthnFailure.MalformedAuthenticatorData, false, 0, "response_incomplete");

            var verification = WebAuthnVerifier.VerifyAssertion(
                clientDataJson,
                authenticatorData,
                signature,
                publicKeyCose,
                (int)record.SignCount,
                challenge,
                response.Origin,
                RelyingPartyId);

            if (!verification.Success)
            {
                var cloneSuspected = verification.Failure == WebAuthnFailure.CounterRegression;
                if (cloneSuspected)
                    logger.LogError("FIDO2 安全密钥签名计数器未前进，疑似被复制：{CredentialId}", record.Id);
                else
                    logger.LogWarning("FIDO2 断言校验失败：{Failure}", verification.Failure);

                return new Fido2AssertionOutcome(Fido2OperationStatus.VerificationFailed, verification.Failure,
                    cloneSuspected, 0, verification.Failure.ToString());
            }

            if (verification.SignCount > record.SignCount || verification.UserVerified != record.UserVerified)
                store.RecordAssertion(record.Id, verification.SignCount, verification.UserVerified);

            return new Fido2AssertionOutcome(
                Fido2OperationStatus.Succeeded,
                WebAuthnFailure.None,
                false,
                verification.SignCount,
                null,
                new Fido2AssertionProof(
                    record.CredentialId,
                    response.ClientDataJson!,
                    response.AuthenticatorData!,
                    response.Signature!,
                    response.Origin),
                response.Origin);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new Fido2AssertionOutcome(Fido2OperationStatus.Cancelled, WebAuthnFailure.None, false, 0,
                "cancelled");
        }
        finally
        {
            _ceremonyGate.Release();
        }
    }

    public bool Remove(string idOrCredentialId) =>
        !string.IsNullOrWhiteSpace(idOrCredentialId) && store.Remove(idOrCredentialId);

    private static Fido2OperationStatus MapBrowserFailure(string? error) => error switch
    {
        "timeout" => Fido2OperationStatus.TimedOut,
        "cancelled" or "NotAllowedError" or "AbortError" => Fido2OperationStatus.Cancelled,
        "listener_unavailable" or "browser_unavailable" => Fido2OperationStatus.BrowserFailed,
        _ => Fido2OperationStatus.BrowserFailed
    };

    private static Fido2CredentialInfo ToInfo(Fido2CredentialRecord record) => new(
        record.Id,
        record.CredentialId,
        record.Label,
        record.Algorithm,
        record.SignCount,
        record.UserVerified,
        record.CreatedAtUtc,
        record.LastUsedAtUtc);
}
