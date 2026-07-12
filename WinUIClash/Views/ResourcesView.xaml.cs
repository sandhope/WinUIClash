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

    private void UpdateProvider_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is ExternalProvider provider)
            ViewModel.UpdateProviderCommand.Execute(provider);
    }
}
