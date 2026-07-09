using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using WinUIClash.Services;

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
    [ObservableProperty] private string _label = string.Empty;
    public string? Url { get; set; }
    public string Path { get; set; } = string.Empty;

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
    public string UsedPercentText => SubscriptionInfo == null ? "" :
        $"{LocalizationHelper.GetString("SubUsed.Text")}{UsedPercent:F0}%";

    /// <summary>剩余流量文本</summary>
    public string RemainingText => SubscriptionInfo == null ? "" :
        $"{LocalizationHelper.GetString("SubRemaining.Text")}{Converters.ByteFormatter.Format(RemainingBytes)}";

    /// <summary>订阅过期显示文本</summary>
    public string ExpiryText
    {
        get
        {
            if (SubscriptionInfo?.Expire == null) return "";
            var remaining = SubscriptionInfo.Expire.Value - DateTime.Now;
            if (remaining.TotalDays < 0) return LocalizationHelper.GetString("SubExpired.Text");
            if (remaining.TotalDays < 1) return $"{LocalizationHelper.GetString("SubRemaining.Text")}{remaining.Hours}{LocalizationHelper.GetString("SubHoursRemaining.Text")}";
            return $"{LocalizationHelper.GetString("SubRemaining.Text")}{(int)remaining.TotalDays}{LocalizationHelper.GetString("SubDaysRemaining.Text")}";
        }
    }

    /// <summary>订阅过期颜色（正常=灰色，即将过期=橙色，已过期=红色）</summary>
    public SolidColorBrush ExpiryBrush
    {
        get
        {
            if (SubscriptionInfo?.Expire == null) return new SolidColorBrush(Color.FromArgb(255, 128, 128, 128));
            var remaining = SubscriptionInfo.Expire.Value - DateTime.Now;
            if (remaining.TotalDays < 0) return new SolidColorBrush(Color.FromArgb(255, 244, 67, 54));
            if (remaining.TotalDays < 3) return new SolidColorBrush(Color.FromArgb(255, 255, 152, 0));
            if (remaining.TotalDays < 7) return new SolidColorBrush(Color.FromArgb(255, 255, 193, 7));
            return new SolidColorBrush(Color.FromArgb(255, 128, 128, 128));
        }
    }

    public void NotifySubscriptionChanged()
    {
        OnPropertyChanged(nameof(UsedPercent));
        OnPropertyChanged(nameof(RemainingBytes));
        OnPropertyChanged(nameof(UsedPercentText));
        OnPropertyChanged(nameof(RemainingText));
        OnPropertyChanged(nameof(ExpiryText));
        OnPropertyChanged(nameof(ExpiryBrush));
    }
}
