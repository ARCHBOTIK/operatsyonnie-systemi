using System.Security.Cryptography;
using System.Text;

namespace SecurePassword;

public class PasswordGenerator : IPasswordGenerator
{
    private const string LowercaseChars = "abcdefghijklmnopqrstuvwxyz";
    private const string UppercaseChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const string DigitChars = "0123456789";
    private const string SpecialChars = "!@#$%^&*()_-+=<>?";
    private const short DefaultPasswordLength = 15;

    public static string GeneratePassword(bool useLowercase, bool useUppercase, bool useDigits, bool useSpecial, byte passwordLength)
    {
        return GeneratePasswordInternal(useLowercase, useUppercase, useDigits, useSpecial, passwordLength);
    }

    public static string GeneratePassword(bool useLowercase, bool useUppercase, bool useDigits, bool useSpecial)
    {
        return GeneratePasswordInternal(useLowercase, useUppercase, useDigits, useSpecial, DefaultPasswordLength);
    }

    public static bool ValidatePassword(string password, bool useLowercase, bool useUppercase, bool useDigits, bool useSpecial)
    {
        bool hasLowercase = password.Any(char.IsLower);
        bool hasUppercase = password.Any(char.IsUpper);
        bool hasDigit = password.Any(char.IsDigit);
        bool hasSpecial = password.Any(ch => SpecialChars.Contains(ch));

        if (useLowercase && !hasLowercase)
            return false;
        if (useUppercase && !hasUppercase)
            return false;
        if (useDigits && !hasDigit)
            return false;
        if (useSpecial && !hasSpecial)
            return false;

        return true;
    }

    private static string GeneratePasswordInternal(bool useLowercase, bool useUppercase, bool useDigits, bool useSpecial, int passwordLength)
    {
        var enabledSets = new List<string>();

        if (useLowercase)
            enabledSets.Add(LowercaseChars);
        if (useUppercase)
            enabledSets.Add(UppercaseChars);
        if (useDigits)
            enabledSets.Add(DigitChars);
        if (useSpecial)
            enabledSets.Add(SpecialChars);

        if (enabledSets.Count == 0)
            throw new ArgumentException("Должен быть выбран хотя бы один тип символов");

        passwordLength = Math.Clamp(passwordLength, 4, 255);

        var allChars = string.Concat(enabledSets);
        var passwordChars = new List<char>(passwordLength);

        foreach (var charSet in enabledSets)
            passwordChars.Add(GetRandomChar(charSet));

        while (passwordChars.Count < passwordLength)
            passwordChars.Add(GetRandomChar(allChars));

        Shuffle(passwordChars);
        return new string(passwordChars.ToArray());
    }

    private static char GetRandomChar(string chars)
    {
        int index = RandomNumberGenerator.GetInt32(chars.Length);
        return chars[index];
    }

    private static void Shuffle(IList<char> chars)
    {
        for (int i = chars.Count - 1; i > 0; i--)
        {
            int swapIndex = RandomNumberGenerator.GetInt32(i + 1);
            (chars[i], chars[swapIndex]) = (chars[swapIndex], chars[i]);
        }
    }
}
