using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinUIClash.Models;
using WinUIClash.Services;

namespace WinUIClash.ViewModels;

/// <summary>
/// 配置页 ViewModel — 配置管理、订阅更新、本地文件存储
/// </summary>
public partial class ProfilesViewModel : ObservableObject, IDisposable
{
    private readonly IClashService _clash;
    private readonly NotificationService _notification;
    private readonly ProfileStorageService _storage;
    private Timer? _autoUpdateTimer;
    private bool _initialized;

    public ProfilesViewModel(IClashService clash, NotificationService notification)
    {
        _clash = clash;
        _notification = notification;
        _storage = new ProfileStorageService();
    }

    [ObservableProperty] private ObservableCollection<Profile> _profiles = new();
    [ObservableProperty] private Profile? _activeProfile;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isSwitching;

    /// <summary>Profile count label for the page header, e.g. "(3)"</summary>
    public string ProfileCountText => Profiles.Count > 0 ? $"({Profiles.Count})" : "";

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        // Load from local storage first (persistent), then merge with any API-provided profiles
        var localList = await _storage.LoadProfileListAsync();
        var apiList = await _clash.GetProfilesAsync();

        // Merge: local profiles take precedence, add any API-only profiles
        var merged = new Dictionary<string, Profile>();
        foreach (var p in localList) merged[p.Id] = p;
        foreach (var p in apiList)
        {
            if (!merged.ContainsKey(p.Id))
                merged[p.Id] = p;
        }

