using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinUIClash.Services;
using WinUIClash.ViewModels.Settings;

namespace WinUIClash.Views.Settings;

public sealed partial class BasicConfigView : UserControl
{
    public BasicConfigViewModel ViewModel { get; }

    public BasicConfigView()
    {
        ViewModel = ServiceLocator.Get<BasicConfigViewModel>();
        InitializeComponent();
    }

    private async void BrowseCoreBinary_Click(object sender, RoutedEventArgs e)
    {
        var picker = new Windows.Storage.Pickers.FileOpenPicker
        {
            SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.ComputerFolder,
        };
        picker.FileTypeFilter.Add(".exe");

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.CurrentWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        var file = await picker.PickSingleFileAsync();
        if (file != null)
        {
            ViewModel.CoreBinaryPath = file.Path;

            // Apply to the running CoreProcessService
            try
            {
                var coreService = ServiceLocator.Get<Services.CoreProcessService>();
                coreService.SetBinaryPath(file.Path);
            }
            catch { }
        }
    }
}
