using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinUIClash.Services;

namespace WinUIClash.Views.Settings;

public sealed partial class AboutView : UserControl
{
    private readonly UpdateService _updateService;

    public AboutView()
    {
        InitializeComponent();
        _updateService = ServiceLocator.Get<UpdateService>();
        VersionText.Text = $"版本 {UpdateService.CurrentVersion}";
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
        var version = $"WinUIClash v{UpdateService.CurrentVersion}\n.NET 10 + Windows App SDK 2.2\nClashMeta (Mihomo)";
        var dp = new Windows.ApplicationModel.DataTransfer.DataPackage();
        dp.SetText(version);
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp);
    }

    private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        CheckUpdateBtn.IsEnabled = false;
        CheckUpdateLabel.Text = "检查中…";
        UpdateStatusText.Visibility = Visibility.Visible;
        UpdateStatusText.Text = "正在检查更新…";

        try
        {
            var update = await _updateService.CheckForUpdateAsync();

            if (update == null)
            {
                UpdateStatusText.Text = $"✓ 当前已是最新版本 (v{UpdateService.CurrentVersion})";
            }
            else
            {
                UpdateStatusText.Text = $"发现新版本 v{update.Version} — 点击前往下载";

                var dialog = new ContentDialog
                {
                    Title = "发现新版本",
                    Content = $"新版本: {update.TagName}\n\n{update.ReleaseNotes}",
                    PrimaryButtonText = "前往下载",
                    CloseButtonText = "稍后",
                    XamlRoot = this.XamlRoot,
                };

                if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                {
                    UpdateService.OpenReleasePage(update.DownloadUrl);
                }
            }
        }
        catch (UpdateCheckException ex)
        {
            UpdateStatusText.Text = $"检查失败: {ex.Message}";
        }
        catch (Exception ex)
        {
            UpdateStatusText.Text = $"检查失败: {ex.Message}";
        }
        finally
        {
            CheckUpdateBtn.IsEnabled = true;
            CheckUpdateLabel.Text = "检查更新";
        }
    }
}
