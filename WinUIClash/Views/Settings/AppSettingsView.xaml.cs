using Microsoft.UI.Xaml.Controls;
using WinUIClash.Models;
using WinUIClash.Services;
using WinUIClash.ViewModels.Settings;

namespace WinUIClash.Views.Settings;

public sealed partial class AppSettingsView : UserControl
{
    public AppSettingsViewModel ViewModel { get; }

    public AppSettingsView()
    {
        var settings = ServiceLocator.Get<AppSettings>();
        var autoLaunch = ServiceLocator.Get<AutoLaunchService>();
        ViewModel = new AppSettingsViewModel(settings, autoLaunch);
        InitializeComponent();
    }
}
