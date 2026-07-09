using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;
using WinUIClash.Models;

namespace WinUIClash.Views;

public sealed partial class DashboardView : Page
{
    public ViewModels.DashboardViewModel ViewModel { get; }
    private readonly NotifyCollectionChangedEventHandler _chartChangedHandler;

    // Chart state for hit-testing
    private long _chartMaxVal;
    private double _chartStepX;

    // Reusable tooltip elements
    private Line? _tooltipLine;
    private Border? _tooltipPanel;
    private TextBlock? _tooltipTime;
    private TextBlock? _tooltipUp;
    private TextBlock? _tooltipDown;

    public DashboardView()
    {
        ViewModel = ServiceLocator.Get<ViewModels.DashboardViewModel>();
        InitializeComponent();

        _chartChangedHandler = (_, e) =>
        {
            if (e.Action is NotifyCollectionChangedAction.Add or NotifyCollectionChangedAction.Remove)
            {
                DispatcherQueue.TryEnqueue(DrawChart);
            }
        };

        Loaded += async (_, _) =>
        {
            await ViewModel.InitializeAsync();
            DrawChart();
        };
        Unloaded += (_, _) => ViewModel.TrafficHistory.CollectionChanged -= _chartChangedHandler;

        // 流量数据变化时重绘图表
        ViewModel.TrafficHistory.CollectionChanged += _chartChangedHandler;
    }

    // ── 网速面积图绘制 ──

    private void SpeedChart_SizeChanged(object sender, SizeChangedEventArgs e) => DrawChart();

    private void DrawChart()
    {
        if (SpeedChart.ActualWidth <= 0 || SpeedChart.ActualHeight <= 0) return;

        SpeedChart.Children.Clear();
        var history = ViewModel.TrafficHistory;
        if (history.Count < 2) return;

        double w = SpeedChart.ActualWidth;
        double h = SpeedChart.ActualHeight;

        long maxVal = 1;
        foreach (var t in history)
        {
            if (t.Down > maxVal) maxVal = t.Down;
            if (t.Up > maxVal) maxVal = t.Up;
        }

        // 将 maxVal 向上取整到整数值（方便标注）
        maxVal = (long)(maxVal * 1.1);
        if (maxVal < 1024) maxVal = 1024;

        double stepX = w / (history.Count - 1);

        // Save chart state for hit-testing
        _chartMaxVal = maxVal;
        _chartStepX = stepX;

        // 绘制网格线（3条水平线）
        var gridColor = new SolidColorBrush(Windows.UI.Color.FromArgb(30, 128, 128, 128));
        for (int i = 1; i <= 3; i++)
        {
            double y = h * i / 4.0;
            var line = new Line
            {
                X1 = 0, Y1 = y, X2 = w, Y2 = y,
                Stroke = gridColor,
                StrokeThickness = 0.5,
                StrokeDashArray = new DoubleCollection { 4, 4 },
            };
            SpeedChart.Children.Add(line);

            // Y轴标签
            var labelVal = (long)(maxVal * (1.0 - i / 4.0));
            var label = new TextBlock
            {
                Text = Converters.ByteFormatter.FormatSpeed(labelVal),
                FontSize = 9,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(100, 128, 128, 128)),
                FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
            };
            Canvas.SetLeft(label, 4);
            Canvas.SetTop(label, y - 12);
            SpeedChart.Children.Add(label);
        }

        // 下载面积（先绘制，在下层）
        var downFill = BuildFill(history, t => t.Down, stepX, h, maxVal,
            Windows.UI.Color.FromArgb(35, 129, 199, 132));
        var downLine = BuildLine(history, t => t.Down, stepX, h, maxVal,
            Windows.UI.Color.FromArgb(200, 129, 199, 132));

        // 上传面积（后绘制，在上层）
        var upFill = BuildFill(history, t => t.Up, stepX, h, maxVal,
            Windows.UI.Color.FromArgb(35, 79, 195, 247));
        var upLine = BuildLine(history, t => t.Up, stepX, h, maxVal,
            Windows.UI.Color.FromArgb(200, 79, 195, 247));

        SpeedChart.Children.Add(downFill);
        SpeedChart.Children.Add(upFill);
        SpeedChart.Children.Add(downLine);
        SpeedChart.Children.Add(upLine);

