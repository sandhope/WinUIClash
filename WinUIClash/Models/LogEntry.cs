namespace WinUIClash.Models;

/// <summary>
/// 日志级别
/// </summary>
public enum LogLevel
{
    Debug,
    Info,
    Warning,
    Error,
    Silent
}

/// <summary>
/// 日志条目
/// </summary>
public class LogEntry
{
    public LogLevel Level { get; set; }
    public string Payload { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.Now;
}
