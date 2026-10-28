using CommunityToolkit.Mvvm.ComponentModel;
using SecRandom.Core.Enums.Configs;

namespace SecRandom.Core.Models.SubConfigs;

public partial class FloatingWindowSettingsConfig : ObservableObject
{
    [ObservableProperty] private bool _startupDisplayFloatingWindow = true;
    [ObservableProperty] private int _floatingWindowOpacity = 80;
    [ObservableProperty] private TopmostMode _floatingWindowTopmostMode = TopmostMode.Topmost;
    [ObservableProperty] private bool _showRollCallButton = true;

    /// <summary>
    ///     Raw-number page button. Off by default: it is a utility for a teacher who asked for it, not part of the
    ///     default classroom surface.
    /// </summary>
    [ObservableProperty] private bool _showRawRandomButton;
    [ObservableProperty] private bool _showQuickDrawButton = true;
    [ObservableProperty] private bool _showLotteryButton = false;
    [ObservableProperty] private bool _showTimerButton = false;
    [ObservableProperty] private List<string> _visiblePluginButtonIds = [];
    [ObservableProperty] private int _floatingWindowPlacement = 1;
    [ObservableProperty] private int _floatingWindowDisplayStyle = 0;
    [ObservableProperty] private int _floatingWindowTheme = 0;
    [ObservableProperty] private bool _stickToEdge = true;
    [ObservableProperty] private int _stickToEdgeRecoverSeconds = 3;
    [ObservableProperty] private int _stickToEdgeDisplayStyle = 1;
    [ObservableProperty] private int _dockedWindowSize = 32;
    [ObservableProperty] private bool _draggable = true;

    // Off by default, and Windows-only in practice: the platform contract reports IsSupported=false elsewhere,
    // so on those platforms this switch stays inert. When on, a quick draw records which window owned the
    // foreground before the draw window took it and gives that window the foreground back when the draw window
    // closes. This is not the same thing as DoNotStealFocus above: that one would avoid taking the foreground
    // at all, while this one hands it back after the result has been shown and closed.
    [ObservableProperty] private bool _restoreFocusAfterQuickDraw;
    [ObservableProperty] private int _floatingWindowSize = 56;
    [ObservableProperty] private int _longPressDuration = 500;
    [ObservableProperty] private bool _doNotStealFocus = true;
    [ObservableProperty] private bool _hideOnForeground = false;
    [ObservableProperty] private string _hideOnForegroundWindowTitles = string.Empty;
    [ObservableProperty] private string _hideOnForegroundProcessNames = string.Empty;
}
