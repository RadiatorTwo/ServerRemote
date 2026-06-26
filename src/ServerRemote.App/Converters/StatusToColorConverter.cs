using System.Globalization;
using ServerRemote.App.Models;

namespace ServerRemote.App.Converters;

/// <summary>
/// Converts a <see cref="MetricStatus"/> or <see cref="DriveStatus"/> into a
/// status color (Success / Warning / Danger) from the central palette.
/// </summary>
public sealed class StatusToColorConverter : IValueConverter
{
    private static Color Resolve(string key, Color fallback) =>
        Application.Current?.Resources.TryGetValue(key, out var v) == true && v is Color c ? c : fallback;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var level = value switch
        {
            MetricStatus.Critical or DriveStatus.Critical => 2,
            MetricStatus.High or DriveStatus.Warning => 1,
            _ => 0
        };

        return level switch
        {
            2 => Resolve("Danger", Color.FromArgb("#E11D48")),
            1 => Resolve("Warning", Color.FromArgb("#F59E0B")),
            _ => Resolve("Success", Color.FromArgb("#22C55E"))
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
