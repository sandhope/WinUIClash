using CommunityToolkit.Mvvm.ComponentModel;
using WinUIClash.Services;
using WinUIClash.ViewModels;

namespace WinUIClash.Models;

/// <summary>仪表盘可拖拽磁贴类型（决定渲染模板与标题）</summary>
public enum DashboardTileType
{
    SystemProxy,
    Tun,
    OutboundMode,
    NetworkCheck,
    TrafficStats,
    Memory,
    ActiveNode,
    ActiveProfile,
    // 以下 6 个默认隐藏，需在“编辑磁贴”中手动启用
    Uptime,
    Connections,
    Language,
    Theme,
    AccentColor,
    ClipboardDetect,
}

/// <summary>仪表盘单个可拖拽磁贴（布局壳：持有类型/可见性/对父 VM 的引用）。</summary>
public partial class DashboardTile : ObservableObject
{
    public DashboardTileType Type { get; }
    public string Id => Type.ToString();
    public DashboardViewModel Vm { get; }

    [ObservableProperty]
    private bool _isVisible = true;

    /// <summary>磁贴标题。用 ObservableProperty 而非计算属性，
    /// 确保 {x:Bind} 编译绑定在语言切换后能可靠重绘（source-generated OnPropertyChanged）。</summary>
    [ObservableProperty]
    private string _title = "";

    public DashboardTile(DashboardTileType type, DashboardViewModel vm)
    {
        Type = type;
        Vm = vm;
        Title = LocalizationHelper.GetString($"DashTile_{Type}.Text");
    }

    /// <summary>语言切换后重新从 LocalizationHelper 读取最新语言文案并通知 UI 刷新。</summary>
    public void RefreshTitle() => Title = LocalizationHelper.GetString($"DashTile_{Type}.Text");
}
