using System;

namespace WinUIClash.Helpers;

/// <summary>
/// 失败抑制器：连续失败时只记录首次日志，恢复（首次成功）时记录一次。
/// <para>
/// 用于采样 / 轮询循环，避免同一个持续性故障在每次循环都刷屏日志。
/// 状态由本实例持有；每个独立的轮询循环持有一个实例即可。如需多线程共享请自行加锁。
/// </para>
/// </summary>
public sealed class FailureSuppressor
{
    private bool _wasFailing;
    private readonly object _gate = new();

    /// <summary>
    /// 记录一次尝试结果。
    /// </summary>
    /// <param name="failed">本次尝试是否失败。</param>
    /// <param name="onFirstFailure">从成功态进入失败态（首次失败）时回调一次。</param>
    /// <param name="onRecovered">从失败态恢复（首次成功）时回调一次。</param>
    /// <param name="onRepeatedFailure">每次失败都会回调（可选，用于累计计数等）。</param>
    public void Record(
        bool failed,
        Action onFirstFailure,
        Action onRecovered,
        Action? onRepeatedFailure = null)
    {
        if (onFirstFailure == null) throw new ArgumentNullException(nameof(onFirstFailure));
        if (onRecovered == null) throw new ArgumentNullException(nameof(onRecovered));

        lock (_gate)
        {
            if (failed)
            {
                if (!_wasFailing)
                {
                    _wasFailing = true;
                    onFirstFailure();
                }
                else
                {
                    onRepeatedFailure?.Invoke();
                }
            }
            else
            {
                if (_wasFailing)
                {
                    _wasFailing = false;
                    onRecovered();
                }
            }
        }
    }

    /// <summary>当前是否处于“持续失败”状态。</summary>
    public bool IsFailing => _wasFailing;
}
