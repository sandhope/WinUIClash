using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinUIClash.Models;
using WinUIClash.Services;

namespace WinUIClash.ViewModels;

/// <summary>
/// 配置页 ViewModel — 配置管理、订阅更新
/// </summary>
public partial class ProfilesViewModel : ObservableObject
{
    private readonly IClashService _clash;
    private bool _initialized;

    public ProfilesViewModel(IClashService clash)
    {
        _clash = clash;
    }

    [ObservableProperty] private ObservableCollection<Profile> _profiles = new();
    [ObservableProperty] private Profile? _activeProfile;
    [ObservableProperty] private bool _isLoading;

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        var list = await _clash.GetProfilesAsync();
        Profiles = new ObservableCollection<Profile>(list);
        ActiveProfile = Profiles.FirstOrDefault(p => p.IsActive);
        IsLoading = false;
    }

    [RelayCommand]
    private async Task SelectProfileAsync(Profile? profile)
    {
        if (profile == null) return;
        await _clash.SwitchProfileAsync(profile.Id);
        foreach (var p in Profiles) p.IsActive = p.Id == profile.Id;
        ActiveProfile = Profiles.FirstOrDefault(p => p.IsActive);
    }

    [RelayCommand]
    private async Task SyncProfileAsync(Profile? profile)
    {
        if (profile == null) return;
        await _clash.SyncProfileAsync(profile.Id);
        profile.LastUpdate = DateTime.Now;
    }

    [RelayCommand]
    private async Task SyncAllAsync()
    {
        foreach (var p in Profiles.Where(p => !string.IsNullOrEmpty(p.Url)))
        {
            try
            {
                await _clash.SyncProfileAsync(p.Id);
                p.LastUpdate = DateTime.Now;
            }
            catch { /* 单个失败不影响其余 */ }
        }
    }

    [RelayCommand]
    private async Task DeleteProfileAsync(Profile? profile)
    {
        if (profile == null) return;
        await _clash.DeleteProfileAsync(profile.Id);
        Profiles.Remove(profile);
    }

    public async Task InitializeAsync()
    {
        if (_initialized) return;
        _initialized = true;
        await LoadAsync();
    }
}
