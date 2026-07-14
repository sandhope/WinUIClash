using Microsoft.UI.Xaml.Controls;
using WinUIClash.Services;
using WinUIClash.ViewModels;

namespace WinUIClash.Views;

public sealed partial class DeveloperToolsView : UserControl
{
    public DeveloperToolsViewModel ViewModel { get; }

    public DeveloperToolsView()
    {
        ViewModel = ServiceLocator.Get<DeveloperToolsViewModel>();
        this.InitializeComponent();
        this.Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        this.Loaded -= OnLoaded;
        await ViewModel.InitializeAsync();
    }
}
