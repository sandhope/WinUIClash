using Microsoft.UI.Xaml.Controls;
using WinUIClash.ViewModels;

namespace WinUIClash.Views;

public sealed partial class LogsView : Page
{
    public LogsViewModel ViewModel { get; }

    public LogsView()
    {
        ViewModel = ServiceLocator.Get<LogsViewModel>();
        InitializeComponent();
        Loaded += (_, _) => ViewModel.StartCommand.Execute(null);
    }
}
