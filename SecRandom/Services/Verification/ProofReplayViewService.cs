using System.Threading;
using System.Threading.Tasks;
using SecRandom.Core.Views;
using SecRandom.Services.ViewEngine;

namespace SecRandom.Services.Verification;

/// <summary>
///     Opens the proof replay window. It mirrors the remaining-list view: one transient window per request, and a
///     fresh host so it can never be attached to another independent view's window.
/// </summary>
public sealed class ProofReplayViewService(
    IViewEngine viewEngine,
    IViewHostProvider hostProvider)
{
    internal const string ViewId = "main.proofReplay";

    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task ShowAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(true);
        try
        {
            // Reopening is the same request as opening: close whatever is there so the chain is re-read.
            await viewEngine.CloseAsync(ViewId, cancellationToken: cancellationToken).ConfigureAwait(true);

            var options = hostProvider is DesktopViewHostProvider
                ? new ViewShowOptions { ActivationPreference = ViewActivationPreference.NewHost }
                : null;

            await viewEngine.ShowAsync(ViewId, options, cancellationToken).ConfigureAwait(true);
        }
        finally
        {
            _gate.Release();
        }
    }
}
