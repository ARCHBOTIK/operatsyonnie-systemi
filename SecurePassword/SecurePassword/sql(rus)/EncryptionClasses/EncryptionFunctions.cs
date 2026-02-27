using System;
using System.Text;
using System.Security.Cryptography;
using Konscious.Security.Cryptography;
using System.Net.WebSockets;
namespace SecurePassword;

public class EncryptionFunctions : IEncryptionFunctions
{
    public static byte[] GenerateSalt(int size = 16) 
    {
        var salt = new byte[size];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(salt); //Использование криптографически устойчивого генератора
        }
        return salt;
    }

    public static byte[] GenerateKEKwArgon2id(string password, byte[] salt, OSType SystemType, int keyLength = 32)
    {
        var req = GetArgonParameters(SystemType); //В зависимости от типа системы мы получаем нужные параметры для алгоритма
        int memorySize = req.MemorySize;
        int iterations = req.Iterations;
        int parallelismDegree = req.ParallelismDegree;
        using (var argon2 = new Argon2id(Encoding.UTF8.GetBytes(password)))
        {
            argon2.Salt = salt; //Установка параметров в алгоритм шифрования
            argon2.MemorySize = memorySize;
            argon2.Iterations = iterations;
            argon2.DegreeOfParallelism = parallelismDegree;
            return argon2.GetBytes(keyLength); //Применение самого алгоритма
        }
    }

    public static ArgonParameters GetArgonParameters(OSType type)
    {
        return type switch
        {
            OSType.Windows => new ArgonParameters(262144, 3, 3), //Для виндоус испольуем 256 Мб, 3 итерации, 3 степень параллелизма
            OSType.Android => new ArgonParameters(32768, 5, 1), //Для андроид испольуем 32 Мб, 5 итераций, 1 степень параллелизма
            _ => throw new ArgumentOutOfRangeException() //Исключение, если выпало что-то другое
        };
    }

    public static byte[] GenerateDEK(int keySize = 32)
    {
        byte[] dek = new byte[keySize];
        using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(dek); //Да, реализация та же самая, что и у генератора соли, только другое значение. Я не уверен, подойдёт ли эта реализация и применю ли я эту функцию вообще
        }
        return dek;
    }

    public static byte[] EncryptDEKwithGCM(byte[] dek, byte[] kek, out byte[] nonce, out byte[] tag, int DEKsize = 32)
    {
        nonce = new byte[12]; //Вектор шифрования, который каждый раз должен обновляться при шифровке-расшивровке через AESGCM
        RandomNumberGenerator.Fill(nonce);
        byte[] ciphertext = new byte[dek.Length]; //Зашифрованный DEK
        tag = new byte[16]; //Тег аутентификации
        using (var aesGcm = new AesGcm(kek, kek.Length))
        {
            aesGcm.Encrypt(nonce, dek, ciphertext, tag); //Применение самого алгоритма шифрования
        }
        byte[] encryptedDEK = PackAESGCMData(nonce, tag, ciphertext); //Объединение данных в одну структуру чтобы не терять ее и шифровать как одно поле
        return encryptedDEK;
    }

    public static byte[] DecryptDEK(byte[] kek, byte[] encryptedDEK)
    {
        byte[] nonce = new byte[12];
        byte[] tag = new byte[16];
        byte[] ciphertext = new byte[encryptedDEK.Length - 12 - 16];
        UnpackAESGCMData(encryptedDEK, out nonce, out tag, out ciphertext); //Распаковка данных о зашифрованном DEK
        byte[] dek = new byte[encryptedDEK.Length - 12 - 16];
        using (var aesGcm = new AesGcm(kek, kek.Length))
        {
            aesGcm.Decrypt(nonce, ciphertext, tag, dek); //Расшифровка с учётом полученных данных
        }
        return dek;
    }

    public static byte[] PackAESGCMData(byte[] nonce, byte[] tag, byte[] ciphertext)
    {
        byte[] pack = new byte[nonce.Length + tag.Length + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, pack, 0, nonce.Length); //По сути просто записали данные подряд в одну байтовую строчку
        Buffer.BlockCopy(tag, 0, pack, nonce.Length, tag.Length); //Можно сделать и под динамические размеры, но для начала проще работать с фиксированными
        Buffer.BlockCopy(ciphertext, 0, pack, nonce.Length + tag.Length, ciphertext.Length); 
        return pack;
    }

    public static void UnpackAESGCMData(byte[] pack, out byte[] nonce, out byte[] tag, out byte[] ciphertext)
    {
        nonce = new byte[12];
        tag = new byte[16];
        ciphertext = new byte[pack.Length - 12 - 16];
        Buffer.BlockCopy(pack, 0, nonce, 0, 12); //Аналогично упаковке, просто читаем байты от нужного места до нужного
        Buffer.BlockCopy(pack, 12, tag, 0, 16);
        Buffer.BlockCopy(pack, 12 + 16, ciphertext, 0, ciphertext.Length);
    }

    public static byte[] EncryptData(byte[] dek, byte[] plaintext) //Та же самая функция для шифрования, только для данных произвольной длины. Может, объединю потом
    {
        byte[] dataNonce = RandomNumberGenerator.GetBytes(12);
        byte[] ciphertextData = new byte[plaintext.Length]; //Длина уже не обязательно 32 байта
        byte[] dataTag = new byte[16];
        using (var aes = new AesGcm(dek, dek.Length))
        {
            aes.Encrypt(dataNonce, plaintext, ciphertextData, dataTag);
        }
        byte[] encryptedData = PackAESGCMData(dataNonce, dataTag, ciphertextData);
        return encryptedData;
    }

    public static byte[] DecryptData(byte[] dek, byte[] encryptedData) //Аналогично, функция для расшифровки данных произвольного размера с помощью AESGCM
    {
        byte[] nonce = new byte[12];
        byte[] tag = new byte[16];
        byte[] ciphertext = new byte[encryptedData.Length - 12 - 16];
        UnpackAESGCMData(encryptedData, out nonce, out tag, out ciphertext);
        byte[] plaintext = new byte[ciphertext.Length];
        using (var aes = new AesGcm(dek, dek.Length))
        {
            aes.Decrypt(nonce, ciphertext, tag, plaintext);
        }
        return plaintext;
    }
}