using System.Globalization;

namespace SecurePassword.Converters;

/// <summary>
/// Converts a hex color string (e.g. "#19A38C") to a MAUI Color object.
/// </summary>
public class StringToColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string hex && !string.IsNullOrWhiteSpace(hex))
        {
            try
            {
                return Color.FromArgb(hex);
            }
            catch
            {
                return Colors.Gray;
            }
        }

        return Colors.Gray;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
