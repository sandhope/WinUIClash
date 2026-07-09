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
}
