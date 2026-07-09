using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using WinUIClash.Models;
using WinUIClash.Services;

namespace WinUIClash.ViewModels;

/// <summary>
/// 连接页 ViewModel — 活跃连接列表 + 自动刷新
/// </summary>
public partial class ConnectionsViewModel : ObservableObject
{
    private readonly IClashService _clash;
    private readonly DispatcherQueue _dispatcher;
    private Timer? _refreshTimer;
    private bool _initialized;

    public ConnectionsViewModel(IClashService clash)
    {
        _clash = clash;
        _dispatcher = DispatcherQueue.GetForCurrentThread()!;
    }

    [ObservableProperty] private ObservableCollection<ConnectionInfo> _connections = new();
    [ObservableProperty] private ObservableCollection<ConnectionInfo> _filteredConnections = new();
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private ConnectionInfo? _selectedConnection;

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
        _dispatcher.TryEnqueue(() =>
        {
            Connections = new ObservableCollection<ConnectionInfo>(list);
            ApplyFilter();
        });
    }

    [RelayCommand]
    private async Task CloseConnectionAsync(ConnectionInfo? connection)
    {
        if (connection == null) return;
        await _clash.CloseConnectionAsync(connection.Id);
        _dispatcher.TryEnqueue(() =>
        {
            Connections.Remove(connection);
            ApplyFilter();
        });
    }

    [RelayCommand]
    private async Task CloseAllAsync()
    {
        await _clash.CloseAllConnectionsAsync();
        _dispatcher.TryEnqueue(() =>
        {
            Connections.Clear();
            FilteredConnections.Clear();
        });
    }

    public async Task InitializeAsync()
    {
        if (_initialized) return;
        _initialized = true;
        await RefreshAsync();

        // 每 2 秒自动刷新
        _refreshTimer = new Timer(async _ => await RefreshAsync(), null, 0, 2000);
    }
}
