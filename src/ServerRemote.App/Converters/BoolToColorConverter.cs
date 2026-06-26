using System.Globalization;

namespace ServerRemote.App.Converters;

/// <summary>true → Success (green), false → Danger (red). For the status indicator light.</summary>
public sealed class BoolToColorConverter : IValueConverter
{
    private static Color Resolve(string key, Color fallback) =>
        Application.Current?.Resources.TryGetValue(key, out var v) == true && v is Color c ? c : fallback;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true
            ? Resolve("Success", Color.FromArgb("#22C55E"))
            : Resolve("Danger", Color.FromArgb("#E11D48"));

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
