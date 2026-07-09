using System.Collections.Concurrent;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using WinUIClash.Models;

namespace WinUIClash.Services;

/// <summary>
/// 应用内通知服务 — 使用 InfoBar 显示 toast 通知（支持队列，避免快速连续通知互相覆盖）
/// </summary>
public class NotificationService
{
    private readonly DispatcherQueue _dispatcher;
    private readonly AppSettings _settings;
    private InfoBar? _infoBar;

    private record NotificationItem(string Title, string Message, InfoBarSeverity Severity, int DurationMs);

    private readonly ConcurrentQueue<NotificationItem> _queue = new();
    private volatile bool _isProcessing;

    public NotificationService(AppSettings settings)
    {
        _dispatcher = DispatcherQueue.GetForCurrentThread()!;
        _settings = settings;
    }

    /// <summary>注册 InfoBar 控件（由 MainWindow 调用）</summary>
    public void Register(InfoBar infoBar)
    {
        _infoBar = infoBar;
    }

    /// <summary>显示信息通知</summary>
    public void Info(string title, string message, int durationMs = 3000)
        => Enqueue(title, message, InfoBarSeverity.Informational, durationMs);

    /// <summary>显示成功通知</summary>
    public void Success(string title, string message, int durationMs = 3000)
        => Enqueue(title, message, InfoBarSeverity.Success, durationMs);

    /// <summary>显示警告通知</summary>
    public void Warning(string title, string message, int durationMs = 5000)
        => Enqueue(title, message, InfoBarSeverity.Warning, durationMs);

    /// <summary>显示错误通知（始终显示）</summary>
    public void Error(string title, string message, int durationMs = 5000)
        => Enqueue(title, message, InfoBarSeverity.Error, durationMs);

    private void Enqueue(string title, string message, InfoBarSeverity severity, int durationMs)
    {
        // 当通知被禁用时，只显示错误通知
        if (!_settings.ShowNotifications && severity != InfoBarSeverity.Error)
            return;

        _queue.Enqueue(new NotificationItem(title, message, severity, durationMs));
        _ = ProcessQueueAsync();
    }

    private async Task ProcessQueueAsync()
    {
        if (_isProcessing) return;
        _isProcessing = true;

        try
        {
            while (_queue.TryDequeue(out var item))
            {
                var tcs = new TaskCompletionSource();

                _dispatcher.TryEnqueue(() =>
                {
                    if (_infoBar == null)
                    {
                        tcs.TrySetResult();
                        return;
                    }

                    _infoBar.Title = item.Title;
                    _infoBar.Message = item.Message;
                    _infoBar.Severity = item.Severity;
                    _infoBar.IsOpen = true;

                    // Close handler: signal completion when user manually closes
                    void OnClosing(InfoBar sender, InfoBarClosingEventArgs args)
                    {
                        sender.Closing -= OnClosing;
                        tcs.TrySetResult();
                    }
                    _infoBar.Closing += OnClosing;

                    tcs.TrySetResult();
                });

                await tcs.Task;

                if (item.DurationMs > 0)
                    await Task.Delay(item.DurationMs);

                _dispatcher.TryEnqueue(() =>
                {
                    if (_infoBar != null)
                        _infoBar.IsOpen = false;
                });

                // Small gap between notifications
                await Task.Delay(200);
            }
        }
        finally
        {
            _isProcessing = false;
        }
    }
}
