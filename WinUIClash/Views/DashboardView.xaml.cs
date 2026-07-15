using System.Collections.Specialized;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using WinUIClash.Models;
using WinUIClash.Services;
using WinUIClash.ViewModels.Settings;

namespace WinUIClash.Views;

public sealed partial class DashboardView : Page
{
    public ViewModels.DashboardViewModel ViewModel { get; }

    private readonly NotifyCollectionChangedEventHandler _chartChangedHandler;

    // Chart state for hit-testing
    private double _chartStepX;

    // Reusable Win2D resources (created once, reused across draw calls)
    private CanvasTextFormat? _textFormat;
    private static readonly CanvasStrokeStyle GridDashStyle = new() { DashStyle = CanvasDashStyle.Dash };

    public DashboardView()
    {
        ViewModel = ServiceLocator.Get<ViewModels.DashboardViewModel>();
        InitializeComponent();

        // 流量数据变化时触发 Win2D 重绘（Invalidate 合并多次调用，下一帧只 Draw 一次）
        _chartChangedHandler = (_, e) =>
        {
            if (e.Action is NotifyCollectionChangedAction.Add or NotifyCollectionChangedAction.Remove)
            {
                DispatcherQueue.TryEnqueue(() => SpeedChart.Invalidate());
            }
        };

        Loaded += async (_, _) =>
        {
            _textFormat?.Dispose();
            try { await ViewModel.InitializeAsync(); }
            catch { /* 核心未运行或初始化出错时保持空状态，避免崩溃 */ }
            _textFormat = new CanvasTextFormat { FontSize = 9, FontFamily = "Consolas" };
            SpeedChart.ActualThemeChanged += SpeedChart_ThemeChanged;
            UpdateSpeedChartHeight();
            SpeedChart.Invalidate();
        };
        Unloaded += (_, _) =>
        {
            _textFormat?.Dispose();
            _textFormat = null;
            ViewModel.TrafficHistory.CollectionChanged -= _chartChangedHandler;
            SpeedChart.ActualThemeChanged -= SpeedChart_ThemeChanged;
        };

        ViewModel.TrafficHistory.CollectionChanged += _chartChangedHandler;
    }

    private void SpeedChart_ThemeChanged(FrameworkElement sender, object e) => SpeedChart.Invalidate();

    // ── 网速面积图绘制（Win2D GPU 即时模式，不创建任何 XAML 元素） ──

    private void SpeedChart_SizeChanged(object sender, SizeChangedEventArgs e) => SpeedChart.Invalidate();

    // 网速面积图高度随窗口尺寸自适应，范围 [150, 280]
    private void RootGrid_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateSpeedChartHeight();

    private void UpdateSpeedChartHeight()
    {
        if (SpeedChartHost is null) return;
        double available = RootGrid.ActualHeight;
        if (available <= 0) return;
        const double minHeight = 150;
        const double maxHeight = 280;
        // 顶部标题行 + 右侧系统代理/TUN 卡片 + 上下留白的基准占用；
        // 矮窗口时先扣掉这部分，图只剩少量空间 → 自然被压到 minHeight，不会抢磁贴空间。
        const double reserve = 300;
        const double factor = 0.4;
        double remaining = Math.Max(0, available - reserve);
        double desired = Math.Clamp(remaining * factor, minHeight, maxHeight);
        SpeedChartHost.Height = desired;
    }

    private void SpeedChart_Draw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        var ds = args.DrawingSession;
        double w = sender.ActualWidth;
        double h = sender.ActualHeight;
        if (w <= 0 || h <= 0) return;

        var history = ViewModel.TrafficHistory;

        // 主题颜色（根据 ActualTheme 从 ThemeDictionaries 读取）
        var baselineColor = GetThemeColor("ChartBaselineBrush");
        var gridColor = GetThemeColor("ChartGridLineBrush");
        var labelColor = GetThemeColor("ChartAxisLabelBrush");
        var upColor = GetThemeColor("ChartUploadBrush");
        var downColor = GetThemeColor("ChartDownloadBrush");

        // 基线（即使无数据也绘制）
        ds.DrawLine(0, (float)(h - 1), (float)w, (float)(h - 1), baselineColor, 1);

        if (history.Count < 2) return;

