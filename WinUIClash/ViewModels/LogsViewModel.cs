using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using WinUIClash.Models;
using WinUIClash.Services;

namespace WinUIClash.ViewModels;

/// <summary>
/// 日志页 ViewModel — 实时日志流 + 搜索过滤
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
    [ObservableProperty] private string _selectedLevel = "全部";

    /// <summary>有新日志追加时触发（供 View 层滚动到底部）</summary>
    public event Action? LogAppended;

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnSelectedLevelChanged(string value) => ApplyFilter();

    private void OnLogReceived(LogEntry entry)
    {
        _dispatcher.TryEnqueue(() =>
        {
            Logs.Add(entry);
            if (Logs.Count > MaxLogEntries) Logs.RemoveAt(0);

            // 增量添加而非重建集合
            if (MatchesFilter(entry))
            {
                FilteredLogs.Add(entry);
                if (FilteredLogs.Count > MaxLogEntries) FilteredLogs.RemoveAt(0);
                LogAppended?.Invoke();
            }
        });
    }

    private bool MatchesFilter(LogEntry entry)
    {
        if (SelectedLevel != "全部")
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
        if (SelectedLevel != "全部")
        {
            var level = Enum.Parse<LogLevel>(SelectedLevel, ignoreCase: true);
            filtered = filtered.Where(l => l.Level == level);
        }
        if (!string.IsNullOrWhiteSpace(SearchText))
            filtered = filtered.Where(l => l.Payload.Contains(SearchText, StringComparison.OrdinalIgnoreCase));

        FilteredLogs = new ObservableCollection<LogEntry>(filtered);
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
    }
}
