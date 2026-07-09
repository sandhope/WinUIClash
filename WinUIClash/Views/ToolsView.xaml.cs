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
        if (sender is Button btn && btn.Tag is ToolsViewModel.SettingItem item)
        {
            ViewModel.OpenSettingCommand.Execute(item);
            SubPageContent.Content = ViewModel.CurrentPage;
        }
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        SubPageContent.Content = null;
        ViewModel.GoBackCommand.Execute(null);
    }

    private void ToolItem_ElementPrepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args)
    {
        if (args.Element is FrameworkElement { DataContext: ToolsViewModel.SettingItem item })
        {
            if (FindDescendant<FluentIcons.WinUI.SymbolIcon>(args.Element, "ItemIcon") is { } icon &&
                Enum.TryParse<FluentIcons.Common.Symbol>(item.IconName, out var sym))
            {
                icon.Symbol = sym;
            }
        }
    }

    private static T? FindDescendant<T>(DependencyObject root, string name) where T : FrameworkElement
    {
        var count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, i);
            if (child is T t && t.Name == name) return t;
            var found = FindDescendant<T>(child, name);
            if (found != null) return found;
        }
        return null;
    }

    /// <summary>
    /// 供外部（搜索导航）同步子页面 Content
    /// </summary>
    public void SyncSubPage(UserControl? page)
    {
        SubPageContent.Content = page;
    }
}
