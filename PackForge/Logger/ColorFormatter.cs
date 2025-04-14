using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Serilog.Events;

namespace PackForge.Logger;

public class ColorFormatter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not LogEventLevel level) return Brushes.DarkGray;

        return level switch
        {
            LogEventLevel.Fatal => Brushes.Red,
            LogEventLevel.Error => Brushes.OrangeRed,
            LogEventLevel.Warning => Brushes.Orange,
            LogEventLevel.Information => Brushes.SkyBlue,
            LogEventLevel.Debug => Brushes.Gray,
            LogEventLevel.Verbose => Brushes.LimeGreen,

            _ => Brushes.White
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}