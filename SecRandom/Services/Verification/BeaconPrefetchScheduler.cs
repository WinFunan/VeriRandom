using System;
using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Services.Config;

namespace SecRandom.Services.Verification;

/// <summary>
///     Keeps a fresh beacon pulse in <see cref="BeaconEntropyProvider" />'s single-pulse cache while the periodic
///     prefetch switch is on, so a draw that starts inside the tick interval consumes it with no network wait.
/// </summary>
/// <remarks>
///     <para>
///         Ticks are aligned to the publication grid instead of drifting from process start: every source publishes
///         on a UTC minute boundary (NIST once a minute, drand every 3 s, which divides a minute exactly), so fetching
///         shortly after a boundary is what makes the cached pulse the *current* period's rather than the previous
///         one. A free-running 60-second loop would walk its phase across every period over a day.
///     </para>
///     <para>
///         The offset after the boundary starts at <see cref="BaseOffset" /> and adapts to the measured round trip:
///         a slow network needs its request to start earlier relative to the boundary to still land in the same
///         period, so the offset settles at about the last latency plus a margin, inside
///         <see cref="MinOffset" />..<see cref="MaxOffset" />. A fast network keeps it as late as possible, which is
///         what keeps the pulse freshest.
///     </para>
///     <para>
///         A successful tick writes nothing: the fetch is an ordinary background refresh and logging every one of
///         them only buries the lines that matter. Failures are logged once per tick, at debug level, because the
///         draw itself reports its own failure with the source and host named.
///     </para>
/// </remarks>
public sealed class BeaconPrefetchScheduler(
    MainConfigHandler configHandler,
    BeaconEntropyProvider beaconEntropyProvider,
    ILogger<BeaconPrefetchScheduler> logger) : BackgroundService
{
    /// <summary>First fetch this far after the minute boundary: the authority has published, the pulse is fresh.</summary>
    private static readonly TimeSpan BaseOffset = TimeSpan.FromSeconds(5);

    private static readonly TimeSpan MinOffset = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MaxOffset = TimeSpan.FromSeconds(20);

    /// <summary>How much later than the measured round trip the fetch should start.</summary>
    private static readonly TimeSpan LatencyMargin = TimeSpan.FromSeconds(2);

    private TimeSpan _offset = BaseOffset;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DelayUntilNextSlotAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            try
            {
                if (IsAutoPrefetchEnabled())
                {
                    var latency = await beaconEntropyProvider.PrefetchAsync(stoppingToken).ConfigureAwait(false);
                    AdoptOffset(latency);
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogDebug(exception, "信标脉冲自动预取失败，下个周期重试。");
            }
        }
    }

    /// <summary>
    ///     Sleeps until the next aligned slot — the coming minute boundary plus the current offset — so an attempt is
    ///     never scheduled *before* the pulse it hopes to fetch can exist.
    /// </summary>
    private async Task DelayUntilNextSlotAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var minuteStart = new DateTimeOffset(
            now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, TimeSpan.Zero);
        var slot = minuteStart.Add(_offset);
        while (slot <= now.AddMilliseconds(250))
            slot = slot.AddMinutes(1);

        await Task.Delay(slot - now, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Settles the offset on "one measured round trip late", clamped so neither extreme breaks freshness.</summary>
    private void AdoptOffset(TimeSpan latency)
    {
        if (latency <= TimeSpan.Zero)
            return;

        var target = latency + LatencyMargin;
        target = target < MinOffset ? MinOffset : target > MaxOffset ? MaxOffset : target;

        // Move at most a second per tick so one slow response cannot swing the schedule.
        var delta = target - _offset;
        if (delta > TimeSpan.FromSeconds(1))
            delta = TimeSpan.FromSeconds(1);
        else if (delta < TimeSpan.FromSeconds(-1))
            delta = TimeSpan.FromSeconds(-1);

        _offset += delta;
    }

    // Both switches are required: the periodic prefetch is the background half of the same feature, so enabling
    // it alone must not start fetching pulses for a user who turned the feature off.
    private bool IsAutoPrefetchEnabled() =>
        configHandler.Data.General.Verification.BeaconPrefetchEnabled &&
        configHandler.Data.General.Verification.BeaconAutoPrefetchEnabled;
}
