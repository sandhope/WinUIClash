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
            await ViewModel.InitializeAsync();
            _textFormat = new CanvasTextFormat { FontSize = 9, FontFamily = "Consolas" };
            SpeedChart.ActualThemeChanged += SpeedChart_ThemeChanged;
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
}