        Profiles = new ObservableCollection<Profile>(merged.Values.OrderBy(p => p.Order));
        Profiles.CollectionChanged += (_, _) => OnPropertyChanged(nameof(ProfileCountText));
        ActiveProfile = Profiles.FirstOrDefault(p => p.IsActive);
        OnPropertyChanged(nameof(ProfileCountText));
        IsLoading = false;
    }

    [RelayCommand]
    private async Task SelectProfileAsync(Profile? profile)
    {
        if (profile == null) return;
        try
        {
            IsSwitching = true;

            // Ensure config file exists locally
            var configPath = profile.Path;
            if (string.IsNullOrWhiteSpace(configPath))
                configPath = _storage.GetConfigPath(profile.Id);

            await _clash.SwitchProfileAsync(profile.Id, configPath);

            foreach (var p in Profiles) p.IsActive = p.Id == profile.Id;
            ActiveProfile = Profiles.FirstOrDefault(p => p.IsActive);
            await SaveProfileListAsync();

            _notification.Success(
                LocalizationHelper.GetString("ProfilesSwitchedTitle.Text"),
                profile.Label);
        }
        catch (Exception ex)
        {
            _notification.Error(
                LocalizationHelper.GetString("ErrorCloseTitle.Text"),
                ex.Message);
        }
        finally
        {
            IsSwitching = false;
        }
    }

    [RelayCommand]
    private async Task SyncProfileAsync(Profile? profile)
    {
        if (profile == null) return;
        try
        {
            var configPath = profile.Path;
            if (string.IsNullOrWhiteSpace(configPath))
                configPath = _storage.GetConfigPath(profile.Id);

            // If this profile has a subscription URL, download the latest config
            if (!string.IsNullOrEmpty(profile.Url))
            {
                var result = await _storage.DownloadAndSaveAsync(profile.Id, profile.Url);
                if (result.SubInfo != null)
                {
                    profile.SubscriptionInfo = result.SubInfo;
                    profile.NotifySubscriptionChanged();
                }
            }
            else
            {
                await _clash.SyncProfileAsync(profile.Id, profile.Url, configPath);
            }

            // Update local path reference
            if (string.IsNullOrWhiteSpace(profile.Path))
                profile.Path = configPath;

            profile.LastUpdate = DateTime.Now;
            await SaveProfileListAsync();

            _notification.Success(
                LocalizationHelper.GetString("ProfilesSyncDoneTitle.Text"),
                profile.Label);
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
                var configPath = p.Path;
                if (string.IsNullOrWhiteSpace(configPath))
                    configPath = _storage.GetConfigPath(p.Id);

                var result = await _storage.DownloadAndSaveAsync(p.Id, p.Url!);
                if (result.SubInfo != null)
                {
                    p.SubscriptionInfo = result.SubInfo;
                    p.NotifySubscriptionChanged();
                }

                if (string.IsNullOrWhiteSpace(p.Path))
                    p.Path = configPath;

                p.LastUpdate = DateTime.Now;
            }
            catch (Exception ex)
            {
                _notification.Warning(
                    LocalizationHelper.GetString("ErrorSyncTitle.Text"),
                    $"{p.Label}: {ex.Message}");
            }
        }
        await SaveProfileListAsync();

        _notification.Success(
            LocalizationHelper.GetString("ProfilesSyncDoneTitle.Text"),
            LocalizationHelper.GetString("ProfilesSyncAllDoneMsg.Text"));
    }

    [RelayCommand]
    private async Task DeleteProfileAsync(Profile? profile)
    {
        if (profile == null) return;
        try
        {
            await _clash.DeleteProfileAsync(profile.Id);
            _storage.DeleteConfig(profile.Id);
            Profiles.Remove(profile);
            await SaveProfileListAsync();
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

        var profileId = Guid.NewGuid().ToString("N")[..8];
        var configPath = _storage.GetConfigPath(profileId);

        var profile = new Profile
        {
            Id = profileId,
            Label = label,
            Url = url,
            Path = configPath,
            LastUpdate = DateTime.Now,
            IsActive = false,
        };

        // 首次导入尝试下载订阅配置
        try
        {
            var result = await _storage.DownloadAndSaveAsync(profileId, url);
            if (result.SubInfo != null)
            {
                profile.SubscriptionInfo = result.SubInfo;
                profile.NotifySubscriptionChanged();
            }
            await _clash.SyncProfileAsync(profileId, url, configPath);
        }
        catch (Exception ex)
        {
            _notification.Warning(
                LocalizationHelper.GetString("ErrorSyncTitle.Text"),
                ex.Message);
        }

        Profiles.Add(profile);
        await SaveProfileListAsync();
    }

    /// <summary>编辑已有档案的 URL 和标签</summary>
    public async Task UpdateProfileAsync(string profileId, string? newLabel, string? newUrl)
    {
        var profile = Profiles.FirstOrDefault(p => p.Id == profileId);
        if (profile == null) return;

        if (!string.IsNullOrWhiteSpace(newLabel))
            profile.Label = newLabel;

        if (newUrl != profile.Url)
        {
            profile.Url = newUrl;
            // If URL changed and there's content, re-download
            if (!string.IsNullOrWhiteSpace(newUrl))
            {
                try
                {
                    var result = await _storage.DownloadAndSaveAsync(profileId, newUrl);
                    if (result.SubInfo != null)
                    {
                        profile.SubscriptionInfo = result.SubInfo;
                        profile.NotifySubscriptionChanged();
                    }
                    profile.LastUpdate = DateTime.Now;
                }
                catch (Exception ex)
                {
                    _notification.Warning(
                        LocalizationHelper.GetString("ErrorSyncTitle.Text"),
                        ex.Message);
                }
            }
        }

        await SaveProfileListAsync();
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
                        var configPath = p.Path;
                        if (string.IsNullOrWhiteSpace(configPath))
                            configPath = _storage.GetConfigPath(p.Id);

                        await _clash.SyncProfileAsync(p.Id, p.Url, configPath);

                        if (string.IsNullOrWhiteSpace(p.Path))
                            p.Path = configPath;

                        p.LastUpdate = now;
                    }
                    catch { /* 自动更新失败静默 */ }
                }
            }
        }, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    /// <summary>持久化档案列表到本地 JSON</summary>
    public async Task SaveProfileListAsync()
    {
        try
        {
            await _storage.SaveProfileListAsync(Profiles.ToList());
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Profiles] Save failed: {ex.Message}");
        }
    }

    public void Dispose()
    {
        _autoUpdateTimer?.Dispose();
        _autoUpdateTimer = null;
    }
}
