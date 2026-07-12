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
        Loaded += (_, _) =>
        {
            try { ViewModel.StartCommand.Execute(null); }
            catch { /* 核心未运行或出错时保持空状态，避免崩溃 */ }
        };

        // 自动滚动到底部
        ViewModel.LogAppended += () =>
        {
            if (ViewModel.AutoScroll && LogList.Items.Count > 0)
            {
                LogList.ScrollIntoView(LogList.Items[^1]);
            }
        };
    }

    private void LogLevelBadge_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: LogLevel level })
        {
            var levelStr = level.ToString();
            ViewModel.SelectedLevel = ViewModel.SelectedLevel == levelStr ? "ALL" : levelStr;
        }
        e.Handled = true;
    }
}
