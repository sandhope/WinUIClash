using CommunityToolkit.Mvvm.ComponentModel;
using WinUIClash.Models;
using WinUIClash.Services;

namespace WinUIClash.ViewModels.Settings;

/// <summary>
/// 语言设置 ViewModel
/// </summary>
public partial class LanguageSettingsViewModel : ObservableObject
{
    private readonly AppSettings _settings;

    public LanguageSettingsViewModel(AppSettings settings)
    {
        _settings = settings;
    }

    public bool IsChinese
    {
        get => _settings.Language == "zh-CN";
        set
        {
            if (value)
            {
                SetLanguage("zh-CN");
            }
        }
    }

    public bool IsEnglish
    {
        get => _settings.Language == "en-US";
        set
        {
            if (value)
            {
                SetLanguage("en-US");
            }
        }
    }

    private void SetLanguage(string language)
    {
        if (_settings.Language != language)
        {
            _settings.Language = language;
            OnPropertyChanged(string.Empty);

            var localizationService = ServiceLocator.Get<LocalizationService>();
            localizationService.SetLanguage(language);
        }
    }
}
