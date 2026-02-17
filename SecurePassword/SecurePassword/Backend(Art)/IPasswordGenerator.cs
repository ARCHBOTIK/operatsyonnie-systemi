using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SecurePassword;

internal interface IPasswordGenerator
    {
        abstract public static string GeneratePassword(bool useLowercase, bool useUppercase, bool useDigits, bool useSpecial, short passwordLength); // генерация пароля с указанием длины
        abstract public static string GeneratePassword(bool useLowercase, bool useUppercase, bool useDigits, bool useSpecial); // генерация пароля без указания длины
    abstract public static bool ValidatePassword(string password,bool useLowercase,bool useUppercase,bool useDigits,bool useSpecial); // валидация пароля
    }
