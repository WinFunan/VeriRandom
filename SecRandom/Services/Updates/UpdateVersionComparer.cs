using System;
using SecRandom.Core.Helpers;

namespace SecRandom.Services.Updates;

/// <summary>
///     Compares a signed update-manifest version against the running build. Release tags are not parseable by
///     <see cref="Version" /> on their own — they may be <c>v3.0.0-beta.1</c> (upstream preview) or
///     <c>v3.1.5 Mu 2</c> (a downstream rebuild of an upstream version) — so the comparison goes through
///     <see cref="ReleaseVersion" />, which owns the one rule every version gate shares.
///     This keeps the pre-release train able to see newer releases, makes a Mu build newer than the plain version it
///     rebuilds and older than the next upstream version, and keeps desktop and mobile update checks on that one rule.
/// </summary>
internal static class UpdateVersionComparer
{
    public static bool IsNewer(string candidate, string current) =>
        ReleaseVersion.TryParse(candidate, out var candidateVersion)
        && ReleaseVersion.TryParse(current, out var currentVersion)
        && candidateVersion.CompareTo(currentVersion) > 0;

    /// <summary>
    ///     The numeric core of a version string, with any pre-release, build suffix or Mu ordinal removed.
    ///     Callers that only need "which major is this" use this; ordering must use <see cref="IsNewer" />.
    /// </summary>
    public static bool TryParse(string text, out Version version)
    {
        if (ReleaseVersion.TryParse(text, out var parsed))
        {
            version = parsed.Core;
            return true;
        }

        version = null!;
        return false;
    }
}
