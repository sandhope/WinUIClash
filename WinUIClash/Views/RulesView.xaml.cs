using Microsoft.UI.Xaml.Controls;
using WinUIClash.ViewModels;

namespace WinUIClash.Views;

public sealed partial class RulesView : Page
{
    public RulesViewModel ViewModel { get; }

    public RulesView()
    {
        ViewModel = ServiceLocator.Get<RulesViewModel>();
        InitializeComponent();
        Loaded += async (_, _) => await ViewModel.InitializeAsync();
    }
}
