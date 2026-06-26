using System.Globalization;

namespace ServerRemote.App.Converters;

/// <summary>
/// Converts a fraction (0–1) into two star columns (filled / empty) so that a
/// BoxView can be filled proportionally by width (for progress bars).
/// </summary>
public sealed class FractionToColumnsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        double f = value switch
        {
            double d => d,
            int i => i,
            _ => 0
        };
        f = Math.Clamp(f, 0, 1);
        return new ColumnDefinitionCollection(
            new ColumnDefinition(new GridLength(f, GridUnitType.Star)),
            new ColumnDefinition(new GridLength(1 - f, GridUnitType.Star)));
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
