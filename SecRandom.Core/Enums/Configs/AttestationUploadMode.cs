namespace SecRandom.Core.Enums.Configs;

/// <summary>
///     Whether a completed ordinary draw may be submitted to the SecRandom/SECTL replay-attestation service.
///     There is deliberately no default: first-run setup must ask the user to choose, and an installation that
///     has never been asked stays at <see cref="Unset"/>, which callers must treat as "do not upload".
/// </summary>
public enum AttestationUploadMode
{
    /// <summary>The user has not been asked yet. Must never upload.</summary>
    Unset = 0,

    /// <summary>The user actively chose to submit ordinary draws for replay attestation.</summary>
    Enabled = 1,

    /// <summary>The user actively chose not to submit ordinary draws.</summary>
    Disabled = 2
}
