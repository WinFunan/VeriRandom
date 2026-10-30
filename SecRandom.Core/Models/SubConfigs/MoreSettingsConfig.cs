using CommunityToolkit.Mvvm.ComponentModel;
using SecRandom.Core.Enums.Configs;
using System.Text.Json.Serialization;

namespace SecRandom.Core.Models.SubConfigs;

public partial class MoreSettingsConfig : ObservableObject
{
    private bool? _legacyBackgroundMusicLoop;

    [JsonPropertyName("background_music_loop")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? LegacyBackgroundMusicLoop
    {
        get => null;
        set => _legacyBackgroundMusicLoop = value;
    }

    [JsonPropertyName("backgroundMusicLoop")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? LegacyBackgroundMusicLoopCamelCase
    {
        get => null;
        set => _legacyBackgroundMusicLoop = value;
    }

    public bool? ConsumeLegacyBackgroundMusicLoop()
    {
        var value = _legacyBackgroundMusicLoop;
        _legacyBackgroundMusicLoop = null;
        return value;
    }

    [ObservableProperty] private bool _lotteryEnabled = true;
    [ObservableProperty] private RollCallControlPanelPosition _rollCallControlPanelPosition = RollCallControlPanelPosition.Right;
    [ObservableProperty] private bool _rollCallResetButton = true;
    [ObservableProperty] private bool _rollCallQuantityControl = true;
    [ObservableProperty] private bool _rollCallStartButton = true;
    [ObservableProperty] private bool _rollCallListSelector = true;
    [ObservableProperty] private bool _rollCallRangeSelector = true;
    [ObservableProperty] private bool _rollCallGenderSelector = true;
    [ObservableProperty] private bool _rollCallRemainingButton = true;
    [ObservableProperty] private bool _rollCallQuantityLabel = true;
    [ObservableProperty] private RollCallControlPanelPosition _lotteryControlPanelPosition = RollCallControlPanelPosition.Right;
    [ObservableProperty] private bool _lotteryResetButton = true;
    [ObservableProperty] private bool _lotteryQuantityControl = true;
    [ObservableProperty] private bool _lotteryStartButton = true;
    [ObservableProperty] private bool _lotteryListSelector = true;
    [ObservableProperty] private bool _lotteryStudentListSelector = true;
    [ObservableProperty] private bool _lotteryRangeSelector = true;
    [ObservableProperty] private bool _lotteryGenderSelector = true;
    [ObservableProperty] private bool _lotteryRemainingButton = true;
    [ObservableProperty] private bool _lotteryQuantityLabel = true;
    [ObservableProperty] private bool _enableShortcut = false;
    [ObservableProperty] private string _openRollCallPageShortcut = string.Empty;
    [ObservableProperty] private string _quickDrawShortcut = string.Empty;
    [ObservableProperty] private string _openLotteryPageShortcut = string.Empty;
    [ObservableProperty] private string _increaseRollCallCountShortcut = string.Empty;
    [ObservableProperty] private string _decreaseRollCallCountShortcut = string.Empty;
    [ObservableProperty] private string _increaseLotteryCountShortcut = string.Empty;
    [ObservableProperty] private string _decreaseLotteryCountShortcut = string.Empty;
    [ObservableProperty] private string _startRollCallShortcut = string.Empty;
    [ObservableProperty] private string _startLotteryShortcut = string.Empty;

    // Raw-number page state. The bounds are remembered between sessions because a classroom reuses the same range
    // (a dice roll, a seat number) far more often than it changes it. Whether the range and source choice may be
    // changed is NOT stored here: locking them is a protection, so it lives in the encrypted security settings as
    // SecuritySettingsConfig.ProtectRawRandomSettings and is released only by a fresh password.
    [ObservableProperty] private long _rawRandomLowerBound;
    [ObservableProperty] private long _rawRandomUpperBound = 100;

    // Explicit candidate set, e.g. "5,10". A set is what a teacher actually needs when the values are fixed tiers
    // (money denominations, seat numbers); a range cannot express that without allowing the values in between.
    [ObservableProperty] private bool _rawRandomUseValues;
    [ObservableProperty] private string _rawRandomValuesText = string.Empty;

    // Advanced output controls. Off by default because they change what "the result" even is; the page shows the
    // advanced block only when this is on.
    [ObservableProperty] private bool _rawRandomAdvancedOptionsEnabled;
    [ObservableProperty] private bool _rawRandomRawEntropyEnabled;
    [ObservableProperty] private int _rawRandomBitCount = 32;

    /// <summary>0 = decimal, 1 = hex, 2 = printable ASCII. Stored as an int so an older build reads a sane default.</summary>
    [ObservableProperty] private int _rawRandomOutputFormat = 1;

    /// <summary>ASCII characters to produce when format 2 is selected, independent of the bit width.</summary>
    [ObservableProperty] private int _rawRandomAsciiCharCount = 16;

    // Result mini-window size. Resizing it on a classroom touchscreen is a deliberate, effortful action, so the
    // chosen size is remembered; the position is not, because the card re-centres on whichever display the app runs on
    // and a remembered position from a disconnected monitor would put it off-screen.
    [ObservableProperty] private double _resultMiniWindowWidth = 360;
    [ObservableProperty] private double _resultMiniWindowHeight = 320;

    // Courseware-marker auto-detection. Off by default: it reads the projected screen, so it must be a deliberate
    // choice rather than something a fresh install starts doing.
    [ObservableProperty] private bool _slideTriggerWatcherEnabled;
}
