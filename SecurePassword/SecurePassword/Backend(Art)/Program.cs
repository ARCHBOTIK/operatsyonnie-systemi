using System;
using System.IO;
using System.Linq;
using SkiaSharp;
public class ServiceImageGenerator
{
    public static byte[] GenerateServiceImage(string serviceName, int width = 200, int height = 200) //
    {
        if (string.IsNullOrWhiteSpace(serviceName))
            serviceName = "?";

        string displayText = GetInitials(serviceName);

        var (hash1, hash2) = GenerateTwoHashes(serviceName);

        SKColor color1 = HashToSkColor(hash1); //SKcolor - 32-битное значение цвета ARGB
        SKColor color2 = HashToSkColor(hash2);

        using (var surface = SKSurface.Create(new SKImageInfo(width, height))) // SKImageInfo описывает технические параметры изображения
        {
            var canvas = surface.Canvas;

            using (var shader = SKShader.CreateLinearGradient(
                new SKPoint(0, 0), // SKPoint представляет собой упорядоченную пару (X, Y) координат в двумерном пространстве
                new SKPoint(width, height), new[] { color1, color2 }, new float[] { 0, 1 }, SKShaderTileMode.Clamp))
            using (var paint = new SKPaint { Shader = shader })
            {
                canvas.DrawRect(new SKRect(0, 0, width, height), paint); // SKRect хранит набор из четырех чисел с плавающей запятой, представляющих верхний левый и нижний правый углы прямоугольника.
            }

            DrawTextCentered(canvas, displayText, width, height);

            using (var image = surface.Snapshot())
            using (var data = image.Encode(SKEncodedImageFormat.Png, 100))
            using (var stream = new MemoryStream()) // MemoryStream использует оперативную память в качестве хранилища вместо физического файла на диске
            {
                data.SaveTo(stream);
                return stream.ToArray();
            }
        }
    } // создает изображение с инициалами и градиентом

    public static void SaveServiceImage(string serviceName, string filePath, int width = 200, int height = 200)
    {
        byte[] imageBytes = GenerateServiceImage(serviceName, width, height);
        File.WriteAllBytes(filePath, imageBytes);
    } // сохранение в файл

    public static string GetInitials(string text)
    {
        string[] strings = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (strings.Length == 0)
            throw new ArgumentException("Название должно содержать символы, передаваемая строка не может быть пустой");
        return (strings.Length < 2) 
            ? strings[0][0].ToString().ToUpper() 
            : strings[0][0].ToString().ToUpper()+ strings[1][0].ToString().ToUpper();
    }
        var words = cleanText.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

        if (words.Length >= 2)
        {
            return $"{words[0][0]}{words[1][0]}".ToUpper();
        }

        return words[0].Length >= 2
            ? words[0].Substring(0, 2).ToUpper()
            : words[0].Substring(0, 1).ToUpper();
    } // Берет инициалы

    public static (int Hash1, int Hash2) GenerateTwoHashes(string input) 
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
    } // Генерирует два разных хеша из строки для цветов градиента

    public static SKColor HashToSkColor(int hash)
    {
        int r = (hash & 0xFF0000) >> 16;
        int g = (hash & 0x00FF00) >> 8;
        int b = hash & 0x0000FF;

        r = EnsureVibrantColor(r);
        g = EnsureVibrantColor(g);
        b = EnsureVibrantColor(b);

        return new SKColor((byte)r, (byte)g, (byte)b);
    }  // Преобразует хеш в цвет

    public static string ColorToHex(SKColor color)
    {
        return $"#{color.Red:X2}{color.Green:X2}{color.Blue:X2}";
    }  // Конвертирует цвет в HEX строку

    private static int EnsureVibrantColor(int component)
    {
        component = component % 256;

        if (component < 100)
        {
            component = component + 155;
        }

        if (component > 230)
        {
            component = component - 30;
        }

        return Math.Min(255, Math.Max(100, component));
    } // Делает цвет ярким и насыщенным

    private static void DrawTextCentered(SKCanvas canvas, string text, int width, int height)
    {
        if (string.IsNullOrEmpty(text)) return;

        int fontSize = Math.Min(width, height) / 3;

        using (var font = new SKFont(SKTypeface.FromFamilyName("Arial", SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright), fontSize))
        using (var textPaint = new SKPaint { Color = SKColors.White, IsAntialias = true })
        {
            var textBounds = new SKRect();
            font.MeasureText(text, out textBounds);

            float x = (width - textBounds.Width) / 2 - textBounds.Left;
            float y = (height - textBounds.Height) / 2 - textBounds.Top;

            canvas.DrawText(text, x, y, SKTextAlign.Left, font, textPaint);
        }
    } // Рисует текст

    public static (byte[] ImageBytes, SKColor Color1, SKColor Color2, string Hex1, string Hex2) GenerateWithColors(string serviceName, int width = 200, int height = 200)
    {
        var (hash1, hash2) = GenerateTwoHashes(serviceName);
        SKColor color1 = HashToSkColor(hash1);
        SKColor color2 = HashToSkColor(hash2);

        byte[] imageBytes = GenerateServiceImage(serviceName, width, height);

        return (imageBytes, color1, color2, ColorToHex(color1), ColorToHex(color2));
    } // Возвращает и картинку, и цвета
}

