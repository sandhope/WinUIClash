using System.Collections.ObjectModel;
using System.Net.Http;
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

    /// <summary>配置增删/切换/激活状态变化时触发，供代理页等订阅刷新（BUG-5）</summary>
    public event EventHandler? ProfilesChanged;

    private void RaiseProfilesChanged() => ProfilesChanged?.Invoke(this, EventArgs.Empty);

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
        if (profile == null || profile.IsActive) return;
        try
        {
            IsSwitching = true;

            // Ensure config file exists locally
            var configPath = profile.Path;
            if (string.IsNullOrWhiteSpace(configPath))
                configPath = _storage.GetConfigPath(profile.Id);

            // 先更新激活状态并落盘，确保 BuildConfigAsync（SwitchProfileAsync 内部）读取到新激活配置
            foreach (var p in Profiles) p.IsActive = p.Id == profile.Id;
            ActiveProfile = Profiles.FirstOrDefault(p => p.IsActive);
            await SaveProfileListAsync();

            await _clash.SwitchProfileAsync(profile.Id, configPath);

            _notification.Success(
                LocalizationHelper.GetString("ProfilesSwitchedTitle.Text"),
                profile.Label);

            // Auto-start core if not running
            if (_clash.CoreState == CoreState.Stopped)
            {
                _notification.Info(
                    LocalizationHelper.GetString("ProfilesAutoStartTitle.Text"),
                    LocalizationHelper.GetString("ProfilesAutoStartMsg.Text"));
                await _clash.StartAsync();
            }
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
            RaiseProfilesChanged();
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
        catch (HttpRequestException httpEx)
        {
            var hint = httpEx.StatusCode switch
            {
                System.Net.HttpStatusCode.NotFound => LocalizationHelper.GetString("ProfilesSyncUrlHint.Text"),
                System.Net.HttpStatusCode.Unauthorized => LocalizationHelper.GetString("ProfilesSyncAuthHint.Text"),
                _ => null
            };
            _notification.Error(
                LocalizationHelper.GetString("ErrorSyncTitle.Text"),
                $"{profile.Label}: {httpEx.Message}{(hint != null ? $"\n{hint}" : "")}");
        }
        catch (Exception ex)
        {
            _notification.Error(
                LocalizationHelper.GetString("ErrorSyncTitle.Text"),
                $"{profile.Label}: {ex.Message}");
        }

        RaiseProfilesChanged();
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

        RaiseProfilesChanged();
    }

    [RelayCommand]
    private async Task DeleteProfileAsync(Profile? profile)
    {
        if (profile == null) return;
        try
        {
            await _clash.DeleteProfileAsync(profile.Id);
            _storage.DeleteConfig(profile.Id);
            var wasActive = profile.IsActive;
            Profiles.Remove(profile);
            await SaveProfileListAsync();

            // 删除的是当前激活配置：自动切换到剩余第一个，保证代理页不会停留在已删除数据
            if (wasActive && Profiles.Count > 0)
                await SelectProfileAsync(Profiles[0]);
            else if (wasActive)
            {
                ActiveProfile = null;
                await _clash.SwitchProfileAsync(string.Empty);
                await _clash.SetOutboundModeAsync(Models.OutboundMode.Direct);
            }
        }
        catch (Exception ex)
        {
            _notification.Error(
                LocalizationHelper.GetString("ErrorDeleteTitle.Text"),
                $"{profile.Label}: {ex.Message}");
        }
        finally
        {
            RaiseProfilesChanged();
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
        }
        catch (Exception ex)
        {
            _notification.Warning(
                LocalizationHelper.GetString("ErrorSyncTitle.Text"),
                ex.Message);
        }

        Profiles.Add(profile);

        // 导入即激活（对齐 FlClash）：设为当前激活配置并让核心热重载，代理页即可见其节点
        foreach (var p in Profiles) p.IsActive = p.Id == profile.Id;
        ActiveProfile = profile;
        await SaveProfileListAsync();

        if (_clash.CoreState == CoreState.Running)
            await _clash.SwitchProfileAsync(profile.Id, configPath);

        RaiseProfilesChanged();
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
        RaiseProfilesChanged();
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
