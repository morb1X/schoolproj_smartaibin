using System;

namespace SmartBinApp.Models
{
    /// <summary>
    /// Log message severity level.
    /// </summary>
    public enum LogLevel
    {
        Info,
        Success,
        Warning,
        Error
    }

    /// <summary>
    /// Telemetry and event log message record.
    /// </summary>
    public class LogMessage
    {
        public DateTime Timestamp { get; } = DateTime.Now;
        public string Message { get; }
        public LogLevel Level { get; }

        public LogMessage(string message, LogLevel level = LogLevel.Info)
        {
            Message = message;
            Level = level;
        }

        public string FormattedTime => Timestamp.ToString("HH:mm:ss.fff");

        public override string ToString() => $"[{FormattedTime}] [{Level}] {Message}";
    }
}
