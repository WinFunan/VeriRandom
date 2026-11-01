using System.Runtime.CompilerServices;
using SecRandom.Core;

namespace SecRandom.Core.Tests;

/// <summary>
///     The security tests deliberately exercise the TOTP-only verification path (the one that reads the
///     readable <c>totp-standalone.json</c> seed copy). That path ships disabled outside a debug build, and
///     CI runs Release, so the test process has to apply the same opt-in the desktop
///     <c>--allow-standalone-totp</c> switch does. It only widens production behavior inside tests.
/// </summary>
internal static class TestStandaloneTotpOptIn
{
    [ModuleInitializer]
    internal static void OptIn() => GlobalConstants.EnableStandaloneTotpVerification();
}
