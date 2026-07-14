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

    /// <summary>语言切换后重置主题色名称缓存（Name 由 LocalizationHelper 在首次访问时固化）。</summary>
    public void RefreshPrimaryColors() { _primaryColors = null; }

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
        ApplyAccentColorInternal(color, true);
    }

    /// <summary>
    /// 读取 Windows 系统主题色并应用
    /// </summary>
    private void ApplySystemAccentColor()
    {
        var accentColor = _uiSettings.GetColorValue(Windows.UI.ViewManagement.UIColorType.Accent);
        ApplyAccentColorInternal(accentColor, false);
    }

    /// <summary>
    /// 应用自定义主题色（由用户通过 ColorPicker 选择）
    /// </summary>
    public void ApplyCustomAccentColor(Color color)
    {
        ApplyAccentColorInternal(color, true);
    }

    private static readonly Color Dark = Color.FromArgb(255, 0, 0, 0);
    private static readonly Color White = Color.FromArgb(255, 255, 255, 255);

    // ── 持久化主题色画刷 ──
    // 只创建一次并注册进资源字典；换色时仅修改其 .Color 属性。
    // 由于 SolidColorBrush.Color 是依赖属性，任何正在使用该画刷的控件
    // （含 NavigationView 选中态）都会即时重绘，无需等待 hover 或
    // 重新进入视觉状态——这正是旧实现每次 new 新实例导致选中底不刷新的根因。
    private static readonly SolidColorBrush
        _accentFillDefault = new(), _accentFillSecondary = new(), _accentFillTertiary = new(), _accentFillDisabled = new(),
        _accentTextPrimary = new(), _accentTextSecondary = new(), _accentTextTertiary = new(), _accentTextDisabled = new(),
        _accentElevationBorder = new(), _strokeOnAccentDefault = new(), _strokeOnAccentSecondary = new(),
        _strokeOnAccentTertiary = new(), _strokeOnAccentDisabled = new(),
        _textOnAccentPrimary = new(), _textOnAccentSecondary = new(), _textOnAccentDisabled = new(), _textOnAccentSelected = new(),
        _navIndicator = new(), _navSelectedBg = new(), _navSelectedBgHover = new(), _navSelectedBgPressed = new(),
        _navSelectedFg = new(), _navSelectedFgHover = new(), _navSelectedFgPressed = new(), _navSeparator = new();

    /// <param name="customAccent">
    /// true  = 应用我们的强调色引擎（覆盖系统默认，圈内文字固定白色）；
    /// false = 系统接管，移除所有覆盖，让 WinUI 原生 accent（含系统强调色与明暗反色）生效。
    /// </param>
    private static void ApplyAccentColorInternal(Color color, bool customAccent)
    {
        if (App.CurrentWindow?.Content is not FrameworkElement element)
            return;

        var res = element.Resources;

        // 系统接管：移除我们对所有 accent 画刷的覆盖，恢复框架原生行为。
        // 此时深浅模式下的 on-accent 文本（圈内黑/白）由系统按系统强调色决定，
        // 我们不再强制白色。
        if (!customAccent)
        {
            RemoveAccentBrushes(res);
            return;
        }

        // 确保持久画刷已注册进【当前窗口】的资源字典。
        // 用「引用比较」判断而非 static bool：切换语言时 App.RecreateMainWindow
        // 会新建窗口，旧 static 标志已是 true 会让新窗口漏注册 → 主题色整体回退默认。
        // 这里对每个新窗口都会重新注册（幂等、开销极小）。
        if (!(res.TryGetValue("AccentFillColorDefaultBrush", out var probe)
              && ReferenceEquals(probe, _accentFillDefault)))
        {
            RegisterAccentBrushes(res);
        }

        // ── 基础色系（Color，作为部分默认模板的中间量） ──
        res["SystemAccentColor"] = color;
        res["SystemAccentColorLight1"] = LightenColor(color, 0.2);
        res["SystemAccentColorLight2"] = LightenColor(color, 0.4);
        res["SystemAccentColorLight3"] = LightenColor(color, 0.6);
        res["SystemAccentColorDark1"] = DarkenColor(color, 0.2);
        res["SystemAccentColorDark2"] = DarkenColor(color, 0.4);
        res["SystemAccentColorDark3"] = DarkenColor(color, 0.6);

        // ── 填充画刷 ──
        _accentFillDefault.Color = color;
        _accentFillSecondary.Color = WithOpacity(color, 0.9);
        _accentFillTertiary.Color = WithOpacity(color, 0.8);
        _accentFillDisabled.Color = WithOpacity(color, 0.36);

        // ── 文本画刷 ──
        _accentTextPrimary.Color = color;
        _accentTextSecondary.Color = color;
        _accentTextTertiary.Color = WithOpacity(color, 0.8);
        _accentTextDisabled.Color = WithOpacity(color, 0.36);

        // ── 描边 / 控件边框 ──
        _accentElevationBorder.Color = color;
        _strokeOnAccentDefault.Color = WithOpacity(color, 0.14);
        _strokeOnAccentSecondary.Color = WithOpacity(color, 0.08);
        _strokeOnAccentTertiary.Color = WithOpacity(color, 0.04);
        _strokeOnAccentDisabled.Color = WithOpacity(color, 0.0);

        // ── Accent 上的文本 ──
        // 固定白色：RadioButton 选中圆点 / ToggleSwitch 开关蕊 / CheckBox 勾选标记
        // 一律显示为白色，不再随强调色亮度反色（避免在亮色主题下圈内变黑）。
        _textOnAccentPrimary.Color = White;
        _textOnAccentSecondary.Color = WithOpacity(White, 0.7);
        _textOnAccentDisabled.Color = WithOpacity(White, 0.5);
        _textOnAccentSelected.Color = White;

        // ── NavigationView 选中态 ──
        _navIndicator.Color = color;
        _navSelectedBg.Color = WithOpacity(color, 0.12);
        _navSelectedBgHover.Color = WithOpacity(color, 0.16);
        _navSelectedBgPressed.Color = WithOpacity(color, 0.08);
        _navSelectedFg.Color = color;
        _navSelectedFgHover.Color = color;
        _navSelectedFgPressed.Color = color;
        _navSeparator.Color = WithOpacity(color, 0.2);
    }

    /// <summary>
    /// 把当前强调色覆盖镜像到指定资源字典（如 ContentDialog.Resources）。
    /// ContentDialog 由 Popup 承载，脱离窗口根元素的视觉树，资源查找看不到写在窗口根
    /// 上的强调色覆盖，会回退到框架默认（系统色）。对这类弹出层需手动把共享强调色画刷
    /// 注册进其自身 Resources，才能显示自定义强调色（勾选框/单选/开关等）。
    /// 复用同一批 static 画刷实例（颜色已是最新），系统接管强调色时不处理（弹窗走框架
    /// 默认即系统色，本就正确）。
    /// </summary>
    public static void ApplyAccentBrushesTo(ResourceDictionary res)
    {
        if (App.CurrentWindow?.Content is not FrameworkElement element)
            return;

        // 仅当窗口根当前已启用自定义强调色覆盖时才镜像（用引用比较判定，与 ApplyAccentColorInternal 一致）
        if (element.Resources.TryGetValue("AccentFillColorDefaultBrush", out var probe)
            && ReferenceEquals(probe, _accentFillDefault))
        {
            RegisterAccentBrushes(res);
        }
    }

    /// <summary>
    /// 把持久强调色画刷注册进指定窗口的资源字典。全部复用同一批 static 画刷实例，
    /// 换色时只改各实例 .Color，即时刷新、无需 hover。
    ///
    /// 关键：ToggleSwitch / RadioButton / CheckBox 的选中态在模板里引用的是「控件专属键」
    /// （如 ToggleSwitchFillOn、RadioButtonOuterEllipseCheckedFill），而这些专属键的
    /// {ThemeResource AccentFillColorDefaultBrush} 是在【框架字典自身作用域】解析的，
    /// 看不到我们写在窗口根的 AccentFillColorDefaultBrush 覆盖 —— 所以必须直接覆盖
    /// 这些控件专属键本身，否则开关/单选/复选仍显示框架默认蓝。
    /// </summary>
    private static void RegisterAccentBrushes(ResourceDictionary res)
    {
        // ── 现代 Fluent Accent 画刷 ──
        res["AccentFillColorDefaultBrush"] = _accentFillDefault;
        res["AccentFillColorSecondaryBrush"] = _accentFillSecondary;
        res["AccentFillColorTertiaryBrush"] = _accentFillTertiary;
        res["AccentFillColorDisabledBrush"] = _accentFillDisabled;
        res["AccentTextFillColorPrimaryBrush"] = _accentTextPrimary;
        res["AccentTextFillColorSecondaryBrush"] = _accentTextSecondary;
        res["AccentTextFillColorTertiaryBrush"] = _accentTextTertiary;
        res["AccentTextFillColorDisabledBrush"] = _accentTextDisabled;
        res["AccentControlElevationBorderBrush"] = _accentElevationBorder;
        res["ControlStrokeColorOnAccentDefaultBrush"] = _strokeOnAccentDefault;
        res["ControlStrokeColorOnAccentSecondaryBrush"] = _strokeOnAccentSecondary;
        res["ControlStrokeColorOnAccentTertiaryBrush"] = _strokeOnAccentTertiary;
        res["ControlStrokeColorOnAccentDisabledBrush"] = _strokeOnAccentDisabled;
        res["TextOnAccentFillColorPrimaryBrush"] = _textOnAccentPrimary;
        res["TextOnAccentFillColorSecondaryBrush"] = _textOnAccentSecondary;
        res["TextOnAccentFillColorDisabledBrush"] = _textOnAccentDisabled;
        res["TextOnAccentFillColorSelectedTextBrush"] = _textOnAccentSelected;

        // ── 老式 SystemControl Accent 键（部分控件模板版本仍在用，别名兜底） ──
        res["SystemControlHighlightAccentBrush"] = _accentFillDefault;
        res["SystemControlHighlightAltAccentBrush"] = _accentFillDefault;
        res["SystemControlForegroundAccentBrush"] = _accentTextPrimary;
        res["SystemControlBackgroundAccentBrush"] = _accentFillDefault;
        res["SystemControlDisabledAccentBrush"] = _accentFillDisabled;
        res["SystemControlHighlightListAccentLowBrush"] = _accentFillTertiary;
        res["SystemControlHighlightListAccentMediumBrush"] = _accentFillSecondary;
        res["SystemControlHighlightListAccentHighBrush"] = _accentFillDefault;

        // ── ToggleSwitch 开态（专属键） ──
        res["ToggleSwitchFillOn"] = _accentFillDefault;
        res["ToggleSwitchFillOnPointerOver"] = _accentFillSecondary;
        res["ToggleSwitchFillOnPressed"] = _accentFillTertiary;
        res["ToggleSwitchFillOnDisabled"] = _accentFillDisabled;
        res["ToggleSwitchStrokeOn"] = _accentFillDefault;
        res["ToggleSwitchStrokeOnPointerOver"] = _accentFillSecondary;
        res["ToggleSwitchStrokeOnPressed"] = _accentFillTertiary;
        res["ToggleSwitchKnobFillOn"] = _textOnAccentPrimary;

        // ── RadioButton 选中态（专属键） ──
        res["RadioButtonOuterEllipseCheckedFill"] = _accentFillDefault;
        res["RadioButtonOuterEllipseCheckedFillPointerOver"] = _accentFillSecondary;
        res["RadioButtonOuterEllipseCheckedFillPressed"] = _accentFillTertiary;
        res["RadioButtonOuterEllipseCheckedFillDisabled"] = _accentFillDisabled;
        res["RadioButtonOuterEllipseCheckedStroke"] = _accentFillDefault;
        res["RadioButtonOuterEllipseCheckedStrokePointerOver"] = _accentFillSecondary;
        res["RadioButtonOuterEllipseCheckedStrokePressed"] = _accentFillTertiary;
        res["RadioButtonCheckGlyphFill"] = _textOnAccentPrimary;
        res["RadioButtonCheckGlyphFillPointerOver"] = _textOnAccentPrimary;
        res["RadioButtonCheckGlyphFillPressed"] = _textOnAccentPrimary;

        // ── CheckBox 选中态（专属键） ──
        res["CheckBoxCheckBackgroundFillChecked"] = _accentFillDefault;
        res["CheckBoxCheckBackgroundFillCheckedPointerOver"] = _accentFillSecondary;
        res["CheckBoxCheckBackgroundFillCheckedPressed"] = _accentFillTertiary;
        res["CheckBoxCheckBackgroundFillCheckedDisabled"] = _accentFillDisabled;
        res["CheckBoxCheckBackgroundStrokeChecked"] = _accentFillDefault;
        res["CheckBoxCheckBackgroundStrokeCheckedPointerOver"] = _accentFillSecondary;
        res["CheckBoxCheckBackgroundStrokeCheckedPressed"] = _accentFillTertiary;
        res["CheckBoxCheckGlyphForegroundChecked"] = _textOnAccentPrimary;
        res["CheckBoxCheckGlyphForegroundCheckedPointerOver"] = _textOnAccentPrimary;
        res["CheckBoxCheckGlyphForegroundCheckedPressed"] = _textOnAccentPrimary;

        // ── AccentButton 强调按钮（专属键；ContentDialog 默认按钮 / AccentButtonStyle 均用此系列） ──
        res["AccentButtonBackground"] = _accentFillDefault;
        res["AccentButtonBackgroundPointerOver"] = _accentFillSecondary;
        res["AccentButtonBackgroundPressed"] = _accentFillTertiary;
        res["AccentButtonBackgroundDisabled"] = _accentFillDisabled;
        res["AccentButtonForeground"] = _textOnAccentPrimary;
        res["AccentButtonForegroundPointerOver"] = _textOnAccentPrimary;
        res["AccentButtonForegroundPressed"] = _textOnAccentSecondary;
        res["AccentButtonForegroundDisabled"] = _textOnAccentDisabled;
        res["AccentButtonBorderBrush"] = _accentElevationBorder;
        res["AccentButtonBorderBrushPointerOver"] = _accentElevationBorder;
        res["AccentButtonBorderBrushPressed"] = _accentElevationBorder;

        // ── NavigationView 选中态 ──
        res["NavigationViewSelectionIndicator"] = _navIndicator;
        res["NavigationViewItemBackgroundSelected"] = _navSelectedBg;
        res["NavigationViewItemBackgroundSelectedPointerOver"] = _navSelectedBgHover;
        res["NavigationViewItemBackgroundSelectedPressed"] = _navSelectedBgPressed;
        res["NavigationViewItemForegroundSelected"] = _navSelectedFg;
        res["NavigationViewItemForegroundSelectedPointerOver"] = _navSelectedFgHover;
        res["NavigationViewItemForegroundSelectedPressed"] = _navSelectedFgPressed;
        res["NavigationViewItemSeparatorForeground"] = _navSeparator;
    }

    /// <summary>
    /// 系统接管时，移除我们对所有 accent 画刷（含 SystemAccentColor 系列）的覆盖，
    /// 让 WinUI 框架原生定义生效。框架默认即跟随 Windows 系统强调色与明暗主题，
    /// 因此系统的 on-accent 文本黑/白反色也由系统决定。
    /// 幂等：键不存在时 Remove 无副作用。
    /// </summary>
    private static void RemoveAccentBrushes(ResourceDictionary res)
    {
        string[] keys =
        [
            "AccentFillColorDefaultBrush", "AccentFillColorSecondaryBrush", "AccentFillColorTertiaryBrush", "AccentFillColorDisabledBrush",
            "AccentTextFillColorPrimaryBrush", "AccentTextFillColorSecondaryBrush", "AccentTextFillColorTertiaryBrush", "AccentTextFillColorDisabledBrush",
            "AccentControlElevationBorderBrush",
            "ControlStrokeColorOnAccentDefaultBrush", "ControlStrokeColorOnAccentSecondaryBrush", "ControlStrokeColorOnAccentTertiaryBrush", "ControlStrokeColorOnAccentDisabledBrush",
            "TextOnAccentFillColorPrimaryBrush", "TextOnAccentFillColorSecondaryBrush", "TextOnAccentFillColorDisabledBrush", "TextOnAccentFillColorSelectedTextBrush",
            "SystemControlHighlightAccentBrush", "SystemControlHighlightAltAccentBrush", "SystemControlForegroundAccentBrush", "SystemControlBackgroundAccentBrush",
            "SystemControlDisabledAccentBrush", "SystemControlHighlightListAccentLowBrush", "SystemControlHighlightListAccentMediumBrush", "SystemControlHighlightListAccentHighBrush",
            "ToggleSwitchFillOn", "ToggleSwitchFillOnPointerOver", "ToggleSwitchFillOnPressed", "ToggleSwitchFillOnDisabled",
            "ToggleSwitchStrokeOn", "ToggleSwitchStrokeOnPointerOver", "ToggleSwitchStrokeOnPressed", "ToggleSwitchKnobFillOn",
            "RadioButtonOuterEllipseCheckedFill", "RadioButtonOuterEllipseCheckedFillPointerOver", "RadioButtonOuterEllipseCheckedFillPressed", "RadioButtonOuterEllipseCheckedFillDisabled",
            "RadioButtonOuterEllipseCheckedStroke", "RadioButtonOuterEllipseCheckedStrokePointerOver", "RadioButtonOuterEllipseCheckedStrokePressed",
            "RadioButtonCheckGlyphFill", "RadioButtonCheckGlyphFillPointerOver", "RadioButtonCheckGlyphFillPressed",
            "CheckBoxCheckBackgroundFillChecked", "CheckBoxCheckBackgroundFillCheckedPointerOver", "CheckBoxCheckBackgroundFillCheckedPressed", "CheckBoxCheckBackgroundFillCheckedDisabled",
            "CheckBoxCheckBackgroundStrokeChecked", "CheckBoxCheckBackgroundStrokeCheckedPointerOver", "CheckBoxCheckBackgroundStrokeCheckedPressed",
            "CheckBoxCheckGlyphForegroundChecked", "CheckBoxCheckGlyphForegroundCheckedPointerOver", "CheckBoxCheckGlyphForegroundCheckedPressed",
            "AccentButtonBackground", "AccentButtonBackgroundPointerOver", "AccentButtonBackgroundPressed", "AccentButtonBackgroundDisabled",
            "AccentButtonForeground", "AccentButtonForegroundPointerOver", "AccentButtonForegroundPressed", "AccentButtonForegroundDisabled",
            "AccentButtonBorderBrush", "AccentButtonBorderBrushPointerOver", "AccentButtonBorderBrushPressed",
            "NavigationViewSelectionIndicator", "NavigationViewItemBackgroundSelected", "NavigationViewItemBackgroundSelectedPointerOver", "NavigationViewItemBackgroundSelectedPressed",
            "NavigationViewItemForegroundSelected", "NavigationViewItemForegroundSelectedPointerOver", "NavigationViewItemForegroundSelectedPressed", "NavigationViewItemSeparatorForeground",
            "SystemAccentColor", "SystemAccentColorLight1", "SystemAccentColorLight2", "SystemAccentColorLight3",
            "SystemAccentColorDark1", "SystemAccentColorDark2", "SystemAccentColorDark3",
        ];
        foreach (var key in keys)
            res.Remove(key);
    }

    private static Color WithOpacity(Color c, double opacity)
        => Color.FromArgb((byte)(c.A * opacity), c.R, c.G, c.B);

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
