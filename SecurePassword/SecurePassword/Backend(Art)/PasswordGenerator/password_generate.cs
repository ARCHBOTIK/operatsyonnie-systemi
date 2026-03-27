using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;


namespace SecurePassword;

public class PasswordGenerator: IPasswordGenerator
{
    private const string LowercaseChars = "abcdefghijklmnopqrstuvwxyz"; 
    private const string UppercaseChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const string DigitChars = "0123456789";
    private const string SpecialChars = "!@#$%^&*()_-+=<>?";
    private const short PasswordLength = 15;

    public static string GeneratePassword(bool useLowercase, bool useUppercase, bool useDigits, bool useSpecial, short passwordLength)
    {

        if (!useLowercase && !useUppercase && !useDigits && !useSpecial) 
            throw new ArgumentException("Должен быть выбран\n хотя бы один тип символов");

        StringBuilder charPool = new StringBuilder();

        // добавление только нужных символов к строке

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

        Random random = new Random(); // модуль генерации случайных чисел


        for (short i = 0; i < passwordLength; i++) // генерация пароля с указанием длины
        {
            int randomIndex = random.Next(availableChars.Length);
            password.Append(availableChars[randomIndex]);
        }

        return password.ToString();
    }
    public static string GeneratePassword(bool useLowercase, bool useUppercase, bool useDigits, bool useSpecial)
    {

        if (!useLowercase && !useUppercase && !useDigits && !useSpecial)
            throw new ArgumentException("Должен быть выбран хотя бы один тип символов");

        StringBuilder charPool = new StringBuilder();

        // добавление только нужных символов к строке

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

        Random random = new Random(); // модуль генерации случайных чисел


        for (short i = 0; i < PasswordLength; i++) // генерация пароля без указанием длины
        {
            int randomIndex = random.Next(availableChars.Length);
            password.Append(availableChars[randomIndex]);
        }

        return password.ToString();
    }

    public static bool ValidatePassword(string password,bool useLowercase,bool useUppercase,bool useDigits,bool useSpecial)
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
}