using System.Collections.Generic;
using Microsoft.UI.Xaml.Controls;
using WinUIClash.Services;

namespace WinUIClash.Views;

/// <summary>
/// 快捷键说明子页面：作为「工具」页的子页面，以表格形式按类别集中展示所有键盘快捷键。
/// </summary>
public sealed partial class ShortcutsView : UserControl
{
    public ShortcutsView()
    {
        InitializeComponent();
    }

    /// <summary>按类别分组的快捷键，供表格分组绑定。</summary>
    public List<ShortcutGroup> Groups { get; } =
    [
        new()
        {
            Name = LocalizationHelper.GetString("ShortcutCatNav.Text"),
            Items =
            [
                new() { Key = "Ctrl+1~9", Desc = LocalizationHelper.GetString("HelpNav.Text") },
                new() { Key = "Ctrl+Tab", Desc = LocalizationHelper.GetString("HelpCyclePage.Text") },
                new() { Key = "Ctrl+B", Desc = LocalizationHelper.GetString("HelpSidebar.Text") },
            ],
        },
        new()
        {
            Name = LocalizationHelper.GetString("ShortcutCatPage.Text"),
            Items =
            [
                new() { Key = "F5 / Ctrl+R", Desc = LocalizationHelper.GetString("HelpRefresh.Text") },
                new() { Key = "Ctrl+E", Desc = LocalizationHelper.GetString("HelpExport.Text") },
                new() { Key = "Escape", Desc = LocalizationHelper.GetString("HelpEscape.Text") },
            ],
        },
        new()
        {
            Name = LocalizationHelper.GetString("ShortcutCatCore.Text"),
            Items =
            [
                new() { Key = "Ctrl+P", Desc = LocalizationHelper.GetString("HelpCoreToggle.Text") },
                new() { Key = "Ctrl+Shift+S", Desc = LocalizationHelper.GetString("HelpProxyToggle.Text") },
                new() { Key = "Ctrl+Shift+D", Desc = LocalizationHelper.GetString("HelpCloseAllConns.Text") },
                new() { Key = "Ctrl+Shift+T", Desc = LocalizationHelper.GetString("HelpThemeToggle.Text") },
            ],
        },
        new()
        {
            Name = LocalizationHelper.GetString("ShortcutCatApp.Text"),
            Items =
            [
                new() { Key = "Ctrl+W", Desc = LocalizationHelper.GetString("HelpMinimize.Text") },
                new() { Key = "Ctrl+,", Desc = LocalizationHelper.GetString("HelpSettings.Text") },
                new() { Key = "Ctrl+Q", Desc = LocalizationHelper.GetString("HelpQuit.Text") },
                new() { Key = "F1", Desc = LocalizationHelper.GetString("HelpShowHelp.Text") },
            ],
        },
    ];
}

/// <summary>快捷键条目（键位 + 说明），供表格行绑定。</summary>
public class ShortcutEntry
{
    public string Key { get; set; } = "";
    public string Desc { get; set; } = "";
}

/// <summary>快捷键分组（类别名 + 条目集合），供表格分组绑定。</summary>
public class ShortcutGroup
{
    public string Name { get; set; } = "";
    public List<ShortcutEntry> Items { get; set; } = [];
}
