using System;
using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SecRandom.Shared;

namespace SecRandom.Services.Consent;

/// <summary>
///     The device's own "send nothing to SECTL" master switch.
/// </summary>
/// <remarks>
///     <para>
///         It is deliberately <b>not</b> part of <c>settings.json</c>. Every other privacy control is an ordinary
///         preference, and a settings import or restore is allowed to rewrite those; this one is a refusal, so an
///         imported archive must not be able to lift it. It lives in its own file under <c>data/config/privacy</c>,
///         like the control-node switch lives in <c>data/config/control</c>.
///     </para>
///     <para>
///         The switch does not delete or rewrite the consent records: clearing an acknowledgement the user may have
///         signed would destroy evidence of what they agreed to. It overrides them instead, so the answer to "may
///         anything leave for SECTL" becomes no while the record of what was once agreed stays intact.
///     </para>
///     <para>
///         <see cref="ForcedDisabled" /> is the build-level lock that ships this refusal pinned on: while it is true the
///         effective answer is always "blocked", the privacy page renders the control read-only, and a stored
///         preference is kept but not applied. It is a single constant on purpose — releasing the lock later is one
///         edit here (set it to <c>false</c>) plus the page follows automatically, because the page and the policy both
///         ask <see cref="IsDisabled" /> instead of reading the file themselves.
///     </para>
/// </remarks>
public sealed class SectlEgressSwitch(ILogger<SectlEgressSwitch> logger)
{
    /// <summary>
    ///     Build-level lock: while true, every SECTL connection is refused and the user cannot lift it from the UI.
    ///     Set to <c>false</c> to hand the choice back; the stored preference written by earlier builds is preserved.
    /// </summary>
    public const bool ForcedDisabled = true;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly object _gate = new();
    private bool? _storedDisabled;

    /// <summary>Raised after the switch changes, so open surfaces can refresh their enabled state.</summary>
    public event EventHandler? Changed;

    /// <summary>True while this build pins the switch on, so a surface can render it read-only.</summary>
    public bool IsForced => ForcedDisabled;

    /// <summary>True when the user has forbidden every SECTL connection, or when this build forces it.</summary>
    public bool IsDisabled
    {
        get
        {
            if (ForcedDisabled)
                return true;

            lock (_gate)
                return _storedDisabled ??= Load();
        }
    }

    /// <summary>
    ///     Stores the user's own preference. While <see cref="ForcedDisabled" /> is on, the value is recorded so a later
    ///     build can honour it, but it does not change the effective answer.
    /// </summary>
    public void SetDisabled(bool disabled)
    {
        lock (_gate)
        {
            if (_storedDisabled == disabled)
                return;

            _storedDisabled = disabled;
            Save(disabled);
        }

        if (ForcedDisabled)
        {
            logger.LogWarning(
                "SECTL 联网总开关在当前版本被固定为禁用，已记录偏好 {State} 但不会生效，直到强制状态解除。",
                disabled ? "已禁用" : "已允许");
            return;
        }

        logger.LogInformation("SECTL 联网总开关：{State}", disabled ? "已禁用" : "已允许");
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static bool Load()
    {
        var path = StorePath;
        if (!File.Exists(path))
            return true;

        try
        {
            var state = JsonSerializer.Deserialize<EgressState>(File.ReadAllText(path), JsonOptions);
            return state?.Disabled ?? true;
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            // A damaged or unreadable refusal file must fail closed: the alternative is silently resuming traffic the
            // user may have forbidden, which is the one mistake this switch exists to prevent.
            return true;
        }
    }

    private static void Save(bool disabled)
    {
        var path = StorePath;
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(new EgressState { Disabled = disabled }, JsonOptions));
        File.Move(temporary, path, true);
    }

    private static string StorePath => Utils.GetFilePath("config", "privacy", "sectl-egress.json");

    /// <summary>On-disk shape; a hand-edited or older file falls back to the property default.</summary>
    private sealed class EgressState
    {
        public bool Disabled { get; init; }
    }
}
