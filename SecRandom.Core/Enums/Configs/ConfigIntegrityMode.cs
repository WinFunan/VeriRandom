namespace SecRandom.Core.Enums.Configs;

/// <summary>
///     How this installation protects its configuration file.
/// </summary>
public enum ConfigIntegrityMode
{
    /// <summary>No verification.</summary>
    Off,

    /// <summary>
    ///     The local fingerprint only: it detects accidental or casual edits, but anyone able to write the
    ///     credential file can refresh it, so it is not protection against a determined local attacker.
    /// </summary>
    LocalFingerprint,

    /// <summary>
    ///     A detached signature produced on another device with a private key this machine never holds.
    ///     The draw machine only carries the public key, so it can verify a policy but never author one.
    /// </summary>
    SignedPolicy
}
