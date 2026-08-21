using System;
using System.Text;
using System.Security.Cryptography;
using Konscious.Security.Cryptography;
using System.Net.WebSockets;
namespace SecurePassword;

public class EncryptionFunctions : IEncryptionFunctions
{
    public const int MinimumSafeMemorySize = 65536; // 64 MB
    public const int MinimumSafeIterations = 3;

    public static byte[] GenerateSalt(int size = 16) 
    {
        var salt = new byte[size];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(salt);
        }
        return salt;
    }

    public static byte[] GenerateKEKwArgon2id(string password, byte[] salt, OSType SystemType, int keyLength = 32)
    {
        var req = GetArgonParameters(SystemType);
        return GenerateKEKwArgon2id(password, salt, req, keyLength);
    }

    public static byte[] GenerateKEKwArgon2id(string password, byte[] salt, ArgonParameters parameters, int keyLength = 32)
    {
        byte[] passwordBytes = Encoding.UTF8.GetBytes(password);
        try
        {
            using var argon2 = new Argon2id(passwordBytes)
            {
                Salt = salt,
                MemorySize = parameters.MemorySize,
                Iterations = parameters.Iterations,
                DegreeOfParallelism = parameters.ParallelismDegree
            };
            return argon2.GetBytes(keyLength);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
        }
    }

    public static ArgonParameters GetArgonParameters(OSType type)
    {
        return type switch
        {
            OSType.Windows => new ArgonParameters(262144, 3, 3), // 256 MB, 3 iter, 3 lanes
            OSType.Android => new ArgonParameters(65536, 3, 2),  // 64 MB, 3 iter, 2 lanes
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unsupported OS type.")
        };
    }

    public static bool NeedsKdfUpgrade(ArgonParameters parameters)
    {
        return parameters.MemorySize < MinimumSafeMemorySize || parameters.Iterations < MinimumSafeIterations;
    }

    public static byte[] GenerateDEK(int keySize = 32)
    {
        byte[] dek = new byte[keySize];
        using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(dek);
        }
        return dek;
    }

    public static byte[] EncryptDEKwithGCM(byte[] dek, byte[] kek, out byte[] nonce, out byte[] tag, int DEKsize = 32)
    {
        nonce = new byte[12];
        RandomNumberGenerator.Fill(nonce);
        byte[] ciphertext = new byte[dek.Length];
        tag = new byte[16];
        using (var aesGcm = new AesGcm(kek, tag.Length))
        {
            aesGcm.Encrypt(nonce, dek, ciphertext, tag);
        }
        byte[] encryptedDEK = PackAESGCMData(nonce, tag, ciphertext);
        return encryptedDEK;
    }

    public static byte[] DecryptDEK(byte[] kek, byte[] encryptedDEK)
    {
        byte[] nonce = new byte[12];
        byte[] tag = new byte[16];
        byte[] ciphertext = new byte[encryptedDEK.Length - 12 - 16];
        UnpackAESGCMData(encryptedDEK, out nonce, out tag, out ciphertext);
        byte[] dek = new byte[encryptedDEK.Length - 12 - 16];
        using (var aesGcm = new AesGcm(kek, tag.Length))
        {
            aesGcm.Decrypt(nonce, ciphertext, tag, dek);
        }
        return dek;
    }

    public static byte[] PackAESGCMData(byte[] nonce, byte[] tag, byte[] ciphertext)
    {
        byte[] pack = new byte[nonce.Length + tag.Length + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, pack, 0, nonce.Length);
        Buffer.BlockCopy(tag, 0, pack, nonce.Length, tag.Length);
        Buffer.BlockCopy(ciphertext, 0, pack, nonce.Length + tag.Length, ciphertext.Length); 
        return pack;
    }

    public static void UnpackAESGCMData(byte[] pack, out byte[] nonce, out byte[] tag, out byte[] ciphertext)
    {
        nonce = new byte[12];
        tag = new byte[16];
        ciphertext = new byte[pack.Length - 12 - 16];
        Buffer.BlockCopy(pack, 0, nonce, 0, 12);
        Buffer.BlockCopy(pack, 12, tag, 0, 16);
        Buffer.BlockCopy(pack, 12 + 16, ciphertext, 0, ciphertext.Length);
    }

    public static byte[] EncryptData(byte[] dek, byte[] plaintext)
    {
        byte[] dataNonce = RandomNumberGenerator.GetBytes(12);
        byte[] ciphertextData = new byte[plaintext.Length];
        byte[] dataTag = new byte[16];
        using (var aes = new AesGcm(dek, dataTag.Length))
        {
            aes.Encrypt(dataNonce, plaintext, ciphertextData, dataTag);
        }
        byte[] encryptedData = PackAESGCMData(dataNonce, dataTag, ciphertextData);
        return encryptedData;
    }

    public static byte[] DecryptData(byte[] dek, byte[] encryptedData)
    {
        byte[] nonce = new byte[12];
        byte[] tag = new byte[16];
        byte[] ciphertext = new byte[encryptedData.Length - 12 - 16];
        UnpackAESGCMData(encryptedData, out nonce, out tag, out ciphertext);
        byte[] plaintext = new byte[ciphertext.Length];
        using (var aes = new AesGcm(dek, tag.Length))
        {
            aes.Decrypt(nonce, ciphertext, tag, plaintext);
        }
        return plaintext;
    }
}