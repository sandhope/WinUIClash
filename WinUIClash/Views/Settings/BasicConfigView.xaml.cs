using Microsoft.UI.Xaml.Controls;
using WinUIClash.Models;
using WinUIClash.ViewModels.Settings;

namespace WinUIClash.Views.Settings;

public sealed partial class BasicConfigView : UserControl
{
    public BasicConfigViewModel ViewModel { get; }

    public BasicConfigView()
    {
        var settings = ServiceLocator.Get<AppSettings>();
        ViewModel = new BasicConfigViewModel(settings);
        InitializeComponent();
    }
}
