using Microsoft.Extensions.DependencyInjection;
using SecRandom.Core.Extensions.Registry;

namespace SecRandom.Core.Services.Draw;

/// <summary>
///     Registers the optional cross-repository draw-algorithm tier when it was compiled in.
/// </summary>
/// <remarks>
///     <para>
///         The tier lives in its own repository (<c>vendors/NewAlgorithm</c>). That checkout can be absent, pinned to a
///         tag that does not exist yet, or fail to compile; none of those may take the main program's build or startup
///         down, so the project reference, the tier itself and this registration are all conditional on the same
///         <c>NEWALGORITHM</c> symbol that <c>SecRandom.Core.csproj</c> defines only while the project file exists.
///     </para>
///     <para>
///         Being absent is reported by absence, never by substitution: no handler here can silently fall back to the
///         built-in fair weights, because a draw labelled with one algorithm while actually using another would be
///         worse than the tier not existing. The algorithm list simply has two entries instead of three.
///     </para>
/// </remarks>
public static class NewAlgorithmRegistrationExtensions
{
    public const string AlgorithmId = "builtin.newAlgorithm";

    public static IServiceCollection AddNewAlgorithmRollCallAlgorithm(
        this IServiceCollection services,
        string displayName)
    {
#if NEWALGORITHM
        services.AddRollCallAlgorithm<NewAlgorithmRollCallAlgorithm>(AlgorithmId, displayName);
#else
        _ = displayName;
#endif
        return services;
    }
}
