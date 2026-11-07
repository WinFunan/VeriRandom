using System.Runtime.CompilerServices;
using SecRandom.Core;

namespace SecRandom.Core.Tests;

/// <summary>
///     The security tests deliberately exercise the TOTP-only verification path (the one that reads the
///     readable <c>totp-standalone.json</c> seed copy). The opt-in is applied explicitly so the suite never
///     depends on the production default of <c>GlobalConstants.AllowStandaloneTotpVerification</c>: narrowing
///     that policy again must not silently turn these tests into no-ops. It only widens behavior inside tests.
/// </summary>
internal static class TestStandaloneTotpOptIn
{
    [ModuleInitializer]
    internal static void OptIn() => GlobalConstants.EnableStandaloneTotpVerification();
}
