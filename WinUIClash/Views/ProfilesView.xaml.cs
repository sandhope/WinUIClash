using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinUIClash.Models;
using WinUIClash.Services;
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
        try
        {
            await ViewModel.InitializeAsync();
            await TryClipboardImportAsync();
        }
        catch { /* 初始化出错时保持空状态，避免崩溃 */ }
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
                Title = LocalizationHelper.GetString("ProfilesClipboardTitle.Text"),
                Content = string.Format(LocalizationHelper.GetString("ProfilesClipboardContent.Text"), text),
                PrimaryButtonText = LocalizationHelper.GetString("ProfilesImport.Text"),
                CloseButtonText = LocalizationHelper.GetString("CommonCancel.Content"),
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

    private async void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not Profile profile) return;

        var dialog = new ContentDialog
        {
            Title = LocalizationHelper.GetString("ProfilesDeleteConfirmTitle.Text"),
            Content = string.Format(LocalizationHelper.GetString("ProfilesDeleteConfirmContent.Text"), profile.Label),
            PrimaryButtonText = LocalizationHelper.GetString("CommonDelete.Content"),
            CloseButtonText = LocalizationHelper.GetString("CommonCancel.Content"),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            ViewModel.DeleteProfileCommand.Execute(profile);
    }

    private async void ImportButton_Click(object sender, RoutedEventArgs e)
    {
        var urlBox = new TextBox
        {
            PlaceholderText = "https://example.com/sub?token=xxx",
            Header = LocalizationHelper.GetString("ProfilesUrlHeader.Text"),
        };

        // Auto-paste from clipboard if it looks like a URL
        try
        {
            var clipContent = await Windows.ApplicationModel.DataTransfer.Clipboard.GetContent()
                .GetTextAsync();
            if (!string.IsNullOrWhiteSpace(clipContent) &&
                (clipContent.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                 clipContent.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
            {
                urlBox.Text = clipContent.Trim();
            }
        }
        catch { /* clipboard empty or not text */ }

        var pasteBtn = new Button
        {
            Content = LocalizationHelper.GetString("ProfilesPasteClipboard.Content"),
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Bottom,
        };
        pasteBtn.Click += async (_, _) =>
        {
            try
            {
                urlBox.Text = (await Windows.ApplicationModel.DataTransfer.Clipboard.GetContent()
                    .GetTextAsync()).Trim();
            }
            catch { }
        };

        var urlRow = new Grid { ColumnSpacing = 8 };
        urlRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        urlRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(urlBox, 0);
        Grid.SetColumn(pasteBtn, 1);
        urlRow.Children.Add(urlBox);
        urlRow.Children.Add(pasteBtn);

        var nameBox = new TextBox
        {
            PlaceholderText = LocalizationHelper.GetString("ProfilesNamePlaceholder.Text"),
            Header = LocalizationHelper.GetString("ProfilesNameHeader.Text"),
            Margin = new Thickness(0, 12, 0, 0),
        };

        var dialog = new ContentDialog
        {
            Title = LocalizationHelper.GetString("ProfilesImportTitle.Text"),
            XamlRoot = XamlRoot,
            PrimaryButtonText = LocalizationHelper.GetString("ProfilesImport.Text"),
            CloseButtonText = LocalizationHelper.GetString("CommonCancel.Content"),
            DefaultButton = ContentDialogButton.Primary,
            Content = new StackPanel
            {
                Spacing = 0,
                Children = { urlRow, nameBox }
            }
        };

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary) return;

        var url = urlBox.Text.Trim();
        if (string.IsNullOrEmpty(url)) return;

        var name = string.IsNullOrWhiteSpace(nameBox.Text) ? null : nameBox.Text.Trim();
        await ViewModel.ImportProfileAsync(url, name);
    }

    private async void ImportFileButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker
            {
                SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary,
            };
            picker.FileTypeFilter.Add(".yaml");
            picker.FileTypeFilter.Add(".yml");

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.CurrentWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var file = await picker.PickSingleFileAsync();
            if (file == null) return;

            var profileId = Guid.NewGuid().ToString("N")[..8];
            var storage = new ProfileStorageService();
            var destPath = storage.GetConfigPath(profileId);

            // Copy the selected file to local profile storage
            File.Copy(file.Path, destPath, overwrite: true);

            var label = Path.GetFileNameWithoutExtension(file.Name);

            var profile = new Profile
            {
                Id = profileId,
                Label = label,
                Path = destPath,
                LastUpdate = DateTime.Now,
                IsActive = false,
            };

            ViewModel.Profiles.Add(profile);
        }
        catch (Exception ex) when (ex is not OperationCanceledException
            && ex.HResult != unchecked((int)0x80004004))
        {
            System.Diagnostics.Debug.WriteLine($"[Profiles] Import file error: {ex.Message}");
        }
    }

    private void MoreButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not Profile profile) return;

        var menu = new MenuFlyout();

        // 查看配置（预览）
        var viewConfig = new MenuFlyoutItem { Text = LocalizationHelper.GetString("ProfilesViewConfig.Text") };
        viewConfig.Click += async (_, _) => await ShowConfigViewerAsync(profile);
        menu.Items.Add(viewConfig);

        // 编辑档案
        var editName = new MenuFlyoutItem { Text = LocalizationHelper.GetString("ProfilesEditProfile.Text") };
        editName.Click += async (_, _) => await ShowEditNameDialogAsync(profile);
        menu.Items.Add(editName);

        // 复制订阅链接
        var copyUrl = new MenuFlyoutItem { Text = LocalizationHelper.GetString("ProfilesCopyUrl.Text") };
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

        // 导出配置
        var exportConfig = new MenuFlyoutItem { Text = LocalizationHelper.GetString("ProfilesExportConfig.Text") };
        exportConfig.Click += async (_, _) =>
        {
            try
            {
                var srcPath = profile.Path;
                if (string.IsNullOrWhiteSpace(srcPath))
                    srcPath = new ProfileStorageService().GetConfigPath(profile.Id);

                if (!File.Exists(srcPath)) return;

                var picker = new Windows.Storage.Pickers.FileSavePicker
                {
                    SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary,
                    SuggestedFileName = $"{profile.Label}.yaml",
                };
                picker.FileTypeChoices.Add("YAML", [".yaml", ".yml"]);
                picker.FileTypeChoices.Add("Text file", [".txt"]);

                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.CurrentWindow);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

                var file = await picker.PickSaveFileAsync();
                if (file == null) return;

                var content = await File.ReadAllTextAsync(srcPath);
                await Windows.Storage.FileIO.WriteTextAsync(file, content);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Profiles] Export error: {ex.Message}");
            }
        };
        menu.Items.Add(exportConfig);

        menu.ShowAt(btn);
    }

    private async Task ShowConfigViewerAsync(Profile profile)
    {
        var storage = new ProfileStorageService();
        // Prefer the stored Path only if that file actually exists; otherwise
        // derive the canonical path (handles MSIX LocalApplicationData redirect).
        var configPath = !string.IsNullOrWhiteSpace(profile.Path) && File.Exists(profile.Path)
            ? profile.Path
            : storage.GetConfigPath(profile.Id);

        var hasRealFile = File.Exists(configPath);
        string yaml;

        // If no local file but profile has a subscription URL, try downloading now
        if (!hasRealFile && !string.IsNullOrEmpty(profile.Url))
        {
            try
            {
                var dlResult = await storage.DownloadAndSaveAsync(profile.Id, profile.Url);
                configPath = dlResult.Path;
                hasRealFile = File.Exists(configPath);
                if (hasRealFile)
                {
                    yaml = await File.ReadAllTextAsync(configPath);
                    // Update subscription info from response headers
                    if (dlResult.SubInfo != null)
                    {
                        profile.SubscriptionInfo = dlResult.SubInfo;
                        profile.NotifySubscriptionChanged();
                    }
                    profile.Path = configPath;
                    profile.LastUpdate = DateTime.Now;
                    await ViewModel.SaveProfileListAsync();
                }
            }
            catch { /* Download failed; will show template below */ }
        }

        if (hasRealFile)
        {
            try
            {
                yaml = await File.ReadAllTextAsync(configPath);
            }
            catch
            {
                yaml = GenerateTemplatePreview(profile);
                hasRealFile = false;
            }
        }
        else
        {
            yaml = GenerateTemplatePreview(profile);
        }

        // 纯文本只读展示组件（TextBlock）。TextBlock 原生支持 \n 换行，
        // 完整多行配置可直接正确呈现，且不可编辑（仅用于查看）。
        var editor = new TextBlock
        {
            Text = yaml,
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
        };

        var scrollViewer = new ScrollViewer
        {
            Content = editor,
            MaxHeight = 480,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        };

        var dialog = new ContentDialog
        {
            Title = LocalizationHelper.GetString("ProfilesConfigViewerTitle.Text") + profile.Label,
            XamlRoot = XamlRoot,
            CloseButtonText = LocalizationHelper.GetString("CommonClose.Content"),
            DefaultButton = ContentDialogButton.Close,
            Content = scrollViewer,
        };

        await dialog.ShowAsync();
    }

    private async Task ShowEditNameDialogAsync(Profile profile)
    {
        var nameBox = new TextBox
        {
            Text = profile.Label,
            Header = LocalizationHelper.GetString("ProfilesNameHeader.Text"),
        };

        var urlBox = new TextBox
        {
            Text = profile.Url ?? "",
            PlaceholderText = "https://example.com/sub?token=xxx",
            Header = LocalizationHelper.GetString("ProfilesUrlHeader.Text"),
            Margin = new Thickness(0, 12, 0, 0),
        };

        var dialog = new ContentDialog
        {
            Title = LocalizationHelper.GetString("ProfilesEditProfileTitle.Text"),
            XamlRoot = XamlRoot,
            PrimaryButtonText = LocalizationHelper.GetString("CommonSave.Content"),
            CloseButtonText = LocalizationHelper.GetString("CommonCancel.Content"),
            DefaultButton = ContentDialogButton.Primary,
            Content = new StackPanel
            {
                Spacing = 0,
                Children = { nameBox, urlBox }
            },
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            var newName = nameBox.Text.Trim();
            var newUrl = urlBox.Text.Trim();
            if (!string.IsNullOrEmpty(newName))
            {
                await ViewModel.UpdateProfileAsync(
                    profile.Id,
                    newName,
                    string.IsNullOrEmpty(newUrl) ? null : newUrl);
            }
        }
    }

    // ── 拖放导入 ──

    private void Page_DragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
        e.DragUIOverride.Caption = LocalizationHelper.GetString("ProfilesImport.Text");
    }

    private async void Page_Drop(object sender, DragEventArgs e)
    {
        try
        {
            if (!e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
                return;

            var items = await e.DataView.GetStorageItemsAsync();
            foreach (var item in items)
            {
                if (item is not Windows.Storage.StorageFile file) continue;
                var ext = Path.GetExtension(file.Path).ToLowerInvariant();
                if (ext != ".yaml" && ext != ".yml") continue;

                var profileId = Guid.NewGuid().ToString("N")[..8];
                var storage = new ProfileStorageService();
                var destPath = storage.GetConfigPath(profileId);

                File.Copy(file.Path, destPath, overwrite: true);

                var label = Path.GetFileNameWithoutExtension(file.Name);
                var profile = new Profile
                {
                    Id = profileId,
                    Label = label,
                    Path = destPath,
                    LastUpdate = DateTime.Now,
                    IsActive = false,
                };

                ViewModel.Profiles.Add(profile);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Profiles] Drop error: {ex.Message}");
        }
    }

    private static string GenerateTemplatePreview(Profile profile)
    {
        return $$"""
            # 注意：这是配置模板预览（本地尚未生成该订阅/配置的真实文件）。
            # 启动核心或同步订阅后，WinUIClash 会基于该订阅自动生成真实 config.yaml。
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
