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

    // 更新已在本分支启用：元数据、签名清单与发布产物都来自本仓库自己的 GitHub Releases
    // （WinFunan/VeriRandom），不再读取上游 SECTL/SecRandom 的任何元数据。
    public const bool UpdatesEnabled = true;

    /// <summary>
    ///     本分支自有的 GitHub 文件代理：只代理 <c>github.com/WinFunan/VeriRandom</c> 下的
    ///     raw / releases / archive 路径，不代理网页、API 与其它仓库。
    /// </summary>
    /// <remarks>
    ///     它不属于 SECTL 的服务，因此**不经过**联网总开关（<c>SectlTrafficPolicy</c>）：
    ///     那个开关只约束会连接 SecRandom/SECTL 服务器的通道。插件市场索引与插件包位于别的仓库，
    ///     这个代理不覆盖，仍然直连 GitHub。
    /// </remarks>
    public const string GitHubFileProxyBaseUrl = "https://vr-ghproxy.yeyixiao.net";

    /// <summary>
    ///     是否在首次引导与隐私设置里展示 SECTL 在线服务隐私政策（条款、免责声明与隐私政策全文）。
    /// </summary>
    /// <remarks>
    ///     暂时隐藏：法律页只保留本分支自己的隐私政策、GPLv3 与时间戳说明。隐藏**不等于同意**——
    ///     <c>SectlTrafficPolicy</c> 仍然要求对应确认，联网总开关也仍然拦着 SECTL 通道；恢复显示后
    ///     再由用户亲自签署。运行时的同意弹窗（<c>SecRandomServicesConsent</c>）才是真正的签署点，
    ///     这个开关只控制首次引导与隐私设置里的常驻文本。
    /// </remarks>
    public const bool ShowSectlServicesPolicy = false;

    /// <summary>
    ///     是否在首次引导与隐私设置里展示跨境数据传输须知。
    /// </summary>
    /// <remarks>暂时隐藏，理由同上：隐藏不等于同意，跨境的确认仍由 <c>SectlTrafficPolicy</c> 把关。</remarks>
    public const bool ShowCrossBorderTransferNotice = false;

    // 本分支的品牌色：默认自定义主题色用它，抽取动画的固定色也从这里取。
    // 不再沿用上游/系统的 Windows 蓝，界面第一眼就能和上游区分开。
    public const string DefaultThemeColor = "#00897B";
    public const string DefaultFontFamily = "avares://SecRandom/Assets/Fonts/MiSans/#MiSans";

#if DEBUG
    public static bool IsDevelopment => true;
#else
    public static bool IsDevelopment => false;
#endif

    // Fork policy (relaxed in this release): TOTP may verify on its own again. Checking a code without the
    // user's password requires keeping a readable seed copy in `data/config/security/totp-standalone.json`,
    // which is a real downgrade: whoever can read that file can mint valid codes, so the protection becomes
    // an interface-level restriction instead of a cryptographic one. The seed still lives inside the
    // owner-only credential directory that no archive, cloud backup, or Android provider can reach, and the
    // security page states that trade-off at the moment TOTP becomes usable alone, so the choice is the
    // user's. `--allow-standalone-totp` and EnableStandaloneTotpVerification() stay as explicit widening
    // hooks so a later policy can narrow the default again without touching any call site.
    private static bool _allowStandaloneTotpVerification = true;

    public static bool AllowStandaloneTotpVerification => _allowStandaloneTotpVerification;

    /// <summary>Startup opt-in used by <c>--allow-standalone-totp</c>.</summary>
    public static void EnableStandaloneTotpVerification() => _allowStandaloneTotpVerification = true;

    /// <summary>
    ///     Honours <c>--allow-standalone-totp</c>. The shipped default is already on, so this is only meaningful
    ///     when a build narrows <see cref="AllowStandaloneTotpVerification" /> again; it must only ever widen,
    ///     never narrow, that.
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
