using System;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SecRandom.Core;
using SecRandom.Core.Models.Verification;
using SecRandom.Core.Services.Verification;

namespace SecRandom.Services.Verification;

/// <summary>
///     Fetches the pulse a draw derives its seed from, for whichever source the user selected.
/// </summary>
/// <remarks>
///     <para>
///         Only the NIST branch is <em>verified</em> at fetch time, and that verification lives in
///         <see cref="NistBeaconClient" />. A drand round carries a BLS signature this build records but cannot check
///         — verifying it needs BLS12-381 pairing, i.e. a third-party library the project does not take — so a drand
///         pulse is trustworthy only <em>after</em> someone re-checks the recorded round, randomness and signature
///         against the chain key embedded in <see cref="BeaconSourceCatalog.DrandQuicknet" />. That is the same
///         "declare now, verify later" stance the reference (Own) chain takes.
///     </para>
///     <para>
///         random.org publishes nothing to verify at all: no timeline, no signature, no index. Its bytes are recorded
///         so the draw stays reproducible, not so they can be proven — an operator can fetch many blocks and keep the
///         one they like, exactly as with a local CSPRNG. What it buys is atmospheric-noise randomness instead of a
///         deterministic local generator.
///     </para>
///     <para>
///         Every failure is reported as <see cref="BeaconEntropyException" /> naming the source, the HTTP status and
///         the host, because the alternative — a bare transport exception inside a draw — reaches the classroom as
///         "抽取失败" with nothing to act on. Both non-NIST sources are reachable only where the network allows them,
///         so the message says which host was refused.
///     </para>
/// </remarks>
public sealed class ExternalEntropySourceClient(
    INistBeaconClient nistClient,
    RandomOrgCredentialStore randomOrgCredentials,
    RandomOrgApiClient randomOrgApi,
    IHttpClientFactory httpClientFactory,
    ILogger<ExternalEntropySourceClient> logger)
{
    /// <summary>random.org answers one decimal byte per line; 32 of them make the 256-bit output a source publishes.</summary>
    private const int RandomOrgByteCount = 32;

    /// <summary>Fetches are on the draw's critical path, so they get a short bound instead of the client default.</summary>
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(6);

    /// <summary>
    ///     drand publishes the same chain from several front ends. The first is the catalogued one; the second is kept
    ///     because a single host being unreachable (or filtered by a local network) must not take the source down
    ///     while an equivalent front end answers normally.
    /// </summary>
    private static readonly string[] DrandHosts = ["https://api.drand.sh", "https://drand.cloudflare.com"];

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<BeaconPulse> FetchLatestAsync(
        BeaconSource source,
        Uri endpoint,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(endpoint);

        return source.Kind switch
        {
            BeaconSourceKind.NistBeaconV2 => await nistClient
                .GetLatestPulseAsync(endpoint, cancellationToken).ConfigureAwait(false),
            BeaconSourceKind.DrandChain => await FetchDrandAsync(source, cancellationToken).ConfigureAwait(false),
            _ => await FetchRandomOrgAsync(source, endpoint, cancellationToken).ConfigureAwait(false)
        };
    }

    private async Task<BeaconPulse> FetchDrandAsync(BeaconSource source, CancellationToken cancellationToken)
    {
        Exception? lastFailure = null;
        foreach (var host in DrandHosts)
        {
            var requestUri = new Uri($"{host.TrimEnd('/')}/{source.ChainHash}/public/latest");
            try
            {
                var body = await GetStringAsync(source, requestUri, "轮次", cancellationToken).ConfigureAwait(false);
                var round = JsonSerializer.Deserialize<DrandRound>(body, JsonOptions)
                    ?? throw new InvalidDataException("drand returned an empty round.");

                if (round.Round <= 0 || string.IsNullOrWhiteSpace(round.Randomness)
                    || string.IsNullOrWhiteSpace(round.Signature))
                    throw new InvalidDataException("drand round did not include a round number, randomness and signature.");

                // Validates the published output shape up front; a malformed round must never reach the seed derivation.
                _ = BeaconSeedDerivation.DecodeOutputValue(round.Randomness);

                var publishedAt = BeaconSourceCatalog.RoundTime(source, round.Round)
                    ?? throw new InvalidDataException($"drand round {round.Round} has no publication time.");

                logger.LogTrace("已获取 drand 轮次：Round={Round}，主机={Host}，链哈希={ChainHash}。",
                    round.Round, requestUri.Host, source.ChainHash);

                // The chain hash rides in the certificate slot: it answers the same "which authority published this"
                // question and the proof has no other free field for it. drand is unchained, so there is no chain index.
                return new BeaconPulse(
                    round.Round,
                    0,
                    source.PeriodSeconds,
                    publishedAt,
                    round.Randomness,
                    round.Signature,
                    source.ChainHash);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                lastFailure = exception;
                logger.LogDebug(exception, "drand 主机 {Host} 取回轮次失败，尝试下一个前端。", requestUri.Host);
            }
        }

        throw new BeaconEntropyException(
            $"熵源 {source.ProviderId} 的所有前端都取回失败：{lastFailure?.Message}", lastFailure!);
    }

    private async Task<BeaconPulse> FetchRandomOrgAsync(
        BeaconSource source,
        Uri endpoint,
        CancellationToken cancellationToken)
    {
        // With a key, ask for the signed form: the response carries a signature and a serial number, which ride into the
        // proof so the origin can be re-checked later. Without a key the plain unsigned endpoint is used, and the proof
        // simply records that there is nothing to check.
        if (randomOrgCredentials.Load() is { } apiKey)
        {
            var signed = await randomOrgApi
                .GenerateSignedBytesAsync(apiKey, RandomOrgByteCount, cancellationToken).ConfigureAwait(false);
            var signedHex = ToHex(signed.Bytes);
            _ = BeaconSeedDerivation.DecodeOutputValue(signedHex);

            logger.LogTrace("已获取 random.org 签名字节 {Count} 个，序列号 {Serial}。",
                RandomOrgByteCount, signed.SerialNumber);

            // The serial number rides in the certificate slot for the same reason drand's chain hash does: it answers
            // "which signed statement is this", and the proof has no other free field for it.
            return new BeaconPulse(
                0,
                0,
                0,
                DateTimeOffset.UtcNow,
                signedHex,
                signed.Signature,
                signed.SerialNumber.ToString(CultureInfo.InvariantCulture),
                -1,
                signed.RawRandomJson,
                signed.SignatureVerified);
        }

        var requestUri = new Uri(
            endpoint,
            $"integers/?num={RandomOrgByteCount}&min=0&max=255&col=1&base=10&format=plain&rnd=new");
        var text = await GetStringAsync(source, requestUri, "随机字节", cancellationToken).ConfigureAwait(false);

        var bytes = new byte[RandomOrgByteCount];
        var count = 0;
        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
                continue;

            if (count == RandomOrgByteCount
                || !byte.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
                throw new BeaconEntropyException(
                    $"熵源 {source.ProviderId} 返回了无法解析的内容：{Describe(text)}");

            bytes[count++] = value;
        }

        if (count != RandomOrgByteCount)
            throw new BeaconEntropyException(
                $"熵源 {source.ProviderId} 只返回了 {count} 个字节，需要 {RandomOrgByteCount} 个：{Describe(text)}");

        var builder = new StringBuilder(RandomOrgByteCount * 2);
        foreach (var value in bytes)
            builder.Append(value.ToString("x2", CultureInfo.InvariantCulture));

        var output = builder.ToString();
        _ = BeaconSeedDerivation.DecodeOutputValue(output);

        logger.LogTrace("已获取 random.org 随机字节 {Count} 个。", count);

        // No round and no signature exist here, so the index stays 0 and the fetched bytes are the whole identity.
        return new BeaconPulse(0, 0, 0, DateTimeOffset.UtcNow, output, string.Empty, "random.org");
    }

    /// <summary>
    ///     One GET with the headers these services expect. random.org refuses or throttles clients it cannot identify,
    ///     so both requests carry an explicit user agent naming this build.
    /// </summary>
    private async Task<string> GetStringAsync(
        BeaconSource source,
        Uri requestUri,
        string what,
        CancellationToken cancellationToken)
    {
        using var client = httpClientFactory.CreateClient(nameof(ExternalEntropySourceClient));
        client.Timeout = RequestTimeout;

        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        request.Headers.UserAgent.ParseAdd($"VeriRandom/{GlobalConstants.Version.TrimStart('v', 'V')}");
        request.Headers.Accept.ParseAdd("*/*");

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            throw new BeaconEntropyException(
                $"熵源 {source.ProviderId} 的{what}请求无法到达 {requestUri.Host}：{exception.Message}。" +
                "该地址可能被当前网络阻断（random.org 与部分 drand 前端在受限网络下不可达）", exception);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
                return body;

            var hint = (int)response.StatusCode == 503
                ? "，random.org 的免费配额按来源 IP 限流，稍后重试或换回 NIST 信标"
                : string.Empty;
            throw new BeaconEntropyException(
                $"熵源 {source.ProviderId} 的{what}请求失败：HTTP {(int)response.StatusCode}{hint}，来自 {requestUri.Host}。" +
                $"响应：{Describe(body)}");
        }
    }

    private static string ToHex(byte[] bytes)
    {
        var builder = new StringBuilder(bytes.Length * 2);
        foreach (var value in bytes)
            builder.Append(value.ToString("x2", CultureInfo.InvariantCulture));

        return builder.ToString();
    }

    private static string Describe(string body)
    {
        var trimmed = body.Trim();
        if (trimmed.Length == 0)
            return "（空响应）";

        return trimmed.Length <= 200 ? trimmed : trimmed[..200] + "…";
    }

    private sealed record DrandRound(long Round, string Randomness, string Signature);
}
