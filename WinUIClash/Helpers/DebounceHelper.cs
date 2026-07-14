using System;
using System.Threading;
using System.Threading.Tasks;

namespace WinUIClash.Helpers;

/// <summary>
/// 异步防抖器（trailing-edge，合并窗口内的重复触发）。
/// <para>
/// 在 <paramref name="delay"/> 静默期内多次 <see cref="Pulse"/>，只在最后一次 Pulse 之后
/// 延迟 <paramref name="delay"/> 执行一次 <paramref name="action"/>；窗口内重复触发被合并。
/// 若上一次执行尚未结束，则在其结束后最多再补一次（合并尾随），保证不会并发执行。
/// </para>
/// 典型用途：把高频的属性变更 / 用户输入合并成一次持久化或一次后端请求（如保存设置、PATCH 配置）。
/// <para>线程安全，实现 <see cref="IDisposable"/>（释放内部计时器）。</para>
/// </summary>
public sealed class DebounceHelper : IDisposable
{
    private readonly Func<CancellationToken, Task> _action;
    private readonly TimeSpan _delay;
    private readonly Timer _timer;
    private int _executing;   // 0 = 空闲, 1 = 正在执行
    private bool _reArm;     // 执行期间又触发了，结束后补一次
    private int _disposed;

    public DebounceHelper(Func<CancellationToken, Task> action, TimeSpan delay)
    {
        _action = action ?? throw new ArgumentNullException(nameof(action));
        _delay = delay;
        // 单发计时器：每次 Pulse 通过 Change 重新安排一次触发
        _timer = new Timer(_ => _ = FireAsync(), null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>触发一次防抖。在静默期内会重置计时器（合并重复触发）。</summary>
    public void Pulse()
    {
        if (_disposed != 0) return;
        try { _timer.Change(_delay, Timeout.InfiniteTimeSpan); }
        catch (ObjectDisposedException) { }
    }

    private async Task FireAsync()
    {
        // 防止同一时刻并发执行 action
        if (Interlocked.Exchange(ref _executing, 1) == 1)
        {
            _reArm = true;   // 已有执行在跑，标记结束后再补一次
            return;
        }

        try
        {
            do
            {
                _reArm = false;
                try
                {
                    await _action(CancellationToken.None);
                }
                catch (Exception ex)
                {
                    // 防抖回调异常不应逃逸到线程池（否则会崩溃进程）
                    System.Diagnostics.Debug.WriteLine($"[DebounceHelper] action threw: {ex}");
                }
            } while (_reArm);
        }
        finally
        {
            Interlocked.Exchange(ref _executing, 0);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _timer.Dispose();
    }
}
