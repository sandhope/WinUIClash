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
public class Profile
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Label { get; set; } = string.Empty;
    public string? Url { get; set; }
    public DateTime LastUpdate { get; set; } = DateTime.Now;
    public bool AutoUpdate { get; set; }
    public TimeSpan AutoUpdateInterval { get; set; } = TimeSpan.FromHours(24);
    public SubscriptionInfo? SubscriptionInfo { get; set; }
    public bool IsActive { get; set; }
    public int Order { get; set; }
}