        long maxVal = 1;
        foreach (var t in history)
        {
            if (t.Down > maxVal) maxVal = t.Down;
            if (t.Up > maxVal) maxVal = t.Up;
        }
        maxVal = (long)(maxVal * 1.1);
        if (maxVal < 1024) maxVal = 1024;

        double stepX = w / (history.Count - 1);
        _chartStepX = stepX;

        // 网格线 + Y 轴标签（3 条水平虚线）
        for (int i = 1; i <= 3; i++)
        {
            double y = h * i / 4.0;
            ds.DrawLine(0, (float)y, (float)w, (float)y, gridColor, 0.5f, GridDashStyle);

            long labelVal = (long)(maxVal * (1.0 - i / 4.0));
            string label = Converters.ByteFormatter.FormatSpeed(labelVal);
            ds.DrawText(label, 4, (float)(y - 12), labelColor, _textFormat!);
        }

        // 下载面积 + 线（下层）
        using var downFill = BuildAreaGeometry(sender, history, t => t.Down, stepX, h, maxVal);
        ds.FillGeometry(downFill, Color.FromArgb(45, downColor.R, downColor.G, downColor.B));
        using var downLine = BuildLineGeometry(sender, history, t => t.Down, stepX, h, maxVal);
        ds.DrawGeometry(downLine, Color.FromArgb(220, downColor.R, downColor.G, downColor.B), 1.5f);

