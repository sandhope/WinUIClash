using CommunityToolkit.Mvvm.ComponentModel;

namespace WinUIClash.Models;

/// <summary>
/// Geo 数据库类型，与 FlClash 的 GeoResource 枚举一一对应。
/// </summary>
public enum GeoResourceType
{
    MMDB,
    ASN,
    GEOIP,
    GEOSITE,
}

/// <summary>
/// 单个 Geo 资源（MMDB / ASN / GEOIP / GEOSITE）的视图模型。
/// 文件存放在核心数据目录（mihomo -d 指定的目录），与 FlClash 一致。
/// </summary>
public partial class GeoResourceItem : ObservableObject
{
    public GeoResourceType Type { get; }

    public string FileName { get; }

    public string DisplayName => Type.ToString();

    [ObservableProperty] private string _url = "";

    [ObservableProperty] private long _size;

    [ObservableProperty] private DateTime? _lastModified;

    [ObservableProperty] private bool _isUpdating;

    /// <summary>综合展示文本：文件大小 + 最后修改时间，未下载时显示提示。</summary>
    [ObservableProperty] private string _fileInfoText = "";

    public GeoResourceItem(GeoResourceType type, string fileName)
    {
        Type = type;
        FileName = fileName;
    }
}
