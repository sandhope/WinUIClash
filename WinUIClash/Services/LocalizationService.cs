using WinUIClash.Models;

namespace WinUIClash.Services;

public class LocalizationService
{
    private readonly StringResources _stringResources;
    private readonly AppSettings _appSettings;

    public LocalizationService(StringResources stringResources, AppSettings appSettings)
    {
        _stringResources = stringResources;
        _appSettings = appSettings;
    }

    public void Initialize()
    {
        var lang = _appSettings.Language;
        if (string.IsNullOrEmpty(lang))
        {
            lang = Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride;
            if (string.IsNullOrEmpty(lang))
            {
                var languages = Windows.Globalization.ApplicationLanguages.Languages;
                lang = languages.Count > 0 ? languages[0] : "en-US";
            }
        }
        _stringResources.Load(lang);
    }

    public void SetLanguage(string language)
    {
        _stringResources.Load(language);
        Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = language;
    }
}