using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Attributes;
using SecRandom.Core.Icons;
using SecRandom.Core.Models.SlideTrigger;
using SR = SecRandom.Langs.SettingsPages.SlideTrigger.Resources;
using SecRandom.Langs.SettingsPages.SlideTrigger;
using SecRandom.Platforms.Abstractions;
using SecRandom.Services.SlideTrigger;

namespace SecRandom.Views.SettingsPages.SlideTrigger;

/// <summary>
///     Where a teacher creates the markers that go into their courseware, and manages the ones already made.
/// </summary>
/// <remarks>
///     <para>
///         Deliberately a settings page rather than a classroom control surface: a marker is prepared while assembling a
///         deck, never in front of a class. Keeping it out of the floating window also means the draw-time UI stays as
///         small as it is.
///     </para>
///     <para>
///         Parameter rows are read by name from the page instead of through a view model. There is no shared state to
///         keep in sync here — the marker library is the source of truth and it lives in <see cref="SlideTriggerStore" /> —
///         so a view model would only add a second copy that can disagree with it.
///     </para>
/// </remarks>
[PageInfo("settings.slideTrigger", FluentIcons.NumberSymbolFilled)]
public sealed partial class SlideTriggerSettingsPage : UserControl
{
    private readonly SlideTriggerStore _store;
    private readonly SlideTriggerMarkerImageService _images;
    private readonly SlideTriggerScreenDecoder _decoder;
    private readonly SlideTriggerWatcher? _watcher;

    /// <summary>Null while the form is creating a marker; set while it is editing an existing one.</summary>
    private SlideTriggerDefinition? _editing;

    public SlideTriggerSettingsPage()
    {
        _store = IAppHost.GetService<SlideTriggerStore>();
        _images = IAppHost.GetService<SlideTriggerMarkerImageService>();
        _decoder = IAppHost.GetService<SlideTriggerScreenDecoder>();
        _watcher = IAppHost.TryGetService<SlideTriggerWatcher>();

        DataContext = this;
        InitializeComponent();
        RefreshLibrary();

        if (_watcher is { IsSupported: true })
        {
            WatcherBox.IsChecked = _watcher.IsEnabled;
            _watcher.MarkerDetected += OnMarkerDetected;
            DetachedFromVisualTree += OnDetachedFromVisualTree;
        }
        else
        {
            WatcherBox.IsEnabled = false;
            WatcherBox.IsChecked = false;
        }
    }

    private void Watcher_OnChanged(object? sender, RoutedEventArgs e)
    {
        if (_watcher is null)
            return;

        _watcher.IsEnabled = WatcherBox.IsChecked == true;
    }

