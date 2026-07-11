using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Controls;
using WinUIClash.Services;

namespace WinUIClash.ViewModels;

/// <summary>
/// 工具页 ViewModel — 子页面导航
/// </summary>
public partial class ToolsViewModel : ObservableObject
{
    // ── 导航 ──

    [ObservableProperty] private UserControl? _currentPage;
    [ObservableProperty] private string _currentTitle = "";
    [ObservableProperty] private bool _isSubPage;

    private string? _currentPageKey;
    private readonly Stack<string> _backStack = new();

    public ToolsViewModel()
    {
        var stringResources = ServiceLocator.Get<StringResources>();
        stringResources.PropertyChanged += OnStringResourcesChanged;
    }

    private void OnStringResourcesChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName) && _currentPageKey != null)
            CurrentTitle = GetTitleForKey(_currentPageKey);
    }

    private static string GetTitleForKey(string key) => key switch
    {
        "LanguageSettings" => LocalizationHelper.GetString("SettingsLanguage.Text"),
        "ThemeSettings" => LocalizationHelper.GetString("SettingsTheme.Text"),
        "BasicConfig" => LocalizationHelper.GetString("SettingsBasicConfig.Text"),
        "AppSettings" => LocalizationHelper.GetString("SettingsApp.Text"),
        "About" => LocalizationHelper.GetString("SettingsAbout.Text"),
        _ => key
    };

    [RelayCommand]
    private void OpenSetting(string? key)
    {
        if (key == null) return;

        var page = CreatePage(key);
        if (page == null) return;

        _backStack.Push(key);
        _currentPageKey = key;
        CurrentPage = page;
        CurrentTitle = GetTitleForKey(key);
        IsSubPage = true;
    }

    [RelayCommand]
    private void GoBack()
    {
        _currentPageKey = null;
        CurrentPage = null;
        CurrentTitle = "";
        IsSubPage = false;
        _backStack.Clear();
    }

    /// <summary>
    /// 当页面被 Frame.Navigate 重建后，重新创建子页面实例（因为旧的 UserControl 仍挂在旧页面的可视化树上）
    /// </summary>
    public void RecreateCurrentPage()
    {
        if (_currentPageKey != null)
        {
            CurrentPage = CreatePage(_currentPageKey);
        }
    }

    private static UserControl? CreatePage(string key)
    {
        return key switch
        {
            "LanguageSettings" => new Views.Settings.LanguageSettingsView(),
            "ThemeSettings" => new Views.Settings.ThemeSettingsView(),
            "BasicConfig" => new Views.Settings.BasicConfigView(),
            "AppSettings" => new Views.Settings.AppSettingsView(),
            "About" => new Views.Settings.AboutView(),
            _ => null
        };
    }
}
