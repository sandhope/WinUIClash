using CommunityToolkit.Mvvm.ComponentModel;

namespace WinUIClash.Models;

/// <summary>
/// 外部提供者（代理/规则订阅源）
/// </summary>
public partial class ExternalProvider : ObservableObject
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public int Count { get; set; }
    public string VehicleType { get; set; } = string.Empty;
    /// <summary>"proxy" or "rule" — determines which API endpoint to use for updates</summary>
    public string Category { get; set; } = "proxy";

    [ObservableProperty] private DateTime _updateAt;
    public SubscriptionInfo? SubscriptionInfo { get; set; }
}
