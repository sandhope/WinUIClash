using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
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

        // 监听系统主题和颜色变化，实时跟随
        _uiSettings.ColorValuesChanged += (_, _) =>
        {
            _dispatcher.TryEnqueue(() =>
            {
                if (_settings.ThemeMode == "System")
                    ApplyTheme("System");

                if (_settings.UseSystemAccentColor)
                    ApplySystemAccentColor();
            });
        };
    }

    // ── 使用系统主题色 ──

    public bool UseSystemAccentColor
    {
        get => _settings.UseSystemAccentColor;
        set
        {
            if (_settings.UseSystemAccentColor != value)
            {
                _settings.UseSystemAccentColor = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(UsePresetColors));
                if (value)
                    ApplySystemAccentColor();
                else
                    ApplyAccentColor();
            }
        }
    }

    /// <summary>当启用系统色时禁用预设色板</summary>
    public bool UsePresetColors => !_settings.UseSystemAccentColor;

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
        var theme = mode switch
        {
            "Light" => ApplicationTheme.Light,
            "Dark" => ApplicationTheme.Dark,
            _ => GetSystemTheme() // "System" → 跟随系统
        };

        var dark = theme == ApplicationTheme.Dark;

        if (App.CurrentWindow?.Content is FrameworkElement root)
        {
            root.RequestedTheme = theme switch
            {
                ApplicationTheme.Light => ElementTheme.Light,
                ApplicationTheme.Dark => ElementTheme.Dark,
                _ => ElementTheme.Default
            };
        }

        ApplyAccentColor();
        ApplyCaptionButtonColors(dark);
    }

    /// <summary>
    /// 同步非客户区按钮（最小化/最大化/关闭）配色到当前有效主题。
    /// 接管标题栏后这些按钮由 DWM 绘制、默认跟随系统主题；当应用被强制为与系统
    /// 不同的主题（例如 Ctrl+Shift+T 切到深色而系统仍是浅色）时，按钮会因配色相反
    /// 而几乎不可见。这里按应用有效主题同步，保证明暗两种模式下按钮始终清晰可读，
    /// 观感与 WinSing 一致。
    /// </summary>
    private static void ApplyCaptionButtonColors(bool dark)
    {
        try
        {
            var titleBar = App.CurrentWindow?.AppWindow?.TitleBar;
            if (titleBar is null) return;

            var fg = dark ? White : Dark;
            var hoverBg = dark
                ? Color.FromArgb(40, 255, 255, 255)
                : Color.FromArgb(40, 0, 0, 0);
            var pressedBg = dark
                ? Color.FromArgb(64, 255, 255, 255)
                : Color.FromArgb(64, 0, 0, 0);
            var inactiveFg = dark
                ? Color.FromArgb(255, 150, 150, 150)
                : Color.FromArgb(255, 120, 120, 120);

            titleBar.ButtonForegroundColor = fg;
            titleBar.ButtonBackgroundColor = Color.FromArgb(0, 0, 0, 0);
            titleBar.ButtonHoverForegroundColor = fg;
            titleBar.ButtonHoverBackgroundColor = hoverBg;
            titleBar.ButtonPressedForegroundColor = fg;
            titleBar.ButtonPressedBackgroundColor = pressedBg;
            titleBar.ButtonInactiveForegroundColor = inactiveFg;
            titleBar.ButtonInactiveBackgroundColor = Color.FromArgb(0, 0, 0, 0);
        }
        catch
        {
            // 标题栏尚未就绪时忽略
        }
    }

    /// <summary>
    /// 将当前选择的主题色应用到窗口资源，覆盖系统默认 Accent 色系
    /// </summary>
    private void ApplyAccentColor()
    {
        if (_settings.UseSystemAccentColor)
        {
            ApplySystemAccentColor();
            return;
        }

        var hex = PrimaryColors[_settings.PrimaryColorIndex].Hex;
        var color = ParseHexColor(hex);
        ApplyAccentColorInternal(color);
    }

    /// <summary>
    /// 读取 Windows 系统主题色并应用
    /// </summary>
    private void ApplySystemAccentColor()
    {
        var accentColor = _uiSettings.GetColorValue(Windows.UI.ViewManagement.UIColorType.Accent);
        ApplyAccentColorInternal(accentColor);
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
            var res = element.Resources;

            // ── 基础色系（Color） ──
            res["SystemAccentColor"] = color;
            res["SystemAccentColorLight1"] = LightenColor(color, 0.2);
            res["SystemAccentColorLight2"] = LightenColor(color, 0.4);
            res["SystemAccentColorLight3"] = LightenColor(color, 0.6);
            res["SystemAccentColorDark1"] = DarkenColor(color, 0.2);
            res["SystemAccentColorDark2"] = DarkenColor(color, 0.4);
            res["SystemAccentColorDark3"] = DarkenColor(color, 0.6);

            // ── 填充画刷 ──
            res["AccentFillColorDefaultBrush"] = Brush(color);
            res["AccentFillColorSecondaryBrush"] = Brush(color, 0.9);
            res["AccentFillColorTertiaryBrush"] = Brush(color, 0.8);
            res["AccentFillColorDisabledBrush"] = Brush(color, 0.36);

            // ── 文本画刷 ──
            res["AccentTextFillColorPrimaryBrush"] = Brush(color);
            res["AccentTextFillColorSecondaryBrush"] = Brush(color);
            res["AccentTextFillColorTertiaryBrush"] = Brush(color, 0.8);
            res["AccentTextFillColorDisabledBrush"] = Brush(color, 0.36);

            // ── 描边 / 控件边框 ──
            res["AccentControlElevationBorderBrush"] = Brush(color);
            res["ControlStrokeColorOnAccentDefaultBrush"] = Brush(color, 0.14);
            res["ControlStrokeColorOnAccentSecondaryBrush"] = Brush(color, 0.08);

            // ── Accent 上的文本 ──
            var onAccent = Luminance(color) > 0.5 ? Dark : White;
            res["TextOnAccentFillColorPrimaryBrush"] = Brush(onAccent);
            res["TextOnAccentFillColorSecondaryBrush"] = Brush(onAccent, 0.7);
            res["TextOnAccentFillColorDisabledBrush"] = Brush(onAccent, 0.5);
            res["TextOnAccentFillColorSelectedTextBrush"] = Brush(onAccent);

            // ── NavigationView 选中态 ──
            res["NavigationViewSelectionIndicator"] = Brush(color);
            res["NavigationViewItemBackgroundSelected"] = Brush(color, 0.12);
            res["NavigationViewItemBackgroundSelectedPointerOver"] = Brush(color, 0.16);
            res["NavigationViewItemBackgroundSelectedPressed"] = Brush(color, 0.08);
            res["NavigationViewItemForegroundSelected"] = Brush(color);
            res["NavigationViewItemForegroundSelectedPointerOver"] = Brush(color);
            res["NavigationViewItemForegroundSelectedPressed"] = Brush(color);
            res["NavigationViewItemSeparatorForeground"] = Brush(color, 0.2);
        }
    }

    private static readonly Color Dark = Color.FromArgb(255, 0, 0, 0);
    private static readonly Color White = Color.FromArgb(255, 255, 255, 255);

    private static SolidColorBrush Brush(Color c, double opacity = 1.0)
        => new() { Color = opacity < 1.0 ? WithOpacity(c, opacity) : c };

    private static Color WithOpacity(Color c, double opacity)
        => Color.FromArgb((byte)(c.A * opacity), c.R, c.G, c.B);

    private static double Luminance(Color c)
        => (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255;

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
