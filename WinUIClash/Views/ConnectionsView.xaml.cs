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
        Unloaded += (_, _) =>
        {
            _detailTimer?.Stop();
            ViewModel.Dispose();
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

    private void CopyHost_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedConnection != null)
            CopyToClipboard(ViewModel.SelectedConnection.Metadata.Host);
    }

    private void CloseConnection_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is ConnectionInfo conn)
            ViewModel.CloseConnectionCommand.Execute(conn);
    }

    private void ConnectionItem_RightTapped(object sender, Microsoft.UI.Xaml.Input.RightTappedRoutedEventArgs e)
    {
        if (sender is not FrameworkElement element) return;
        if (element.DataContext is not ConnectionInfo conn) return;

        var menu = new MenuFlyout();

        var copyHost = new MenuFlyoutItem { Text = LocalizationHelper.GetString("RequestsCopyHost.Text") };
        copyHost.Click += (_, _) => CopyToClipboard(conn.Metadata.Host);
        menu.Items.Add(copyHost);

        var copySource = new MenuFlyoutItem { Text = LocalizationHelper.GetString("ConnCopySource.Text") };
        copySource.Click += (_, _) => CopyToClipboard($"{conn.Metadata.SourceIP}:{conn.Metadata.SourcePort}");
        menu.Items.Add(copySource);

        var copyChains = new MenuFlyoutItem { Text = LocalizationHelper.GetString("ConnCopyChains.Text") };
        copyChains.Click += (_, _) => CopyToClipboard(string.Join(" → ", conn.Chains));
        menu.Items.Add(copyChains);

        menu.Items.Add(new MenuFlyoutSeparator());

        var close = new MenuFlyoutItem { Text = LocalizationHelper.GetString("ConnClose.ToolTip") };
        close.Click += (_, _) => ViewModel.CloseConnectionCommand.Execute(conn);
        menu.Items.Add(close);

        menu.ShowAt(element, e.GetPosition(element));
    }

    private void CloseSelected_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedConnection != null)
            ViewModel.CloseConnectionCommand.Execute(ViewModel.SelectedConnection);
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

    private void DetailPanel_RightTapped(object sender, Microsoft.UI.Xaml.Input.RightTappedRoutedEventArgs e)
    {
        var conn = ViewModel.SelectedConnection;
        if (conn == null || sender is not FrameworkElement element) return;

        var menu = new MenuFlyout();

        var copyHost = new MenuFlyoutItem { Text = LocalizationHelper.GetString("RequestsCopyHost.Text") };
        copyHost.Click += (_, _) => CopyToClipboard(conn.Metadata.Host);
        menu.Items.Add(copyHost);

        var copyChains = new MenuFlyoutItem { Text = LocalizationHelper.GetString("ConnCopyChains.Text") };
        copyChains.Click += (_, _) => CopyToClipboard(string.Join(" → ", conn.Chains));
        menu.Items.Add(copyChains);

        var copyRule = new MenuFlyoutItem { Text = LocalizationHelper.GetString("RequestsCopyRule.Text") };
        copyRule.Click += (_, _) => CopyToClipboard(
            string.IsNullOrEmpty(conn.RulePayload) ? conn.Rule : $"{conn.Rule} ({conn.RulePayload})");
        menu.Items.Add(copyRule);

        menu.Items.Add(new MenuFlyoutSeparator());

        var copyAll = new MenuFlyoutItem { Text = LocalizationHelper.GetString("RequestsCopyAll.Text") };
        copyAll.Click += (_, _) => CopyToClipboard(
            $"{conn.Metadata.Host} | {conn.Metadata.SourceIP}:{conn.Metadata.SourcePort} → {conn.Metadata.DestinationIP}:{conn.Metadata.DestinationPort} | {string.Join(", ", conn.Chains)} | {conn.Rule}");
        menu.Items.Add(copyAll);

        menu.ShowAt(element, e.GetPosition(element));
    }

    private static void CopyToClipboard(string text)
    {
        var dp = new Windows.ApplicationModel.DataTransfer.DataPackage();
        dp.SetText(text);
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp);
    }
}
