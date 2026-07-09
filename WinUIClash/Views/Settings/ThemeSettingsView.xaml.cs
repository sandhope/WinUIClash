using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinUIClash.ViewModels.Settings;

namespace WinUIClash.Views.Settings;

public sealed partial class ThemeSettingsView : UserControl
{
    public ThemeSettingsViewModel ViewModel { get; }

    public ThemeSettingsView()
    {
        ViewModel = ServiceLocator.Get<ThemeSettingsViewModel>();
        InitializeComponent();
    }

    private void Color_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is ThemeSettingsViewModel.ThemeColor color)
        {
            var index = Array.IndexOf(ViewModel.PrimaryColors, color);
            if (index >= 0) ViewModel.PrimaryColorIndex = index;
        }
    }
}
