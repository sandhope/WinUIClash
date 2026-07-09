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
    private bool _initialized;

    public ProxiesViewModel(IClashService clash)
    {
        _clash = clash;
    }

    [ObservableProperty] private ObservableCollection<ProxyGroup> _groups = new();
    [ObservableProperty] private ProxyGroup? _selectedGroup;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private bool _isLoading;

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        var list = await _clash.GetProxyGroupsAsync();
        Groups = new ObservableCollection<ProxyGroup>(list);
        SelectedGroup = Groups.FirstOrDefault();
        IsLoading = false;
    }

    /// <summary>切换代理组 Tab</summary>
    [RelayCommand]
    private void SelectGroup(ProxyGroup? group)
    {
        if (group != null) SelectedGroup = group;
    }

    /// <summary>选中某个代理节点</summary>
    [RelayCommand]
    private async Task SelectProxyAsync(Proxy? proxy)
    {
        if (SelectedGroup == null || proxy == null) return;
        await _clash.ChangeProxyAsync(SelectedGroup.Name, proxy.Name);
        SelectedGroup.Now = proxy.Name;
    }

    /// <summary>测试单个代理延迟</summary>
    [RelayCommand]
    private async Task TestDelayAsync(Proxy? proxy)
    {
        if (proxy == null || proxy.Type is "Direct" or "Reject") return;
        proxy.Delay = await _clash.TestDelayAsync(proxy.Name);
    }

    /// <summary>对当前组所有代理并行测速</summary>
    [RelayCommand]
    private async Task TestAllDelaysAsync()
    {
        if (SelectedGroup == null) return;
        var tasks = SelectedGroup.Proxies
            .Where(p => p.Type is not ("Direct" or "Reject"))
            .Select(async p => { p.Delay = await _clash.TestDelayAsync(p.Name); });
        await Task.WhenAll(tasks);
    }

    public async Task InitializeAsync()
    {
        if (_initialized) return;
        _initialized = true;
        await LoadAsync();
    }
}
