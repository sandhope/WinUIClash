using CommunityToolkit.Mvvm.ComponentModel;
using WinUIClash.Models;
using WinUIClash.Services;

namespace WinUIClash.ViewModels.Settings;

/// <summary>
/// 应用设置 ViewModel
/// </summary>
public partial class AppSettingsViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly AutoLaunchService _autoLaunch;

    public AppSettingsViewModel(AppSettings settings, AutoLaunchService autoLaunch)
    {
        _settings = settings;
        _autoLaunch = autoLaunch;
    }

    public bool MinimizeOnExit
    {
        get => _settings.MinimizeOnExit;
        set { if (_settings.MinimizeOnExit != value) { _settings.MinimizeOnExit = value; OnPropertyChanged(); } }
    }

    public bool AutoLaunch
    {
        get => _settings.AutoLaunch;
        set
        {
            if (_settings.AutoLaunch != value)
            {
                _settings.AutoLaunch = value;
                _autoLaunch.ApplyState(value);
                OnPropertyChanged();
            }
        }
    }

    public bool SilentLaunch
    {
        get => _settings.SilentLaunch;
        set { if (_settings.SilentLaunch != value) { _settings.SilentLaunch = value; OnPropertyChanged(); } }
    }

    public bool AutoRun
    {
        get => _settings.AutoRun;
        set { if (_settings.AutoRun != value) { _settings.AutoRun = value; OnPropertyChanged(); } }
    }

    public bool AutoCheckUpdate
    {
        get => _settings.AutoCheckUpdate;
        set { if (_settings.AutoCheckUpdate != value) { _settings.AutoCheckUpdate = value; OnPropertyChanged(); } }
    }

    public bool CloseConnections
    {
        get => _settings.CloseConnections;
        set { if (_settings.CloseConnections != value) { _settings.CloseConnections = value; OnPropertyChanged(); } }
    }

    public bool OnlyStatisticsProxy
    {
        get => _settings.OnlyStatisticsProxy;
        set { if (_settings.OnlyStatisticsProxy != value) { _settings.OnlyStatisticsProxy = value; OnPropertyChanged(); } }
    }

    // 系统代理
    public bool SystemProxy
    {
        get => _settings.SystemProxy;
        set { if (_settings.SystemProxy != value) { _settings.SystemProxy = value; OnPropertyChanged(); } }
    }

    public string BypassDomains
    {
        get => _settings.BypassDomains;
        set { if (_settings.BypassDomains != value) { _settings.BypassDomains = value; OnPropertyChanged(); } }
    }

    public record LanguageOption(string Label, string Value);

    public LanguageOption[] Languages { get; } =
    [
        new("简体中文", "zh-CN"),
        new("English", "en-US"),
    ];

    public string Language
    {
        get => _settings.Language;
        set
        {
            if (_settings.Language != value)
            {
                _settings.Language = value;
                OnPropertyChanged();
                // Note: language change requires app restart to take full effect
            }
        }
    }
}
