using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Models.SlideTrigger;
using SecRandom.Services.Draw;
using SecRandom.Services.Linkage;
using SecRandom.Services.Security;

namespace SecRandom.Services.SlideTrigger;

/// <summary>Why a detected marker did or did not turn into a draw.</summary>
public enum SlideTriggerExecutionCode
{
    Succeeded,
    /// <summary>The draw gate refused it: remote lock, class time, or a verification only a person could pass.</summary>
    RefusedByGate,
    /// <summary>The marker asks for something the shared draw request cannot express yet.</summary>
    UnsupportedParameters,
    DrawFailed
}

public sealed record SlideTriggerExecution(
    SlideTriggerExecutionCode Code,
    int DrawnCount,
    LinkageDrawGate Gate = LinkageDrawGate.Allowed);

/// <summary>
///     Turns a decoded marker into a real draw, through the same gates and the same service a teacher's click uses.
/// </summary>
/// <remarks>
///     <para>
///         The gate is checked <em>before</em> authorizing, exactly like the control node's remote draw: a marker that
///         would raise a password prompt must be refused instead of executed, because an unattended classroom machine
///         cannot answer one and the trigger would hang until someone walks over.
///     </para>
///     <para>
///         Repeat control and history weighting are <b>refused rather than ignored</b>. The shared request carries only
///         list, group, gender and count, so a marker asking for "allow repeats" or "ignore history weights" cannot be
///         honoured through it — and silently drawing with the global settings while the marker claims otherwise is the
///         kind of quiet mismatch this project exists to avoid.
///     </para>
/// </remarks>
public sealed class SlideTriggerExecutor(
    LinkageDrawCoordinator coordinator,
    RollCallDrawService drawService,
    ILogger<SlideTriggerExecutor> logger)
{
    public async Task<SlideTriggerExecution> ExecuteAsync(
        SlideTriggerParameters parameters,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        if (parameters.AllowRepeat || parameters.IgnoreHistoryWeights)
        {
            logger.LogWarning(
                "课件标记要求「可重复」或「忽略历史权重」，当前抽取路径无法表达，已跳过本次触发");

            return new SlideTriggerExecution(SlideTriggerExecutionCode.UnsupportedParameters, 0);
        }

        var gate = coordinator.EvaluateGate(SecurityOperation.RollCallStart);
        if (gate != LinkageDrawGate.Allowed)
        {
            logger.LogInformation("课件标记触发被闸门拒绝：{Gate}", gate);
            return new SlideTriggerExecution(SlideTriggerExecutionCode.RefusedByGate, 0, gate);
        }

        RollCallDrawResult? result = null;

        var authorized = await coordinator.AuthorizeAsync(
            SecurityOperation.RollCallStart,
            async () => result = await drawService
                .DrawAsync(
                    new RollCallDrawRequest(
                        parameters.StudentListName ?? string.Empty,
                        parameters.Group ?? string.Empty,
                        parameters.Gender ?? string.Empty,
                        parameters.Count,
                        string.Empty),
                    cancellationToken)
                .ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);

        if (!authorized)
            return new SlideTriggerExecution(SlideTriggerExecutionCode.RefusedByGate, 0, gate);

        if (result is null)
            return new SlideTriggerExecution(SlideTriggerExecutionCode.DrawFailed, 0);

        return new SlideTriggerExecution(
            SlideTriggerExecutionCode.Succeeded,
            result.Students.Count);
    }
}
