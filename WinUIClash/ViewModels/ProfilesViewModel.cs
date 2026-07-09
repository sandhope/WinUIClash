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
    private async Task SelectProfileAsync(string profileId)
    {
        await _clash.SwitchProfileAsync(profileId);
        foreach (var p in Profiles) p.IsActive = p.Id == profileId;
        ActiveProfile = Profiles.FirstOrDefault(p => p.IsActive);
    }

    [RelayCommand]
    private async Task SyncProfileAsync(string profileId)
    {
        await _clash.SyncProfileAsync(profileId);
        var p = Profiles.FirstOrDefault(x => x.Id == profileId);
        if (p != null) p.LastUpdate = DateTime.Now;
    }

    [RelayCommand]
    private async Task SyncAllAsync()
    {
        foreach (var p in Profiles.Where(p => !string.IsNullOrEmpty(p.Url)))
        {
            await _clash.SyncProfileAsync(p.Id);
            p.LastUpdate = DateTime.Now;
        }
    }

    [RelayCommand]
    private async Task DeleteProfileAsync(string profileId)
    {
        await _clash.DeleteProfileAsync(profileId);
        var toRemove = Profiles.FirstOrDefault(p => p.Id == profileId);
        if (toRemove != null) Profiles.Remove(toRemove);
    }

    public async Task InitializeAsync()
    {
        await LoadAsync();
    }
}
