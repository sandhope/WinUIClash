using System.ComponentModel;
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
        _settings.PropertyChanged += OnSettingsPropertyChanged;
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // 托盘 / 仪表盘等外部修改设置后，刷新本页对应绑定
        OnPropertyChanged(e.PropertyName);
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

    public bool AutoRestart
    {
        get => _settings.AutoRestart;
        set { if (_settings.AutoRestart != value) { _settings.AutoRestart = value; OnPropertyChanged(); } }
    }

    public bool ShowNotifications
    {
        get => _settings.ShowNotifications;
        set { if (_settings.ShowNotifications != value) { _settings.ShowNotifications = value; OnPropertyChanged(); } }
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

    public bool ProxyGuardEnabled
    {
        get => _settings.ProxyGuardEnabled;
        set { if (_settings.ProxyGuardEnabled != value) { _settings.ProxyGuardEnabled = value; OnPropertyChanged(); } }
    }

    public int ProxyGuardInterval
    {
        get => _settings.ProxyGuardInterval;
        set { if (_settings.ProxyGuardInterval != value) { _settings.ProxyGuardInterval = value; OnPropertyChanged(); } }
    }

    // TUN 模式
    public bool TunMode
    {
        get => _settings.TunMode;
        set { if (_settings.TunMode != value) { _settings.TunMode = value; OnPropertyChanged(); } }
    }

    public string[] TunStackOptions { get; } = ["mixed", "system", "gvisor"];

    public string TunStack
    {
        get => _settings.TunStack;
        set { if (_settings.TunStack != value) { _settings.TunStack = value; OnPropertyChanged(); } }
    }

}
