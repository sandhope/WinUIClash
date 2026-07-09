using CommunityToolkit.Mvvm.ComponentModel;

namespace WinUIClash.Models;

/// <summary>
/// 订阅信息
/// </summary>
public class SubscriptionInfo
{
    public long Upload { get; set; }
    public long Download { get; set; }
    public long Total { get; set; }
    public DateTime? Expire { get; set; }
}

/// <summary>
/// 配置档案
/// </summary>
public partial class Profile : ObservableObject
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Label { get; set; } = string.Empty;
    public string? Url { get; set; }

    [ObservableProperty] private DateTime _lastUpdate = DateTime.Now;
    public bool AutoUpdate { get; set; }
    public TimeSpan AutoUpdateInterval { get; set; } = TimeSpan.FromHours(24);
    public SubscriptionInfo? SubscriptionInfo { get; set; }

    [ObservableProperty] private bool _isActive;
    public int Order { get; set; }

    // ── 计算属性 ──

    /// <summary>已用流量百分比（0-100）</summary>
    public double UsedPercent
    {
        get
        {
            if (SubscriptionInfo == null || SubscriptionInfo.Total <= 0) return 0;
            double used = SubscriptionInfo.Upload + SubscriptionInfo.Download;
            return Math.Min(100, used / SubscriptionInfo.Total * 100);
        }
    }

    /// <summary>剩余流量（字节）</summary>
    public long RemainingBytes =>
        SubscriptionInfo == null ? 0 :
        Math.Max(0, SubscriptionInfo.Total - SubscriptionInfo.Upload - SubscriptionInfo.Download);

    /// <summary>已用流量百分比文本</summary>
    public string UsedPercentText => SubscriptionInfo == null ? "" : $"已用 {UsedPercent:F0}%";

    /// <summary>剩余流量文本</summary>
    public string RemainingText => SubscriptionInfo == null ? "" :
        $"剩余 {Converters.ByteFormatter.Format(RemainingBytes)}";

    public void NotifySubscriptionChanged()
    {
        OnPropertyChanged(nameof(UsedPercent));
        OnPropertyChanged(nameof(RemainingBytes));
        OnPropertyChanged(nameof(UsedPercentText));
        OnPropertyChanged(nameof(RemainingText));
    }
}
