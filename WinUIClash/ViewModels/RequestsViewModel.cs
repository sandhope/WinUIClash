using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using WinUIClash.Models;
using WinUIClash.Services;

namespace WinUIClash.ViewModels;

/// <summary>
/// 请求页 ViewModel — 历史请求追踪 + 搜索过滤 + 自动轮询 + 排序 + 导出
/// </summary>
public partial class RequestsViewModel : ObservableObject, IDisposable
{
    private readonly IClashService _clash;
    private readonly NotificationService _notification;
    private readonly DispatcherTimer _pollTimer;
    private readonly DispatcherQueue _dispatcher;
    private bool _initialized;
    private readonly HashSet<string> _seenIds = new();
    private readonly EventHandler<object> _tickHandler;

    public enum ReqSortMode { None, Host, Time, Upload, Download, Rule }

    public RequestsViewModel(IClashService clash, NotificationService notification)
    {
        _clash = clash;
        _notification = notification;
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        _tickHandler = async (_, _) => await PollAsync();

        _pollTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(3),
        };
        _pollTimer.Tick += _tickHandler;
    }

    [ObservableProperty] private ObservableCollection<ConnectionInfo> _requests = new();
    [ObservableProperty] private ObservableCollection<ConnectionInfo> _filteredRequests = new();
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private bool _autoScroll = true;
    [ObservableProperty] private int _requestCount;
    [ObservableProperty] private bool _isPaused;
    [ObservableProperty] private string _pauseLabel = "";
    [ObservableProperty] private ReqSortMode _currentSort = ReqSortMode.None;

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnCurrentSortChanged(ReqSortMode value)
    {
        OnPropertyChanged(nameof(SortModeLabel));
        ApplyFilter();
    }
    partial void OnIsPausedChanged(bool value)
    {
        PauseLabel = value
            ? LocalizationHelper.GetString("RequestsResume.Content")
            : LocalizationHelper.GetString("RequestsPause.Content");
        if (value)
            _pollTimer.Stop();
        else
            _pollTimer.Start();
    }

    private void ApplyFilter()
    {
        IEnumerable<ConnectionInfo> query = Requests;

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            query = query.Where(r =>
                r.Metadata.Host.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                r.Metadata.Process.Contains(SearchText, StringComparison.OrdinalIgnoreCase));
        }

        query = CurrentSort switch
        {
            ReqSortMode.Host => query.OrderBy(r => r.Metadata.Host, StringComparer.OrdinalIgnoreCase),
            ReqSortMode.Time => query.OrderByDescending(r => r.Start),
            ReqSortMode.Upload => query.OrderByDescending(r => r.Upload),
            ReqSortMode.Download => query.OrderByDescending(r => r.Download),
            ReqSortMode.Rule => query.OrderBy(r => r.Rule, StringComparer.OrdinalIgnoreCase),
            _ => query,
        };

        FilteredRequests = new ObservableCollection<ConnectionInfo>(query);
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
    private void TogglePause()
    {
        IsPaused = !IsPaused;
    }

    [RelayCommand]
    private void CycleSortMode()
    {
        CurrentSort = CurrentSort switch
        {
            ReqSortMode.None => ReqSortMode.Host,
            ReqSortMode.Host => ReqSortMode.Time,
            ReqSortMode.Time => ReqSortMode.Upload,
            ReqSortMode.Upload => ReqSortMode.Download,
            ReqSortMode.Download => ReqSortMode.Rule,
            _ => ReqSortMode.None,
        };
    }

    public string SortModeLabel => CurrentSort switch
    {
        ReqSortMode.Host => LocalizationHelper.GetString("ReqSortHost.Text"),
        ReqSortMode.Time => LocalizationHelper.GetString("ReqSortTime.Text"),
        ReqSortMode.Upload => LocalizationHelper.GetString("ReqSortUpload.Text"),
        ReqSortMode.Download => LocalizationHelper.GetString("ReqSortDownload.Text"),
        ReqSortMode.Rule => LocalizationHelper.GetString("ReqSortRule.Text"),
        _ => LocalizationHelper.GetString("ReqSortNone.Text"),
    };

    /// <summary>导出请求记录到文件</summary>
    [RelayCommand]
    private async Task ExportAsync()
    {
        try
        {
            var picker = new Windows.Storage.Pickers.FileSavePicker
            {
                SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary,
                SuggestedFileName = $"winuiclash-requests-{DateTime.Now:yyyyMMdd-HHmmss}.csv",
            };
            picker.FileTypeChoices.Add("CSV", [".csv"]);
            picker.FileTypeChoices.Add("Text file", [".txt"]);

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.CurrentWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var file = await picker.PickSaveFileAsync();
            if (file == null) return;

            var lines = new List<string>
            {
                "Time,Host,Network,Process,Rule,Upload,Download"
            };
            foreach (var r in FilteredRequests)
            {
                lines.Add(
                    $"{r.Start:yyyy-MM-dd HH:mm:ss},\"{r.Metadata.Host}\",{r.Metadata.Network},\"{r.Metadata.Process}\",{r.Rule},{r.Upload},{r.Download}");
            }
            var content = string.Join(Environment.NewLine, lines);
            await Windows.Storage.FileIO.WriteTextAsync(file, content);

            _notification.Success(
                LocalizationHelper.GetString("RequestsExportSuccessTitle.Text"),
                LocalizationHelper.GetString("RequestsExportSuccessMsg.Text"));
        }
        catch (Exception ex)
        {
            _notification.Error(
                LocalizationHelper.GetString("RequestsExportFailTitle.Text"),
                ex.Message);
        }
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
        PauseLabel = LocalizationHelper.GetString("RequestsPause.Content");
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
        _pollTimer.Tick -= _tickHandler;
    }
}
