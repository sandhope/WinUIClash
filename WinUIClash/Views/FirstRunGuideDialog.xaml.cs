using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinUIClash.Services;

namespace WinUIClash.Views;

/// <summary>
/// 首次启动引导向导（评审 §4.1）。
/// 三步：欢迎 + 快速上手 → 一键导入订阅 → 基础设置（主题 / 开机自启）。
/// 复用现有能力：ProfilesViewModel.ImportProfileAsync、ThemeSettingsViewModel、AutoLaunchService。
/// 说明：ClashSharp 的 StartupGuideDialog 实为「启动健康检查提示」，本向导按评审文档的建议做真·首次引导。
/// </summary>
public sealed partial class FirstRunGuideDialog : ContentDialog
{
    private const int LastStep = 2;
    private int _step;

    private readonly ViewModels.ProfilesViewModel _profiles;
    private readonly ViewModels.Settings.ThemeSettingsViewModel _theme;
    private readonly AutoLaunchService _autoLaunch;
    private readonly Models.AppSettings _settings;

    public FirstRunGuideDialog()
    {
        InitializeComponent();

        _profiles = ServiceLocator.Get<ViewModels.ProfilesViewModel>();
        _theme = ServiceLocator.Get<ViewModels.Settings.ThemeSettingsViewModel>();
        _autoLaunch = ServiceLocator.Get<AutoLaunchService>();
        _settings = ServiceLocator.Get<Models.AppSettings>();

        // ── 静态文案 ──
        Title = L("FirstRunTitle.Text");

        WelcomeTitle.Text = L("FirstRunWelcomeTitle.Text");
        WelcomeSubtitle.Text = L("FirstRunWelcomeSubtitle.Text");
        Step1Point1.Text = L("FirstRunStep1Point1.Text");
        Step1Point2.Text = L("FirstRunStep1Point2.Text");
        Step1Point3.Text = L("FirstRunStep1Point3.Text");

        Step2Title.Text = L("FirstRunImportTitle.Text");
        Step2Hint.Text = L("FirstRunImportHint.Text");
        UrlBox.PlaceholderText = L("FirstRunUrlPlaceholder.Text");
        NameBox.PlaceholderText = L("FirstRunNamePlaceholder.Text");
        ImportButton.Content = L("FirstRunImportButton.Content");

        Step3Title.Text = L("FirstRunSettingsTitle.Text");
        ThemeLabel.Text = L("FirstRunThemeLabel.Text");
        AutoLaunchLabel.Text = L("FirstRunAutoLaunchLabel.Text");
        AutoLaunchHint.Text = L("FirstRunAutoLaunchHint.Text");

        // ── 主题下拉（顺序须与 ApplyFinalSettings 的索引映射一致：0=System 1=Light 2=Dark）──
        ThemeCombo.Items.Add(L("ThemeModeSystem.Text"));
        ThemeCombo.Items.Add(L("ThemeModeLight.Text"));
        ThemeCombo.Items.Add(L("ThemeModeDark.Text"));
        ThemeCombo.SelectedIndex = _settings.ThemeMode switch
        {
            "Light" => 1,
            "Dark" => 2,
            _ => 0,
        };

        // ── 开机自启：以注册表实际状态为准 ──
        AutoLaunchToggle.IsOn = _autoLaunch.IsEnabled();

        CloseButtonText = L("FirstRunSkip.Content");
        DefaultButton = ContentDialogButton.Primary;

        UpdateStepUi();
    }

    private static string L(string key) => LocalizationHelper.GetString(key);

    /// <summary>根据当前步骤切换面板可见性与按钮文案。</summary>
    private void UpdateStepUi()
    {
        Step1Panel.Visibility = _step == 0 ? Visibility.Visible : Visibility.Collapsed;
        Step2Panel.Visibility = _step == 1 ? Visibility.Visible : Visibility.Collapsed;
        Step3Panel.Visibility = _step == 2 ? Visibility.Visible : Visibility.Collapsed;

        // 上一步：步骤 0 隐藏（空字符串隐藏按钮）
        SecondaryButtonText = _step == 0 ? string.Empty : L("FirstRunPrev.Content");
        // 下一步 / 完成
        PrimaryButtonText = _step == LastStep ? L("FirstRunFinish.Content") : L("FirstRunNext.Content");
    }

    /// <summary>下一步 / 完成。非末步阻止关闭并前进；末步应用设置后放行关闭。</summary>
    private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (_step < LastStep)
        {
            args.Cancel = true;
            _step++;
            UpdateStepUi();
            return;
        }

        // 末步：应用主题 + 开机自启
        ApplyFinalSettings();
    }

    /// <summary>上一步。阻止关闭并回退。</summary>
    private void OnSecondaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = true;
        if (_step > 0)
        {
            _step--;
            UpdateStepUi();
        }
    }

    /// <summary>应用基础设置（主题即时生效、开机自启写注册表）。</summary>
    private void ApplyFinalSettings()
    {
        var mode = ThemeCombo.SelectedIndex switch
        {
            1 => "Light",
            2 => "Dark",
            _ => "System",
        };
        // 通过 VM setter 应用：会写入设置并即时刷新窗口主题
        _theme.ThemeMode = mode;

        _settings.AutoLaunch = AutoLaunchToggle.IsOn;
        _autoLaunch.ApplyState(AutoLaunchToggle.IsOn);
    }

    /// <summary>一键导入订阅（内容区按钮，独立于向导导航）。</summary>
    private async void OnImportClick(object sender, RoutedEventArgs e)
    {
        var url = UrlBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(url))
        {
            ShowStatus(L("FirstRunImportEmpty.Text"), isError: true);
            return;
        }
        if (!Uri.TryCreate(url, UriKind.Absolute, out _))
        {
            ShowStatus(L("FirstRunImportInvalid.Text"), isError: true);
            return;
        }

        ImportButton.IsEnabled = false;
        ImportRing.IsActive = true;
        ImportRing.Visibility = Visibility.Visible;
        ImportStatus.Visibility = Visibility.Collapsed;

        try
        {
            await _profiles.InitializeAsync();
            var before = _profiles.Profiles.Count;
            var name = NameBox.Text?.Trim();
            await _profiles.ImportProfileAsync(url, string.IsNullOrEmpty(name) ? null : name);

            if (_profiles.Profiles.Count > before)
                ShowStatus(L("FirstRunImportOk.Text"), isError: false);
            else
                // ImportProfileAsync 内部已弹通知说明失败原因（重复/下载/校验），此处仅给轻量提示
                ShowStatus(L("FirstRunImportFail.Text"), isError: true);
        }
        catch (Exception ex)
        {
            ShowStatus(string.Format(L("FirstRunImportError.Text"), ex.Message), isError: true);
        }
        finally
        {
            ImportRing.IsActive = false;
            ImportRing.Visibility = Visibility.Collapsed;
            ImportButton.IsEnabled = true;
        }
    }

    private void ShowStatus(string text, bool isError)
    {
        ImportStatus.Text = text;
        ImportStatus.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[
            isError ? "SystemFillColorCriticalBrush" : "SystemFillColorSuccessBrush"];
        ImportStatus.Visibility = Visibility.Visible;
    }
}
