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
            ViewModel.OpenSettingCommand.Execute(item);
    }
}
