using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using SmartBinApp.Models;

namespace SmartBinApp.Converters
{
    /// <summary>
    /// Конвертер логического значения в видимость с возможностью инверсии.
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
    /// Конвертер уровня логирования в цвет текста.
    /// </summary>
    public class LogLevelToBrushConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is LogLevel level)
            {
                return level switch
                {
                    LogLevel.Success => new SolidColorBrush(Color.FromRgb(52, 211, 153)), // Изумрудно-зеленый
                    LogLevel.Warning => new SolidColorBrush(Color.FromRgb(251, 191, 36)), // Янтарно-желтый
                    LogLevel.Error => new SolidColorBrush(Color.FromRgb(248, 113, 113)),   // Кораллово-красный
                    _ => new SolidColorBrush(Color.FromRgb(203, 213, 225))                // Светло-серый
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
    /// Конвертер статуса подключения в цвет индикатора.
    /// </summary>
    public class ConnectionStatusBrushConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            bool isConnected = value is bool b && b;
            return isConnected
                ? new SolidColorBrush(Color.FromRgb(34, 197, 94))   // Зеленый (#22c55e)
                : new SolidColorBrush(Color.FromRgb(148, 163, 184)); // Серый (#94a3b8)
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    /// <summary>
    /// Конвертер категории мусора в фирменный цвет плашки.
    /// </summary>
    public class WasteTypeToBrushConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is WasteType wasteType)
            {
                return wasteType switch
                {
                    WasteType.Plastic => new SolidColorBrush(Color.FromRgb(56, 189, 248)),  // Голубой (#38bdf8)
                    WasteType.Paper => new SolidColorBrush(Color.FromRgb(245, 158, 11)),    // Оранжевый (#f59e0b)
                    WasteType.Organic => new SolidColorBrush(Color.FromRgb(34, 197, 94)),   // Зеленый (#22c55e)
                    _ => new SolidColorBrush(Color.FromRgb(148, 163, 184))                  // Серый (#94a3b8)
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
    /// Конвертер статуса загрузки модели в цвет бейджа.
    /// </summary>
    public class ModelStatusBrushConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            bool isLoaded = value is bool b && b;
            return isLoaded
                ? new SolidColorBrush(Color.FromRgb(34, 197, 94))   // Зеленый (#22c55e)
                : new SolidColorBrush(Color.FromRgb(239, 68, 68));  // Красный (#ef4444)
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
