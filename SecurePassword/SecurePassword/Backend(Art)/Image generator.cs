using SkiaSharp;

namespace SecurePassword;

public static class ServiceImageGenerator
{
    public static string GetServiceIconPath(string? serviceName)
    {
        return GetServiceIconSource(serviceName);
    }

    public static string GetServiceIconSource(string? serviceName, string? fallbackText = null)
    {
        string key = BuildLookupValue(serviceName, fallbackText);
        string displayText = GetDisplayLetters(string.IsNullOrWhiteSpace(serviceName) ? fallbackText ?? "?" : serviceName);
        byte[] imageBytes = GenerateServiceImage(displayText, key, 200, 200);
        return $"data:image/png;base64,{Convert.ToBase64String(imageBytes)}";
    }

    public static (string IconPath, string Color1, string Color2) GetServiceIconWithColors(string? serviceName, string? fallbackText = null)
    {
        string key = BuildLookupValue(serviceName, fallbackText);
        var (color1, color2) = GenerateContrastingColors(key);
        string iconPath = GetServiceIconSource(serviceName, fallbackText);
        return (iconPath, ColorToHex(color1), ColorToHex(color2));
    }

    private static byte[] GenerateServiceImage(string displayText, string colorSeed, int width, int height)
    {
        var (color1, color2) = GenerateContrastingColors(colorSeed);
        var imageInfo = new SKImageInfo(width, height);

        using var surface = SKSurface.Create(imageInfo);
        var canvas = surface.Canvas;

        using var backgroundPaint = new SKPaint
        {
            IsAntialias = true,
            Shader = SKShader.CreateLinearGradient(
                new SKPoint(0, 0),
                new SKPoint(width, height),
                [color1, color2],
                null,
                SKShaderTileMode.Clamp)
        };

        canvas.Clear(SKColors.Transparent);
        canvas.DrawRoundRect(new SKRoundRect(new SKRect(0, 0, width, height), 48, 48), backgroundPaint);
        DrawTextCentered(canvas, displayText, width, height);

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static string GetDisplayLetters(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "?";

        var parts = text
            .Split([' ', '-', '_', '.', '@'], StringSplitOptions.RemoveEmptyEntries)
            .Select(part => new string(part.Where(char.IsLetterOrDigit).ToArray()))
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .ToArray();

        if (parts.Length >= 2)
            return $"{char.ToUpperInvariant(parts[0][0])}{char.ToUpperInvariant(parts[1][0])}";

        string normalized = parts.FirstOrDefault() ?? new string(text.Where(char.IsLetterOrDigit).ToArray());
        if (string.IsNullOrWhiteSpace(normalized))
            return "?";

        return new string(normalized
            .Take(2)
            .Select(char.ToUpperInvariant)
            .ToArray());
    }

    private static (SKColor, SKColor) GenerateContrastingColors(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            input = "default";

        int hash = input.Aggregate(17, (acc, c) => unchecked(acc * 31 + c));
        float baseHue = Math.Abs(hash % 360);
        float secondHue = (baseHue + 46 + Math.Abs(hash % 90)) % 360;

        var color1 = SKColor.FromHsv(baseHue, 72, 88);
        var color2 = SKColor.FromHsv(secondHue, 88, 70);
        return (color1, color2);
    }

    private static void DrawTextCentered(SKCanvas canvas, string text, int width, int height)
    {
        int fontSize = text.Length > 1
            ? (int)(Math.Min(width, height) * 0.34f)
            : (int)(Math.Min(width, height) * 0.52f);

        using var font = new SKFont(GetPreferredTypeface(), fontSize);
        using var textPaint = new SKPaint
        {
            Color = SKColors.White,
            IsAntialias = true,
            FakeBoldText = true
        };

        SKRect bounds = default;
        float textWidth = font.MeasureText(text, out bounds, textPaint);
        float x = (width - textWidth) / 2f;
        float y = (height - bounds.Height) / 2f - bounds.Top;

        canvas.DrawText(text, x, y, font, textPaint);
    }

    private static string BuildLookupValue(string? serviceName, string? fallbackText)
    {
        if (!string.IsNullOrWhiteSpace(serviceName))
            return serviceName.Trim();

        if (!string.IsNullOrWhiteSpace(fallbackText))
            return fallbackText.Trim();

        return "default";
    }

    private static string ColorToHex(SKColor color)
    {
        return $"#{color.Red:X2}{color.Green:X2}{color.Blue:X2}";
    }

    private static SKTypeface GetPreferredTypeface()
    {
        return SKTypeface.FromFamilyName("Noto Sans", SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
            ?? SKTypeface.FromFamilyName("Segoe UI", SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
            ?? SKTypeface.FromFamilyName("Roboto", SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
            ?? SKTypeface.Default;
    }
}
