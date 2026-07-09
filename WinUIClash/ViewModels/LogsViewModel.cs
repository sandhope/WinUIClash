using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using WinUIClash.Models;
using WinUIClash.Services;

namespace WinUIClash.ViewModels;

/// <summary>
/// 日志页 ViewModel — 实时日志流 + 搜索过滤 + 暂停/导出
/// </summary>
public partial class LogsViewModel : ObservableObject
{
    private readonly IClashService _clash;
    private readonly DispatcherQueue _dispatcher;
    private const int MaxLogEntries = 500;
    private bool _started;

    public LogsViewModel(IClashService clash)
    {
        _clash = clash;
        _dispatcher = DispatcherQueue.GetForCurrentThread()!;
        _clash.LogReceived += OnLogReceived;
    }

    [ObservableProperty] private ObservableCollection<LogEntry> _logs = new();
    [ObservableProperty] private ObservableCollection<LogEntry> _filteredLogs = new();
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private bool _autoScroll = true;
    [ObservableProperty] private bool _isPaused;
    [ObservableProperty] private string _selectedLevel = "ALL";
    [ObservableProperty] private int _logCount;
    [ObservableProperty] private bool _hasLogs;

    /// <summary>有新日志追加时触发（供 View 层滚动到底部）</summary>
    public event Action? LogAppended;

    /// <summary>日志级别过滤选项</summary>
    public string[] LevelOptions { get; } = ["ALL", "Debug", "Info", "Warning", "Error"];

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnSelectedLevelChanged(string value) => ApplyFilter();

    private void OnLogReceived(LogEntry entry)
    {
        if (IsPaused) return;

        _dispatcher.TryEnqueue(() =>
        {
            Logs.Add(entry);
            HasLogs = true;
            if (Logs.Count > MaxLogEntries) Logs.RemoveAt(0);

            if (MatchesFilter(entry))
            {
                FilteredLogs.Add(entry);
                if (FilteredLogs.Count > MaxLogEntries) FilteredLogs.RemoveAt(0);
                LogCount = FilteredLogs.Count;
                LogAppended?.Invoke();
            }
        });
    }

    private bool MatchesFilter(LogEntry entry)
    {
        if (SelectedLevel != "ALL")
        {
            var level = Enum.Parse<LogLevel>(SelectedLevel, ignoreCase: true);
            if (entry.Level != level) return false;
        }
        if (!string.IsNullOrWhiteSpace(SearchText) &&
            !entry.Payload.Contains(SearchText, StringComparison.OrdinalIgnoreCase))
            return false;
        return true;
    }

    private void ApplyFilter()
    {
        var filtered = Logs.AsEnumerable();
        if (SelectedLevel != "ALL")
        {
            var level = Enum.Parse<LogLevel>(SelectedLevel, ignoreCase: true);
            filtered = filtered.Where(l => l.Level == level);
        }
        if (!string.IsNullOrWhiteSpace(SearchText))
            filtered = filtered.Where(l => l.Payload.Contains(SearchText, StringComparison.OrdinalIgnoreCase));

        FilteredLogs = new ObservableCollection<LogEntry>(filtered);
        LogCount = FilteredLogs.Count;
    }

    [RelayCommand]
    private async Task StartAsync()
    {
        if (_started) return;
        _started = true;
        await _clash.StartLogAsync();
    }

    [RelayCommand]
    private async Task StopAsync()
    {
        _started = false;
        await _clash.StopLogAsync();
    }

    [RelayCommand]
    private void Clear()
    {
        Logs.Clear();
        FilteredLogs.Clear();
        LogCount = 0;
        HasLogs = false;
    }

    /// <summary>切换暂停/恢复日志流</summary>
    [RelayCommand]
    private void TogglePause()
    {
        IsPaused = !IsPaused;
    }

    public string PauseLabel => IsPaused
        ? LocalizationHelper.GetString("LogsResume.Content")
        : LocalizationHelper.GetString("LogsPause.Content");

    partial void OnIsPausedChanged(bool value) => OnPropertyChanged(nameof(PauseLabel));

    /// <summary>导出日志到文件</summary>
    [RelayCommand]
    private async Task ExportAsync()
    {
        try
        {
            var picker = new Windows.Storage.Pickers.FileSavePicker
            {
                SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary,
                SuggestedFileName = $"winuiclash-logs-{DateTime.Now:yyyyMMdd-HHmmss}.log",
            };
            picker.FileTypeChoices.Add("Log file", [".log"]);
            picker.FileTypeChoices.Add("Text file", [".txt"]);

            // WinUI 3 需要设置窗口句柄
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.CurrentWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var file = await picker.PickSaveFileAsync();
            if (file == null) return;

            var lines = FilteredLogs.Select(l =>
                $"[{l.Timestamp:HH:mm:ss}] [{l.Level}] {l.Payload}");
            var content = string.Join(Environment.NewLine, lines);

            await Windows.Storage.FileIO.WriteTextAsync(file, content);
        }
        catch { /* 用户取消或导出失败时静默 */ }
    }
}
