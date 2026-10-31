using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Microsoft.Extensions.Logging;

namespace SecRandom.Services.SlideTrigger;

/// <summary>
///     Puts the shortcut's courseware marker on the clipboard.
/// </summary>
/// <remarks>
///     This is the classroom-assembly path: the teacher is in their deck, presses one key, and pastes. It deliberately
///     opens no window — a popup would steal focus from a running slideshow, which is the one moment nothing may
///     interrupt. Which marker is copied is picked on the settings page (<see cref="SlideTriggerStore.QuickMarker" />).
/// </remarks>
public sealed class SlideTriggerQuickCopyService(
    SlideTriggerStore store,
    SlideTriggerMarkerImageService images,
    ILogger<SlideTriggerQuickCopyService> logger)
{
    /// <summary>Renders the quick marker and copies it. False means nothing landed on the clipboard.</summary>
    public async Task<bool> CopyQuickMarkerAsync()
    {
        var marker = store.QuickMarker;
        if (marker is null)
        {
            // The most likely cause of "the hotkey does nothing": no marker was picked yet, or the picked one was
            // deleted. Saying it in the log is what makes that diagnosable instead of mysterious.
            logger.LogInformation("快捷复制课件标记：尚未指定标记，已忽略");
            return false;
        }

        var copied = await images.TryCopyToClipboardAsync(FindTopLevel(), marker.MarkerText).ConfigureAwait(true);
        if (!copied)
        {
            logger.LogWarning("快捷复制课件标记失败：剪贴板不可用");
            return false;
        }

        logger.LogInformation("快捷复制课件标记：{Name}", marker.Name);
        return true;
    }

    /// <summary>
    ///     Avalonia's clipboard hangs off a top level, and the premise here is that no window is in front. The floating
    ///     window exists from startup, so any live window will do: even a hidden one owns the platform clipboard.
    /// </summary>
    private static TopLevel? FindTopLevel()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            return desktop.MainWindow
                   ?? desktop.Windows.FirstOrDefault(window => window.IsVisible)
                   ?? desktop.Windows.FirstOrDefault();

        return Application.Current?.MainWindow;
    }
}
