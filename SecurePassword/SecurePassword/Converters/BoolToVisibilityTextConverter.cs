using System.Globalization;

namespace SecurePassword.Converters;

/// <summary>
/// Converts a boolean password visibility flag into text ("Скрыть" when visible, "Показать" when hidden).
/// </summary>
public class BoolToVisibilityTextConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isVisible && isVisible)
            return "Скрыть";

        return "Показать";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
