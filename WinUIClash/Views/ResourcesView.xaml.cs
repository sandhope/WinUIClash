using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinUIClash.Models;
using WinUIClash.Services;
using WinUIClash.ViewModels;

namespace WinUIClash.Views;

public sealed partial class ResourcesView : Page
{
    public ResourcesViewModel ViewModel { get; }

    public ResourcesView()
    {
        ViewModel = ServiceLocator.Get<ResourcesViewModel>();
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            try { await ViewModel.InitializeAsync(); }
            catch { /* 核心未运行或初始化出错时保持空状态，避免崩溃 */ }
        };
        Unloaded += (_, _) =>
        {
            if (ViewModel is IDisposable d) d.Dispose();
        };
    }

    private async void Sync_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is GeoResourceItem item)
            await ViewModel.UpdateItemCommand.ExecuteAsync(item);
    }

    private async void EditUrl_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not GeoResourceItem item) return;

        var box = new TextBox
        {
            Text = item.Url,
            AcceptsReturn = false,
            Header = LocalizationHelper.GetString("GeoEditUrlTitle.Text"),
        };
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = item.DisplayName,
            Content = box,
            PrimaryButtonText = LocalizationHelper.GetString("CommonConfirm.Content"),
            CloseButtonText = LocalizationHelper.GetString("CommonClose.Content"),
            DefaultButton = ContentDialogButton.Primary,
        };
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            var url = box.Text?.Trim() ?? "";
            if (!string.IsNullOrEmpty(url))
                ViewModel.SetUrl(item, url);
        }
    }
}
