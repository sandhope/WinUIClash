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

    /// <summary>
    /// 切换界面语言。应用自身的字符串由 StringResources.Load 完成（会触发
    /// PropertyChanged 让界面重绑）。ApplicationLanguages.PrimaryLanguageOverride
    /// 在非打包（unpackaged）的 WinUI 3 宿主下不受支持，调用其 setter 会抛
    /// InvalidOperationException (0x80073D54)，因此仅作“尽力而为”的 OS 级覆盖，
    /// 失败则忽略——不影响应用自身的本地化。
    /// </summary>
    public void SetLanguage(string language)
    {
        _stringResources.Load(language);
        try
        {
            Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = language;
        }
        catch (InvalidOperationException)
        {
            // 非打包宿主不支持该 API，忽略；应用自身本地化已由上面完成。
        }
    }
}