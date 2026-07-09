using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using WinUIClash.Models;

namespace WinUIClash.ViewModels.Settings;

/// <summary>
/// 主题设置 ViewModel
/// </summary>
public partial class ThemeSettingsViewModel : ObservableObject
{
    private readonly AppSettings _settings;

    public ThemeSettingsViewModel(AppSettings settings)
    {
        _settings = settings;
    }

    public record ThemeOption(string Label, string Value);

    public ThemeOption[] ThemeModes { get; } =
    [
        new("跟随系统", "System"),
        new("浅色模式", "Light"),
        new("深色模式", "Dark"),
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

    public ThemeColor[] PrimaryColors { get; } =
    [
        new("蓝色", "#2196F3"),
        new("青色", "#00BCD4"),
        new("绿色", "#4CAF50"),
        new("橙色", "#FF9800"),
        new("红色", "#F44336"),
        new("紫色", "#9C27B0"),
        new("粉色", "#E91E63"),
        new("靛蓝", "#3F51B5"),
    ];

    public int PrimaryColorIndex
    {
        get => _settings.PrimaryColorIndex;
        set { if (_settings.PrimaryColorIndex != value) { _settings.PrimaryColorIndex = value; OnPropertyChanged(); } }
    }

    private static void ApplyTheme(string mode)
    {
        if (Application.Current is App app)
        {
            // WinUI 3 没有 ApplicationTheme.Default，"System" 时不设置 RequestedTheme 即可跟随系统
            app.RequestedTheme = mode switch
            {
                "Light" => ApplicationTheme.Light,
                "Dark" => ApplicationTheme.Dark,
                _ => ApplicationTheme.Light // 默认回退到 Light，实际应读取系统设置
            };
        }
    }
}