        // 上传面积 + 线（上层）
        using var upFill = BuildAreaGeometry(sender, history, t => t.Up, stepX, h, maxVal);
        ds.FillGeometry(upFill, Color.FromArgb(45, upColor.R, upColor.G, upColor.B));
        using var upLine = BuildLineGeometry(sender, history, t => t.Up, stepX, h, maxVal);
        ds.DrawGeometry(upLine, Color.FromArgb(220, upColor.R, upColor.G, upColor.B), 1.5f);
    }

    private static CanvasGeometry BuildAreaGeometry(
        ICanvasResourceCreatorWithDpi resourceCreator,
        System.Collections.ObjectModel.ObservableCollection<Traffic> data,
        Func<Traffic, long> selector,
        double stepX, double h, long maxVal)
    {
        using var pb = new CanvasPathBuilder(resourceCreator);
        pb.BeginFigure(0, (float)h);
        for (int i = 0; i < data.Count; i++)
        {
            double x = i * stepX;
            double y = h - (double)selector(data[i]) / maxVal * (h - 2);
            pb.AddLine((float)x, (float)Math.Max(0, y));
        }
        pb.AddLine((float)((data.Count - 1) * stepX), (float)h);
        pb.EndFigure(CanvasFigureLoop.Closed);
        return CanvasGeometry.CreatePath(pb);
    }

    private static CanvasGeometry BuildLineGeometry(
        ICanvasResourceCreatorWithDpi resourceCreator,
        System.Collections.ObjectModel.ObservableCollection<Traffic> data,
        Func<Traffic, long> selector,
        double stepX, double h, long maxVal)
    {
        using var pb = new CanvasPathBuilder(resourceCreator);
        double y0 = h - (double)selector(data[0]) / maxVal * (h - 2);
        pb.BeginFigure(0, (float)Math.Max(0, y0));
        for (int i = 1; i < data.Count; i++)
        {
            double x = i * stepX;
            double y = h - (double)selector(data[i]) / maxVal * (h - 2);
            pb.AddLine((float)x, (float)Math.Max(0, y));
        }
        pb.EndFigure(CanvasFigureLoop.Open);
        return CanvasGeometry.CreatePath(pb);
    }

    private Color GetThemeColor(string key)
    {
        var theme = SpeedChart.ActualTheme;
        string themeKey = theme == ElementTheme.Dark ? "Dark" : "Light";
        if (Application.Current.Resources.ThemeDictionaries.TryGetValue(themeKey, out var td)
            && td is ResourceDictionary rd
            && rd.TryGetValue(key, out var res)
            && res is SolidColorBrush b)
        {
        return b.Color;
        }
        return Color.FromArgb(255, 128, 128, 128);
    }

    // ── Chart hover tooltip（XAML 覆盖层，TranslateTransform 定位） ──

    private void SpeedChart_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var history = ViewModel.TrafficHistory;
        if (history.Count < 2 || _chartStepX <= 0) return;

        var pos = e.GetCurrentPoint(SpeedChart).Position;
        double w = SpeedChart.ActualWidth;
        double h = SpeedChart.ActualHeight;

        if (pos.X < 0 || pos.X > w || pos.Y < 0 || pos.Y > h)
        {
            HideTooltip();
            return;
        }

        int index = (int)Math.Round(pos.X / _chartStepX);
        index = Math.Clamp(index, 0, history.Count - 1);

        var traffic = history[index];
        double snapX = index * _chartStepX;

        // 竖线
        TooltipLineTransform.X = snapX;
        TooltipLine.Visibility = Visibility.Visible;

        // 文字
        TooltipTime.Text = traffic.Timestamp.ToString("HH:mm:ss");
        TooltipUp.Text = "↑ " + Converters.ByteFormatter.FormatSpeed(traffic.Up);
        TooltipDown.Text = "↓ " + Converters.ByteFormatter.FormatSpeed(traffic.Down);

        // 面板定位（贴右边缘时翻转到左侧）
        TooltipPanel.Visibility = Visibility.Visible;
        TooltipPanel.UpdateLayout();
        double panelW = TooltipPanel.ActualWidth;
        double panelH = TooltipPanel.ActualHeight;

        double tooltipX = snapX + 10;
        if (tooltipX + panelW > w)
            tooltipX = snapX - panelW - 10;

        double tooltipY = Math.Max(0, pos.Y - panelH / 2);
        if (tooltipY + panelH > h)
            tooltipY = h - panelH;

        TooltipPanelTransform.X = tooltipX;
        TooltipPanelTransform.Y = tooltipY;
    }

    private void SpeedChart_PointerExited(object sender, PointerRoutedEventArgs e) => HideTooltip();

    private void HideTooltip()
    {
        TooltipLine.Visibility = Visibility.Collapsed;
        TooltipPanel.Visibility = Visibility.Collapsed;
    }

    // ── Copy IP handlers ──

    private void CopyExternalIp_Click(object sender, RoutedEventArgs e)
    {
        var ip = ViewModel.ExternalIp;
        if (string.IsNullOrEmpty(ip)) return;
        var dp = new Windows.ApplicationModel.DataTransfer.DataPackage();
        dp.SetText(ip);
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp);
    }

    private void CopyLocalIp_Click(object sender, RoutedEventArgs e)
    {
        var ip = ViewModel.LocalIp;
        if (string.IsNullOrEmpty(ip)) return;
        var dp = new Windows.ApplicationModel.DataTransfer.DataPackage();
        dp.SetText(ip);
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp);
    }

    // 主题色磁贴：色板容器（用于选中态刷新）。ElementPrepared 首次实体化时捕获。
    private ItemsRepeater? _accentRepeater;

    private void AccentSwatch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is ThemeSettingsViewModel.ThemeColor color)
        {
            ViewModel.SelectAccentColor(color);
            UpdateAccentSwatchSelection();
        }
    }

    // 色板项实体化时：捕获容器引用并按当前选中索引设置黑框（参考主题设置子页面）。
    private void AccentSwatch_ElementPrepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args)
    {
        _accentRepeater = sender;
        UpdateAccentSwatchBorder(args.Element, args.Index);
    }

    // 遍历刷新所有已实体化色板的选中态。
    private void UpdateAccentSwatchSelection()
    {
        if (_accentRepeater is null) return;
        for (int i = 0; i < ViewModel.AccentColors.Length; i++)
        {
            var element = _accentRepeater.TryGetElement(i);
            if (element != null) UpdateAccentSwatchBorder(element, i);
        }
    }

    // 选中项加黑色 2px 边框（含 hover/pressed 态一并锁定为黑色），未选中项无边框。
    private void UpdateAccentSwatchBorder(UIElement element, int index)
    {
        if (element is not Button btn) return;
        if (index == ViewModel.CurrentAccentColorIndex)
        {
            var blackBrush = new SolidColorBrush(Microsoft.UI.Colors.Black);
            btn.BorderThickness = new Thickness(2);
            btn.BorderBrush = blackBrush;
            btn.Resources["ButtonBorderBrushPointerOver"] = blackBrush;
            btn.Resources["ButtonBorderBrushPressed"] = blackBrush;
        }
        else
        {
            var transparentBrush = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            btn.BorderThickness = new Thickness(0);
            btn.BorderBrush = null;
            btn.Resources["ButtonBorderBrushPointerOver"] = transparentBrush;
            btn.Resources["ButtonBorderBrushPressed"] = transparentBrush;
        }
    }

    private void TunToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch ts)
        {
            // Only respond to user-initiated toggles (not programmatic changes)
            if (ts.IsOn != ViewModel.IsTunEnabled)
            {
                ViewModel.ToggleTunModeCommand.Execute(null);
            }
        }
    }

    // ── 磁贴拖拽重排：拖拽结束后把新顺序写回设置 ──

    private void TileGrid_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        ViewModel.SaveTileOrder();
    }

    // ── 编辑磁贴可见性 ──

    private async void EditTiles_Click(object sender, RoutedEventArgs e)
    {
        var stack = new StackPanel { Spacing = 8 };
        foreach (var tile in ViewModel.DashboardTiles)
        {
            var cb = new CheckBox
            {
                Content = tile.Title,
                IsChecked = tile.IsVisible,
            };
            cb.Checked += (_, _) => tile.IsVisible = true;
            cb.Unchecked += (_, _) => tile.IsVisible = false;
            stack.Children.Add(cb);
        }

        var dialog = new ContentDialog
        {
            Title = LocalizationHelper.GetString("DashEditTiles.Content"),
            Content = new ScrollViewer
            {
                Content = stack,
                MaxHeight = 360,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            },
            CloseButtonText = LocalizationHelper.GetString("CommonDone.Content"),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };

        // ContentDialog 由 Popup 承载，脱离窗口根视觉树，看不到窗口根上的自定义强调色覆盖。
        // 手动把当前强调色画刷镜像进弹窗自身 Resources，使勾选框等显示自定义主题色而非系统色。
        ThemeSettingsViewModel.ApplyAccentBrushesTo(dialog.Resources);

        await dialog.ShowAsync();
        // 勾选时 IsVisible 已实时驱动 VisibleDashboardTiles 增删（ViewModel 内维护），此处仅持久化。
        ViewModel.SaveTileVisibility();
    }
}

