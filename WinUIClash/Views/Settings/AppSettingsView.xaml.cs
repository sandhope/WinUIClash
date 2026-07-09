using Microsoft.UI.Xaml.Controls;
using WinUIClash.ViewModels.Settings;

namespace WinUIClash.Views.Settings;

public sealed partial class AppSettingsView : UserControl
{
    public AppSettingsViewModel ViewModel { get; }

    public AppSettingsView()
    {
        ViewModel = ServiceLocator.Get<AppSettingsViewModel>();
        InitializeComponent();
    }
}
