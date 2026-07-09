using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;
using WinUIClash.Models;

namespace WinUIClash.Views;

public sealed partial class DashboardView : Page
{
    public ViewModels.DashboardViewModel ViewModel { get; }

    public DashboardView()
    {
        ViewModel = ServiceLocator.Get<ViewModels.DashboardViewModel>();
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            await ViewModel.InitializeAsync();
            DrawChart();
        };

        // 流量数据变化时重绘图表
        ViewModel.TrafficHistory.CollectionChanged += (_, e) =>
        {
            if (e.Action is NotifyCollectionChangedAction.Add or NotifyCollectionChangedAction.Remove)
            {
                DispatcherQueue.TryEnqueue(DrawChart);
            }
        };
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
