using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

public class PasswordGenerator
{
    private const string LowercaseChars = "abcdefghijklmnopqrstuvwxyz";
    private const string UppercaseChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const string DigitChars = "0123456789";
    private const string SpecialChars = "!@#$%^&*()_-+=<>?";
    private const int PasswordLength = 15;

    public static string GeneratePassword(bool useLowercase, bool useUppercase, bool useDigits, bool useSpecial)
    {

        if (!useLowercase && !useUppercase && !useDigits && !useSpecial)
            throw new ArgumentException("Должен быть выбран хотя бы один тип символов");

        StringBuilder charPool = new StringBuilder();

        if (useLowercase)
            charPool.Append(LowercaseChars);
        if (useUppercase)
            charPool.Append(UppercaseChars);
        if (useDigits)
            charPool.Append(DigitChars);
        if (useSpecial)
            charPool.Append(SpecialChars);

        string availableChars = charPool.ToString();

        StringBuilder password = new StringBuilder();

        using (RNGCryptoServiceProvider rng = new RNGCryptoServiceProvider())
        {
            byte[] randomNumber = new byte[1];

            for (int i = 0; i < PasswordLength; i++)
            {
                rng.GetBytes(randomNumber);
                int randomIndex = randomNumber[0] % availableChars.Length;
                password.Append(availableChars[randomIndex]);
            }
        }

        if (!ValidatePassword(password.ToString(), useLowercase, useUppercase, useDigits, useSpecial))
        {
            return GeneratePassword(useLowercase, useUppercase, useDigits, useSpecial);
        }

        return password.ToString();
    }

    private static bool ValidatePassword(string password,
        bool useLowercase,
        bool useUppercase,
        bool useDigits,
        bool useSpecial)
    {
        bool hasLowercase = false;
        bool hasUppercase = false;
        bool hasDigit = false;
        bool hasSpecial = false;

        // Проходим по каждому символу пароля
        foreach (char c in password)
        {
            if (char.IsLower(c)) hasLowercase = true;
            else if (char.IsUpper(c)) hasUppercase = true;
            else if (char.IsDigit(c)) hasDigit = true;
            else if (SpecialChars.Contains(c)) hasSpecial = true;
        }

        // Проверяем результаты
        if (useLowercase && !hasLowercase) return false;
        if (useUppercase && !hasUppercase) return false;
        if (useDigits && !hasDigit) return false;
        if (useSpecial && !hasSpecial) return false;

        return true;
    }
}