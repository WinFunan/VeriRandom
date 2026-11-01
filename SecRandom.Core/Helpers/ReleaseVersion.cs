using System;
using System.Globalization;

namespace SecRandom.Core.Helpers;

/// <summary>
///     A parsed release version: the numeric SemVer core plus this fork's downstream revision ordinal.
///     Ordinal 0 means "the plain upstream version"; a positive value means the n-th downstream rebuild of it.
/// </summary>
public readonly record struct ReleaseVersionKey(Version Core, int MuOrdinal) : IComparable<ReleaseVersionKey>
{
    public int CompareTo(ReleaseVersionKey other)
    {
        var core = Core.CompareTo(other.Core);
        return core != 0 ? core : MuOrdinal.CompareTo(other.MuOrdinal);
    }
}

/// <summary>
///     Parses the release tags this fork can carry, and is the single rule every version gate must agree on.
/// </summary>
/// <remarks>
///     Upstream tags are plain SemVer (<c>v3.1.5</c>, <c>v3.0.0-beta.1</c>), and this fork keeps following
///     upstream's numbering instead of inventing its own. Releasing a downstream revision of the same upstream
///     version therefore appends a <c>Mu</c> ordinal — <c>v3.1.5 Mu 1</c>, which is what the version surface shows.
///     That spelling is deliberately outside SemVer: SemVer has no way to say "same version, next build of it" that
///     also sorts, and a <c>-mu.N</c> pre-release would make Go/NuGet tooling treat a finished downstream build as
///     a preview of the upstream version.
///     <para>
///         The tag may spell the ordinal with any single separator, and <c>Mu</c> is case-insensitive, so
///         <c>v3.1.5 Mu 1</c>, <c>v3.1.5-Mu.1</c>, <c>v3.1.5_Mu_1</c> and <c>v3.1.5+Mu1</c> all mean the same thing.
///         The space form is the display form; a tag that has to survive a URL or an archive file name should use a
///         dot or a hyphen instead, because CI slugifies file names rather than requiring the tag to be slug-safe.
///     </para>
///     <para>
///         Ordering is (numeric core, Mu ordinal): <c>v3.1.5 Mu 2</c> is newer than <c>v3.1.5 Mu 1</c> and older than
///         <c>v3.1.6</c>, and every Mu build of a version is newer than that version itself. That is what lets a
///         downstream build be offered as an update without ever touching upstream's numbers.
///     </para>
/// </remarks>
public static class ReleaseVersion
{
    /// <summary>The downstream ordinal marker. Compared case-insensitively.</summary>
    public const string MuMarker = "Mu";

    /// <summary>
    ///     Parses a tag or version string. SemVer pre-release/build suffixes (<c>-alpha.2</c>, <c>+build</c>) are
    ///     dropped as before, so a preview tag keeps parsing to its numeric core with ordinal 0.
    /// </summary>
    public static bool TryParse(string? text, out ReleaseVersionKey key)
    {
        key = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var normalized = text.Trim();
        if (normalized.Length > 0 && (normalized[0] == 'v' || normalized[0] == 'V'))
            normalized = normalized[1..];

        if (TrySplitMu(normalized, out var core, out var ordinal))
            return Version.TryParse(core, out var muCore) && Assign(muCore, ordinal, out key);

        var metadataIndex = normalized.IndexOfAny(['-', '+']);
        if (metadataIndex >= 0)
            normalized = normalized[..metadataIndex];

        return Version.TryParse(normalized, out var plain) && Assign(plain, 0, out key);
    }

    /// <summary>Formats a slug that is safe in a URL and in an archive file name: separators become dots.</summary>
    public static string ToSlug(string text) =>
        string.Join('.', text.Trim().Split([' ', '\t', '_', '+'], StringSplitOptions.RemoveEmptyEntries));

    private static bool Assign(Version? core, int ordinal, out ReleaseVersionKey key)
    {
        if (core is null)
        {
            key = default;
            return false;
        }

        key = new ReleaseVersionKey(core, ordinal);
        return true;
    }

    /// <summary>
    ///     Splits a trailing <c>Mu</c> ordinal off the core. Walking from the end keeps this from mistaking the
    ///     patch number for an ordinal, and keeps a pre-release like <c>3.1.5-beta.1</c> on its SemVer path.
    /// </summary>
    private static bool TrySplitMu(string text, out string core, out int ordinal)
    {
        core = string.Empty;
        ordinal = 0;

        var digitsStart = text.Length;
        while (digitsStart > 0 && char.IsAsciiDigit(text[digitsStart - 1]))
            digitsStart--;
        if (digitsStart == text.Length)
            return false;

        if (!int.TryParse(text[digitsStart..], NumberStyles.None, CultureInfo.InvariantCulture, out ordinal))
            return false;

        var markerEnd = digitsStart;
        while (markerEnd > 0 && IsSeparator(text[markerEnd - 1]))
            markerEnd--;

        if (markerEnd < MuMarker.Length
            || !text.AsSpan(markerEnd - MuMarker.Length, MuMarker.Length)
                .Equals(MuMarker, StringComparison.OrdinalIgnoreCase))
            return false;

        core = text[..(markerEnd - MuMarker.Length)].TrimEnd(' ', '\t', '-', '_', '+', '.');
        return core.Length > 0;
    }

    private static bool IsSeparator(char value) => value is ' ' or '\t' or '-' or '_' or '+' or '.';
}
