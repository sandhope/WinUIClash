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
        VersionText.Text = LocalizationHelper.GetString("AboutVersion.Text") + UpdateService.CurrentVersion;
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
        CheckUpdateLabel.Text = LocalizationHelper.GetString("AboutCheckingUpdate.Text");
        UpdateIcon.Visibility = Visibility.Collapsed;
        UpdateProgressRing.IsActive = true;
        UpdateProgressRing.Visibility = Visibility.Visible;
        UpdateStatusText.Visibility = Visibility.Visible;
        UpdateStatusText.Text = LocalizationHelper.GetString("AboutCheckingStatus.Text");

        try
        {
            var update = await _updateService.CheckForUpdateAsync();

            if (update == null)
            {
                UpdateStatusText.Text = string.Format(LocalizationHelper.GetString("AboutUpToDate.Text"), UpdateService.CurrentVersion);
            }
            else
            {
                UpdateStatusText.Text = string.Format(LocalizationHelper.GetString("AboutNewVersionFound.Text"), update.Version);

                var dialog = new ContentDialog
                {
                    Title = LocalizationHelper.GetString("AboutNewVersionTitle.Text"),
                    Content = string.Format(LocalizationHelper.GetString("AboutNewVersionContent.Text"), update.TagName, update.ReleaseNotes),
                    PrimaryButtonText = LocalizationHelper.GetString("AboutGoDownload.Content"),
                    CloseButtonText = LocalizationHelper.GetString("AboutLater.Content"),
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
            UpdateStatusText.Text = string.Format(LocalizationHelper.GetString("AboutCheckFailed.Text"), ex.Message);
        }
        catch (Exception ex)
        {
            UpdateStatusText.Text = string.Format(LocalizationHelper.GetString("AboutCheckFailed.Text"), ex.Message);
        }
        finally
        {
            UpdateIcon.Visibility = Visibility.Visible;
            UpdateProgressRing.IsActive = false;
            UpdateProgressRing.Visibility = Visibility.Collapsed;
            CheckUpdateBtn.IsEnabled = true;
            CheckUpdateLabel.Text = LocalizationHelper.GetString("AboutCheckUpdate.Content");
        }
    }

    private async void ExportLogs_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new Windows.Storage.Pickers.FileSavePicker
            {
                SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary,
                SuggestedFileName = $"WinUIClash_Diagnostic_{DateTime.Now:yyyyMMdd_HHmmss}.txt",
            };
            picker.FileTypeChoices.Add("Text file", [".txt"]);
            picker.FileTypeChoices.Add("Log file", [".log"]);

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.CurrentWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var file = await picker.PickSaveFileAsync();
            if (file == null) return;

            var dataDir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WinUIClash");

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"WinUIClash Diagnostic Report");
            sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Version: {UpdateService.CurrentVersion}");
            sb.AppendLine($"OS: {Environment.OSVersion}");
            sb.AppendLine($".NET: {Environment.Version}");
            sb.AppendLine();

            // Append crash log if it exists
            var crashLog = System.IO.Path.Combine(dataDir, "crash.log");
            if (File.Exists(crashLog))
            {
                sb.AppendLine("=== crash.log ===");
                sb.AppendLine(await File.ReadAllTextAsync(crashLog));
            }
            else
            {
                sb.AppendLine("(No crash.log found)");
            }

            await Windows.Storage.FileIO.WriteTextAsync(file, sb.ToString());

            var notification = ServiceLocator.Get<NotificationService>();
            notification.Success(
                LocalizationHelper.GetString("AboutExportLogs.Text"),
                file.Path);
        }
        catch (Exception ex) when (ex is not OperationCanceledException
            && ex.HResult != unchecked((int)0x80004004))
        {
            var notification = ServiceLocator.Get<NotificationService>();
            notification.Error("Export Failed", ex.Message);
        }
    }
}