    /// <summary>
    ///     The page is transient but the watcher is a host singleton, so an unsubscribed handler would keep this page
    ///     alive for the rest of the process — and one of those pages per visit.
    /// </summary>
    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (_watcher is not null)
            _watcher.MarkerDetected -= OnMarkerDetected;
    }

    /// <summary>
    ///     Detection happens on a pool thread, so reporting it has to hop back to the UI thread.
    /// </summary>
    private void OnMarkerDetected(object? sender, SlideTriggerDetection detection) =>
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            StatusText.Text = detection.AlreadyUsedThisSession || detection.Payload.Parameters is null
                ? SR.M_UsedThisSession
                : string.Format(SR.M_ProbeFoundFormat, DescribeParameters(detection.Payload.Parameters));

            RefreshLibrary();
        });

    // ---------------------------------------------------------------- generation

    private async void Generate_OnClick(object? sender, RoutedEventArgs e) => await GenerateAsync(saveToFile: false);

    private async void GenerateAndSave_OnClick(object? sender, RoutedEventArgs e) => await GenerateAsync(saveToFile: true);

    private async Task GenerateAsync(bool saveToFile)
    {
        var parameters = ReadParameters();
        var expiresAt = ReadExpiry();
        var editing = _editing;

        SlideTriggerDefinition definition;
        try
        {
            if (editing is null)
            {
                definition = _store.Create(NameBox.Text ?? string.Empty, parameters, expiresAt);
            }
            else
            {
                // Editing keeps the id, so the per-session consumption record and the deck's slide keep pointing at the
                // same marker — but the parameters live in the PNG, so the teacher has to swap the image for this to
                // take effect. Both messages below say so rather than letting the edit look applied.
                //
                // A detached copy is handed to the store: `editing` is the stored instance itself, so writing the form
                // into it would leave a half-updated record behind when the encode refuses an over-long list name.
                var updated = new SlideTriggerDefinition
                {
                    Id = editing.Id,
                    Name = NameBox.Text ?? string.Empty,
                    Parameters = parameters,
                    ExpiresAt = expiresAt
                };

                _store.Update(updated);

                // The record can be gone if the library was refreshed elsewhere; copying an empty marker text would be
                // worse than saying so, because the clipboard would then paste nothing useful into the deck.
                var stored = _store.Find(editing.Id);
                if (stored is null)
                {
                    ClearEditMode();
                    StatusText.Text = string.Format(SR.M_DeletedFormat, updated.Name);
                    RefreshLibrary();
                    return;
                }

                definition = stored;
            }
        }
        catch (ArgumentException exception)
        {
            StatusText.Text = string.Format(SR.M_GenerateFailedFormat, exception.Message);
            return;
        }

        if (saveToFile)
        {
            var path = await _images.ExportAsync(
                TopLevel.GetTopLevel(this),
                definition.MarkerText,
                SafeFileName(definition.Name),
                SR.S_ExportPicker,
                SR.C_PngFileType);

            StatusText.Text = path is null
                ? SR.M_ExportCancelled
                : string.Format(SR.M_SavedFormat, path);
        }
        else
        {
            var copied = await _images.TryCopyToClipboardAsync(TopLevel.GetTopLevel(this), definition.MarkerText);
            StatusText.Text = copied
                ? editing is null
                    ? string.Format(SR.M_CopiedFormat, Describe(definition))
                    : string.Format(SR.M_EditedFormat, definition.Name, Describe(definition))
                : SR.M_ClipboardUnavailable;
        }

        if (editing is not null)
            ClearEditMode();

        RefreshLibrary();
    }

    private SlideTriggerParameters ReadParameters() => new()
    {
        StudentListName = ListBox.Text,
        Count = (int)(CountBox.Value ?? 1),
        ShowAnimation = ShowAnimationBox.IsChecked == true,
        VoiceEnabled = VoiceBox.IsChecked == true,
        DelaySeconds = (double)(DelayBox.Value ?? 1),
        TriggerAfterInputs = (int)(InputsBox.Value ?? 1),
        ArmedTimeoutSeconds = (double)(TimeoutBox.Value ?? 15),
        Timing = TimingBox.SelectedIndex == 1
            ? SlideTriggerTiming.AfterInputs
            : SlideTriggerTiming.AfterDelay,
        Gender = GenderBox.Text,
        Group = GroupBox.Text
    };

    /// <summary>Zero days means "never expires", which is what a re-used deck wants.</summary>
    private DateTimeOffset? ReadExpiry()
    {
        var days = (int)(ExpiresBox.Value ?? 0);
        return days <= 0 ? null : DateTimeOffset.UtcNow.AddDays(days);
    }

    // ---------------------------------------------------------------- editing

    /// <summary>
    ///     Loads an existing marker into the form. The parameters travel inside the PNG, so this is not a live edit of
    ///     what a deck already does: it re-issues the marker and the teacher replaces the image. The banner says that
    ///     explicitly, because an "edited" status that never reaches the slide would be a lie about the deck.
    /// </summary>
    private void EditMarker_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: SlideTriggerDefinition definition })
            return;

        _editing = definition;
        FillForm(definition);

        EditBannerText.Text = string.Format(SR.M_EditModeFormat, definition.Name);
        EditBanner.IsVisible = true;
        StatusText.Text = string.Empty;
    }

    private void CancelEdit_OnClick(object? sender, RoutedEventArgs e)
    {
        ClearEditMode();
        StatusText.Text = SR.M_EditCanceled;
    }

    private void FillForm(SlideTriggerDefinition definition)
    {
        var parameters = definition.Parameters;

        NameBox.Text = definition.Name;
        ListBox.Text = parameters.StudentListName ?? string.Empty;
        CountBox.Value = parameters.Count;
        ExpiresBox.Value = RemainingDays(definition.ExpiresAt);
        ShowAnimationBox.IsChecked = parameters.ShowAnimation;
        VoiceBox.IsChecked = parameters.VoiceEnabled;
        TimingBox.SelectedIndex = parameters.Timing == SlideTriggerTiming.AfterInputs ? 1 : 0;
        DelayBox.Value = (decimal)parameters.DelaySeconds;
        InputsBox.Value = parameters.TriggerAfterInputs;
        TimeoutBox.Value = (decimal)parameters.ArmedTimeoutSeconds;
        GenderBox.Text = parameters.Gender ?? string.Empty;
        GroupBox.Text = parameters.Group ?? string.Empty;
    }

    private void ClearEditMode()
    {
        _editing = null;
        EditBanner.IsVisible = false;
        EditBannerText.Text = string.Empty;
    }

    /// <summary>
    ///     The expiry is an instant but the form takes a number of days, so editing would otherwise push an old
    ///     marker's expiry forward by however long ago it was created.
    /// </summary>
    private static decimal RemainingDays(DateTimeOffset? expiresAt)
    {
        if (expiresAt is not { } expiry)
            return 0;

        var days = Math.Ceiling((expiry - DateTimeOffset.UtcNow).TotalDays);
        return days <= 0 ? 0 : (decimal)days;
    }

    // ---------------------------------------------------------------- library

    private void NewSession_OnClick(object? sender, RoutedEventArgs e)
    {
        SessionText.Text = string.Format(SR.M_SessionFormat, _store.StartNewSession());
        StatusText.Text = SR.M_SessionRolled;
        RefreshLibrary();
    }

    /// <summary>
    ///     One-shot check of what is on screen right now. This is the only way to answer "does this work on my
    ///     projector" without waiting for a lesson, and it is deliberately manual: it reads one frame per display, only
    ///     when the button is pressed.
    /// </summary>
    /// <remarks>
    ///     Deliberately <em>not</em> gated on "a presentation is in front". Clicking this button puts our own settings
    ///     window in front, so that gate — which exists to stop background sampling — would make the button refuse every
    ///     single time. Every display is read once instead, which is also the honest answer on a dual-screen classroom
    ///     machine where the deck is full-screen on the other monitor.
    /// </remarks>
    private void Probe_OnClick(object? sender, RoutedEventArgs e)
    {
        var probe = IAppHost.TryGetService<IPlatformServiceRoot>()?.ScreenProbe;
        if (probe is not { IsSupported: true })
        {
            StatusText.Text = SR.M_ProbeUnsupported;
            return;
        }

        var frames = probe.CaptureAllDisplays();
        if (frames.Count == 0)
        {
            StatusText.Text = SR.M_ProbeUnsupported;
            return;
        }

        foreach (var frame in frames)
        {
            var decoded = _decoder.Decode(frame);
            if (decoded is not { Success: true, Parameters: { } parameters })
                continue;

            StatusText.Text = string.Format(SR.M_ProbeFoundFormat, DescribeParameters(parameters));
            return;
        }

        StatusText.Text = SR.M_ProbeNotFound;
    }

    private void RefreshLibrary()
    {
        SessionText.Text = string.Format(SR.M_SessionFormat, _store.Session);
        EmptyHint.IsVisible = _store.Triggers.Count == 0;

        MarkerRows.Children.Clear();
        foreach (var definition in _store.Triggers.OrderByDescending(item => item.CreatedAt))
            MarkerRows.Children.Add(BuildRow(definition));
    }

    private Control BuildRow(SlideTriggerDefinition definition)
    {
        var summary = new TextBlock
        {
            Text = $"{definition.Name} · {Describe(definition)}",
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };

        var used = definition.IsConsumedIn(_store.Session);
        var state = new TextBlock
        {
            Text = used ? SR.M_UsedThisSession : SR.M_NotUsed,
            FontSize = 12,
            Opacity = 0.75,
            VerticalAlignment = VerticalAlignment.Center
        };

        var copy = new Button { Content = SR.C_Copy, Tag = definition };
        copy.Click += CopyMarker_OnClick;

        var export = new Button { Content = SR.C_SaveAs, Tag = definition };
        export.Click += ExportMarker_OnClick;

        var edit = new Button { Content = SR.C_Edit, Tag = definition };
        edit.Click += EditMarker_OnClick;

        var remove = new Button { Content = SR.C_Delete, Tag = definition };
        remove.Click += DeleteMarker_OnClick;

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children = { edit, copy, export, remove }
        };

        return new Border
        {
            Padding = new Thickness(10),
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Colors.Gray, 0.25),
            Child = new DockPanel
            {
                LastChildFill = true,
                Children =
                {
                    new StackPanel
                    {
                        Orientation = Orientation.Vertical,
                        Spacing = 4,
                        Children = { summary, state }
                    },
                    actions
                }
            }
        };
    }

    private async void CopyMarker_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: SlideTriggerDefinition definition })
            return;

        var copied = await _images.TryCopyToClipboardAsync(TopLevel.GetTopLevel(this), definition.MarkerText);
        StatusText.Text = copied
            ? string.Format(SR.M_CopiedFormat, Describe(definition))
            : SR.M_ClipboardUnavailable;
    }

    private async void ExportMarker_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: SlideTriggerDefinition definition })
            return;

        var path = await _images.ExportAsync(
            TopLevel.GetTopLevel(this),
            definition.MarkerText,
            SafeFileName(definition.Name),
            SR.S_ExportPicker,
            SR.C_PngFileType);

        if (path is not null)
            StatusText.Text = string.Format(SR.M_SavedFormat, path);
    }

    private void DeleteMarker_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: SlideTriggerDefinition definition })
            return;

        _store.Remove(definition.Id);

        // The form may still be pointing at the record that just disappeared; keeping it would let the next save
        // silently fall back to creating a new marker with the same name.
        if (_editing is { } editing && string.Equals(editing.Id, definition.Id, StringComparison.Ordinal))
            ClearEditMode();

        StatusText.Text = string.Format(SR.M_DeletedFormat, definition.Name);
        RefreshLibrary();
    }

    // ---------------------------------------------------------------- helpers

    private string Describe(SlideTriggerDefinition definition)
    {
        var parts = new List<string> { DescribeParameters(definition.Parameters) };

        if (definition.ExpiresAt is { } expiry)
            parts.Add(string.Format(SR.M_ExpiresFormat, expiry.ToLocalTime().ToString("yyyy-MM-dd")));

        return string.Join(" · ", parts);
    }

    /// <summary>
    ///     The parameter half of a marker's summary, shared with the probe result so "what did it read off the screen"
    ///     and "what is in my library" cannot drift apart in wording.
    /// </summary>
    private string DescribeParameters(SlideTriggerParameters parameters)
    {
        var parts = new List<string>
        {
            string.Format(SR.M_CountFormat, parameters.Count),
            parameters.StudentListName ?? SR.M_ActiveList,
            parameters.Timing == SlideTriggerTiming.AfterInputs
                ? string.Format(SR.M_TimingInputsFormat, parameters.TriggerAfterInputs)
                : string.Format(SR.M_TimingDelayFormat, parameters.DelaySeconds)
        };

        if (parameters.AllowRepeat)
            parts.Add(SR.M_AllowRepeatBadge);

        if (parameters.IgnoreHistoryWeights)
            parts.Add(SR.M_IgnoreWeightsBadge);

        // Both flags come from a marker made before the page stopped offering them: the trigger path cannot express
        // either, so the marker will be refused at fire time. Saying so here is what turns a silent no-op into a
        // diagnosable one.
        if (parameters.AllowRepeat || parameters.IgnoreHistoryWeights)
            parts.Add(SR.M_UnsupportedParamsBadge);

        return string.Join(" · ", parts);
    }

    private static string SafeFileName(string name)
    {
        var invalid = System.IO.Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(character => invalid.Contains(character) ? '_' : character).ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "marker" : cleaned.Trim();
    }
}
