using System;
using SecRandom.Core.Services.Config;
using SecRandom.ViewModels.MainPages;
using SecRandom.Views.MainPages;

namespace SecRandom.Services.ViewEngine;

/// <summary>
///     Opens the roll-call result as a borderless topmost window and steps the main window aside, which is the
///     "shrink the result into a floating card" action on the roll-call page.
/// </summary>
/// <remarks>
///     <para>
///         The order is load-bearing: the floating window is shown <em>before</em> the main window is hidden, because
///         Avalonia shuts the application down when the last window closes — the same rule the timer mini window follows.
///         Desktop only: the floating window is a desktop window, and on a phone or tablet the main view hosts the
///         result in place instead.
///     </para>
///     <para>
///         The size the user dragged the card to is remembered across openings. On a classroom touchscreen reaching the
///         grip is the expensive part, so making the next lesson resize it again would be exactly the wrong default;
///         the position is deliberately not remembered (see <c>MoreSettings.ResultMiniWindowWidth</c>).
///     </para>
/// </remarks>
public sealed class ResultMiniWindowService(RollCallPageViewModel viewModel, MainConfigHandler configHandler)
{
    private const double DefaultWidth = 360;
    private const double DefaultHeight = 320;
    private const double MinimumWidth = 240;
    private const double MinimumHeight = 220;

    /// <summary>Whatever a hand-edited settings file claims, a result card wider than this is not a result card.</summary>
    private const double MaximumSize = 4000;

    private ResultMiniWindow? _window;

    public void Show()
    {
        if (_window is { IsVisible: true })
        {
            _window.Activate();
            return;
        }

        var more = configHandler.Data.MoreSettings;

        _window = new ResultMiniWindow(
            viewModel,
            RestoreMainWindow,
            Sanitize(more.ResultMiniWindowWidth, DefaultWidth, MinimumWidth),
            Sanitize(more.ResultMiniWindowHeight, DefaultHeight, MinimumHeight));

        _window.Closed += OnWindowClosed;
        _window.Show();

        App.SetMainWindowVisibility("hide");
    }

    public void RestoreMainWindow()
    {
        CloseWindow();
        App.ShowMainWindow();
    }

    internal void CloseWindow()
    {
        _window?.AllowClose();
        _window?.Close();
        _window = null;
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        if (sender is not ResultMiniWindow window)
            return;

        window.Closed -= OnWindowClosed;
        PersistSize(window);
    }

    private void PersistSize(ResultMiniWindow window)
    {
        if (!double.IsFinite(window.Width) || !double.IsFinite(window.Height))
            return;

        if (window.Width < MinimumWidth || window.Height < MinimumHeight)
            return;

        var more = configHandler.Data.MoreSettings;
        if (Math.Abs(more.ResultMiniWindowWidth - window.Width) < 0.5
            && Math.Abs(more.ResultMiniWindowHeight - window.Height) < 0.5)
            return;

        more.ResultMiniWindowWidth = window.Width;
        more.ResultMiniWindowHeight = window.Height;
        configHandler.Save();
    }

    private static double Sanitize(double value, double fallback, double minimum) =>
        double.IsFinite(value) && value >= minimum && value <= MaximumSize ? value : fallback;
}
