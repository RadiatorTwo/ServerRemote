using System.Globalization;

namespace ServerRemote.App.Converters;

/// <summary>Converts a percentage value (0-100) into a ProgressBar fraction (0-1).</summary>
public sealed class PercentToFractionConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        double pct = value switch
        {
            double d => d,
            int i => i,
            long l => l,
            _ => 0
        };
        return Math.Clamp(pct / 100.0, 0, 1);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
