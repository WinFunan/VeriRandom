using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Models.Linkage;
using SecRandom.Core.Services.Config;
using SecRandom.Services.Linkage;

namespace SecRandom.Services.SlideTrigger;

/// <summary>
///     Rolls the courseware-marker session when a lesson starts, so a deck reused all term fires its markers again
///     without the teacher remembering to press "start a new lesson".
/// </summary>
/// <remarks>
///     <para>
///         It rides on the course linkage (ClassIsland or CSES) rather than a wall clock, because only a real schedule
///         knows when a lesson begins. If the linkage is off or unavailable nothing happens: this is an arming
///         convenience, never a gate — no detection and no draw depends on it.
///     </para>
///     <para>
///         <b>Joining a lesson mid-way never rolls.</b> The first available snapshot after startup only seeds the lesson
///         identity. Otherwise launching the app twenty minutes into a lesson would re-arm markers that already fired in
///         that lesson, and a slide the class has already seen would fire a second draw. The price is that a lesson which
///         starts while the app is not running keeps the previous session — the safe direction, since a marker staying
///         consumed is recoverable while a duplicate draw is not.
///     </para>
/// </remarks>
public sealed class SlideTriggerClassSessionService : IHostedService
{
    private readonly CourseLinkageService _linkage;
    private readonly SlideTriggerStore _store;
    private readonly MainConfigHandler _configHandler;
    private readonly ILogger<SlideTriggerClassSessionService> _logger;

    private readonly object _gate = new();

    /// <summary>Lesson identity of the last observed class period; null when the last observation was outside class.</summary>
    private string? _lessonKey;

    /// <summary>True until the first available snapshot is processed, which only records the starting point.</summary>
    private bool _seedOnly = true;

    public SlideTriggerClassSessionService(
        CourseLinkageService linkage,
        SlideTriggerStore store,
        MainConfigHandler configHandler,
        ILogger<SlideTriggerClassSessionService> logger)
    {
        _linkage = linkage;
        _store = store;
        _configHandler = configHandler;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _linkage.StateChanged += OnStateChanged;

        // Seed from what the linkage already knows instead of waiting for the next change. Hosted services start in
        // registration order, so an earlier refresh would otherwise be invisible here and the first lesson of the run
        // would be mistaken for the baseline — its markers would never be re-armed.
        Observe(_linkage.Snapshot);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _linkage.StateChanged -= OnStateChanged;
        return Task.CompletedTask;
    }

    private void OnStateChanged(object? sender, EventArgs e) => Observe(_linkage.Snapshot);

    private void Observe(CourseScheduleSnapshot snapshot)
    {
        if (!snapshot.IsAvailable)
            return;

        // A lesson is identified by course plus start time: two consecutive periods of the same subject are different
        // lessons, while an unchanged period must not re-arm anything.
        var key = snapshot.State == CourseTimeState.OnClass && snapshot.CurrentCourse is { } course
            ? $"{course.Name}|{course.StartTime:HH:mm}"
            : null;

        lock (_gate)
        {
            if (_seedOnly)
            {
                _seedOnly = false;
                _lessonKey = key;
                return;
            }

            if (key is null || string.Equals(_lessonKey, key, StringComparison.Ordinal))
            {
                // Outside class, or still the same period. Not updating on "outside class" keeps a spurious break
                // notification inside one lesson from re-arming markers when it ends.
                return;
            }

            _lessonKey = key;
        }

        if (!_configHandler.Data.MoreSettings.SlideTriggerAutoSessionOnClassStart)
            return;

        // Nothing to roll for an empty library, and rolling would still write the catalog file.
        if (_store.Triggers.Count == 0)
            return;

        var session = _store.StartNewSession();
        _logger.LogInformation(
            "课程开始（{Course}），已自动开始新的课件标记会话：{Session}",
            snapshot.CurrentCourse?.Name, session);
    }
}
