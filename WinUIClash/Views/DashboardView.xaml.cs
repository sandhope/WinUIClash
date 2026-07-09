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

        double stepX = w / (history.Count - 1);

        // 下载面积（先绘制，在下层）
        var downFill = BuildFill(history, t => t.Down, stepX, h, maxVal,
            Color.FromArgb(35, 129, 199, 132));
        var downLine = BuildLine(history, t => t.Down, stepX, h, maxVal,
            Color.FromArgb(200, 129, 199, 132));

        // 上传面积（后绘制，在上层）
        var upFill = BuildFill(history, t => t.Up, stepX, h, maxVal,
            Color.FromArgb(35, 79, 195, 247));
        var upLine = BuildLine(history, t => t.Up, stepX, h, maxVal,
            Color.FromArgb(200, 79, 195, 247));

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
