using System;
using System.IO;
using System.Linq;
using Microsoft.Maui.Storage;
using SkiaSharp;

public class ServiceImageGenerator
{
    public static void SaveServiceImage(string serviceName, string filePath, int width = 200, int height = 200)
    {
        byte[] imageBytes = GenerateServiceImage(serviceName, width, height);
        File.WriteAllBytes(filePath, imageBytes);
    }

    public static string GetServiceIconPath(string serviceName)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
            serviceName = "default";

        string iconsDirectory = GetIconsDirectory();
        Directory.CreateDirectory(iconsDirectory);

        string fileName = $"{SanitizeFileName(serviceName)}.png";
        string fullPath = Path.Combine(iconsDirectory, fileName);

        if (!File.Exists(fullPath))
            SaveServiceImage(serviceName, fullPath);

        return BuildWebRelativePath("service-icons", fileName);
    }

    public static (string IconPath, string Color1, string Color2) GetServiceIconWithColors(string serviceName)
    {
        var (color1, color2) = GenerateContrastingColors(serviceName);
        string iconPath = GetServiceIconPath(serviceName);
        return (iconPath, ColorToHex(color1), ColorToHex(color2));
    }

    private static byte[] GenerateServiceImage(string serviceName, int width = 200, int height = 200)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
            serviceName = "?";

        string displayText = GetDisplayLetters(serviceName);
        var (color1, color2) = GenerateContrastingColors(serviceName);

        var imageInfo = new SKImageInfo(width, height);
        using var surface = SKSurface.Create(imageInfo);
        var canvas = surface.Canvas;

        using var backgroundPaint = new SKPaint
        {
            IsAntialias = true,
            Shader = SKShader.CreateLinearGradient(
                new SKPoint(0, 0),
                new SKPoint(width, height),
                new[] { color1, color2 },
                null,
                SKShaderTileMode.Clamp)
        };

        canvas.Clear(SKColors.Transparent);
        canvas.DrawRect(new SKRect(0, 0, width, height), backgroundPaint);

        DrawTextCentered(canvas, displayText, width, height);

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static string GetDisplayLetters(string text)
    {
        string normalized = new string(text.Trim().Where(char.IsLetterOrDigit).ToArray());

        if (string.IsNullOrWhiteSpace(normalized))
            return "?";

        int lettersCount = Math.Clamp(normalized.Length, 2, 4);
        return normalized[..lettersCount].ToUpperInvariant();
    }

    private static (SKColor, SKColor) GenerateContrastingColors(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            input = "default";

        int hash = input.Aggregate(0, (acc, c) => acc * 31 + c);
        float baseHue = Math.Abs(hash % 360);
        float secondHue = (baseHue + 180) % 360;

        var color1 = SKColor.FromHsv(baseHue, 85, 85);
        var color2 = SKColor.FromHsv(secondHue, 90, 75);
        return (color1, color2);
    }

    private static string ColorToHex(SKColor color)
    {
        return $"#{color.Red:X2}{color.Green:X2}{color.Blue:X2}";
    }

    private static void DrawTextCentered(SKCanvas canvas, string text, int width, int height)
    {
        int fontSize = (int)(Math.Min(width, height) * 0.38f);

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

    private static string GetIconsDirectory()
    {
#if ANDROID || IOS
        return Path.Combine(FileSystem.AppDataDirectory, "service-icons");
#else
        return Path.Combine(AppContext.BaseDirectory, "wwwroot", "service-icons");
#endif
    }

    private static string SanitizeFileName(string fileName)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
            fileName = fileName.Replace(c, '_');

        return fileName;
    }

    private static SKTypeface GetPreferredTypeface()
    {
        return SKTypeface.FromFamilyName("Noto Sans", SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
            ?? SKTypeface.FromFamilyName("Segoe UI", SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
            ?? SKTypeface.FromFamilyName("Roboto", SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
            ?? SKTypeface.Default;
    }

    private static string BuildWebRelativePath(params string[] segments)
    {
        string normalizedPath = string.Join('/',
            segments.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim().Replace('\\', '/').Trim('/')));

        return $"/{normalizedPath}";
    }
}
