using Avalonia.Media;
using System.Reflection;

namespace SecRandom.Core;

public static class GlobalConstants
{
    private static readonly object VersionSourceGate = new();
    private static Assembly? _versionAssembly;
    private static VersionMetadata? _versionMetadata;

    /// <summary>
    ///     Assembly this build's version metadata is read from. The metadata is compiled into the platform head
    ///     assembly (root AssemblyInfo.cs plus the GitInfo generator), and Android has no managed entry point, so
    ///     <see cref="Assembly.GetEntryAssembly" /> returns null there and the head must publish itself through
    ///     <see cref="SetVersionAssembly" />; otherwise the version falls back to this library, which carries no
    ///     build metadata and renders as v0.0.0.0.
    /// </summary>
    private static Assembly VersionAssembly =>
        _versionAssembly ?? Assembly.GetEntryAssembly() ?? typeof(GlobalConstants).Assembly;

    private static VersionMetadata Metadata
    {
        get
        {
            var cached = _versionMetadata;
            if (cached is not null)
                return cached;

            lock (VersionSourceGate)
                return _versionMetadata ??= ReadMetadata(VersionAssembly);
        }
    }

    internal static Assembly VersionSource => VersionAssembly;

    /// <summary>
    ///     Publishes the platform head assembly that carries this build's
    ///     <see cref="AssemblyInformationalVersionAttribute" />. Platform heads call this during startup, before any
    ///     version value is read; re-publishing clears the cached metadata.
    /// </summary>
    public static void SetVersionAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        lock (VersionSourceGate)
        {
            _versionAssembly = assembly;
            _versionMetadata = null;
        }
    }

    public static string Tag => Metadata.Tag;
    public static string Branch => Metadata.Branch;
    public static string CommitHash => Metadata.CommitHash[..Math.Min(7, Metadata.CommitHash.Length)];
    public static string FullCommitHash => Metadata.CommitHash;

    public static string CodeName => @"Nonomi";
    public static string Version => Tag.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? Tag : $@"v{Tag}";
    public static string AssemblyVersion => VersionSource
        .GetCustomAttribute<AssemblyVersionAttribute>()?.Version ?? "0.0.0.0";
    public static string DisplayVersion => $@"{Version} (Codename {CodeName})";
    public static string VersionLong => $@"{Version}-{CodeName}-{CommitHash}({Branch})";

    public static string PlatformExecutableExtension => OperatingSystem.IsWindows() ? @".exe" : "";

    // 桌面与移动端遥测共用的 Sentry DSN；两端各自适配器不得再硬编码副本
    public const string SentryDsn = "https://7614b2b2fd46a451e7cb3ed670279e75@o4510689230192640.ingest.us.sentry.io/4511675887910912";
    public const string BehindSceneAttachedSettings = "F45DFB95-7D20-4BAB-86A3-8864BBDFCE9E";
    public const string SpecificAnnouncementAttachedSettings = "10F2C686-07D7-47E7-9A4F-B7A4724A6A10";
    public const string DrawImageAttachedSettings = "4C88E037-4F69-42D0-A32F-16D2827B7B6D";
    public const string DrawMusicAttachedSettings = "A16F1E84-77E8-4E09-B9EC-8BAF5C148057";

    // Fork policy: update discovery in this repository still targets the upstream SecRandom release
    // channel, which this fork must not consume. The whole update implementation stays intact so the
    // capability is preserved; set this back to true once this fork has its own metadata/release server.
    public const bool UpdatesEnabled = false;

    public const string DefaultThemeColor = "#0078D4"; // 系统自带主题色蓝  66CCFF 天依蓝
    public const string DefaultFontFamily = "avares://SecRandom/Assets/Fonts/MiSans/#MiSans";

#if DEBUG
    public static bool IsDevelopment => true;
#else
    public static bool IsDevelopment => false;
