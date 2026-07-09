using CommunityToolkit.Mvvm.ComponentModel;

namespace WinUIClash.Models;

/// <summary>
/// 代理节点
/// </summary>
public partial class Proxy : ObservableObject
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;

    [ObservableProperty] private int _delay = -1;
}
