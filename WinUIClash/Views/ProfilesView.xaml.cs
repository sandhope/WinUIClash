using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinUIClash.Models;
using WinUIClash.ViewModels;

namespace WinUIClash.Views;

public sealed partial class ProfilesView : Page
{
    public ProfilesViewModel ViewModel { get; }

    public ProfilesView()
    {
        ViewModel = ServiceLocator.Get<ProfilesViewModel>();
        InitializeComponent();
        Loaded += async (_, _) => await ViewModel.InitializeAsync();
    }

    private void ProfileGrid_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Profile profile)
            ViewModel.SelectProfileCommand.Execute(profile);
    }

    private void SyncButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is Profile profile)
            ViewModel.SyncProfileCommand.Execute(profile);
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is Profile profile)
            ViewModel.DeleteProfileCommand.Execute(profile);
    }

    private async void ImportButton_Click(object sender, RoutedEventArgs e)
    {
        var urlBox = new TextBox
        {
            PlaceholderText = "https://example.com/sub?token=xxx",
            Header = "订阅地址",
        };

        var nameBox = new TextBox
        {
            PlaceholderText = "自动识别（可手动修改）",
            Header = "配置名称",
            Margin = new Thickness(0, 12, 0, 0),
        };

        var dialog = new ContentDialog
        {
            Title = "导入订阅",
            XamlRoot = XamlRoot,
            PrimaryButtonText = "导入",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            Content = new StackPanel
            {
                Spacing = 0,
                Children = { urlBox, nameBox }
            }
        };

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary) return;

        var url = urlBox.Text.Trim();
        if (string.IsNullOrEmpty(url)) return;

        var name = string.IsNullOrWhiteSpace(nameBox.Text) ? null : nameBox.Text.Trim();
        await ViewModel.ImportProfileAsync(url, name);
    }
}
