using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Models.Verification;
using SecRandom.Core.Services.Config;
using SecRandom.Core.Services.Verification;
using SecRandom.Shared;
using SecRandom.Shared.Models.Verification;

namespace SecRandom.Services.Verification;

/// <summary>
///     Reserves a beacon-derived seed for one draw. The pulse and the per-pulse sequence number are the
///     only inputs, so the operator cannot inject custom entropy. The sequence anchor is persisted to
///     <c>data/proofs/beacon-state.json</c> before the seed is returned, which turns a crash or a failed
///     draw into a visible gap rather than a reused seed.
/// </summary>
public sealed class BeaconEntropyProvider(
    ExternalEntropySourceClient sourceClient,
    MainConfigHandler configHandler,
    ILogger<BeaconEntropyProvider> logger)
{
    private const string StateFileName = "beacon-state.json";

    /// <summary>
    ///     Provider family of the beacon this build fetches. It also prefixes every recorded beacon source id,
    ///     so a mixed-source draw can still be traced back to the authority that published each pulse.
    /// </summary>
    private const string BeaconProviderId = "nist-beacon-v2";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _stateLoaded;
    private string? _stateSourceId;
    private BeaconSequenceState? _state;

    /// <summary>
    ///     How long a prefetched pulse may sit unused, i.e. exactly one period. This is what makes the periodic
    ///     prefetch safe: a pulse fetched by a tick that no draw consumed is dropped rather than reused by a much
    ///     later draw, which would cross a period boundary and halve the trust score — the prefetch exists to save
    ///     a cost, never to trade one for another.
    /// </summary>
    private static readonly TimeSpan PrefetchedPulseMaxAge = TimeSpan.FromSeconds(60);

    private readonly object _prefetchGate = new();
    private (string SourceId, Uri Endpoint, Task<BeaconPulse> Pulse, DateTimeOffset FetchedAt)? _prefetched;

    /// <summary>The configured source, or the official beacon when the id is missing or unknown this build.</summary>
    private BeaconSource ResolveSource() =>
        BeaconSourceCatalog.Find(configHandler.Data.General.Verification.BeaconSourceId)
        ?? BeaconSourceCatalog.Default;

    // Only the NIST source honours the configurable endpoint; the others are addressed by their catalog entry.
    private Uri ResolveEndpoint(BeaconSource source) =>
        source.Kind == BeaconSourceKind.NistBeaconV2
            ? BeaconEndpointPolicy.Normalize(configHandler.Data.General.Verification.BeaconEndpoint)
            : new Uri(source.Endpoint);

    /// <summary>
    ///     Starts fetching the latest pulse without reserving anything, so a caller that knows a draw is about
    ///     to happen (the quick-draw rolling animation is the intended window) can hide the HTTP round trip
    ///     behind it. Deliberately does <em>not</em> touch the per-pulse sequence — only
    ///     <see cref="ReserveAsync" /> does — so a prefetch whose draw never happens consumes no sequence and
    ///     leaves no gap in the proof chain. A failed prefetch is dropped quietly: the draw fetches again and
    ///     fails loudly if the beacon is genuinely unreachable, because a draw must never fall back to a
    ///     local seed.
    /// </summary>
    public void Prefetch()
    {
        // random.org metes out a per-IP quota, so it is never prefetched: a cached fetch would spend the allowance on
        // a draw that may never happen.
        if (!IsEnabled || ResolveSource().Kind == BeaconSourceKind.RandomOrgEntropy)
            return;

        var source = ResolveSource();
        var endpoint = ResolveEndpoint(source);

        lock (_prefetchGate)
        {
            if (_prefetched is { Pulse.IsCompleted: false })
                return;

            // The endpoint is remembered with the task: an operator may switch endpoints between the
            // prefetch and the draw, and a pulse from the old endpoint must never be recorded as if it came
            // from the new one.
            _prefetched = (source.Id, endpoint,
                sourceClient.FetchLatestAsync(source, endpoint, CancellationToken.None), DateTimeOffset.UtcNow);
        }
    }

    /// <summary>
    ///     The awaited form of <see cref="Prefetch" />, for the background scheduler: it needs a result (or a failure)
    ///     to measure the round trip, which is what lets the tick stay aligned to the publication grid. Returns the
    ///     measured latency of the fetch it started.
    /// </summary>
    public async Task<TimeSpan> PrefetchAsync(CancellationToken cancellationToken)
    {
        // Same rule as Prefetch: random.org is fetched only when a draw actually needs it, never speculatively.
        if (!IsEnabled || ResolveSource().Kind == BeaconSourceKind.RandomOrgEntropy)
            return TimeSpan.Zero;

        var source = ResolveSource();
        var endpoint = ResolveEndpoint(source);

        Task<BeaconPulse> pending;
        lock (_prefetchGate)
        {
            // A prefetch that is still in flight is reused rather than replaced: two concurrent fetches for one slot
            // would only race for the same cache slot.
            if (_prefetched is { Pulse.IsCompleted: false } running)
            {
                pending = running.Pulse;
            }
            else
            {
                pending = sourceClient.FetchLatestAsync(source, endpoint, cancellationToken);
                _prefetched = (source.Id, endpoint, pending, DateTimeOffset.UtcNow);
            }
        }

        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        await pending.WaitAsync(cancellationToken).ConfigureAwait(false);
        return System.Diagnostics.Stopwatch.GetElapsedTime(started);
    }

    private async Task<BeaconPulse> TakePrefetchedPulseAsync(
        BeaconSource source,
        Uri endpoint,
        CancellationToken cancellationToken)
    {
        (string SourceId, Uri Endpoint, Task<BeaconPulse> Pulse, DateTimeOffset FetchedAt)? pending;
        lock (_prefetchGate)
        {
            pending = _prefetched;
            _prefetched = null;
        }

        // The cached pulse is only worth keeping while it still belongs to the current period, which is what lets
        // a once-a-minute prefetch serve a draw inside the same minute without ever feeding a later draw a pulse
        // from an older period.
        if (pending is { } candidate
            && string.Equals(candidate.SourceId, source.Id, StringComparison.Ordinal)
            && candidate.Endpoint == endpoint
            && DateTimeOffset.UtcNow - candidate.FetchedAt <= PrefetchedPulseMaxAge)
        {
            try
            {
                return await candidate.Pulse.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogDebug(exception, "信标脉冲预取结果不可用，抽取前重新获取。");
            }
        }

        return await sourceClient.FetchLatestAsync(source, endpoint, cancellationToken).ConfigureAwait(false);
    }

    public bool IsEnabled => configHandler.Data.General.Verification.BeaconEntropyEnabled;

    /// <summary>The source the next draw would use, after the unknown-id fallback.</summary>
    public BeaconSource CurrentSource => ResolveSource();

    /// <summary>
    ///     Fetches one pulse from <see cref="CurrentSource" /> without reserving a sequence: the settings page's
    ///     connectivity test. It goes through the same client as a real draw, so a refused host surfaces here with
    ///     the same source-specific detail instead of only failing mid-draw.
    /// </summary>
    public Task<BeaconPulse> FetchForTestAsync(CancellationToken cancellationToken)
    {
        var source = ResolveSource();
        return sourceClient.FetchLatestAsync(source, ResolveEndpoint(source), cancellationToken);
    }

    public async Task<BeaconSeedReservation> ReserveAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var source = ResolveSource();
            var endpoint = ResolveEndpoint(source);
            var pulse = await TakePrefetchedPulseAsync(source, endpoint, cancellationToken).ConfigureAwait(false);

            // A source without a public timeline (random.org) has no pulse index and no chain position, so the
            // sequence anchor has nothing to anchor to: uniqueness comes from the fetched bytes themselves and no
            // state is written, which also means such a source can never report a chain gap.
            var sequence = 0L;
            if (source.Kind != BeaconSourceKind.RandomOrgEntropy)
            {
                var state = LoadState(source);
                var resolved = BeaconSequencePolicy.ResolveNext(state, pulse, out var failure);
                if (failure != BeaconSequenceFailure.None)
                    throw new BeaconEntropyException(DescribeFailure(failure, pulse, state));

                SaveState(source, new BeaconSequenceState(pulse.PulseIndex, pulse.ChainIndex, pulse.OutputValue, resolved));
                sequence = resolved;
            }

            var seed = BeaconSeedDerivation.Derive(pulse, sequence);

            logger.LogInformation(
                "已预留信标熵：PulseIndex={PulseIndex}，ChainIndex={ChainIndex}，Sequence={Sequence}，端点={Endpoint}。",
                pulse.PulseIndex, pulse.ChainIndex, sequence, endpoint);

            return new BeaconSeedReservation(seed, new DrawProofBeacon
            {
                Provider = source.ProviderId,
                Endpoint = endpoint.ToString(),
                PulseIndex = pulse.PulseIndex,
                ChainIndex = pulse.ChainIndex,
                PeriodSeconds = pulse.PeriodSeconds,
                PulseTimeStamp = pulse.TimeStamp,
                OutputValue = pulse.OutputValue,
                SignatureValue = pulse.SignatureValue,
                CertificateId = pulse.CertificateId,
                CipherSuite = pulse.CipherSuite,
                BeaconSources = [$"{source.ProviderId}@{endpoint}"],
                Sequence = sequence,
                Derivation = BeaconSeedDerivation.DerivationId,
                // Both stay null unless the source signs its output; a reader treats null as "nothing to re-check".
                SignaturePayload = pulse.SignaturePayload,
                SignatureVerified = pulse.SignatureVerified
            });
        }
        finally
        {
            _gate.Release();
        }
    }

    private static string DescribeFailure(BeaconSequenceFailure failure, BeaconPulse pulse,
        BeaconSequenceState? state)
    {
        return failure switch
        {
            BeaconSequenceFailure.PulseIndexRegression =>
                $"信标脉冲回退：当前 {pulse.PulseIndex}，已记录 {state?.PulseIndex}。已拒绝本次抽取。",
            BeaconSequenceFailure.PulseValueConflict =>
                $"信标脉冲 {pulse.PulseIndex} 的输出值与已记录值不一致。已拒绝本次抽取。",
            _ => "信标序号状态异常。"
        };
    }

    private static string StateFilePath => Utils.GetFilePath("proofs", StateFileName);

    private BeaconSequenceState? LoadState(BeaconSource source)
    {
        if (_stateLoaded && string.Equals(_stateSourceId, source.Id, StringComparison.Ordinal))
            return _state;

        _stateLoaded = true;
        _stateSourceId = source.Id;
        var path = StateFilePath;
        if (!File.Exists(path))
            return _state = null;

        try
        {
            var document = JsonSerializer.Deserialize<BeaconSequenceStateDocument>(File.ReadAllText(path), JsonOptions);
            // The anchor belongs to one source's timeline. NIST counts pulses and drand counts rounds, so switching
            // sources legitimately moves the index in either direction; keeping the old anchor would read that as a
            // regression and refuse every draw until the new source outgrew the old number. Documents written before
            // the source was recorded can only have come from NIST, which is what they are treated as.
            var recordedSource = string.IsNullOrEmpty(document?.SourceId)
                ? BeaconSourceCatalog.NistBeaconV2.Id
                : document!.SourceId;

            _state = document is null || document.PulseIndex < 0 || document.Sequence < 0
                     || !string.Equals(recordedSource, source.Id, StringComparison.Ordinal)
                ? null
                : new BeaconSequenceState(document.PulseIndex, document.ChainIndex, document.OutputValue ?? string.Empty,
                    document.Sequence);
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            // A corrupt anchor cannot be trusted to prevent reuse, so it is treated as absent: the next
            // draw restarts at sequence 0 and the resulting duplicate becomes visible in the proof chain.
            logger.LogWarning(exception, "读取信标序号状态失败，将从 0 重新开始记录。");
            _state = null;
        }

        return _state;
    }

    private void SaveState(BeaconSource source, BeaconSequenceState state)
    {
        var path = StateFilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = path + $".{Guid.NewGuid():N}.tmp";
        var document = new BeaconSequenceStateDocument
        {
            SourceId = source.Id,
            PulseIndex = state.PulseIndex,
            ChainIndex = state.ChainIndex,
            OutputValue = state.OutputValue,
            Sequence = state.Sequence
        };
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(document, JsonOptions));
        File.Move(temporaryPath, path, true);
        _state = state;
        _stateSourceId = source.Id;
        _stateLoaded = true;
    }

    private sealed class BeaconSequenceStateDocument
    {
        /// <summary>Which source's timeline the anchor belongs to; empty in documents written before it existed.</summary>
        public string? SourceId { get; set; }

        public long PulseIndex { get; set; } = -1;
        public long ChainIndex { get; set; }
        public string OutputValue { get; set; } = string.Empty;
        public long Sequence { get; set; } = -1;
    }
}
