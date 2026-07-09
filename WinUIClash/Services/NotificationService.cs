using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;

namespace WinUIClash.Services;

/// <summary>
/// 应用内通知服务 — 使用 InfoBar 显示 toast 通知
/// </summary>
public class NotificationService
{
    private readonly DispatcherQueue _dispatcher;
    private InfoBar? _infoBar;

    public NotificationService()
    {
        _dispatcher = DispatcherQueue.GetForCurrentThread()!;
    }

    /// <summary>注册 InfoBar 控件（由 MainWindow 调用）</summary>
    public void Register(InfoBar infoBar)
    {
        _infoBar = infoBar;
    }

    /// <summary>显示信息通知</summary>
    public void Info(string title, string message, int durationMs = 3000)
        => Show(title, message, InfoBarSeverity.Informational, durationMs);

    /// <summary>显示成功通知</summary>
    public void Success(string title, string message, int durationMs = 3000)
        => Show(title, message, InfoBarSeverity.Success, durationMs);

    /// <summary>显示警告通知</summary>
    public void Warning(string title, string message, int durationMs = 5000)
        => Show(title, message, InfoBarSeverity.Warning, durationMs);

    /// <summary>显示错误通知</summary>
    public void Error(string title, string message, int durationMs = 5000)
        => Show(title, message, InfoBarSeverity.Error, durationMs);

    private void Show(string title, string message, InfoBarSeverity severity, int durationMs)
    {
        _dispatcher.TryEnqueue(async () =>
        {
            if (_infoBar == null) return;

            _infoBar.Title = title;
            _infoBar.Message = message;
            _infoBar.Severity = severity;
            _infoBar.IsOpen = true;

            if (durationMs > 0)
            {
                await Task.Delay(durationMs);
                _infoBar.IsOpen = false;
            }
        });
    }
}
