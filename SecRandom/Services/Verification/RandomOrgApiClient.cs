using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace SecRandom.Services.Verification;

/// <summary>Quota reported by random.org for one API key.</summary>
public sealed record RandomOrgUsage(int BitsLeft, int RequestsLeft);

/// <summary>The signed form of one random.org draw: the bytes plus the material that proves their origin.</summary>
/// <param name="Bytes">The random bytes themselves.</param>
/// <param name="Signature">random.org's signature over the response, recorded verbatim so a verifier can re-check it.</param>
/// <param name="SerialNumber">random.org's serial number for this response; it identifies the signed statement.</param>
public sealed record RandomOrgSignedBytes(
    byte[] Bytes,
    string Signature,
    long SerialNumber,
    string RawRandomJson,
    bool SignatureVerified);

/// <summary>Raised when the key itself is rejected, as opposed to the request being unreadable.</summary>
public sealed class RandomOrgKeyRejectedException(string message) : Exception(message);

/// <summary>
///     Minimal client for random.org's JSON-RPC API, used to check that a pasted key actually works before it is
///     stored. It deliberately does not fetch random numbers: the draw path keeps using the plain endpoint until the
///     signed path is wired, and a validation request costs one request out of the key's quota rather than touching
///     any draw.
/// </summary>
public sealed class RandomOrgApiClient(IHttpClientFactory httpClientFactory, ILogger<RandomOrgApiClient> logger)
{
    private const string Endpoint = "https://api.random.org/json-rpc/4/invoke";
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    ///     The usage numbers for <paramref name="apiKey" />. A rejected key throws
    ///     <see cref="RandomOrgKeyRejectedException" /> with random.org's own message; anything unreadable throws.
    /// </summary>
    public async Task<RandomOrgUsage> GetUsageAsync(string apiKey, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);

        var request = new JsonRpcRequest("getUsage", new GetUsageParameters(apiKey.Trim()), 1);
        using var client = httpClientFactory.CreateClient(nameof(RandomOrgApiClient));
        client.Timeout = RequestTimeout;

        using var response = await client
            .PostAsJsonAsync(Endpoint, request, JsonOptions, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"random.org 返回 HTTP {(int)response.StatusCode}：{Trim(body)}", null, response.StatusCode);

        var envelope = JsonSerializer.Deserialize<JsonRpcResponse>(body, JsonOptions)
            ?? throw new InvalidOperationException("random.org 返回了空响应。");

        if (envelope.Error is { } error)
        {
            // -10xx is random.org's range for "key is bad/expired": that is the answer the user needs to see, and it
            // must not be reported as "network problem".
            throw new RandomOrgKeyRejectedException($"random.org 拒绝了该密钥（{error.Code}）：{error.Message}");
        }

        var usage = envelope.Result?.Usage
            ?? throw new InvalidOperationException("random.org 的响应里没有用量信息。");

        logger.LogInformation(
            "random.org 密钥校验通过：剩余位数={BitsLeft}，剩余请求={RequestsLeft}。",
            usage.BitsLeft, usage.RequestsLeft);

