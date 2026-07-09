using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinUIClash.Models;
using WinUIClash.Services;

namespace WinUIClash.ViewModels;

/// <summary>
/// 日志页 ViewModel — 实时日志流 + 搜索过滤
/// </summary>
public partial class LogsViewModel : ObservableObject
{
    private readonly IClashService _clash;
    private const int MaxLogEntries = 500;

    public LogsViewModel(IClashService clash)
    {
        _clash = clash;
        _clash.LogReceived += OnLogReceived;
    }

    [ObservableProperty] private ObservableCollection<LogEntry> _logs = new();
    [ObservableProperty] private ObservableCollection<LogEntry> _filteredLogs = new();
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private bool _autoScroll = true;
    [ObservableProperty] private string _selectedLevel = "全部";

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnSelectedLevelChanged(string value) => ApplyFilter();

    private void OnLogReceived(LogEntry entry)
    {
        Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()?.TryEnqueue(() =>
        {
            Logs.Add(entry);
            if (Logs.Count > MaxLogEntries) Logs.RemoveAt(0);
            ApplyFilter();
        });
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
        {
            filtered = filtered.Where(l =>
                l.Payload.Contains(SearchText, StringComparison.OrdinalIgnoreCase));
        }

        FilteredLogs = new ObservableCollection<LogEntry>(filtered);
    }

    [RelayCommand]
    private async Task StartAsync()
    {
        await _clash.StartLogAsync();
    }

    [RelayCommand]
    private void Clear()
    {
        Logs.Clear();
        FilteredLogs.Clear();
    }
}