/// <summary>按磁贴类型选择渲染模板</summary>
public partial class DashboardTileSelector : DataTemplateSelector
{
    public DataTemplate? OutboundModeTemplate { get; set; }
    public DataTemplate? NetworkCheckTemplate { get; set; }
    public DataTemplate? TrafficStatsTemplate { get; set; }
    public DataTemplate? MemoryTemplate { get; set; }
    public DataTemplate? ActiveNodeTemplate { get; set; }
    public DataTemplate? ActiveProfileTemplate { get; set; }
    public DataTemplate? UptimeTemplate { get; set; }
    public DataTemplate? ConnectionsTemplate { get; set; }
    public DataTemplate? LanguageTemplate { get; set; }
    public DataTemplate? ThemeTemplate { get; set; }
    public DataTemplate? AccentColorTemplate { get; set; }
    public DataTemplate? ClipboardDetectTemplate { get; set; }

    protected override DataTemplate? SelectTemplateCore(object item)
    {
        if (item is not DashboardTile tile) return null;
        return tile.Type switch
        {
            DashboardTileType.OutboundMode => OutboundModeTemplate,
            DashboardTileType.NetworkCheck => NetworkCheckTemplate,
            DashboardTileType.TrafficStats => TrafficStatsTemplate,
            DashboardTileType.Memory => MemoryTemplate,
            DashboardTileType.ActiveNode => ActiveNodeTemplate,
            DashboardTileType.ActiveProfile => ActiveProfileTemplate,
            DashboardTileType.Uptime => UptimeTemplate,
            DashboardTileType.Connections => ConnectionsTemplate,
            DashboardTileType.Language => LanguageTemplate,
            DashboardTileType.Theme => ThemeTemplate,
            DashboardTileType.AccentColor => AccentColorTemplate,
            DashboardTileType.ClipboardDetect => ClipboardDetectTemplate,
            _ => null,
        };
    }
}
