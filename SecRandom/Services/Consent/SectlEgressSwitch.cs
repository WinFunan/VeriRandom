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
/// </remarks>
public sealed class SectlEgressSwitch(ILogger<SectlEgressSwitch> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly object _gate = new();
    private bool? _disabled;

    /// <summary>Raised after the switch changes, so open surfaces can refresh their enabled state.</summary>
    public event EventHandler? Changed;

    /// <summary>True when the user has forbidden every SECTL connection.</summary>
    public bool IsDisabled
    {
        get
        {
            lock (_gate)
                return _disabled ??= Load();
        }
    }

    public void SetDisabled(bool disabled)
    {
        lock (_gate)
        {
            if (_disabled == disabled)
                return;

            _disabled = disabled;
            Save(disabled);
        }

        logger.LogInformation("SECTL 联网总开关：{State}", disabled ? "已禁用" : "已允许");
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static bool Load()
    {
        var path = StorePath;
        if (!File.Exists(path))
            return false;

        try
        {
            var state = JsonSerializer.Deserialize<EgressState>(File.ReadAllText(path), JsonOptions);
            return state?.Disabled ?? false;
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
