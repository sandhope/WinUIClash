namespace WinUIClash.Services;

public static class LocalizationHelper
{
    // 缓存 StringResources 实例。StringResources 现在内部用纯托管 Dictionary 快照，
    // Get() 不触碰任何 WinRT COM 对象，可在任意线程安全调用（不会再抛 RPC_E_WRONG_THREAD）。
    private static StringResources? _stringResources;

    public static void Initialize(StringResources stringResources)
    {
        _stringResources = stringResources;
    }

    public static string GetString(string resourceKey)
    {
        // 完全不访问 Application.Current.Resources（WinRT COM 对象，后台线程会抛
        // RPC_E_WRONG_THREAD）。StringResources.Get 走托管 Dictionary 快照，线程安全。
        if (_stringResources != null)
        {
            var v = _stringResources.Get(resourceKey);
            if (v != resourceKey) return v;
        }
        return resourceKey;
    }

    public static void Refresh()
    {
    }
}
