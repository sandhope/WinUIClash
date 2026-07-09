using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Controls;

namespace WinUIClash.ViewModels;

/// <summary>
/// 工具页 ViewModel — 设置入口 + 子页面导航
/// </summary>
public partial class ToolsViewModel : ObservableObject
{
    public record SettingItem(string Title, string Subtitle, string PageKey);

    public IReadOnlyList<SettingItem> SettingsItems { get; } =
    [
        new("基础配置", "端口、日志级别、UA、IPv6 等", "BasicConfig"),
        new("应用设置", "启动行为、系统代理、自动更新", "AppSettings"),
        new("主题设置", "明暗模式、主题色", "ThemeSettings"),
    ];

    public IReadOnlyList<SettingItem> OtherItems { get; } =
    [
        new("关于", "版本信息、项目链接", "About"),
    ];

    // ── 导航 ──

    [ObservableProperty] private UserControl? _currentPage;
    [ObservableProperty] private string _currentTitle = "";
    [ObservableProperty] private bool _isSubPage;

    private readonly Stack<string> _backStack = new();

    [RelayCommand]
    private void OpenSetting(SettingItem? item)
    {
        if (item == null) return;

        var page = CreatePage(item.PageKey);
        if (page == null) return;

        _backStack.Push(item.Title);
        CurrentPage = page;
        CurrentTitle = item.Title;
        IsSubPage = true;
    }

    [RelayCommand]
    private void GoBack()
    {
        CurrentPage = null;
        CurrentTitle = "";
        IsSubPage = false;
        _backStack.Clear();
    }

    private static UserControl? CreatePage(string key)
    {
        return key switch
        {
            "BasicConfig" => new Views.Settings.BasicConfigView(),
            "AppSettings" => new Views.Settings.AppSettingsView(),
            "ThemeSettings" => new Views.Settings.ThemeSettingsView(),
            "About" => new Views.Settings.AboutView(),
            _ => null
        };
    }
}
