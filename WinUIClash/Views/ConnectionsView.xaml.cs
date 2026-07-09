using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinUIClash.Models;
using WinUIClash.Services;
using WinUIClash.ViewModels;

namespace WinUIClash.Views;

public sealed partial class ConnectionsView : Page
{
    public ConnectionsViewModel ViewModel { get; }
    private readonly DispatcherTimer _detailTimer;

    public ConnectionsView()
    {
        ViewModel = ServiceLocator.Get<ConnectionsViewModel>();
        InitializeComponent();
        Loaded += async (_, _) => await ViewModel.InitializeAsync();

        // 监听选中连接变化 → 更新详情面板
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;

        // 详情面板自动刷新（每秒更新持续时长和流量）
        _detailTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1),
        };
        _detailTimer.Tick += (_, _) =>
        {
            if (ViewModel.SelectedConnection != null)
                UpdateDetailPanel(ViewModel.SelectedConnection);
        };
        _detailTimer.Start();
    }

    private void OnViewModelPropertyChanged(object? sender,
        System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ConnectionsViewModel.SelectedConnection))
        {
            UpdateDetailPanel(ViewModel.SelectedConnection);
        }
    }

    private void UpdateDetailPanel(ConnectionInfo? conn)
    {
        if (conn == null) return;

        DetailHost.Text = conn.Metadata.Host;
        DetailSource.Text = $"{conn.Metadata.SourceIP}:{conn.Metadata.SourcePort}";
        DetailDest.Text = $"{conn.Metadata.DestinationIP}:{conn.Metadata.DestinationPort}";

        DetailTransfer.Text =
            $"{Converters.ByteFormatter.Format(conn.Upload)} ↑ / {Converters.ByteFormatter.Format(conn.Download)} ↓";

        DetailChains.Text = conn.Chains.Count > 0
            ? string.Join(" → ", conn.Chains)
            : LocalizationHelper.GetString("ConnDirect.Text");

        DetailRule.Text = string.IsNullOrEmpty(conn.RulePayload)
            ? conn.Rule
            : $"{conn.Rule} ({conn.RulePayload})";

        DetailStart.Text = conn.Start.ToString("yyyy-MM-dd HH:mm:ss");

        var duration = DateTime.Now - conn.Start;
        DetailDuration.Text = duration.TotalHours >= 1
            ? $"{(int)duration.TotalHours}h {duration.Minutes}m"
            : duration.Minutes > 0
                ? $"{duration.Minutes}m {duration.Seconds}s"
                : $"{duration.Seconds}s";
    }

    private void CloseConnection_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is ConnectionInfo conn)
            ViewModel.CloseConnectionCommand.Execute(conn);
    }

    private void CloseSelected_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedConnection != null)
            ViewModel.CloseConnectionCommand.Execute(ViewModel.SelectedConnection);
    }
}
