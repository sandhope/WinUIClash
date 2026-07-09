using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinUIClash.Models;
using WinUIClash.Services;

namespace WinUIClash.ViewModels;

/// <summary>
/// 资源页 ViewModel — GeoIP/GeoSite/ASN 等数据文件管理
/// </summary>
public partial class ResourcesViewModel : ObservableObject
{
    private readonly IClashService _clash;
    private bool _initialized;

    public ResourcesViewModel(IClashService clash)
    {
        _clash = clash;
    }

    [ObservableProperty] private ObservableCollection<ExternalProvider> _providers = new();
    [ObservableProperty] private bool _isLoading;

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        var list = await _clash.GetExternalProvidersAsync();
        Providers = new ObservableCollection<ExternalProvider>(list);
        IsLoading = false;
    }

    [RelayCommand]
    private async Task UpdateProviderAsync(ExternalProvider? provider)
    {
        if (provider == null) return;
        await _clash.UpdateExternalProviderAsync(provider.Name);
        provider.UpdateAt = DateTime.Now;
    }

    [RelayCommand]
    private async Task UpdateAllAsync()
    {
        foreach (var p in Providers)
        {
            try
            {
                await _clash.UpdateExternalProviderAsync(p.Name);
                p.UpdateAt = DateTime.Now;
            }
            catch { /* 单个失败不影响其余 */ }
        }
    }

    public async Task InitializeAsync()
    {
        if (_initialized) return;
        _initialized = true;
        await LoadAsync();
    }
}
