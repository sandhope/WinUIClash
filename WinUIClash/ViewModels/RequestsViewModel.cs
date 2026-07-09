using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using WinUIClash.Models;
using WinUIClash.Services;

namespace WinUIClash.ViewModels;

/// <summary>
/// 请求页 ViewModel — 历史请求追踪 + 搜索过滤 + 自动轮询
/// </summary>
public partial class RequestsViewModel : ObservableObject, IDisposable
{
    private readonly IClashService _clash;
    private readonly DispatcherTimer _pollTimer;
    private readonly DispatcherQueue _dispatcher;
    private bool _initialized;
    private readonly HashSet<string> _seenIds = new();

    public RequestsViewModel(IClashService clash)
    {
        _clash = clash;
        _dispatcher = DispatcherQueue.GetForCurrentThread();

        _pollTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(3),
        };
        _pollTimer.Tick += async (_, _) => await PollAsync();
    }

    [ObservableProperty] private ObservableCollection<ConnectionInfo> _requests = new();
    [ObservableProperty] private ObservableCollection<ConnectionInfo> _filteredRequests = new();
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private bool _autoScroll = true;
    [ObservableProperty] private int _requestCount;

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            FilteredRequests = new ObservableCollection<ConnectionInfo>(Requests);
        }
        else
        {
            var filtered = Requests.Where(r =>
                r.Metadata.Host.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                r.Metadata.Process.Contains(SearchText, StringComparison.OrdinalIgnoreCase));
            FilteredRequests = new ObservableCollection<ConnectionInfo>(filtered);
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        var list = await _clash.GetConnectionsAsync();
        Requests.Clear();
        _seenIds.Clear();
        foreach (var conn in list)
        {
            Requests.Add(conn);
            _seenIds.Add(conn.Id);
        }
        RequestCount = Requests.Count;
        ApplyFilter();
    }

    [RelayCommand]
    private void Clear()
    {
        Requests.Clear();
        FilteredRequests.Clear();
        _seenIds.Clear();
        RequestCount = 0;
    }

    public async Task InitializeAsync()
    {
        if (_initialized) return;
        _initialized = true;
        await RefreshAsync();
        _pollTimer.Start();
    }

    private async Task PollAsync()
    {
        try
        {
            var list = await _clash.GetConnectionsAsync();
            bool hasNew = false;

            foreach (var conn in list)
            {
                if (_seenIds.Add(conn.Id))
                {
                    Requests.Insert(0, conn);
                    hasNew = true;
                }
            }

            // Cap at 500 entries to prevent unbounded growth
            while (Requests.Count > 500)
            {
                var removed = Requests[^1];
                Requests.RemoveAt(Requests.Count - 1);
                _seenIds.Remove(removed.Id);
            }

            RequestCount = Requests.Count;

            if (hasNew) ApplyFilter();
        }
        catch
        {
            // Silently ignore polling errors (core may be stopped)
        }
    }

    public void Dispose()
    {
        _pollTimer.Stop();
        _pollTimer.Tick -= async (_, _) => await PollAsync();
    }
}
