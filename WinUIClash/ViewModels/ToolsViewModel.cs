using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace WinUIClash.ViewModels;

/// <summary>
/// 工具页 ViewModel — 设置入口集合
/// </summary>
public partial class ToolsViewModel : ObservableObject
{
    // 设置项数据
    public record SettingItem(string Title, string Subtitle, string Icon);

    public IReadOnlyList<SettingItem> SettingsItems { get; } =
    [
        new("基础配置", "端口、日志级别、UA 等基础设置", "Settings"),
        new("高级配置", "DNS、TUN、路由、规则覆写", "SettingsGear"),
        new("应用设置", "启动行为、最小化、自动检查更新", "AppGeneric"),
        new("主题设置", "主题色、明暗模式、字体缩放", "Color"),
        new("备份与恢复", "WebDAV 云备份与本地备份", "Save"),
        new("快捷键管理", "全局快捷键绑定", "Keyboard"),
    ];

    public IReadOnlyList<SettingItem> OtherItems { get; } =
    [
        new("关于", "版本信息、项目链接", "Info"),
    ];

    [RelayCommand]
    private void OpenSetting(string title)
    {
        // TODO: 导航到对应设置子页面
    }
}
