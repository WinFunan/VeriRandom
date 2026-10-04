using System;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Models.Verification;
using SecRandom.Core.Services.Verification;

namespace SecRandom.Services.Verification;

/// <summary>
///     Thin NIST Beacon v2 transport. It reads the published pulse and validates its shape; the pulse
///     signature is recorded verbatim so an external verifier can check it against the authority's
///     certificate independently of the endpoint this client used.
/// </summary>
public sealed class NistBeaconClient(HttpClient httpClient, ILogger<NistBeaconClient> logger) : INistBeaconClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<BeaconPulse> GetLatestPulseAsync(Uri endpoint, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        var requestUri = new Uri(endpoint, "pulse/last");
        using var response = await httpClient.GetAsync(requestUri, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new HttpRequestException(
                $"Beacon request failed with {(int)response.StatusCode}: {error}", null, response.StatusCode);
        }

        var envelope = await response.Content
            .ReadFromJsonAsync<NistBeaconEnvelope>(JsonOptions, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidDataException("Beacon service returned an empty response.");
        var pulse = envelope.Pulse
            ?? throw new InvalidDataException("Beacon response did not contain a pulse.");

        if (pulse.PulseIndex <= 0)
            throw new InvalidDataException($"Beacon pulse index {pulse.PulseIndex} is invalid.");

        // Validates the 512-bit output shape up front; a malformed pulse must never reach the seed derivation.
        _ = BeaconSeedDerivation.DecodeOutputValue(pulse.OutputValue);

        if (string.IsNullOrWhiteSpace(pulse.SignatureValue))
            throw new InvalidDataException("Beacon pulse did not include a signature value.");

        if (!DateTimeOffset.TryParse(pulse.TimeStamp, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind,
                out var timeStamp))
            throw new InvalidDataException("Beacon pulse did not include a parseable timestamp.");

        logger.LogDebug(
            "已获取信标脉冲：PulseIndex={PulseIndex}，ChainIndex={ChainIndex}，证书={CertificateId}。",
            pulse.PulseIndex, pulse.ChainIndex, pulse.CertificateId);

        return new BeaconPulse(
            pulse.PulseIndex,
            pulse.ChainIndex,
            pulse.Period,
            timeStamp,
            pulse.OutputValue,
            pulse.SignatureValue,
            pulse.CertificateId,
            pulse.CipherSuite);
    }

    private sealed class NistBeaconEnvelope
    {
        [JsonPropertyName("pulse")]
        public NistBeaconPulsePayload? Pulse { get; init; }
    }

    private sealed class NistBeaconPulsePayload
    {
        [JsonPropertyName("version")]
        public string? Version { get; init; }

        [JsonPropertyName("cipherSuite")]
        public int CipherSuite { get; init; }

        [JsonPropertyName("period")]
        public int Period { get; init; }

        [JsonPropertyName("certificateId")]
        public string CertificateId { get; init; } = string.Empty;

        [JsonPropertyName("chainIndex")]
        public long ChainIndex { get; init; }

        [JsonPropertyName("pulseIndex")]
        public long PulseIndex { get; init; }

        [JsonPropertyName("timeStamp")]
        public string TimeStamp { get; init; } = string.Empty;

        [JsonPropertyName("outputValue")]
        public string OutputValue { get; init; } = string.Empty;

        [JsonPropertyName("signatureValue")]
        public string SignatureValue { get; init; } = string.Empty;
    }
}
