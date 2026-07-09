using Microsoft.UI.Xaml.Controls;
using WinUIClash.ViewModels;

namespace WinUIClash.Views;

public sealed partial class ToolsView : Page
{
    public ToolsViewModel ViewModel { get; }

    public ToolsView()
    {
        ViewModel = ServiceLocator.Get<ToolsViewModel>();
        InitializeComponent();
    }
}