        return new RandomOrgUsage(usage.BitsLeft, usage.RequestsLeft);
    }

    private static string Trim(string body)
    {
        var trimmed = body.Trim();
        return trimmed.Length <= 200 ? trimmed : trimmed[..200] + "…";
    }

    /// <summary>
    ///     Asks random.org for <paramref name="count" /> signed random bytes and returns them with the signature.
    /// </summary>
    /// <remarks>
    ///     This is the only "signed" call this build makes: the signature and the serial number are recorded verbatim in
    ///     the proof so the response can be checked later against random.org's published signature key. Verifying it
    ///     here would need that key and the exact byte layout random.org signs; neither is embedded in this repository,
    ///     so a local check would either be wrong or pretend — the proof keeps the material instead.
    /// </remarks>
    public async Task<RandomOrgSignedBytes> GenerateSignedBytesAsync(
        string apiKey,
        int count,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);

        var request = new JsonRpcRequest(
            "generateSignedIntegers",
            new GenerateSignedIntegersParameters(apiKey.Trim(), count, 0, 255, true),
            1);
        using var client = httpClientFactory.CreateClient(nameof(RandomOrgApiClient));
        client.Timeout = RequestTimeout;

        using var response = await client
            .PostAsJsonAsync(Endpoint, request, JsonOptions, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"random.org 返回 HTTP {(int)response.StatusCode}：{Trim(body)}", null, response.StatusCode);

        var envelope = JsonSerializer.Deserialize<SignedIntegersResponse>(body, JsonOptions)
            ?? throw new InvalidOperationException("random.org 返回了空响应。");

        if (envelope.Error is { } error)
            throw new RandomOrgKeyRejectedException($"random.org 拒绝了该请求（{error.Code}）：{error.Message}");

        var random = envelope.Result?.Random
            ?? throw new InvalidOperationException("random.org 的响应里没有随机数。");
        if (random.Data.Length != count)
            throw new InvalidOperationException($"random.org 返回了 {random.Data.Length} 个数，需要 {count} 个。");
        if (string.IsNullOrWhiteSpace(envelope.Result!.Signature))
            throw new InvalidOperationException("random.org 的响应里没有签名。");

        var bytes = new byte[count];
        for (var index = 0; index < count; index++)
            bytes[index] = (byte)random.Data[index];

        var rawRandomJson = string.Empty;
        var signatureVerified = false;

        // Verify before the bytes are allowed to seed a draw. A pin or certificate problem is "cannot vouch", not
        // "forged", so it only downgrades the claim; the verdict is logged either way and both the signature and the
        // payload it covers are recorded in the proof so a third party can re-check it independently.
        using (var document = JsonDocument.Parse(body))
        {
            rawRandomJson = document.RootElement.GetProperty("result").GetProperty("random").GetRawText();
            var check = SecRandom.Core.Abstraction.IAppHost
                .TryGetService<RandomOrgSignatureVerifier>()?
                .Verify(rawRandomJson, envelope.Result.Signature);

            signatureVerified = check is { IsValid: true };
            if (signatureVerified)
                logger.LogInformation("random.org 签名校验通过（证书过期：{Expired}）。", check!.CertificateExpired);
            else
                logger.LogWarning("random.org 签名未能验证，本次熵源按未验证处理。");
        }

        logger.LogInformation("已获取 random.org 签名随机字节 {Count} 个，序列号 {Serial}。",
            count, random.SerialNumber);

        return new RandomOrgSignedBytes(bytes, envelope.Result.Signature, random.SerialNumber,
            rawRandomJson, signatureVerified);
    }

    private sealed record JsonRpcRequest(string Method, object Params, int Id)
    {
        // JSON-RPC requires the member to be exactly "jsonrpc". The serializer's camelCase policy would emit
        // "jsonRpc", which the service rejects outright with -32600 Invalid Request.
        [System.Text.Json.Serialization.JsonPropertyName("jsonrpc")]
        public string JsonRpc { get; init; } = "2.0";
    }

    private sealed record GetUsageParameters(string ApiKey);

    private sealed record GenerateSignedIntegersParameters(
        string ApiKey,
        int N,
        int Min,
        int Max,
        bool Replacement);

    private sealed record JsonRpcResponse(JsonRpcError? Error, JsonRpcResult? Result);

    private sealed record JsonRpcError(int Code, string Message);

    // getUsage returns bitsLeft/requestsLeft directly under result (there is no "usage" object); the computed
    // property keeps the call site reading as a usage payload without inventing a nesting the service does not send.
    private sealed record JsonRpcResult(int BitsLeft, int RequestsLeft)
    {
        public RandomOrgUsagePayload Usage => new(BitsLeft, RequestsLeft);
    }

    private sealed record SignedIntegersResponse(JsonRpcError? Error, SignedIntegersResult? Result);

    private sealed record SignedIntegersResult(SignedRandom Random, string Signature);

    private sealed record SignedRandom(long[] Data, long SerialNumber);

    private sealed record RandomOrgUsagePayload(int BitsLeft, int RequestsLeft);
}
