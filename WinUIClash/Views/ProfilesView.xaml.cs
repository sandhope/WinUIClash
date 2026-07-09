using Microsoft.UI.Xaml.Controls;
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
}
