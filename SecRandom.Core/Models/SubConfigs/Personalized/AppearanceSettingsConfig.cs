using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using SecRandom.Core.Enums.Configs;

namespace SecRandom.Core.Models.SubConfigs.Personalized;

public partial class AppearanceSettingsConfig : ObservableObject
{
    [ObservableProperty] private ThemeMode _theme = ThemeMode.Auto;
    [ObservableProperty] private string _font = GlobalConstants.DefaultFontFamily;
    [ObservableProperty] private FontWeightMode _fontWeight = FontWeightMode.Regular;
    // 默认用本分支自己的品牌色，而不是跟随系统强调色：跟随系统时新装用户看到的
    // 仍是 Windows 蓝，和上游在外观上几乎分不开，用户仍可在外观设置里切回跟随系统。
    [ObservableProperty] private ThemeColorMode _themeColorMode = ThemeColorMode.Custom;
    [ObservableProperty] private Color _themeColor = Color.Parse(GlobalConstants.DefaultThemeColor);
}