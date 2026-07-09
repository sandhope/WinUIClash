using System.Collections.ObjectModel;

namespace WinUIClash.Models;

/// <summary>
/// 连接元数据
/// </summary>
public class ConnectionMetadata
{
    public string Network { get; set; } = string.Empty;
    public string SourceIP { get; set; } = string.Empty;
    public string SourcePort { get; set; } = string.Empty;
    public string DestinationIP { get; set; } = string.Empty;
    public string DestinationPort { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public string Process { get; set; } = string.Empty;
    public string DnsMode { get; set; } = string.Empty;
    public string? DestinationGeoIP { get; set; }
    public string? DestinationIPASN { get; set; }
}

/// <summary>
/// 活跃连接
/// </summary>
public class ConnectionInfo
{
    public string Id { get; set; } = string.Empty;
    public long Upload { get; set; }
    public long Download { get; set; }
    public long UploadSpeed { get; set; }
    public long DownloadSpeed { get; set; }
    public DateTime Start { get; set; } = DateTime.Now;
    public ConnectionMetadata Metadata { get; set; } = new();
    public ObservableCollection<string> Chains { get; set; } = new();
    public string Rule { get; set; } = string.Empty;
    public string RulePayload { get; set; } = string.Empty;
}
