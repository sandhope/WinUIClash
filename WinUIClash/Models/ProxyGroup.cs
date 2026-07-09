using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace WinUIClash.Models;

/// <summary>
/// 代理组类型
/// </summary>
public enum ProxyGroupType
{
    Selector,
    URLTest,
    Fallback,
    LoadBalance,
    Relay
}

/// <summary>
/// 代理组
/// </summary>
public partial class ProxyGroup : ObservableObject
{
    public string Name { get; set; } = string.Empty;
    public ProxyGroupType Type { get; set; }

    [ObservableProperty] private string _now = string.Empty;
    public ObservableCollection<Proxy> Proxies { get; set; } = new();
    public bool Hidden { get; set; }
    public string? Icon { get; set; }
    public string? TestUrl { get; set; }

    /// <summary>Node count label for tab display, e.g. "(12)"</summary>
    public string NodeCountText => Proxies.Count > 0 ? $"({Proxies.Count})" : "";
}
