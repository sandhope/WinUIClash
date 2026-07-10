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
