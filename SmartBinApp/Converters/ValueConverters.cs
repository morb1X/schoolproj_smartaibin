using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using SmartBinApp.Models;

namespace SmartBinApp.Converters
{
    /// <summary>
    /// Converts a boolean value to Visibility with optional inversion.
    /// </summary>
    public class BoolToVisibilityConverter : IValueConverter
    {
        public bool Invert { get; set; }

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            bool boolVal = value is bool b && b;
            if (Invert) boolVal = !boolVal;
            return boolVal ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    /// <summary>
    /// Converts log level to text foreground brush.
    /// </summary>
    public class LogLevelToBrushConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is LogLevel level)
            {
                return level switch
                {
                    LogLevel.Success => new SolidColorBrush(Color.FromRgb(52, 211, 153)), // Emerald green
                    LogLevel.Warning => new SolidColorBrush(Color.FromRgb(251, 191, 36)), // Amber yellow
                    LogLevel.Error => new SolidColorBrush(Color.FromRgb(248, 113, 113)),   // Coral red
                    _ => new SolidColorBrush(Color.FromRgb(203, 213, 225))                // Light slate
                };
            }
            return new SolidColorBrush(Color.FromRgb(203, 213, 225));
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    /// <summary>
    /// Converts connection status to status indicator brush.
    /// </summary>
    public class ConnectionStatusBrushConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            bool isConnected = value is bool b && b;
            return isConnected
                ? new SolidColorBrush(Color.FromRgb(34, 197, 94))   // Green (#22c55e)
                : new SolidColorBrush(Color.FromRgb(148, 163, 184)); // Slate (#94a3b8)
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    /// <summary>
    /// Converts waste category to branded card accent brush.
    /// </summary>
    public class WasteTypeToBrushConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is WasteType wasteType)
            {
                return wasteType switch
                {
                    WasteType.Plastic => new SolidColorBrush(Color.FromRgb(56, 189, 248)),  // Sky blue (#38bdf8)
                    WasteType.Paper => new SolidColorBrush(Color.FromRgb(245, 158, 11)),    // Amber orange (#f59e0b)
                    WasteType.Organic => new SolidColorBrush(Color.FromRgb(34, 197, 94)),   // Emerald green (#22c55e)
                    _ => new SolidColorBrush(Color.FromRgb(148, 163, 184))                  // Slate (#94a3b8)
                };
            }
            return new SolidColorBrush(Color.FromRgb(148, 163, 184));
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    /// <summary>
    /// Converts model loading status to badge status brush.
    /// </summary>
    public class ModelStatusBrushConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            bool isLoaded = value is bool b && b;
            return isLoaded
                ? new SolidColorBrush(Color.FromRgb(34, 197, 94))   // Green (#22c55e)
                : new SolidColorBrush(Color.FromRgb(239, 68, 68));  // Red (#ef4444)
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
