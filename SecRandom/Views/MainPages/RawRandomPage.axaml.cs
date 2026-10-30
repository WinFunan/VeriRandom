using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using SecRandom.Core.Attributes;
using SecRandom.Core.Enums;
using SecRandom.Core.Icons;
using SecRandom.ViewModels.MainPages;
using SR = SecRandom.Langs.MainPages.RawRandom.Resources;

namespace SecRandom.Views.MainPages;

/// <summary>
///     主界面「随机数」页：在给定范围内生成一个裸随机数。
/// </summary>
[PageInfo("main.rawRandom", FluentIcons.NumberSymbolFilled, location: PageLocation.Bottom, hidePageTitle: true)]
public sealed partial class RawRandomPage : UserControl
{
    public RawRandomPage(RawRandomPageViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();
        Loaded += OnLoaded;
    }

    public RawRandomPageViewModel ViewModel { get; }

    /// <summary>
    ///     Copies exactly what is on screen. Long hex output is the whole reason this exists: a 1280-bit result is 320
    ///     characters, and "select it with the mouse" is not a workflow a teacher can use in front of a class.
    /// </summary>
    private async void CopyResult_OnClick(object? sender, RoutedEventArgs e)
    {
        var text = ViewModel.ResultText;

        if (string.IsNullOrEmpty(text))
        {
            ViewModel.StatusText = SR.M_NothingToCopy;
            return;
        }

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null)
        {
            ViewModel.StatusText = SR.M_ClipboardUnavailable;
            return;
        }

        await clipboard.SetTextAsync(text);
        ViewModel.StatusText = string.Format(SR.M_CopiedResultFormat, text.Length);
    }

    /// <summary>
    ///     每次进页面都从加密的安全设置里重读锁定状态：另一个窗口可能刚改过它，页面缓存下来的旧值会让"已锁定"
    ///     的界面允许编辑。
    /// </summary>
    private void OnLoaded(object? sender, RoutedEventArgs e) => ViewModel.RefreshLockState();

    /// <summary>
    ///     锁定/解锁都走 <c>ISecurityService</c> 的密码闸门。控件是 <c>OneWay</c> 绑定，点击后由这里决定最终状态：
    ///     用户在密码框上取消时，必须把勾选框拨回真实状态，否则界面会显示"已解锁"而实际仍然锁着。
    /// </summary>
    private async void LockSettings_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox box)
            return;

        var desired = box.IsChecked == true;
        await ViewModel.SetLockedAsync(TopLevel.GetTopLevel(this), desired);
        box.IsChecked = ViewModel.IsSettingsLocked;
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
