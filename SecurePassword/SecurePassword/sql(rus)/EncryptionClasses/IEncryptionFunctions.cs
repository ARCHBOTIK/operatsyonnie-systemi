using System;
using System.Text;
using System.Security.Cryptography;
using Konscious.Security.Cryptography;
namespace SecurePassword;

internal interface IEncryptionFunctions
{
    abstract public static byte[] GenerateSalt(int size = 16); //Функция для генерации соли, использует криптографический генератор, размер соли по умочанию - 16 байт
    abstract public static byte[] GenerateKEKwArgon2id(string password, byte[] salt, OSType SystemType, int keyLength = 32); //Функция для создания KEK
    abstract public static ArgonParameters GetArgonParameters(OSType type); //Функция взятия параметров для Argon2, чтобы удобнее было настраивать параметры в будущем
    abstract public static byte[] GenerateDEK(int keySize = 32); //Функция для генерации DEK
    abstract public static byte[] EncryptDEKwithGCM(byte[] dek, byte[] kek, out byte[] nonce, out byte[] tag, int DEKSize = 32); //Функция, где создаётся и шифруется DEK
    abstract public static byte[] DecryptDEK(byte[] kek, byte[] encryptedDEK); //Функция расшифровки DEK
    abstract public static byte[] PackAESGCMData(byte[] nonce, byte[] tag, byte[] ciphertext); //Объединение данных для шифровки DEK
    abstract public static void UnpackAESGCMData(byte[] pack, out byte[] nonce, out byte[] tag, out byte[] ciphertext); //Чтение шифрованного DEK 
    abstract public static byte[] EncryptData(byte[] dek, byte[] plaintext); //Та же самая функция для шифрования, только для данных произвольной длины. Может, объединю потом
    abstract public static byte[] DecryptData(byte[] dek, byte[] encryptedData); //Аналогично, функция для расшифровки данных произвольного размера с помощью AESGCM
}