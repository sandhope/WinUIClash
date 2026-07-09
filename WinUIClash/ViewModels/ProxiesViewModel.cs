using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinUIClash.Models;
using WinUIClash.Services;

namespace WinUIClash.ViewModels;

/// <summary>
/// 代理页 ViewModel — 代理组展示、代理切换、延迟测试
/// </summary>
public partial class ProxiesViewModel : ObservableObject
{
    private readonly IClashService _clash;

    public ProxiesViewModel(IClashService clash)
    {
        _clash = clash;
    }

    [ObservableProperty] private ObservableCollection<ProxyGroup> _groups = new();
    [ObservableProperty] private ProxyGroup? _selectedGroup;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _viewType = "Grid"; // Grid or List

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        var list = await _clash.GetProxyGroupsAsync();
        Groups = new ObservableCollection<ProxyGroup>(list);
        SelectedGroup = Groups.FirstOrDefault();
        IsLoading = false;
    }

    [RelayCommand]
    private async Task SelectProxyAsync(string proxyName)
    {
        if (SelectedGroup == null || string.IsNullOrEmpty(proxyName)) return;
        await _clash.ChangeProxyAsync(SelectedGroup.Name, proxyName);
        SelectedGroup.Now = proxyName;
    }

    [RelayCommand]
    private async Task TestDelayAsync(string proxyName)
    {
        var delay = await _clash.TestDelayAsync(proxyName);
        // 更新对应代理的延迟值
        foreach (var group in Groups)
        {
            var proxy = group.Proxies.FirstOrDefault(p => p.Name == proxyName);
            if (proxy != null) proxy.Delay = delay;
        }
    }

    [RelayCommand]
    private async Task TestAllDelaysAsync()
    {
        if (SelectedGroup == null) return;
        foreach (var proxy in SelectedGroup.Proxies)
        {
            if (proxy.Type is "Direct" or "Reject") continue;
            proxy.Delay = await _clash.TestDelayAsync(proxy.Name);
        }
    }

    public async Task InitializeAsync()
    {
        await LoadAsync();
    }
}
