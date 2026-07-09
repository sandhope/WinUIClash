using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace WinUIClash.Views.Settings;

public sealed partial class AboutView : UserControl
{
    public AboutView()
    {
        InitializeComponent();
    }

    private void OpenDataFolder_Click(object sender, RoutedEventArgs e)
    {
        var dataDir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WinUIClash");

        if (!System.IO.Directory.Exists(dataDir))
            System.IO.Directory.CreateDirectory(dataDir);

        Process.Start(new ProcessStartInfo
        {
            FileName = dataDir,
            UseShellExecute = true,
        });
    }

    private void CopyVersion_Click(object sender, RoutedEventArgs e)
    {
        var version = "WinUIClash v0.1.0 (dev)\n.NET 10 + Windows App SDK 2.2\nClashMeta (Mihomo)";
        var dp = new Windows.ApplicationModel.DataTransfer.DataPackage();
        dp.SetText(version);
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp);
    }
}