        // Tooltip overlay (created once, reused on hover)
        EnsureTooltipElements();
    }

    private void EnsureTooltipElements()
    {
        double h = SpeedChart.ActualHeight;

        if (_tooltipLine == null)
        {
            _tooltipLine = new Line
            {
                Y1 = 0, Y2 = h,
                Stroke = new SolidColorBrush(Windows.UI.Color.FromArgb(80, 255, 255, 255)),
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 3, 3 },
                Visibility = Visibility.Collapsed,
                IsHitTestVisible = false,
            };
        }
        else
        {
            _tooltipLine.Y2 = h;
        }

        if (_tooltipPanel == null)
        {
            _tooltipTime = new TextBlock { FontSize = 10, Opacity = 0.7, FontFamily = new FontFamily("Consolas") };
            _tooltipUp = new TextBlock { FontSize = 11, Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 79, 195, 247)) };
            _tooltipDown = new TextBlock { FontSize = 11, Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 129, 199, 132)) };

            var stack = new StackPanel { Spacing = 2 };
            stack.Children.Add(_tooltipTime);
            stack.Children.Add(_tooltipUp);
            stack.Children.Add(_tooltipDown);

            _tooltipPanel = new Border
            {
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(220, 30, 30, 30)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 6, 8, 6),
                Child = stack,
                Visibility = Visibility.Collapsed,
                IsHitTestVisible = false,
            };
        }

        // Add to canvas if not already present
        if (!SpeedChart.Children.Contains(_tooltipLine))
            SpeedChart.Children.Add(_tooltipLine);
        if (!SpeedChart.Children.Contains(_tooltipPanel))
            SpeedChart.Children.Add(_tooltipPanel);
    }

    // ── Chart hover tooltip ──

    private void SpeedChart_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var history = ViewModel.TrafficHistory;
        if (history.Count < 2 || _tooltipLine == null || _tooltipPanel == null) return;

        var pos = e.GetCurrentPoint(SpeedChart).Position;
        double w = SpeedChart.ActualWidth;
        double h = SpeedChart.ActualHeight;

        if (pos.X < 0 || pos.X > w || pos.Y < 0 || pos.Y > h)
        {
            HideTooltip();
            return;
        }

        // Find nearest data point index
        int index = (int)Math.Round(pos.X / _chartStepX);
        index = Math.Clamp(index, 0, history.Count - 1);

        var traffic = history[index];
        double snapX = index * _chartStepX;

        // Update vertical line position
        _tooltipLine.X1 = snapX;
        _tooltipLine.X2 = snapX;
        _tooltipLine.Y2 = h;
        _tooltipLine.Visibility = Visibility.Visible;

        // Update tooltip text
        _tooltipTime!.Text = traffic.Timestamp.ToString("HH:mm:ss");
        _tooltipUp!.Text = "↑ " + Converters.ByteFormatter.FormatSpeed(traffic.Up);
        _tooltipDown!.Text = "↓ " + Converters.ByteFormatter.FormatSpeed(traffic.Down);

        // Position tooltip panel — flip side when near right edge
        _tooltipPanel.Visibility = Visibility.Visible;
        _tooltipPanel.UpdateLayout();
        double panelW = _tooltipPanel.ActualWidth;
        double panelH = _tooltipPanel.ActualHeight;

        double tooltipX = snapX + 10;
        if (tooltipX + panelW > w)
            tooltipX = snapX - panelW - 10;

        double tooltipY = Math.Max(0, pos.Y - panelH / 2);
        if (tooltipY + panelH > h)
            tooltipY = h - panelH;

        Canvas.SetLeft(_tooltipPanel, tooltipX);
        Canvas.SetTop(_tooltipPanel, tooltipY);
    }

    private void SpeedChart_PointerExited(object sender, PointerRoutedEventArgs e) => HideTooltip();

    private void HideTooltip()
    {
        if (_tooltipLine != null) _tooltipLine.Visibility = Visibility.Collapsed;
        if (_tooltipPanel != null) _tooltipPanel.Visibility = Visibility.Collapsed;
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

    private static Polygon BuildFill(
        System.Collections.ObjectModel.ObservableCollection<Traffic> data,
        Func<Traffic, long> selector,
        double stepX, double h, long maxVal, Color color)
    {
        var pts = new PointCollection();
        pts.Add(new Windows.Foundation.Point(0, h));
        for (int i = 0; i < data.Count; i++)
        {
            double x = i * stepX;
            double y = h - (double)selector(data[i]) / maxVal * (h - 2);
            pts.Add(new Windows.Foundation.Point(x, Math.Max(0, y)));
        }
        pts.Add(new Windows.Foundation.Point((data.Count - 1) * stepX, h));
        return new Polygon { Fill = new SolidColorBrush(color), Points = pts };
    }

    private static Polyline BuildLine(
        System.Collections.ObjectModel.ObservableCollection<Traffic> data,
        Func<Traffic, long> selector,
        double stepX, double h, long maxVal, Color color)
    {
        var pts = new PointCollection();
        for (int i = 0; i < data.Count; i++)
        {
            double x = i * stepX;
            double y = h - (double)selector(data[i]) / maxVal * (h - 2);
            pts.Add(new Windows.Foundation.Point(x, Math.Max(0, y)));
        }
        return new Polyline
        {
            Stroke = new SolidColorBrush(color),
            StrokeThickness = 1.5,
            Points = pts
        };
    }
}
