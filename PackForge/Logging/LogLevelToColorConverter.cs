using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace PackForge.Logging;

public class LogLevelToColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string level) return Brushes.White;

        return level.ToUpperInvariant() switch
        {
            "FATAL" => Brushes.Red,
            "ERROR" => Brushes.OrangeRed,
            "WARNING" => Brushes.Orange,
            "DEBUG" => Brushes.Gray,
            _ => Brushes.White
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}