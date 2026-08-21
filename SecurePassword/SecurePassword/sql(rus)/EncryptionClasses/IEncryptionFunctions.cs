using System;
using System.Text;
using System.Security.Cryptography;
using Konscious.Security.Cryptography;
namespace SecurePassword;

internal interface IEncryptionFunctions
{
    abstract public static byte[] GenerateSalt(int size = 16);
    abstract public static byte[] GenerateKEKwArgon2id(string password, byte[] salt, OSType SystemType, int keyLength = 32);
    abstract public static ArgonParameters GetArgonParameters(OSType type);
    abstract public static bool NeedsKdfUpgrade(ArgonParameters parameters);
    abstract public static byte[] GenerateDEK(int keySize = 32);
    abstract public static byte[] EncryptDEKwithGCM(byte[] dek, byte[] kek, out byte[] nonce, out byte[] tag, int DEKSize = 32);
    abstract public static byte[] DecryptDEK(byte[] kek, byte[] encryptedDEK);
    abstract public static byte[] PackAESGCMData(byte[] nonce, byte[] tag, byte[] ciphertext);
    abstract public static void UnpackAESGCMData(byte[] pack, out byte[] nonce, out byte[] tag, out byte[] ciphertext);
    abstract public static byte[] EncryptData(byte[] dek, byte[] plaintext);
    abstract public static byte[] DecryptData(byte[] dek, byte[] encryptedData);
}