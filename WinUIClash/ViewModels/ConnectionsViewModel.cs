using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using WinUIClash.Models;
using WinUIClash.Services;

namespace WinUIClash.ViewModels;

/// <summary>
/// 连接页 ViewModel — 活跃连接列表 + 自动刷新 + 排序 + 暂停
/// </summary>
public partial class ConnectionsViewModel : ObservableObject
{
    private readonly IClashService _clash;
    private readonly NotificationService _notification;
    private readonly DispatcherQueue _dispatcher;
    private Timer? _refreshTimer;
    private bool _initialized;

    public enum ConnSortMode { None, Host, Upload, Download, Duration }

    public ConnectionsViewModel(IClashService clash, NotificationService notification)
    {
        _clash = clash;
        _notification = notification;
        _dispatcher = DispatcherQueue.GetForCurrentThread()!;
    }

    [ObservableProperty] private ObservableCollection<ConnectionInfo> _connections = new();
    [ObservableProperty] private ObservableCollection<ConnectionInfo> _filteredConnections = new();
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private ConnectionInfo? _selectedConnection;
    [ObservableProperty] private ConnSortMode _currentSort = ConnSortMode.None;
    [ObservableProperty] private int _connectionCount;
    [ObservableProperty] private bool _isPaused;
    [ObservableProperty] private string _pauseLabel = "";

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnCurrentSortChanged(ConnSortMode value) => ApplyFilter();
    partial void OnIsPausedChanged(bool value)
    {
        PauseLabel = value
            ? LocalizationHelper.GetString("ConnResume.Content")
            : LocalizationHelper.GetString("ConnPause.Content");
        if (value)
            _refreshTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        else
            _refreshTimer?.Change(0, 2000);
    }

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
        try
        {
            var list = await _clash.GetConnectionsAsync();
            _dispatcher.TryEnqueue(() =>
            {
                Connections = new ObservableCollection<ConnectionInfo>(list);
                ConnectionCount = list.Count;
                ApplyFilter();
            });
        }
        catch
        {
            // Silently ignore refresh errors (core may be stopped)
        }
    }

    [RelayCommand]
    private async Task CloseConnectionAsync(ConnectionInfo? connection)
    {
        if (connection == null) return;
        try
        {
            await _clash.CloseConnectionAsync(connection.Id);
            _dispatcher.TryEnqueue(() =>
            {
                Connections.Remove(connection);
                ApplyFilter();
                ConnectionCount = Connections.Count;
            });
        }
        catch (Exception ex)
        {
            _notification.Error(
                LocalizationHelper.GetString("ErrorCloseTitle.Text"),
                ex.Message);
        }
    }

    [RelayCommand]
    private async Task CloseAllAsync()
    {
        try
        {
            await _clash.CloseAllConnectionsAsync();
            _dispatcher.TryEnqueue(() =>
            {
                Connections.Clear();
                FilteredConnections.Clear();
                ConnectionCount = 0;
            });
        }
        catch (Exception ex)
        {
            _notification.Error(
                LocalizationHelper.GetString("ErrorCloseTitle.Text"),
                ex.Message);
        }
    }

    [RelayCommand]
    private void TogglePause()
    {
        IsPaused = !IsPaused;
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
        ConnSortMode.Host => LocalizationHelper.GetString("ConnSortHost.Text"),
        ConnSortMode.Upload => LocalizationHelper.GetString("ConnSortUpload.Text"),
        ConnSortMode.Download => LocalizationHelper.GetString("ConnSortDownload.Text"),
        ConnSortMode.Duration => LocalizationHelper.GetString("ConnSortDuration.Text"),
        _ => LocalizationHelper.GetString("ConnSortNone.Text"),
    };

    public async Task InitializeAsync()
    {
        if (_initialized) return;
        _initialized = true;
        PauseLabel = LocalizationHelper.GetString("ConnPause.Content");
        await RefreshAsync();

        // 每 2 秒自动刷新
        _refreshTimer = new Timer(async _ => await RefreshAsync(), null, 0, 2000);
    }
}
