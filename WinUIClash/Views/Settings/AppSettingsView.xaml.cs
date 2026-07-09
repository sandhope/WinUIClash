using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinUIClash.Services;
using WinUIClash.ViewModels.Settings;

namespace WinUIClash.Views.Settings;

public sealed partial class AppSettingsView : UserControl
{
    public AppSettingsViewModel ViewModel { get; }

    public AppSettingsView()
    {
        ViewModel = ServiceLocator.Get<AppSettingsViewModel>();
        InitializeComponent();
    }

    private void BypassPresets_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element) return;

        var menu = new MenuFlyout();

        // 默认
        AddPresetItem(menu, LocalizationHelper.GetString("BypassPresetDefault.Text"),
            "localhost;127.0.0.1;<local>");

        // 局域网
        AddPresetItem(menu, LocalizationHelper.GetString("BypassPresetLan.Text"),
            "localhost;127.0.0.1;<local>;10.*;172.16.*;172.17.*;172.18.*;172.19.*;172.20.*;172.21.*;172.22.*;172.23.*;172.24.*;172.25.*;172.26.*;172.27.*;172.28.*;172.29.*;172.30.*;172.31.*;192.168.*");

        // 中国直连
        AddPresetItem(menu, LocalizationHelper.GetString("BypassPresetChina.Text"),
            "localhost;127.0.0.1;<local>;*.cn;*.baidu.com;*.aliyun.com;*.taobao.com;*.jd.com;*.qq.com;*.weixin.qq.com;*.wechat.com;*.163.com;*.126.com;*.bilibili.com;*.douyin.com;*.tiktokv.com;*.bytedance.com;*.zhihu.com;*.weibo.com;*.sina.com;*.sina.com.cn");

        // 全部合并
        menu.Items.Add(new MenuFlyoutSeparator());
        AddMergeItem(menu, LocalizationHelper.GetString("BypassPresetMergeLan.Text"),
            "10.*;172.16.*;172.17.*;172.18.*;172.19.*;172.20.*;172.21.*;172.22.*;172.23.*;172.24.*;172.25.*;172.26.*;172.27.*;172.28.*;172.29.*;172.30.*;172.31.*;192.168.*");

        AddMergeItem(menu, LocalizationHelper.GetString("BypassPresetMergeChina.Text"),
            "*.cn;*.baidu.com;*.aliyun.com;*.taobao.com;*.jd.com;*.qq.com;*.weixin.qq.com;*.wechat.com;*.163.com;*.126.com;*.bilibili.com;*.douyin.com;*.tiktokv.com;*.bytedance.com;*.zhihu.com;*.weibo.com;*.sina.com;*.sina.com.cn");

        menu.ShowAt(element);
    }

    private void AddPresetItem(MenuFlyout menu, string text, string domains)
    {
        var item = new MenuFlyoutItem { Text = text };
        item.Click += (_, _) => ViewModel.BypassDomains = domains;
        menu.Items.Add(item);
    }

    private void AddMergeItem(MenuFlyout menu, string text, string domains)
    {
        var item = new MenuFlyoutItem
        {
            Text = $"+ {text}",
        };
        item.Click += (_, _) =>
        {
            var current = ViewModel.BypassDomains;
            var existing = current.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var newItems = domains.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            bool changed = false;
            foreach (var d in newItems)
            {
                if (existing.Add(d))
                {
                    current = current.TrimEnd(';') + ";" + d;
                    changed = true;
                }
            }
            if (changed) ViewModel.BypassDomains = current;
        };
        menu.Items.Add(item);
    }
}
