namespace WinUIClash.Models;

/// <summary>
/// 实时流量数据（上传/下载速度）
/// </summary>
public class Traffic
{
    public long Up { get; set; }
    public long Down { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.Now;
}
