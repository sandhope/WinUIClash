using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Windows.UI;
using WinUIClash.Models;
using WinUIClash.Services;

namespace WinUIClash.ViewModels.Settings;

/// <summary>
/// 主题设置 ViewModel
/// </summary>
public partial class ThemeSettingsViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly Windows.UI.ViewManagement.UISettings _uiSettings;
    private readonly DispatcherQueue _dispatcher;

    public ThemeSettingsViewModel(AppSettings settings)
    {
        _settings = settings;
        _dispatcher = DispatcherQueue.GetForCurrentThread()!;
        _uiSettings = new Windows.UI.ViewManagement.UISettings();

        // 监听系统主题变化，实时跟随
        _uiSettings.ColorValuesChanged += (_, _) =>
        {
            if (_settings.ThemeMode == "System")
            {
                _dispatcher.TryEnqueue(() => ApplyTheme("System"));
            }
        };
    }

    public record ThemeOption(string Label, string Value);

    private ThemeOption[]? _themeModes;
    public ThemeOption[] ThemeModes => _themeModes ??=
    [
        new(LocalizationHelper.GetString("ThemeModeSystem.Text"), "System"),
        new(LocalizationHelper.GetString("ThemeModeLight.Text"), "Light"),
        new(LocalizationHelper.GetString("ThemeModeDark.Text"), "Dark"),
    ];

    public string ThemeMode
    {
        get => _settings.ThemeMode;
        set
        {
            if (_settings.ThemeMode != value)
            {
                _settings.ThemeMode = value;
                OnPropertyChanged();
                ApplyTheme(value);
                OnPropertyChanged(nameof(IsSystemTheme));
                OnPropertyChanged(nameof(IsLightTheme));
                OnPropertyChanged(nameof(IsDarkTheme));
            }
        }
    }

    // ── Radio button 绑定 ──

    public bool IsSystemTheme
    {
        get => _settings.ThemeMode == "System";
        set { if (value) ThemeMode = "System"; }
    }

    public bool IsLightTheme
    {
        get => _settings.ThemeMode == "Light";
        set { if (value) ThemeMode = "Light"; }
    }

    public bool IsDarkTheme
    {
        get => _settings.ThemeMode == "Dark";
        set { if (value) ThemeMode = "Dark"; }
    }

    /// <summary>预设主题色板</summary>
    public record ThemeColor(string Name, string Hex);

    private ThemeColor[]? _primaryColors;
    public ThemeColor[] PrimaryColors => _primaryColors ??=
    [
        new(LocalizationHelper.GetString("ColorBlue.Text"), "#2196F3"),
        new(LocalizationHelper.GetString("ColorCyan.Text"), "#00BCD4"),
        new(LocalizationHelper.GetString("ColorGreen.Text"), "#4CAF50"),
        new(LocalizationHelper.GetString("ColorOrange.Text"), "#FF9800"),
        new(LocalizationHelper.GetString("ColorRed.Text"), "#F44336"),
        new(LocalizationHelper.GetString("ColorPurple.Text"), "#9C27B0"),
        new(LocalizationHelper.GetString("ColorPink.Text"), "#E91E63"),
        new(LocalizationHelper.GetString("ColorIndigo.Text"), "#3F51B5"),
    ];

    public int PrimaryColorIndex
    {
        get => _settings.PrimaryColorIndex;
        set
        {
            if (_settings.PrimaryColorIndex != value)
            {
                _settings.PrimaryColorIndex = value;
                OnPropertyChanged();
                ApplyAccentColor();
            }
        }
    }

    private void ApplyTheme(string mode)
    {
        // WinUI 3 中 Application.RequestedTheme 是只读的，
        // 必须设置到窗口根 FrameworkElement 上才能生效
        if (App.CurrentWindow?.Content is FrameworkElement root)
        {
            var theme = mode switch
            {
                "Light" => ApplicationTheme.Light,
                "Dark" => ApplicationTheme.Dark,
                _ => GetSystemTheme() // "System" → 跟随系统
            };
            root.RequestedTheme = theme switch
            {
                ApplicationTheme.Light => ElementTheme.Light,
                ApplicationTheme.Dark => ElementTheme.Dark,
                _ => ElementTheme.Default
            };
        }

        ApplyAccentColor();
    }

    /// <summary>
    /// 将当前选择的主题色应用到窗口资源，覆盖系统默认 Accent 色系
    /// </summary>
    private void ApplyAccentColor()
    {
        var hex = PrimaryColors[_settings.PrimaryColorIndex].Hex;
        var color = ParseHexColor(hex);
        ApplyAccentColorInternal(color);
    }

    /// <summary>
    /// 应用自定义主题色（由用户通过 ColorPicker 选择）
    /// </summary>
    public void ApplyCustomAccentColor(Color color)
    {
        ApplyAccentColorInternal(color);
    }

    private static void ApplyAccentColorInternal(Color color)
    {
        if (App.CurrentWindow?.Content is FrameworkElement element)
        {
            element.Resources["SystemAccentColor"] = color;
            element.Resources["SystemAccentColorLight1"] = LightenColor(color, 0.2);
            element.Resources["SystemAccentColorLight2"] = LightenColor(color, 0.4);
            element.Resources["SystemAccentColorLight3"] = LightenColor(color, 0.6);
            element.Resources["SystemAccentColorDark1"] = DarkenColor(color, 0.2);
            element.Resources["SystemAccentColorDark2"] = DarkenColor(color, 0.4);
            element.Resources["SystemAccentColorDark3"] = DarkenColor(color, 0.6);
        }
    }

    /// <summary>
    /// 应用启动时调用一次，恢复保存的主题和主题色
    /// </summary>
    public static void InitializeTheme()
    {
        var vm = ServiceLocator.Get<ThemeSettingsViewModel>();
        vm.ApplyTheme(vm._settings.ThemeMode);
        vm.ApplyAccentColor();
    }

    private static Color ParseHexColor(string hex)
    {
        hex = hex.TrimStart('#');
        byte a = 255, r = 0, g = 0, b = 0;
        if (hex.Length == 8)
        {
            a = Convert.ToByte(hex[..2], 16);
            r = Convert.ToByte(hex[2..4], 16);
            g = Convert.ToByte(hex[4..6], 16);
            b = Convert.ToByte(hex[6..8], 16);
        }
        else if (hex.Length == 6)
        {
            r = Convert.ToByte(hex[..2], 16);
            g = Convert.ToByte(hex[2..4], 16);
            b = Convert.ToByte(hex[4..6], 16);
        }
        return Color.FromArgb(a, r, g, b);
    }

    private static Color LightenColor(Color color, double amount)
    {
        return Color.FromArgb(
            color.A,
            (byte)(color.R + (255 - color.R) * amount),
            (byte)(color.G + (255 - color.G) * amount),
            (byte)(color.B + (255 - color.B) * amount));
    }

    private static Color DarkenColor(Color color, double amount)
    {
        return Color.FromArgb(
            color.A,
            (byte)(color.R * (1 - amount)),
            (byte)(color.G * (1 - amount)),
            (byte)(color.B * (1 - amount)));
    }

    private ApplicationTheme GetSystemTheme()
    {
        var bgColor = _uiSettings.GetColorValue(Windows.UI.ViewManagement.UIColorType.Background);
        // 系统背景为深色 → 当前是暗色模式
        return bgColor.R < 128 ? ApplicationTheme.Dark : ApplicationTheme.Light;
    }
}
