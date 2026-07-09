using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WinUIClash.Models;
using WinUIClash.Services;
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

        // 自动滚动到底部
        ViewModel.LogAppended += () =>
        {
            if (ViewModel.AutoScroll && LogList.Items.Count > 0)
            {
                LogList.ScrollIntoView(LogList.Items[^1]);
            }
        };
    }

    private void LogItem_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: LogEntry entry } element) return;

        var menu = new MenuFlyout();

        var copyPayload = new MenuFlyoutItem
        {
            Text = LocalizationHelper.GetString("LogsCopyPayload.Text")
        };
        copyPayload.Click += (_, _) => CopyToClipboard(entry.Payload);
        menu.Items.Add(copyPayload);

        var copyFull = new MenuFlyoutItem
        {
            Text = LocalizationHelper.GetString("LogsCopyFull.Text")
        };
        copyFull.Click += (_, _) => CopyToClipboard(
            $"[{entry.Timestamp:HH:mm:ss}] [{entry.Level}] {entry.Payload}");
        menu.Items.Add(copyFull);

        menu.ShowAt(element, e.GetPosition(element));
    }

    private static void CopyToClipboard(string text)
    {
        var dp = new Windows.ApplicationModel.DataTransfer.DataPackage();
        dp.SetText(text);
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp);
    }
}
