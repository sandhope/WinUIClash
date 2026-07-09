namespace WinUIClash.Models;

/// <summary>
/// ClashMeta 路由规则
/// </summary>
public class Rule
{
    public string Type { get; set; } = "";       // DOMAIN, DOMAIN-SUFFIX, GEOIP, GEOSITE, MATCH 等
    public string Payload { get; set; } = "";    // 规则内容
    public string Proxy { get; set; } = "";      // 目标代理/策略组
    public int Size { get; set; }                // 规则集大小（如果是 Provider 规则）
}
