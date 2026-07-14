using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinUIClash.Services;

namespace WinUIClash.ViewModels;

/// <summary>
/// 网络诊断与开发者工具页 ViewModel。
/// 提供 WSL 代理桥接、终端代理环境变量、Microsoft Store 回环豁免三项的
/// 状态展示与一键应用 / 重置。
/// </summary>
public partial class DeveloperToolsViewModel : ObservableObject
{
    private readonly WindowsNetworkDiagnosticService _service;

    [ObservableProperty] private WindowsDiagnosticResult _wslResult;
    [ObservableProperty] private WindowsDiagnosticResult _terminalResult;
    [ObservableProperty] private WindowsDiagnosticResult _storeResult;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _busyMessage = string.Empty;

    public DeveloperToolsViewModel(WindowsNetworkDiagnosticService service)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
    }

    /// <summary>页面加载时调用，执行首次诊断。</summary>
    public Task InitializeAsync() => DiagnoseAllAsync();

    [RelayCommand]
    private async Task DiagnoseAllAsync()
    {
        if (IsBusy) return;

        await RunBusyAsync(LocalizationHelper.GetString("DevDiagChecking.Text"), async () =>
        {
            var results = await Task.WhenAll(
                _service.DiagnoseAsync(WindowsDiagnosticTarget.Wsl),
                _service.DiagnoseAsync(WindowsDiagnosticTarget.Terminal),
                _service.DiagnoseAsync(WindowsDiagnosticTarget.MicrosoftStore)).ConfigureAwait(false);

            WslResult = results[0];
            TerminalResult = results[1];
            StoreResult = results[2];
        });
    }

    [RelayCommand] private Task ApplyWsl() => ApplyTargetAsync(WindowsDiagnosticTarget.Wsl);
    [RelayCommand] private Task ResetWsl() => ResetTargetAsync(WindowsDiagnosticTarget.Wsl);
    [RelayCommand] private Task ApplyTerminal() => ApplyTargetAsync(WindowsDiagnosticTarget.Terminal);
    [RelayCommand] private Task ResetTerminal() => ResetTargetAsync(WindowsDiagnosticTarget.Terminal);
    [RelayCommand] private Task ApplyStore() => ApplyTargetAsync(WindowsDiagnosticTarget.MicrosoftStore);
    [RelayCommand] private Task ResetStore() => ResetTargetAsync(WindowsDiagnosticTarget.MicrosoftStore);

    private async Task ApplyTargetAsync(WindowsDiagnosticTarget target)
    {
        if (IsBusy) return;

        await RunBusyAsync(LocalizationHelper.GetString("DevDiagApplying.Text"), async () =>
        {
            var result = await _service.ApplyAsync(target).ConfigureAwait(false);
            SetResult(target, result);
        });
    }

    private async Task ResetTargetAsync(WindowsDiagnosticTarget target)
    {
        if (IsBusy) return;

        await RunBusyAsync(LocalizationHelper.GetString("DevDiagResetting.Text"), async () =>
        {
            var result = await _service.ResetAsync(target).ConfigureAwait(false);
            SetResult(target, result);
        });
    }

    private void SetResult(WindowsDiagnosticTarget target, WindowsDiagnosticResult result)
    {
        switch (target)
        {
            case WindowsDiagnosticTarget.Wsl:
                WslResult = result;
                break;
            case WindowsDiagnosticTarget.Terminal:
                TerminalResult = result;
                break;
            case WindowsDiagnosticTarget.MicrosoftStore:
                StoreResult = result;
                break;
        }
    }

    private async Task RunBusyAsync(string message, Func<Task> work)
    {
        IsBusy = true;
        BusyMessage = message;
        try
        {
            await work().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DeveloperTools] action failed: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
            BusyMessage = string.Empty;
        }
    }
}
