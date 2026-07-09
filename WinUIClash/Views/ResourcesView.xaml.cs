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
        Loaded += async (_, _) => await ViewModel.InitializeAsync();
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

    private void TypeFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox combo && combo.SelectedItem is ComboBoxItem item)
        {
            ViewModel.TypeFilter = item.Tag as string ?? "ALL";
        }
    }

    private async void UpdateGeoIp_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var notification = ServiceLocator.Get<NotificationService>();
            notification.Info(
                LocalizationHelper.GetString("ResGeoUpdating.Text"),
                LocalizationHelper.GetString("ResGeoUpdateGeoIp.Text"));
            var clash = ServiceLocator.Get<IClashService>();
            await clash.UpdateGeoDatabaseAsync("geoip");
            notification.Success(
                LocalizationHelper.GetString("ResGeoUpdateSuccess.Text"),
                "GeoIP");
        }
        catch
        {
            var notification = ServiceLocator.Get<NotificationService>();
            notification.Error(
                LocalizationHelper.GetString("ErrorUpdateTitle.Text"),
                "GeoIP");
        }
    }

    private async void UpdateGeoSite_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var notification = ServiceLocator.Get<NotificationService>();
            notification.Info(
                LocalizationHelper.GetString("ResGeoUpdating.Text"),
                LocalizationHelper.GetString("ResGeoUpdateGeoSite.Text"));
            var clash = ServiceLocator.Get<IClashService>();
            await clash.UpdateGeoDatabaseAsync("geosite");
            notification.Success(
                LocalizationHelper.GetString("ResGeoUpdateSuccess.Text"),
                "GeoSite");
        }
        catch
        {
            var notification = ServiceLocator.Get<NotificationService>();
            notification.Error(
                LocalizationHelper.GetString("ErrorUpdateTitle.Text"),
                "GeoSite");
        }
    }
}
