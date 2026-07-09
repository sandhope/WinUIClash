using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinUIClash.Models;
using WinUIClash.Services;

namespace WinUIClash.ViewModels;

/// <summary>
/// 配置页 ViewModel — 配置管理、订阅更新
/// </summary>
public partial class ProfilesViewModel : ObservableObject, IDisposable
{
    private readonly IClashService _clash;
    private readonly NotificationService _notification;
    private Timer? _autoUpdateTimer;
    private bool _initialized;

    public ProfilesViewModel(IClashService clash, NotificationService notification)
    {
        _clash = clash;
        _notification = notification;
    }

    [ObservableProperty] private ObservableCollection<Profile> _profiles = new();
    [ObservableProperty] private Profile? _activeProfile;
    [ObservableProperty] private bool _isLoading;

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        var list = await _clash.GetProfilesAsync();
        Profiles = new ObservableCollection<Profile>(list);
        ActiveProfile = Profiles.FirstOrDefault(p => p.IsActive);
        IsLoading = false;
    }

    [RelayCommand]
    private async Task SelectProfileAsync(Profile? profile)
    {
        if (profile == null) return;
        try
        {
            await _clash.SwitchProfileAsync(profile.Id);
            foreach (var p in Profiles) p.IsActive = p.Id == profile.Id;
            ActiveProfile = Profiles.FirstOrDefault(p => p.IsActive);
        }
        catch (Exception ex)
        {
            _notification.Error(
                LocalizationHelper.GetString("ErrorCloseTitle.Text"),
                ex.Message);
        }
    }

    [RelayCommand]
    private async Task SyncProfileAsync(Profile? profile)
    {
        if (profile == null) return;
        try
        {
            await _clash.SyncProfileAsync(profile.Id);
            profile.LastUpdate = DateTime.Now;
        }
        catch (Exception ex)
        {
            _notification.Error(
                LocalizationHelper.GetString("ErrorSyncTitle.Text"),
                $"{profile.Label}: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task SyncAllAsync()
    {
        foreach (var p in Profiles.Where(p => !string.IsNullOrEmpty(p.Url)))
        {
            try
            {
                await _clash.SyncProfileAsync(p.Id);
                p.LastUpdate = DateTime.Now;
            }
            catch (Exception ex)
            {
                _notification.Warning(
                    LocalizationHelper.GetString("ErrorSyncTitle.Text"),
                    $"{p.Label}: {ex.Message}");
            }
        }
    }

    [RelayCommand]
    private async Task DeleteProfileAsync(Profile? profile)
    {
        if (profile == null) return;
        try
        {
            await _clash.DeleteProfileAsync(profile.Id);
            Profiles.Remove(profile);
        }
        catch (Exception ex)
        {
            _notification.Error(
                LocalizationHelper.GetString("ErrorDeleteTitle.Text"),
                $"{profile.Label}: {ex.Message}");
        }
    }

    /// <summary>检查 URL 是否已存在</summary>
    public bool HasProfileWithUrl(string url) =>
        Profiles.Any(p => p.Url == url);

    /// <summary>
    /// 从 URL 导入订阅配置
    /// </summary>
    public async Task ImportProfileAsync(string url, string? name)
    {
        // 检查重复 URL
        if (HasProfileWithUrl(url))
        {
            _notification.Warning(
                LocalizationHelper.GetString("ProfilesImportTitle.Text"),
                LocalizationHelper.GetString("ProfilesDuplicateUrl.Text"));
            return;
        }

        // 从 URL 提取名称或使用用户指定的名称
        var label = name;
        if (string.IsNullOrEmpty(label))
        {
            try
            {
                var uri = new Uri(url);
                label = uri.Host.Split('.').FirstOrDefault() ?? LocalizationHelper.GetString("ProfilesFallbackLabel.Text");
            }
            catch
            {
                label = LocalizationHelper.GetString("ProfilesFallbackLabel.Text");
            }
        }

        var profile = new Profile
        {
            Id = Guid.NewGuid().ToString("N")[..8],
            Label = label,
            Url = url,
            LastUpdate = DateTime.Now,
            IsActive = false,
        };

        // 首次导入尝试同步
        try
        {
            await _clash.SyncProfileAsync(profile.Id);
        }
        catch (Exception ex)
        {
            _notification.Warning(
                LocalizationHelper.GetString("ErrorSyncTitle.Text"),
                ex.Message);
        }

        Profiles.Add(profile);
    }

    public async Task InitializeAsync()
    {
        if (_initialized) return;
        _initialized = true;
        await LoadAsync();

        // 每分钟检查一次是否有需要自动更新的订阅
        _autoUpdateTimer = new Timer(async _ =>
        {
            var now = DateTime.Now;
            foreach (var p in Profiles.Where(p => p.AutoUpdate && !string.IsNullOrEmpty(p.Url)))
            {
                if (now - p.LastUpdate >= p.AutoUpdateInterval)
                {
                    try
                    {
                        await _clash.SyncProfileAsync(p.Id);
                        p.LastUpdate = now;
                    }
                    catch { /* 自动更新失败静默 */ }
                }
            }
        }, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    public void Dispose()
    {
        _autoUpdateTimer?.Dispose();
        _autoUpdateTimer = null;
    }
}
