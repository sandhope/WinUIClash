using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace WinUIClash.Converters;

/// <summary>
/// 日志级别 → 前景色
/// </summary>
public partial class LogLevelToColorConverter : IValueConverter
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
public partial class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => value is Visibility.Visible;
}

/// <summary>
/// Bool → Visibility（取反：true → Collapsed，false → Visible）
/// </summary>
public partial class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => value is Visibility.Collapsed;
}

/// <summary>
/// string 相等 → bool。把字符串状态（如出站模式 "rule"/"global"/"direct"）与
/// RadioButton.IsChecked 双向绑定：<br/>
/// Convert：value == parameter（忽略大小写）→ 该单选项是否被选中；<br/>
/// ConvertBack：仅当被选中（true）时回写 parameter 对应的字符串；取消选中（false）时返回
/// UnsetValue，避免把源值覆盖为空——这样一个单选组里只有“新选中项”会写回来源。
/// </summary>
public partial class StringEqualsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => string.Equals(value as string, parameter as string, StringComparison.OrdinalIgnoreCase);

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => value is true ? (parameter as string ?? string.Empty) : DependencyProperty.UnsetValue;
}

/// <summary>
/// Bool → Opacity。true → 1.0（完全不透明）；false → 半透明（默认 0.5，可用 parameter 指定，如 "0.6"）。<br/>
/// 用于「预设色板被禁用时」给色块整体降透明度，直观表达"不可用"，且作用范围仅限绑定它的控件（不影响全局按钮）。
/// </summary>
public partial class BoolToOpacityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        double disabledOpacity = 0.5;
        if (parameter is string s && double.TryParse(s, out var p) && p is >= 0 and <= 1)
            disabledOpacity = p;
        return value is true ? 1.0 : disabledOpacity;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}

/// <summary>
/// 非 null → Visible，null → Collapsed
/// </summary>
public partial class NullToVisibilityConverter : IValueConverter
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
public partial class DelayToColorConverter : IValueConverter
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
public partial class DelayToTextConverter : IValueConverter
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
public partial class BytesToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is long bytes ? ByteFormatter.Format(bytes) : "0 B";

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}

/// <summary>
/// 字节速度 → 人类可读字符串/s
/// </summary>
public partial class BytesToSpeedConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is long bytes ? ByteFormatter.FormatSpeed(bytes) : "0 B/s";

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}

/// <summary>
/// DateTime → 相对时间
/// </summary>
public partial class DateTimeToRelativeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is DateTime dt ? TimeFormatter.Relative(dt) : "—";

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}

/// <summary>
/// Hex 颜色字符串 → Windows.UI.Color
/// </summary>
public partial class HexToColorConverter : IValueConverter
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
public partial class DateTimeToTimeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is DateTime dt ? dt.ToString("HH:mm:ss") : "—";

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}

/// <summary>
/// 集合 → 空状态可见性（集合为空时显示，有元素时隐藏）
/// </summary>
public partial class EmptyCollectionToVisibilityConverter : IValueConverter
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
public partial class NonEmptyCollectionToVisibilityConverter : IValueConverter
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
public partial class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is bool b ? !b : value;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => value is bool b ? !b : value;
}
