using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using WinUIClash.Models;
using WinUIClash.Services;

namespace WinUIClash.ViewModels;

/// <summary>
/// 连接页 ViewModel — 活跃连接列表 + 自动刷新 + 排序
/// </summary>
public partial class ConnectionsViewModel : ObservableObject
{
    private readonly IClashService _clash;
    private readonly DispatcherQueue _dispatcher;
    private Timer? _refreshTimer;
    private bool _initialized;

    public enum ConnSortMode { None, Host, Upload, Download, Duration }

    public ConnectionsViewModel(IClashService clash)
    {
        _clash = clash;
        _dispatcher = DispatcherQueue.GetForCurrentThread()!;
    }

    [ObservableProperty] private ObservableCollection<ConnectionInfo> _connections = new();
    [ObservableProperty] private ObservableCollection<ConnectionInfo> _filteredConnections = new();
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private ConnectionInfo? _selectedConnection;
    [ObservableProperty] private ConnSortMode _currentSort = ConnSortMode.None;
    [ObservableProperty] private int _connectionCount;

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnCurrentSortChanged(ConnSortMode value) => ApplyFilter();

    private void ApplyFilter()
    {
        IEnumerable<ConnectionInfo> filtered;

        if (string.IsNullOrWhiteSpace(SearchText))
        {
            filtered = Connections;
        }
        else
        {
            filtered = Connections.Where(c =>
                c.Metadata.Host.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                c.Metadata.Process.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                c.Chains.Any(ch => ch.Contains(SearchText, StringComparison.OrdinalIgnoreCase)));
        }

        filtered = CurrentSort switch
        {
            ConnSortMode.Host => filtered.OrderBy(c => c.Metadata.Host, StringComparer.OrdinalIgnoreCase),
            ConnSortMode.Upload => filtered.OrderByDescending(c => c.Upload),
            ConnSortMode.Download => filtered.OrderByDescending(c => c.Download),
            ConnSortMode.Duration => filtered.OrderBy(c => c.Start),
            _ => filtered,
        };

        FilteredConnections = new ObservableCollection<ConnectionInfo>(filtered);
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        var list = await _clash.GetConnectionsAsync();
        _dispatcher.TryEnqueue(() =>
        {
            Connections = new ObservableCollection<ConnectionInfo>(list);
            ConnectionCount = list.Count;
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
            ConnectionCount = Connections.Count;
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
            ConnectionCount = 0;
        });
    }

    [RelayCommand]
    private void CycleSortMode()
    {
        CurrentSort = CurrentSort switch
        {
            ConnSortMode.None => ConnSortMode.Host,
            ConnSortMode.Host => ConnSortMode.Upload,
            ConnSortMode.Upload => ConnSortMode.Download,
            ConnSortMode.Download => ConnSortMode.Duration,
            _ => ConnSortMode.None,
        };
    }

    public string SortModeLabel => CurrentSort switch
    {
        ConnSortMode.Host => "按主机",
        ConnSortMode.Upload => "按上传",
        ConnSortMode.Download => "按下载",
        ConnSortMode.Duration => "按时长",
        _ => "默认排序",
    };

    public async Task InitializeAsync()
    {
        if (_initialized) return;
        _initialized = true;
        await RefreshAsync();

        // 每 2 秒自动刷新
        _refreshTimer = new Timer(async _ => await RefreshAsync(), null, 0, 2000);
    }
}
