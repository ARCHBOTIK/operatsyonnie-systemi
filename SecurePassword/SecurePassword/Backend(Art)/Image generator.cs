using System;
using System.IO;
using System.Linq;
using SkiaSharp;

public class ServiceImageGenerator
{
    // Публичный метод для сохранения иконки
    public static void SaveServiceImage(string serviceName, string filePath, int width = 200, int height = 200)
    {
        byte[] imageBytes = GenerateServiceImage(serviceName, width, height);
        File.WriteAllBytes(filePath, imageBytes);
    }

    // Публичный метод для получения пути к иконке (для UI)
    public static string GetServiceIconPath(string serviceName)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
            serviceName = "default";

        string iconsDirectory = Path.Combine(AppContext.BaseDirectory, "wwwroot", "service-icons");
        Directory.CreateDirectory(iconsDirectory);

        string fileName = $"{SanitizeFileName(serviceName)}.png";
        string fullPath = Path.Combine(iconsDirectory, fileName);

        if (!File.Exists(fullPath))
        {
            SaveServiceImage(serviceName, fullPath);
        }

        return BuildWebRelativePath("service-icons", fileName);
    }

    // Публичный метод для получения иконки и цветов
    public static (string IconPath, string Color1, string Color2) GetServiceIconWithColors(string serviceName)
    {
        var (hash1, hash2) = GenerateTwoHashes(serviceName);
        SKColor color1 = HashToSkColor(hash1);
        SKColor color2 = HashToSkColor(hash2);

        string iconPath = GetServiceIconPath(serviceName);
        
        return (iconPath, ColorToHex(color1), ColorToHex(color2));
    }

    // Приватные методы (были публичными, теперь приватные)
    private static byte[] GenerateServiceImage(string serviceName, int width = 200, int height = 200)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
            serviceName = "?";

        string displayText = GetDisplayLetters(serviceName);

        var (hash1, hash2) = GenerateTwoHashes(serviceName);

        SKColor color1 = HashToSkColor(hash1);
        SKColor color2 = HashToSkColor(hash2);

        using (var surface = SKSurface.Create(new SKImageInfo(width, height)))
        {
            var canvas = surface.Canvas;

            using (var shader = SKShader.CreateLinearGradient(
                new SKPoint(0, 0),
                new SKPoint(width, height), new[] { color1, color2 }, new float[] { 0, 1 }, SKShaderTileMode.Clamp))
            using (var paint = new SKPaint { Shader = shader })
            {
                canvas.DrawRect(new SKRect(0, 0, width, height), paint);
            }

            DrawTextCentered(canvas, displayText, width, height);

            using (var image = surface.Snapshot())
            using (var data = image.Encode(SKEncodedImageFormat.Png, 100))
            using (var stream = new MemoryStream())
            {
                data.SaveTo(stream);
                return stream.ToArray();
            }
        }
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

    private static (int Hash1, int Hash2) GenerateTwoHashes(string input)
    {
        if (string.IsNullOrEmpty(input))
            input = "default";

        int hash1 = 17;
        int hash2 = 23;

        for (int i = 0; i < input.Length; i++)
        {
            char c = input[i];
            hash1 = hash1 * 31 + c;
            hash2 = hash2 * 37 + c;
        }

        hash1 = hash1 ^ (hash1 >> 16);
        hash2 = hash2 ^ (hash2 >> 16);

        return (Math.Abs(hash1), Math.Abs(hash2));
    }

    private static SKColor HashToSkColor(int hash)
    {
        int r = (hash & 0xFF0000) >> 16;
        int g = (hash & 0x00FF00) >> 8;
        int b = hash & 0x0000FF;

        r = EnsureVibrantColor(r);
        g = EnsureVibrantColor(g);
        b = EnsureVibrantColor(b);

        return new SKColor((byte)r, (byte)g, (byte)b);
    }

    private static string ColorToHex(SKColor color)
    {
        return $"#{color.Red:X2}{color.Green:X2}{color.Blue:X2}";
    }

    private static int EnsureVibrantColor(int component)
    {
        component = Math.Abs(component % 256);

        if (component < 70)
            component += 130;

        if (component > 210)
            component -= 45;

        return Math.Min(220, Math.Max(60, component));
    }

    private static void DrawTextCentered(SKCanvas canvas, string text, int width, int height)
    {
        if (string.IsNullOrEmpty(text)) return;

        int fontSize = (int)(Math.Min(width, height) * 0.8f);

        using (var font = new SKFont(GetPreferredTypeface(), fontSize))
        using (var textPaint = new SKPaint { Color = SKColors.White, IsAntialias = true, FakeBoldText = true })
        {
            var textBounds = new SKRect();
            font.MeasureText(text, out textBounds);

            float x = (width - textBounds.Width) / 2 - textBounds.Left;
            float y = (height - textBounds.Height) / 2 - textBounds.Top;

            canvas.DrawText(text, x, y, SKTextAlign.Left, font, textPaint);
        }
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
