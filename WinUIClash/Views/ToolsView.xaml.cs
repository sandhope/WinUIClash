using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinUIClash.ViewModels;

namespace WinUIClash.Views;

public sealed partial class ToolsView : Page
{
    public ToolsViewModel ViewModel { get; }

    public ToolsView()
    {
        ViewModel = ServiceLocator.Get<ToolsViewModel>();
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // 当 Frame.Navigate 重新创建此页面时，恢复子页面状态
        if (ViewModel.IsSubPage)
        {
            ViewModel.RecreateCurrentPage();
            SubPageContent.Content = ViewModel.CurrentPage;
        }
    }

    private void SettingItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string key)
        {
            ViewModel.OpenSettingCommand.Execute(key);
            SubPageContent.Content = ViewModel.CurrentPage;
        }
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        SubPageContent.Content = null;
        ViewModel.GoBackCommand.Execute(null);
    }

    /// <summary>
    /// 供外部（搜索导航）同步子页面 Content
    /// </summary>
    public void SyncSubPage(UserControl? page)
    {
        SubPageContent.Content = page;
    }
}
