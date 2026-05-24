using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SecurePassword;

internal interface IPasswordGenerator
{
        abstract public static string GeneratePassword(bool useLowercase, bool useUppercase, bool useDigits, bool useSpecial, byte passwordLength);
        abstract public static string GeneratePassword(bool useLowercase, bool useUppercase, bool useDigits, bool useSpecial);
        abstract public static bool ValidatePassword(string password,bool useLowercase,bool useUppercase,bool useDigits,bool useSpecial);

}
