using System;

namespace SmartBinApp.Models
{
    /// <summary>
    /// Уровень важности сообщения в журнале.
    /// </summary>
    public enum LogLevel
    {
        Info,
        Success,
        Warning,
        Error
    }

    /// <summary>
    /// Модель записи в журнале событий приложения.
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
