using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace WinUIClash.Converters;

/// <summary>
/// 字节数 → 人类可读字符串（KB / MB / GB）
/// </summary>
public static class ByteFormatter
{
    public static string Format(long bytes)
    {
        if (bytes < 0) return "0 B";
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
    }

    public static string FormatSpeed(long bytesPerSecond) => Format(bytesPerSecond) + "/s";
}

/// <summary>
/// 日期时间 → 相对时间字符串
/// </summary>
public static class TimeFormatter
{
    public static string Relative(DateTime dt)
    {
        var span = DateTime.Now - dt;
        if (span.TotalSeconds < 60) return "刚刚";
        if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes} 分钟前";
        if (span.TotalHours < 24) return $"{(int)span.TotalHours} 小时前";
        if (span.TotalDays < 30) return $"{(int)span.TotalDays} 天前";
        return dt.ToString("yyyy-MM-dd");
    }

    public static string Duration(TimeSpan ts)
    {
        if (ts.TotalHours >= 1) return $"{(int)ts.TotalHours}h {ts.Minutes}m";
        if (ts.TotalMinutes >= 1) return $"{(int)ts.TotalMinutes}m {ts.Seconds}s";
        return $"{ts.Seconds}s";
    }
}
