namespace WinUIClash.Models;

/// <summary>
/// 代理节点
/// </summary>
public class Proxy
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public int Delay { get; set; } = -1;
    public string History { get; set; } = string.Empty;
}
