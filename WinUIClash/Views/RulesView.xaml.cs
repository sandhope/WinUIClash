using Microsoft.UI.Xaml.Controls;
using WinUIClash.ViewModels;

namespace WinUIClash.Views;

public sealed partial class RulesView : Page
{
    public RulesViewModel ViewModel { get; }

    public RulesView()
    {
        ViewModel = ServiceLocator.Get<RulesViewModel>();
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            await ViewModel.InitializeAsync();
            PopulateProxyFilter();
        };

        // 监听代理选项变化以刷新 ComboBox
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(RulesViewModel.ProxyOptions))
                PopulateProxyFilter();
        };
    }

    private void PopulateProxyFilter()
    {
        ProxyFilterCombo.Items.Clear();
        ProxyFilterCombo.Items.Add(new ComboBoxItem { Content = "全部目标", Tag = "" });
        foreach (var proxy in ViewModel.ProxyOptions)
        {
            if (string.IsNullOrEmpty(proxy)) continue;
            ProxyFilterCombo.Items.Add(new ComboBoxItem { Content = proxy, Tag = proxy });
        }
        ProxyFilterCombo.SelectedIndex = 0;
    }

    private void ProxyFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox combo && combo.SelectedItem is ComboBoxItem item)
        {
            ViewModel.SelectedProxyFilter = item.Tag as string ?? "";
        }
    }
}
