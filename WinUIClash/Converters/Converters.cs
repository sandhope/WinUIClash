using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace WinUIClash.Converters;

/// <summary>
/// 日志级别 → 前景色
/// </summary>
public class LogLevelToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is Models.LogLevel level)
        {
            return level switch
            {
                Models.LogLevel.Debug => new SolidColorBrush(Color.FromArgb(255, 130, 130, 130)),
                Models.LogLevel.Info => new SolidColorBrush(Color.FromArgb(255, 100, 180, 255)),
                Models.LogLevel.Warning => new SolidColorBrush(Color.FromArgb(255, 255, 180, 50)),
                Models.LogLevel.Error => new SolidColorBrush(Color.FromArgb(255, 255, 80, 80)),
                _ => new SolidColorBrush(Color.FromArgb(255, 180, 180, 180)),
            };
        }
        return new SolidColorBrush(Color.FromArgb(255, 180, 180, 180));
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}

/// <summary>
/// Bool → Visibility
/// </summary>
public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => value is Visibility.Visible;
}

/// <summary>
/// Bool → Visibility（取反：true → Collapsed，false → Visible）
/// </summary>
public class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => value is Visibility.Collapsed;
}

/// <summary>
/// 非 null → Visible，null → Collapsed
/// </summary>
public class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value != null ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}

/// <summary>
/// 代理延迟 → 颜色 (绿/黄/红/灰)。<br/>
/// 使用系统主题资源，确保在 Light/Dark 主题下均有良好的对比度。
/// </summary>
public class DelayToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is int delay)
        {
            if (delay <= 0) return GetBrush("TextFillColorDisabled");
            if (delay < 100) return GetBrush("SystemFillColorSuccess");
            if (delay < 300) return GetBrush("SystemFillColorCaution");
            return GetBrush("SystemFillColorCritical");
        }
        return GetBrush("TextFillColorDisabled");
    }

    private static Brush GetBrush(string resourceKey)
    {
        if (Application.Current.Resources.TryGetValue(resourceKey, out var val))
        {
            if (val is Color color) return new SolidColorBrush(color);
            if (val is Brush brush) return brush;
        }
        return new SolidColorBrush(Color.FromArgb(255, 130, 130, 130));
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}

/// <summary>
/// 延迟毫秒 → 显示文本
/// </summary>
public class DelayToTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is int delay)
        {
            if (delay <= 0) return "—";
            return $"{delay} ms";
        }
        return "—";
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}

/// <summary>
/// 字节数 → 人类可读字符串
/// </summary>
public class BytesToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is long bytes ? ByteFormatter.Format(bytes) : "0 B";

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}

/// <summary>
/// 字节速度 → 人类可读字符串/s
/// </summary>
public class BytesToSpeedConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is long bytes ? ByteFormatter.FormatSpeed(bytes) : "0 B/s";

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}

/// <summary>
/// DateTime → 相对时间
/// </summary>
public class DateTimeToRelativeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is DateTime dt ? TimeFormatter.Relative(dt) : "—";

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}

/// <summary>
/// Hex 颜色字符串 → Windows.UI.Color
/// </summary>
public class HexToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is string hex && hex.Length >= 7)
        {
            try
            {
                hex = hex.TrimStart('#');
                byte a = hex.Length == 8 ? System.Convert.ToByte(hex[0..2], 16) : (byte)255;
                int offset = hex.Length == 8 ? 2 : 0;
                byte r = System.Convert.ToByte(hex[offset..(offset + 2)], 16);
                byte g = System.Convert.ToByte(hex[(offset + 2)..(offset + 4)], 16);
                byte b = System.Convert.ToByte(hex[(offset + 4)..(offset + 6)], 16);
                return Color.FromArgb(a, r, g, b);
            }
            catch { }
        }
        return Color.FromArgb(255, 0, 0, 0);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}

/// <summary>
/// DateTime → HH:mm:ss 短时间格式
/// </summary>
public class DateTimeToTimeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is DateTime dt ? dt.ToString("HH:mm:ss") : "—";

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}

/// <summary>
/// 集合 → 空状态可见性（集合为空时显示，有元素时隐藏）
/// </summary>
public class EmptyCollectionToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        bool isEmpty = value switch
        {
            System.Collections.ICollection c => c.Count == 0,
            int count => count == 0,
            null => true,
            _ => true,
        };
        return isEmpty ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}

/// <summary>
/// 集合 → 非空可见性（集合有元素时显示，为空时隐藏）
/// </summary>
public class NonEmptyCollectionToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        bool hasItems = value switch
        {
            System.Collections.ICollection c => c.Count > 0,
            int count => count > 0,
            null => false,
            _ => false,
        };
        return hasItems ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}

/// <summary>
/// Bool → Bool（取反：true → false，false → true），用于 IsEnabled 绑定
/// </summary>
public class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is bool b ? !b : value;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => value is bool b ? !b : value;
}