#endif

    // Fork policy: a TOTP code can only be checked without the user's password by keeping a plaintext
    // seed copy in `data/config/security/totp-standalone.json`. That readable seed is the one credential
    // this fork refuses to keep, so TOTP-only verification is off unless the build is a debug build or the
    // process was started with the explicit opt-in below. USB-only and password-augmented verification are
    // unaffected.
    private static bool _allowStandaloneTotpVerification = IsDevelopment;

    public static bool AllowStandaloneTotpVerification => _allowStandaloneTotpVerification;

    /// <summary>Startup opt-in used by <c>--allow-standalone-totp</c>.</summary>
    public static void EnableStandaloneTotpVerification() => _allowStandaloneTotpVerification = true;

    /// <summary>
    ///     Enables TOTP-only verification when the process was started with <c>--allow-standalone-totp</c>.
    ///     Debug builds already allow it, and this must only ever widen, never narrow, that.
    /// </summary>
    public static bool EnableStandaloneTotpVerificationIfRequested(IEnumerable<string>? arguments)
    {
        if (arguments is null)
            return false;

        foreach (var argument in arguments)
        {
            if (!string.Equals(argument, "--allow-standalone-totp", StringComparison.OrdinalIgnoreCase))
                continue;

            EnableStandaloneTotpVerification();
            return true;
        }

        return false;
    }

    public static FontFamily FluentIconsFontFamily { get; } =
        new(@"avares://SecRandom/Assets/Fonts/#FluentSystemIcons-Resizable");

    public static FontFamily DefaultAvaFontFamily { get; } =
        new(@"avares://SecRandom/Assets/Fonts/MiSans/#MiSans");

    internal static VersionMetadata ReadMetadata(Assembly assembly)
    {
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        if (string.IsNullOrWhiteSpace(informationalVersion))
            return new VersionMetadata(TaglessFallbackTag, "Unknown", "Unknown");

        var separator = informationalVersion.IndexOf('+');
        var generatedGitInfo = assembly.GetType("SecRandom.GitInfo");
        var branch = generatedGitInfo?.GetField("Branch", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as string
                     ?? generatedGitInfo?.GetProperty("Branch", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as string
                     ?? "Unknown";
        var tag = separator < 0 ? informationalVersion : informationalVersion[..separator];
        var commitHash = separator < 0 ? "Unknown" : informationalVersion[(separator + 1)..];

        // The GitInfo generator embeds whatever `git describe` printed. In a tagless repository that is the
        // tool's stderr ("fatal: No names found, cannot describe anything."), which would otherwise become
        // this build's version everywhere it matters — the crash report, `producer_version`, the Sentry
        // release, and the update gate. An implausible tag therefore falls back to the fork's dev version
        // instead of propagating tool output as an identity.
        if (!LooksLikeVersionTag(tag))
            tag = TaglessFallbackTag;

        return new VersionMetadata(tag, branch, commitHash);
    }

    /// <summary>
    ///     Stand-in version for a build whose generated tag is not a version at all (tagless checkout).
    ///     Keep its major in step with <c>SecRandom.PluginSdk/PluginApiVersions.Current</c>, the same rule
    ///     the release workflow follows for its own fallback.
    /// </summary>
    private const string TaglessFallbackTag = "v3.0.0-dev";

    private static bool LooksLikeVersionTag(string value)
    {
        var span = value.AsSpan().Trim();
        if (span.Length > 0 && (span[0] == 'v' || span[0] == 'V'))
            span = span[1..];

        // Require at least `MAJOR.MINOR`, both numeric, so `fatal: ...` and any other tool output is rejected.
        var majorEnd = span.IndexOf('.');
        if (majorEnd <= 0)
            return false;

        for (var index = 0; index < majorEnd; index++)
        {
            if (!char.IsAsciiDigit(span[index]))
                return false;
        }

        var remaining = span[(majorEnd + 1)..];
        var minorEnd = remaining.IndexOf('.');
        var minor = minorEnd < 0 ? remaining : remaining[..minorEnd];
        if (minor.IsEmpty)
            return false;

        foreach (var character in minor)
        {
            if (!char.IsAsciiDigit(character))
                return false;
        }

        return true;
    }
}

internal sealed record VersionMetadata(string Tag, string Branch, string CommitHash);
