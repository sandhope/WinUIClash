using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinUIClash.Models;
using WinUIClash.Services;

namespace WinUIClash.ViewModels;

/// <summary>
/// 连接页 ViewModel — 活跃连接列表
/// </summary>
public partial class ConnectionsViewModel : ObservableObject
{
    private readonly IClashService _clash;

    public ConnectionsViewModel(IClashService clash)
    {
        _clash = clash;
    }

    [ObservableProperty] private ObservableCollection<ConnectionInfo> _connections = new();
    [ObservableProperty] private ObservableCollection<ConnectionInfo> _filteredConnections = new();
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private ConnectionInfo? _selectedConnection;
    [ObservableProperty] private bool _isAutoRefresh = true;

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            FilteredConnections = new ObservableCollection<ConnectionInfo>(Connections);
        }
        else
        {
            var filtered = Connections.Where(c =>
                c.Metadata.Host.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                c.Metadata.Process.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                c.Chains.Any(ch => ch.Contains(SearchText, StringComparison.OrdinalIgnoreCase)));
            FilteredConnections = new ObservableCollection<ConnectionInfo>(filtered);
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        var list = await _clash.GetConnectionsAsync();
        Connections = new ObservableCollection<ConnectionInfo>(list);
        ApplyFilter();
    }

    [RelayCommand]
    private async Task CloseConnectionAsync(string id)
    {
        await _clash.CloseConnectionAsync(id);
        var conn = Connections.FirstOrDefault(c => c.Id == id);
        if (conn != null) Connections.Remove(conn);
        ApplyFilter();
    }

    [RelayCommand]
    private async Task CloseAllAsync()
    {
        await _clash.CloseAllConnectionsAsync();
        Connections.Clear();
        FilteredConnections.Clear();
    }

    public async Task InitializeAsync()
    {
        await RefreshAsync();
    }
}
