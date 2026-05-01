using System;
using System.IO;
using System.Linq;
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
        {
            SaveServiceImage(serviceName, fullPath);
        }

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

        DrawTextCentered(canvas, displayText, width, height);

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = new MemoryStream();

        data.SaveTo(stream);
        return stream.ToArray();
    }

    private static string GetDisplayLetters(string text)
    {
        string normalized = new string(text
            .Trim()
            .Where(char.IsLetterOrDigit)
            .ToArray());

        if (string.IsNullOrWhiteSpace(normalized))
            return "?";

        int lettersCount = Math.Clamp(normalized.Length, 2, 4);
        return normalized[..lettersCount].ToUpperInvariant();
    }

    // --- Цвета (контраст через HSV) ---

    private static (SKColor, SKColor) GenerateContrastingColors(string input)
    {
        if (string.IsNullOrEmpty(input))
            input = "default";

        int hash = input.Aggregate(0, (acc, c) => acc * 31 + c);

        float baseHue = Math.Abs(hash % 360);
        float secondHue = (baseHue + 180) % 360;

        var color1 = SKColor.FromHsv(baseHue, 90, 90);
        var color2 = SKColor.FromHsv(secondHue, 90, 90);

        return (color1, color2);
    }

    private static string ColorToHex(SKColor color)
    {
        return $"#{color.Red:X2}{color.Green:X2}{color.Blue:X2}";
    }

    // --- Рендер текста (80% площади) ---

    private static void DrawTextCentered(SKCanvas canvas, string text, int width, int height)
    {
        component = Math.Abs(component % 256);

        if (component < 70)
            component += 130;

        if (component > 210)
            component -= 45;

        return Math.Min(220, Math.Max(60, component));
    }

    // --- Шрифты ---

        int fontSize = (int)(Math.Min(width, height) * 0.8f);

        using (var font = new SKFont(GetPreferredTypeface(), fontSize))
        using (var textPaint = new SKPaint { Color = SKColors.White, IsAntialias = true, FakeBoldText = true })
        {
            var textBounds = new SKRect();
            font.MeasureText(text, out textBounds);

        return SKTypeface.FromFamilyName("sans-serif", SKFontStyle.Bold);
    }

    private static string GetFontsDirectory()
    {
#if ANDROID || IOS
        return FileSystem.AppDataDirectory;
#else
        return Path.Combine(AppContext.BaseDirectory, "wwwroot", "fonts");
#endif
    }

    // --- Пути ---

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
        var invalidChars = Path.GetInvalidFileNameChars();
        foreach (char c in invalidChars)
        {
            fileName = fileName.Replace(c, '_');
        }
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
            segments
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim().Replace('\\', '/').Trim('/')));

        return $"/{normalizedPath}";
    }
}
