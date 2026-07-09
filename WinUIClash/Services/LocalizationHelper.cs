using Windows.ApplicationModel.Resources;

namespace WinUIClash.Services;

/// <summary>
/// 轻量级本地化帮助类 — 封装 ResourceLoader
/// </summary>
public static class LocalizationHelper
{
    private static readonly ResourceLoader Loader = ResourceLoader.GetForViewIndependentUse();

    /// <summary>获取本地化字符串</summary>
    public static string GetString(string resourceKey)
    {
        return Loader.GetString(resourceKey) ?? resourceKey;
    }
}
