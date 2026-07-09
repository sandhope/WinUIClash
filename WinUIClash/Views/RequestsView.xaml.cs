using Microsoft.UI.Xaml.Controls;
using WinUIClash.ViewModels;

namespace WinUIClash.Views;

public sealed partial class RequestsView : Page
{
    public RequestsViewModel ViewModel { get; }

    public RequestsView()
    {
        ViewModel = ServiceLocator.Get<RequestsViewModel>();
        InitializeComponent();
        Loaded += async (_, _) => await ViewModel.InitializeAsync();
    }
}
