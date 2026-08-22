using System.Globalization;

namespace SecurePassword.Converters;

/// <summary>
/// Inverts a boolean value (true -> false, false -> true).
/// Useful for toggling IsPassword when an IsVisible flag is set.
/// </summary>
public class InverseBoolConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b)
            return !b;

        return true;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b)
            return !b;

        return true;
    }
}
