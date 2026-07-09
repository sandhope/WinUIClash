using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Controls;
using WinUIClash.Services;

namespace WinUIClash.ViewModels;

/// <summary>
/// 工具页 ViewModel — 设置入口 + 子页面导航
/// </summary>
public partial class ToolsViewModel : ObservableObject
{
    public record SettingItem(string Title, string Subtitle, string PageKey);

    public IReadOnlyList<SettingItem> SettingsItems { get; } =
    [
        new(LocalizationHelper.GetString("SettingsBasicConfig.Text"), LocalizationHelper.GetString("ToolsBasicConfigSub.Text"), "BasicConfig"),
        new(LocalizationHelper.GetString("SettingsApp.Text"), LocalizationHelper.GetString("ToolsAppSettingsSub.Text"), "AppSettings"),
        new(LocalizationHelper.GetString("SettingsTheme.Text"), LocalizationHelper.GetString("ToolsThemeSettingsSub.Text"), "ThemeSettings"),
    ];

    public IReadOnlyList<SettingItem> OtherItems { get; } =
    [
        new(LocalizationHelper.GetString("SettingsAbout.Text"), LocalizationHelper.GetString("ToolsAboutSub.Text"), "About"),
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
