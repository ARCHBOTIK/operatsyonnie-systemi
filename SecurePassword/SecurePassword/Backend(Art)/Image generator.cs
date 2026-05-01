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

        return $"service-icons/{fileName}";
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

        string displayText = GetInitials(serviceName);
        var (color1, color2) = GenerateContrastingColors(serviceName);

        using var surface = SKSurface.Create(new SKImageInfo(width, height));
        var canvas = surface.Canvas;

        using (var shader = SKShader.CreateLinearGradient(
            new SKPoint(0, 0),
            new SKPoint(width, height),
            new[] { color1, color2 },
            new float[] { 0, 1 },
            SKShaderTileMode.Clamp))
        using (var paint = new SKPaint { Shader = shader })
        {
            canvas.DrawRect(new SKRect(0, 0, width, height), paint);
        }

        DrawTextCentered(canvas, displayText, width, height);

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = new MemoryStream();

        data.SaveTo(stream);
        return stream.ToArray();
    }

    private static string GetInitials(string text)
    {
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length == 0)
            return "?";

        return (parts.Length < 2)
            ? parts[0][0].ToString().ToUpper()
            : (parts[0][0].ToString() + parts[1][0]).ToUpper();
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
        if (string.IsNullOrEmpty(text)) return;

        float targetSize = Math.Min(width, height) * 0.8f;

        using var typeface = LoadTypeface();

        using var paint = new SKPaint
        {
            Color = SKColors.White,
            IsAntialias = true
        };

        float fontSize = targetSize;

        using var font = new SKFont(typeface, fontSize);

        var bounds = new SKRect();
        font.MeasureText(text, out bounds);

        float scale = targetSize / Math.Max(bounds.Width, bounds.Height);
        fontSize *= scale;

        using var finalFont = new SKFont(typeface, fontSize);

        float x = width / 2f;
        float y = height / 2f - bounds.MidY;

        // тень
        using var shadowPaint = new SKPaint
        {
            Color = SKColors.Black.WithAlpha(100),
            IsAntialias = true
        };

        canvas.DrawText(text, x + 2, y + 2, SKTextAlign.Center, finalFont, shadowPaint);
        canvas.DrawText(text, x, y, SKTextAlign.Center, finalFont, paint);
    }

    // --- Шрифты ---

    private static SKTypeface LoadTypeface()
    {
        string fontPath = Path.Combine(GetFontsDirectory(), "NotoSans-Bold.ttf");

        if (File.Exists(fontPath))
        {
            return SKTypeface.FromFile(fontPath);
        }

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
        foreach (char c in Path.GetInvalidFileNameChars())
        {
            fileName = fileName.Replace(c, '_');
        }
        return fileName;
    }
}
