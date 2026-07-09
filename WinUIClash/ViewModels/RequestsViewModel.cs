using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinUIClash.Models;
using WinUIClash.Services;

namespace WinUIClash.ViewModels;

/// <summary>
/// 请求页 ViewModel — 历史请求追踪
/// </summary>
public partial class RequestsViewModel : ObservableObject
{
    private readonly IClashService _clash;

    public RequestsViewModel(IClashService clash)
    {
        _clash = clash;
    }

    [ObservableProperty] private ObservableCollection<ConnectionInfo> _requests = new();
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private bool _autoScroll = true;

    [RelayCommand]
    private async Task RefreshAsync()
    {
        // 请求视图复用连接接口
        var list = await _clash.GetConnectionsAsync();
        Requests = new ObservableCollection<ConnectionInfo>(list);
    }

    public async Task InitializeAsync()
    {
        await RefreshAsync();
    }
}
