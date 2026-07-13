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
        Loaded += async (_, _) =>
        {
            try { await ViewModel.InitializeAsync(); }
            catch { /* 核心未运行或初始化出错时保持空状态，避免崩溃 */ }
        };
        Unloaded += (_, _) =>
        {
            // 注意：不要在此 Dispose ViewModel —— ConnectionsViewModel 是单例，
            // Dispose 会停掉 2 秒刷新定时器且其 _initialized 守卫导致再次进入页面时
            // 无法重启，连接列表将永远空白。只停掉本页私有的详情刷新计时器即可。
            _detailTimer?.Stop();
        };

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
        DetailProcess.Text = string.IsNullOrEmpty(conn.Metadata.Process)
            ? "—"
            : conn.Metadata.Process;
        DetailDnsMode.Text = string.IsNullOrEmpty(conn.Metadata.DnsMode)
            ? "—"
            : conn.Metadata.DnsMode;

        DetailTransfer.Text =
            $"{Converters.ByteFormatter.Format(conn.Upload)} ↑ / {Converters.ByteFormatter.Format(conn.Download)} ↓";

        DetailChains.Text = conn.Chains.Count > 0
            ? string.Join(" → ", conn.Chains)
            : LocalizationHelper.GetString("ConnDirect.Text");

        DetailRule.Text = string.IsNullOrEmpty(conn.RulePayload)
            ? conn.Rule
            : $"{conn.Rule} ({conn.RulePayload})";

        // GeoIP and ASN info
        var geoParts = new List<string>();
        if (!string.IsNullOrEmpty(conn.Metadata.DestinationGeoIP))
            geoParts.Add(conn.Metadata.DestinationGeoIP);
        if (!string.IsNullOrEmpty(conn.Metadata.DestinationIPASN))
            geoParts.Add($"ASN: {conn.Metadata.DestinationIPASN}");
        DetailGeoIP.Text = geoParts.Count > 0 ? string.Join(" | ", geoParts) : "—";

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

    private async void CloseAll_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = LocalizationHelper.GetString("ConnCloseAllConfirmTitle.Text"),
            Content = LocalizationHelper.GetString("ConnCloseAllConfirmContent.Text"),
            PrimaryButtonText = LocalizationHelper.GetString("CommonDelete.Content"),
            CloseButtonText = LocalizationHelper.GetString("CommonCancel.Content"),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            ViewModel.CloseAllCommand.Execute(null);
    }

}
