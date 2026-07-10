using Microsoft.UI.Xaml.Controls;
using WinUIClash.Services;
using WinUIClash.ViewModels.Settings;

namespace WinUIClash.Views.Settings;

public sealed partial class LanguageSettingsView : UserControl
{
    public LanguageSettingsViewModel ViewModel { get; }

    public LanguageSettingsView()
    {
        ViewModel = ServiceLocator.Get<LanguageSettingsViewModel>();
        InitializeComponent();
    }
}
