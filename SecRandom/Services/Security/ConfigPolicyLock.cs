using Avalonia.Controls;
using SecRandom.Core.Abstraction;
using SecRandom.Services.Security;

// Declared in the root namespace on purpose: every settings page lives under SecRandom.Views.SettingsPages.*,
// so C# resolves an outer namespace without a using directive and each page's wiring stays a single line
// instead of also needing a using edit.
namespace SecRandom;

/// <summary>
///     Locks the settings a deployed signed policy covers, so a covered value cannot be edited by accident —
///     editing one is precisely what invalidates the signature.
///     <para>
///         This is an affordance, not the enforcement. The settings file and the signature are both writable by
///         anyone with access to the machine, so a locked control stops mistakes rather than an attacker; the
///         check that matters is the digest comparison at startup and on every save.
///     </para>
///     <para>
///         Pages apply it once their controls exist, passing exactly the controls that edit covered settings.
///         The integrity fields themselves (integrity mode, tamper action, restore source) are outside the
///         covered set and stay editable, because switching the mode off, changing the policy, re-signing, and
///         switching it back on is the intended way to change a covered setting.
///     </para>
/// </summary>
internal static class ConfigPolicyLock
{
    /// <summary>Disables every supplied control while a signed policy is in force.</summary>
    internal static void Apply(params Control[] coveredControls)
    {
        // Fail open where the service is absent (mobile never registers it): no policy service means there is
        // no signed policy to protect.
        if (IAppHost.TryGetService<ConfigPolicySignatureService>() is not { } service
            || !service.IsPolicyLockEnforced)
            return;

        foreach (var control in coveredControls)
            control.IsEnabled = false;
    }
}
