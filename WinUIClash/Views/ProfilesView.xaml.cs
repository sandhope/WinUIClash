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

        // 查看配置
        var viewConfig = new MenuFlyoutItem { Text = LocalizationHelper.GetString("ProfilesViewConfig.Text") };
        viewConfig.Click += async (_, _) => await ShowConfigViewerAsync(profile);
        menu.Items.Add(viewConfig);

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

        // 复制配置文件路径
        var copyPath = new MenuFlyoutItem { Text = LocalizationHelper.GetString("ProfilesCopyPath.Text") };
        copyPath.Click += (_, _) =>
        {
            var path = profile.Path;
            if (string.IsNullOrWhiteSpace(path))
                path = new ProfileStorageService().GetConfigPath(profile.Id);
            var dp = new Windows.ApplicationModel.DataTransfer.DataPackage();
            dp.SetText(path);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp);
        };
        menu.Items.Add(copyPath);

        // 在文件管理器中打开
        var openInExplorer = new MenuFlyoutItem { Text = LocalizationHelper.GetString("ProfilesOpenInExplorer.Text") };
        openInExplorer.Click += async (_, _) =>
        {
            try
            {
                var path = profile.Path;
                if (string.IsNullOrWhiteSpace(path))
                    path = new ProfileStorageService().GetConfigPath(profile.Id);
                if (File.Exists(path))
                {
                    var folder = System.IO.Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(folder))
                    {
                        var storageFolder = await Windows.Storage.StorageFolder.GetFolderFromPathAsync(folder);
                        await Windows.System.Launcher.LaunchFolderAsync(storageFolder);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Profiles] Open in Explorer error: {ex.Message}");
            }
        };
        menu.Items.Add(openInExplorer);

        menu.Items.Add(new MenuFlyoutSeparator());

        // 复制配置
        var duplicate = new MenuFlyoutItem { Text = LocalizationHelper.GetString("ProfilesDuplicate.Text") };
        duplicate.Click += async (_, _) =>
        {
            try
            {
                var newId = Guid.NewGuid().ToString("N")[..8];
                var srcPath = profile.Path;
                if (string.IsNullOrWhiteSpace(srcPath))
                    srcPath = new ProfileStorageService().GetConfigPath(profile.Id);
                var destPath = new ProfileStorageService().GetConfigPath(newId);

                if (File.Exists(srcPath))
                    File.Copy(srcPath, destPath);

                var newProfile = new Profile
                {
                    Id = newId,
                    Label = $"{profile.Label} (Copy)",
                    Url = profile.Url,
                    Path = destPath,
                    LastUpdate = DateTime.Now,
                    IsActive = false,
                    Order = ViewModel.Profiles.Count,
                };
                ViewModel.Profiles.Add(newProfile);
                await ViewModel.SaveProfileListAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Profiles] Duplicate error: {ex.Message}");
            }
        };
        menu.Items.Add(duplicate);

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

        // 自动更新开关
        var autoUpdate = new ToggleMenuFlyoutItem
        {
            Text = LocalizationHelper.GetString("ProfilesAutoUpdate.Text"),
            IsChecked = profile.AutoUpdate,
        };
        autoUpdate.Click += async (_, _) =>
        {
            profile.AutoUpdate = !profile.AutoUpdate;
            await ViewModel.SaveProfileListAsync();
        };
        menu.Items.Add(autoUpdate);

        menu.Items.Add(new MenuFlyoutSeparator());

        // 编辑档案
        var editName = new MenuFlyoutItem { Text = LocalizationHelper.GetString("ProfilesEditProfile.Text") };
        editName.Click += async (_, _) => await ShowEditNameDialogAsync(profile);
        menu.Items.Add(editName);

        menu.Items.Add(new MenuFlyoutSeparator());

        // Move Up / Move Down
        var index = ViewModel.Profiles.IndexOf(profile);
        if (index > 0)
        {
            var moveUp = new MenuFlyoutItem { Text = LocalizationHelper.GetString("ProfilesMoveUp.Text") };
            moveUp.Click += async (_, _) =>
            {
                ViewModel.Profiles.Move(index, index - 1);
                for (int i = 0; i < ViewModel.Profiles.Count; i++)
                    ViewModel.Profiles[i].Order = i;
                await ViewModel.SaveProfileListAsync();
            };
            menu.Items.Add(moveUp);
        }
        if (index >= 0 && index < ViewModel.Profiles.Count - 1)
        {
            var moveDown = new MenuFlyoutItem { Text = LocalizationHelper.GetString("ProfilesMoveDown.Text") };
            moveDown.Click += async (_, _) =>
            {
                ViewModel.Profiles.Move(index, index + 1);
                for (int i = 0; i < ViewModel.Profiles.Count; i++)
                    ViewModel.Profiles[i].Order = i;
                await ViewModel.SaveProfileListAsync();
            };
            menu.Items.Add(moveDown);
        }

        menu.ShowAt(btn);
    }

    private async Task ShowConfigViewerAsync(Profile profile)
    {
        // Try to read actual config from local file
        var yaml = "";
        var storage = new ProfileStorageService();
        var configPath = profile.Path;
        if (string.IsNullOrWhiteSpace(configPath))
            configPath = storage.GetConfigPath(profile.Id);

        if (File.Exists(configPath))
        {
            try
            {
                yaml = await File.ReadAllTextAsync(configPath);
            }
            catch
            {
                yaml = GenerateMockConfig(profile);
            }
        }
        else
        {
            yaml = GenerateMockConfig(profile);
        }

        var editor = new TextBox
        {
            Text = yaml,
            IsReadOnly = false,
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

        var capturedPath = configPath;
        var capturedProfile = profile;

        var dialog = new ContentDialog
        {
            Title = LocalizationHelper.GetString("ProfilesConfigViewerTitle.Text") + profile.Label,
            XamlRoot = XamlRoot,
            PrimaryButtonText = LocalizationHelper.GetString("CommonSave.Content"),
            SecondaryButtonText = LocalizationHelper.GetString("ConfigSaveReload.Text"),
            CloseButtonText = LocalizationHelper.GetString("CommonClose.Content"),
            DefaultButton = ContentDialogButton.Secondary,
            Content = scrollViewer,
        };

        var result = await dialog.ShowAsync();

        if (result == ContentDialogResult.Primary || result == ContentDialogResult.Secondary)
        {
            try
            {
                // Save to file
                await File.WriteAllTextAsync(capturedPath, editor.Text);
                capturedProfile.Path = capturedPath;

                // If secondary (Save & Reload), also reload config in the running core
                if (result == ContentDialogResult.Secondary)
                {
                    try
                    {
                        var clash = ServiceLocator.Get<IClashService>();
                        await clash.SwitchProfileAsync(capturedProfile.Id, capturedPath);
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Profiles] Config save error: {ex.Message}");
            }
        }
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
