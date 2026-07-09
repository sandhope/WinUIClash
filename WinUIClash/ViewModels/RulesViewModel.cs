using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinUIClash.Models;
using WinUIClash.Services;

namespace WinUIClash.ViewModels;

/// <summary>
/// 规则页 ViewModel — 展示当前活跃的路由规则，支持按类型和目标代理筛选
/// </summary>
public partial class RulesViewModel : ObservableObject
{
    private readonly IClashService _clash;
    private readonly NotificationService _notification;
    private bool _initialized;

    public RulesViewModel(IClashService clash, NotificationService notification)
    {
        _clash = clash;
        _notification = notification;
    }

    [ObservableProperty] private ObservableCollection<Rule> _rules = new();
    [ObservableProperty] private ObservableCollection<Rule> _filteredRules = new();
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private int _totalCount;

    /// <summary>规则类型统计项</summary>
    public record RuleTypeOption(string Label, string Value, int Count);

    /// <summary>可选的规则类型列表</summary>
    [ObservableProperty] private ObservableCollection<RuleTypeOption> _ruleTypes = new();

    /// <summary>当前选中的类型筛选（"ALL" = 全部）</summary>
    [ObservableProperty] private string _selectedTypeFilter = "ALL";

    /// <summary>可选的目标代理列表</summary>
    [ObservableProperty] private ObservableCollection<string> _proxyOptions = new();

    /// <summary>当前选中的目标代理筛选（localized "All" label = show all）</summary>
    [ObservableProperty] private string _selectedProxyFilter = "";

    /// <summary>筛选后的规则数</summary>
    [ObservableProperty] private int _filteredCount;

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    partial void OnSelectedTypeFilterChanged(string value) => ApplyFilter();

    partial void OnSelectedProxyFilterChanged(string value) => ApplyFilter();

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            var list = await _clash.GetRulesAsync();
            Rules = new ObservableCollection<Rule>(list);
            TotalCount = list.Count;

            // 构建类型统计
            var typeGroups = list.GroupBy(r => r.Type)
                .OrderByDescending(g => g.Count())
                .ToList();

            var types = new ObservableCollection<RuleTypeOption>
            {
                new(LocalizationHelper.GetString("RulesAllTypes.Text"), "ALL", list.Count)
            };
            foreach (var g in typeGroups)
            {
                types.Add(new RuleTypeOption($"{g.Key} ({g.Count()})", g.Key, g.Count()));
            }
            RuleTypes = types;

            // 构建代理选项
            var proxies = list.Select(r => r.Proxy)
                .Distinct()
                .OrderBy(p => p)
                .ToList();
            var proxyOpts = new ObservableCollection<string> { LocalizationHelper.GetString("RulesAllProxies.Text") };
            foreach (var p in proxies)
            {
                proxyOpts.Add(p);
            }
            ProxyOptions = proxyOpts;

            ApplyFilter();
        }
        catch (Exception ex)
        {
            _notification.Error(
                LocalizationHelper.GetString("ErrorLoadRules.Text"),
                ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void ApplyFilter()
    {
        IEnumerable<Rule> query = Rules;

        // 按类型筛选
        if (!string.IsNullOrEmpty(SelectedTypeFilter) && SelectedTypeFilter != "ALL")
        {
            query = query.Where(r => r.Type == SelectedTypeFilter);
        }

        // 按目标代理筛选 (skip if first "all proxies" label)
        if (!string.IsNullOrEmpty(SelectedProxyFilter) &&
            SelectedProxyFilter != LocalizationHelper.GetString("RulesAllProxies.Text"))
        {
            query = query.Where(r => r.Proxy == SelectedProxyFilter);
        }

        // 按关键词搜索
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var keyword = SearchText.Trim();
            query = query.Where(r =>
                r.Type.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                r.Payload.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                r.Proxy.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        }

        var result = query.ToList();
        FilteredRules = new ObservableCollection<Rule>(result);
        FilteredCount = result.Count;
    }

    public async Task InitializeAsync()
    {
        if (_initialized) return;
        _initialized = true;
        await LoadAsync();
    }

    [RelayCommand]
    private async Task ExportAsync()
    {
        try
        {
            var picker = new Windows.Storage.Pickers.FileSavePicker
            {
                SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary,
                SuggestedFileName = $"WinUIClash_Rules_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
            };
            picker.FileTypeChoices.Add("CSV", [".csv"]);
            picker.FileTypeChoices.Add("Text file", [".txt"]);

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.CurrentWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var file = await picker.PickSaveFileAsync();
            if (file == null) return;

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Type,Payload,Proxy");
            foreach (var r in FilteredRules)
            {
                sb.AppendLine($"\"{r.Type}\",\"{r.Payload}\",\"{r.Proxy}\"");
            }

            await Windows.Storage.FileIO.WriteTextAsync(file, sb.ToString());

            _notification.Success(
                LocalizationHelper.GetString("RequestsExportSuccessTitle.Text"),
                file.Path);
        }
        catch (Exception ex) when (ex is not OperationCanceledException
            && ex.HResult != unchecked((int)0x80004004))
        {
            _notification.Error(
                LocalizationHelper.GetString("RequestsExportFailTitle.Text"),
                ex.Message);
        }
    }
}
