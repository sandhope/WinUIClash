using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using WinUIClash.Models;
using WinUIClash.Services;

namespace WinUIClash.ViewModels;

/// <summary>
/// 资源页 ViewModel — 1:1 还原 FlClash 的 Geo 资源页（MMDB / ASN / GEOIP / GEOSITE），
/// 展示每个数据库文件的大小/修改时间与下载地址，并支持更新与地址编辑。
/// </summary>
public partial class ResourcesViewModel : ObservableObject, IDisposable
{
    private readonly GeoResourceService _geo;
    private readonly NotificationService _notification;
    private readonly AppSettings _settings;
    private readonly DispatcherQueue _dispatcher;
    private readonly System.Threading.Timer _autoUpdateTimer;
    private bool _initialized;

    public ResourcesViewModel(GeoResourceService geo, NotificationService notification, AppSettings settings)
    {
        _geo = geo;
        _notification = notification;
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        _settings = settings;

        // 自动更新定时器：每 30 分钟检查一次
        _autoUpdateTimer = new System.Threading.Timer(
            async _ => await AutoUpdateCheckAsync(),
            null,
            System.Threading.Timeout.Infinite,
            System.Threading.Timeout.Infinite);
    }

    public ObservableCollection<GeoResourceItem> Items { get; } = new();

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _geoAutoUpdate;
    [ObservableProperty] private int _geoUpdateInterval = 24;

    partial void OnGeoAutoUpdateChanged(bool value) => _settings.GeoAutoUpdate = value;
    partial void OnGeoUpdateIntervalChanged(int value) => _settings.GeoUpdateInterval = value;

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            Items.Clear();
            foreach (var type in new[]
            {
                GeoResourceType.MMDB,
                GeoResourceType.ASN,
                GeoResourceType.GEOIP,
                GeoResourceType.GEOSITE,
            })
            {
                var item = new GeoResourceItem(type, GeoResourceService.FileNameFor(type))
                {
                    Url = UrlFor(type),
                };
                RefreshFileInfo(item);
                Items.Add(item);
            }

            GeoAutoUpdate = _settings.GeoAutoUpdate;
            GeoUpdateInterval = _settings.GeoUpdateInterval;

            _autoUpdateTimer.Change(TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(30));
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        _initialized = false;
        await InitializeAsync();
    }

    private string UrlFor(GeoResourceType type) => type switch
    {
        GeoResourceType.MMDB => _settings.GeoMmdbUrl,
        GeoResourceType.ASN => _settings.GeoAsnUrl,
        GeoResourceType.GEOIP => _settings.GeoIpUrl,
        GeoResourceType.GEOSITE => _settings.GeoSiteUrl,
        _ => "",
    };

    private void RefreshFileInfo(GeoResourceItem item)
    {
        var info = _geo.GetFileInfo(item.Type);
        if (info.HasValue)
        {
            item.Size = info.Value.size;
            item.LastModified = info.Value.lastModified;
            item.FileInfoText = $"{FormatSize(info.Value.size)} · {info.Value.lastModified:yyyy-MM-dd HH:mm}";
        }
        else
        {
            item.Size = 0;
            item.LastModified = null;
            item.FileInfoText = LocalizationHelper.GetString("GeoNotDownloaded.Text");
        }
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        double value = bytes;
        string[] units = ["KB", "MB", "GB"];
        int i = -1;
        do
        {
            value /= 1024.0;
            i++;
        } while (value >= 1024 && i < units.Length - 1);
        return $"{value:F1} {units[i]}";
    }

    /// <summary>更新单个 Geo 资源（下载到核心数据目录）。</summary>
    [RelayCommand]
    private async Task UpdateItemAsync(GeoResourceItem? item)
    {
        if (item == null) return;
        item.IsUpdating = true;
        try
        {
            var progress = new Progress<double>(_ => { });
            await _geo.UpdateAsync(item.Type, item.Url, progress);
            _dispatcher.TryEnqueue(() => RefreshFileInfo(item));
            _notification.Success(
                LocalizationHelper.GetString("ResGeoUpdateSuccess.Text"),
                item.DisplayName);
        }
        catch (Exception ex)
        {
            _notification.Error(
                LocalizationHelper.GetString("ErrorUpdateTitle.Text"),
                $"{item.DisplayName}: {ex.Message}");
        }
        finally
        {
            item.IsUpdating = false;
        }
    }

    /// <summary>保存用户编辑后的 Geo 资源下载地址。</summary>
    public void SetUrl(GeoResourceItem item, string url)
    {
        item.Url = url;
        switch (item.Type)
        {
            case GeoResourceType.MMDB: _settings.GeoMmdbUrl = url; break;
            case GeoResourceType.ASN: _settings.GeoAsnUrl = url; break;
            case GeoResourceType.GEOIP: _settings.GeoIpUrl = url; break;
            case GeoResourceType.GEOSITE: _settings.GeoSiteUrl = url; break;
        }
    }

    /// <summary>自动更新：更新超过 geoUpdateInterval 小时未更新的资源。</summary>
    private async Task AutoUpdateCheckAsync()
    {
        if (!_settings.GeoAutoUpdate) return;
        var threshold = DateTime.Now.AddHours(-Math.Max(1, _settings.GeoUpdateInterval));
        foreach (var item in Items)
        {
            var info = _geo.GetFileInfo(item.Type);
            if (info == null || info.Value.lastModified < threshold)
            {
                try
                {
                    await _geo.UpdateAsync(item.Type, item.Url);
                    _dispatcher.TryEnqueue(() => RefreshFileInfo(item));
                }
                catch { /* 自动更新失败静默 */ }
            }
        }
    }

    public async Task InitializeAsync()
    {
        if (_initialized) return;
        await LoadAsync();
        _initialized = true;
    }

    public void Dispose()
    {
        _autoUpdateTimer.Dispose();
    }
}
