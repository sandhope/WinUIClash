namespace WinUIClash.Models;

/// <summary>
/// 外部提供者（代理/规则订阅源）
/// </summary>
public class ExternalProvider
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public int Count { get; set; }
    public string VehicleType { get; set; } = string.Empty;
    public DateTime UpdateAt { get; set; }
    public SubscriptionInfo? SubscriptionInfo { get; set; }
}
