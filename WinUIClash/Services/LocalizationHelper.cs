using Microsoft.UI.Xaml;

namespace WinUIClash.Services;

public static class LocalizationHelper
{
    public static string GetString(string resourceKey)
    {
        if (Application.Current?.Resources.TryGetValue("S", out var obj) == true && obj is StringResources s)
        {
            return s.Get(resourceKey);
        }
        return resourceKey;
    }

    public static void Refresh()
    {
    }
}