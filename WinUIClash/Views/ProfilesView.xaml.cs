using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinUIClash.Models;
using WinUIClash.ViewModels;

namespace WinUIClash.Views;

public sealed partial class ProfilesView : Page
{
    public ProfilesViewModel ViewModel { get; }

    public ProfilesView()
    {
        ViewModel = ServiceLocator.Get<ProfilesViewModel>();
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.InitializeAsync();
        await TryClipboardImportAsync();
    }

    private async Task TryClipboardImportAsync()
    {
        try
        {
            var dp = Windows.ApplicationModel.DataTransfer.Clipboard.GetContent();
            if (!dp.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.Text))
                return;

            var text = await dp.GetTextAsync();
            if (string.IsNullOrWhiteSpace(text)) return;

            text = text.Trim();

            // 检测是否是订阅链接（常见格式）
            if (!IsSubscriptionUrl(text)) return;

            var dialog = new ContentDialog
            {
                Title = "检测到订阅链接",
                Content = $"剪贴板中发现订阅链接:\n\n{text}\n\n是否导入？",
                PrimaryButtonText = "导入",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot,
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                await ViewModel.ImportProfileAsync(text, null);
            }
        }
        catch { /* 剪贴板访问失败时静默 */ }
    }

    private static bool IsSubscriptionUrl(string text)
    {
        if (!text.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !text.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return false;

        // 检查是否包含常见订阅关键词
        var lower = text.ToLowerInvariant();
        string[] keywords = ["sub", "subscribe", "clash", "token", "profile", "config", "yaml"];
        return keywords.Any(k => lower.Contains(k));
    }

    private void ProfileGrid_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Profile profile)
            ViewModel.SelectProfileCommand.Execute(profile);
    }

    private void SyncButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is Profile profile)
            ViewModel.SyncProfileCommand.Execute(profile);
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is Profile profile)
            ViewModel.DeleteProfileCommand.Execute(profile);
    }

    private async void ImportButton_Click(object sender, RoutedEventArgs e)
    {
        var urlBox = new TextBox
        {
            PlaceholderText = "https://example.com/sub?token=xxx",
            Header = "订阅地址",
        };

        var nameBox = new TextBox
        {
            PlaceholderText = "自动识别（可手动修改）",
            Header = "配置名称",
            Margin = new Thickness(0, 12, 0, 0),
        };

        var dialog = new ContentDialog
        {
            Title = "导入订阅",
            XamlRoot = XamlRoot,
            PrimaryButtonText = "导入",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            Content = new StackPanel
            {
                Spacing = 0,
                Children = { urlBox, nameBox }
            }
        };

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary) return;

        var url = urlBox.Text.Trim();
        if (string.IsNullOrEmpty(url)) return;

        var name = string.IsNullOrWhiteSpace(nameBox.Text) ? null : nameBox.Text.Trim();
        await ViewModel.ImportProfileAsync(url, name);
    }

    private void MoreButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not Profile profile) return;

        var menu = new MenuFlyout();

        // 查看配置
        var viewConfig = new MenuFlyoutItem { Text = "查看配置" };
        viewConfig.Click += async (_, _) => await ShowConfigViewerAsync(profile);
        menu.Items.Add(viewConfig);

        // 复制订阅链接
        var copyUrl = new MenuFlyoutItem { Text = "复制链接" };
        copyUrl.Click += (_, _) =>
        {
            if (!string.IsNullOrEmpty(profile.Url))
            {
                var dp = new Windows.ApplicationModel.DataTransfer.DataPackage();
                dp.SetText(profile.Url);
                Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp);
            }
        };
        menu.Items.Add(copyUrl);

        // 编辑名称
        var editName = new MenuFlyoutItem { Text = "编辑名称" };
        editName.Click += async (_, _) => await ShowEditNameDialogAsync(profile);
        menu.Items.Add(editName);

        menu.ShowAt(btn);
    }

    private async Task ShowConfigViewerAsync(Profile profile)
    {
        // 生成模拟 YAML 配置（接入真实后端后替换为实际配置）
        var yaml = GenerateMockConfig(profile);

        var editor = new TextBox
        {
            Text = yaml,
            IsReadOnly = true,
            AcceptsReturn = true,
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
            FontSize = 12,
            TextWrapping = TextWrapping.NoWrap,
            MinWidth = 560,
            MinHeight = 400,
        };

        var scrollViewer = new ScrollViewer
        {
            Content = editor,
            HorizontalScrollMode = ScrollMode.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        };

        var dialog = new ContentDialog
        {
            Title = $"配置: {profile.Label}",
            XamlRoot = XamlRoot,
            CloseButtonText = "关闭",
            Content = scrollViewer,
        };

        await dialog.ShowAsync();
    }

    private async Task ShowEditNameDialogAsync(Profile profile)
    {
        var nameBox = new TextBox
        {
            Text = profile.Label,
            Header = "配置名称",
        };

        var dialog = new ContentDialog
        {
            Title = "编辑名称",
            XamlRoot = XamlRoot,
            PrimaryButtonText = "保存",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            Content = nameBox,
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            var newName = nameBox.Text.Trim();
            if (!string.IsNullOrEmpty(newName))
                profile.Label = newName;
        }
    }

    private static string GenerateMockConfig(Profile profile)
    {
        return $$"""
            # WinUIClash Configuration
            # Profile: {{profile.Label}}

            mixed-port: 7890
            socks-port: 7891
            port: 7892
            allow-lan: false
            mode: rule
            log-level: info
            unified-delay: true
            tcp-concurrent: true

            geodata-mode: true
            geodata-loader: standard
            find-process-mode: strict

            geodata-auto-update: false

            profile:
              store-selected: true
              store-fake-ip: true

            sniffer:
              enable: true
              sniff:
                HTTP:
                  ports: [80, 8080-8880]
                  override-destination: true
                TLS:
                  ports: [443, 8443]
                QUIC:
                  ports: [443, 8443]
              skip-domain:
                - "Mijia Cloud"
                - "+.push.apple.com"

            dns:
              enable: true
              ipv6: false
              enhanced-mode: fake-ip
              fake-ip-range: 198.18.0.1/16
              fake-ip-filter:
                - "*.lan"
                - "*.local"
                - "dns.msftncsi.com"
              default-nameserver:
                - 223.5.5.5
                - 119.29.29.29
              nameserver:
                - "https://dns.alidns.com/dns-query"
                - "https://doh.pub/dns-query"

            proxies:
              - name: "🇭🇰 Hong Kong 01"
                type: trojan
                server: hk01.example.com
                port: 443
                password: "password"
                sni: hk01.example.com
              - name: "🇯🇵 Japan 01"
                type: vmess
                server: jp01.example.com
                port: 443
                uuid: "00000000-0000-0000-0000-000000000000"
                alterId: 0
                cipher: auto
                tls: true
              - name: "🇺🇸 US 01"
                type: ss
                server: us01.example.com
                port: 8388
                cipher: aes-256-gcm
                password: "password"

            proxy-groups:
              - name: "🚀 Proxy"
                type: select
                proxies:
                  - "🇭🇰 Hong Kong 01"
                  - "🇯🇵 Japan 01"
                  - "🇺🇸 US 01"
                  - DIRECT
              - name: "♻️ Auto"
                type: url-test
                proxies:
                  - "🇭🇰 Hong Kong 01"
                  - "🇯🇵 Japan 01"
                url: "http://www.gstatic.com/generate_204"
                interval: 300

            rules:
              - DOMAIN-SUFFIX,google.com,🚀 Proxy
              - DOMAIN-SUFFIX,github.com,🚀 Proxy
              - GEOIP,CN,DIRECT
              - MATCH,🚀 Proxy
            """;
    }
}
