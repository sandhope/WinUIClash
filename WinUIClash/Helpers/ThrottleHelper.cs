using System;
using System.Threading;
using System.Threading.Tasks;

namespace WinUIClash.Helpers;

/// <summary>
/// 异步节流器（leading + trailing）。
/// <para>
/// 保证在 <paramref name="period"/> 窗口内 <paramref name="action"/> 最多执行一次：
/// 首次 <see cref="Pulse"/> 立即执行（leading），窗口内后续触发合并为窗口结束时的最后一次（trailing）。
/// 同一时刻不会并发执行 <paramref name="action"/>。
/// </para>
/// 典型用途：把高频事件（流量采样、拖拽、连续属性变更）限制为至多每 period 执行一次，
/// 同时保留窗口末的最新状态。
/// <para>线程安全，实现 <see cref="IDisposable"/>。</para>
/// </summary>
public sealed class ThrottleHelper : IDisposable
{
    private readonly Func<CancellationToken, Task> _action;
    private readonly TimeSpan _period;
    private readonly object _gate = new();
    private readonly Timer _timer;
    private DateTime _nextAllowed = DateTime.MinValue;
    private bool _trailingScheduled;
    private int _running;
    private int _disposed;

    public ThrottleHelper(Func<CancellationToken, Task> action, TimeSpan period)
    {
        _action = action ?? throw new ArgumentNullException(nameof(action));
        _period = period;
        _timer = new Timer(_ => _ = FireTrailingAsync(), null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>触发一次节流。窗口内重复触发会被合并（最多 leading + 一次 trailing）。</summary>
    public void Pulse()
    {
        if (_disposed != 0) return;
        lock (_gate)
        {
            var now = DateTime.Now;
            if (now >= _nextAllowed && Interlocked.Exchange(ref _running, 1) == 0)
            {
                // 窗口外且空闲 → 立即执行（leading）
                _nextAllowed = now + _period;
                _ = FireAsync();
            }
            else
            {
                // 窗口内或正在执行 → 标记窗口末补跑（trailing）
                _trailingScheduled = true;
            }
        }
    }

    private async Task FireAsync()
    {
        await SafeAction();
        Interlocked.Exchange(ref _running, 0);
        MaybeScheduleTrailing();
    }

    private void MaybeScheduleTrailing()
    {
        lock (_gate)
        {
            if (!_trailingScheduled) return;

            var now = DateTime.Now;
            if (now >= _nextAllowed)
            {
                _trailingScheduled = false;
                if (Interlocked.Exchange(ref _running, 1) == 0)
                {
                    _nextAllowed = now + _period;
                    _ = FireAsync();
                }
            }
            else
            {
                var due = (int)(_nextAllowed - now).TotalMilliseconds;
                try { _timer.Change(due, Timeout.Infinite); }
                catch (ObjectDisposedException) { }
            }
        }
    }

    private async Task FireTrailingAsync()
    {
        bool go;
        lock (_gate)
        {
            var now = DateTime.Now;
            go = _trailingScheduled && now >= _nextAllowed;
            if (go)
            {
                _trailingScheduled = false;
                _nextAllowed = now + _period;
            }
        }
        if (!go) return;
        if (Interlocked.Exchange(ref _running, 1) != 0) return;

        await SafeAction();
        Interlocked.Exchange(ref _running, 0);
        MaybeScheduleTrailing();
    }

    private async Task SafeAction()
    {
        try
        {
            await _action(CancellationToken.None);
        }
        catch (Exception ex)
        {
            // 节流回调异常不应逃逸到线程池
            System.Diagnostics.Debug.WriteLine($"[ThrottleHelper] action threw: {ex}");
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed != 0) return;
            _disposed = 1;
        }
        _timer.Dispose();
    }
}
