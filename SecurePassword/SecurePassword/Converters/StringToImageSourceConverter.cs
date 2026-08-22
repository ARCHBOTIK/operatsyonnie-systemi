using System.Globalization;

namespace SecurePassword.Converters;

/// <summary>
/// Converts an icon URI string, file name, or base64 data URI (from ServiceImageGenerator)
/// into a native MAUI <see cref="ImageSource"/>.
/// </summary>
public sealed class StringToImageSourceConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string source || string.IsNullOrWhiteSpace(source))
            return null;

        // Base64 Data URL (e.g. data:image/png;base64,iVBORw0KGgo...)
        if (source.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
        {
            int commaIndex = source.IndexOf(',', StringComparison.Ordinal);
            if (commaIndex >= 0 && commaIndex < source.Length - 1)
            {
                try
                {
                    string base64 = source[(commaIndex + 1)..];
                    byte[] bytes = System.Convert.FromBase64String(base64);
                    return ImageSource.FromStream(() => new MemoryStream(bytes));
                }
                catch
                {
                    return null;
                }
            }
        }

        // Web icon path mappings to MAUI Resources/Images
        string normalized = source.Trim().ToLowerInvariant();
        if (normalized.Contains("login")) return ImageSource.FromFile("icon_login.png");
        if (normalized.Contains("card")) return ImageSource.FromFile("icon_card.png");
        if (normalized.Contains("note")) return ImageSource.FromFile("icon_note.png");
        if (normalized.Contains("search")) return ImageSource.FromFile("icon_search.png");
        if (normalized.Contains("plus")) return ImageSource.FromFile("icon_plus.png");
        if (normalized.Contains("sort-asc") || normalized.Contains("sort_asc")) return ImageSource.FromFile("icon_sort_asc.png");
        if (normalized.Contains("sort-desc") || normalized.Contains("sort_desc")) return ImageSource.FromFile("icon_sort_desc.png");
        if (normalized.Contains("sort-type") || normalized.Contains("sort_type")) return ImageSource.FromFile("icon_sort_type.png");
        if (normalized.Contains("close")) return ImageSource.FromFile("icon_close.png");
        if (normalized.Contains("copy")) return ImageSource.FromFile("icon_copy.png");
        if (normalized.Contains("eye-off") || normalized.Contains("eye_off")) return ImageSource.FromFile("icon_eye_off.png");
        if (normalized.Contains("eye")) return ImageSource.FromFile("icon_eye.png");
        if (normalized.Contains("edit")) return ImageSource.FromFile("icon_edit.png");
        if (normalized.Contains("delete")) return ImageSource.FromFile("icon_delete.png");

        return ImageSource.FromFile(source);

    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
