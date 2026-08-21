using System.IO;
using System.Security.Cryptography;

namespace SecurePassword;

public class keyManager
{
    private static readonly byte[] KeyFileMagic = [0x53, 0x50, 0x4B, 0x31];
    private static readonly ArgonParameters TransferArgonParameters =
        EncryptionFunctions.GetArgonParameters(OSType.Android);

    private readonly string _keyFilePath;
    private byte[]? _salt;
    private byte[]? _encryptedDek;
    private byte[]? _dek;

    public int KeyVersion { get; private set; }

    public keyManager(string keyFilePath)
    {
        _keyFilePath = keyFilePath;
    }

    public void CreateKeyFile(string password)
    {
        _dek = EncryptionFunctions.GenerateDEK(32);
        _salt = EncryptionFunctions.GenerateSalt(16);
        ArgonParameters targetParameters = EncryptionFunctions.GetArgonParameters(GetPlatformType());

        byte[] kek = EncryptionFunctions.GenerateKEKwArgon2id(password, _salt, targetParameters);
        try
        {
            _encryptedDek = EncryptionFunctions.EncryptDEKwithGCM(_dek, kek, out _, out _);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(kek);
        }

        KeyVersion++;
        SaveKeyFile();
    }

    public void LoadKeyFile(string password)
    {
        byte[] keyFileBytes = FileWorker.readFile(Path.GetFileName(_keyFilePath));
        ReadKeyFile(keyFileBytes, out byte[] loadedSalt, out byte[] loadedEncryptedDek, out ArgonParameters loadedParameters);

        byte[] kek = EncryptionFunctions.GenerateKEKwArgon2id(password, loadedSalt, loadedParameters);
        byte[]? decryptedDek = null;
        try
        {
            decryptedDek = EncryptionFunctions.DecryptDEK(kek, loadedEncryptedDek);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(kek);
        }

        if (_dek is not null)
        {
            CryptographicOperations.ZeroMemory(_dek);
        }

        _dek = decryptedDek;
        _salt = loadedSalt;
        _encryptedDek = loadedEncryptedDek;

        UpgradeKdfIfNeeded(loadedParameters, password);

        KeyVersion++;
    }

    public bool IsDekLoaded()
    {
        return _dek is { Length: > 0 };
    }

    public void ClearLoadedKey()
    {
        if (_dek is not null)
        {
            CryptographicOperations.ZeroMemory(_dek);
            _dek = null;
        }
        KeyVersion++;
    }

    public byte[] GetDEK()
    {
        if (_dek is null || _dek.Length == 0)
            throw new InvalidOperationException("DEK was not loaded. Call LoadKeyFile first.");

        return _dek;
    }

    public void ChangePassword(string newPassword)
    {
        if (_dek is null || _dek.Length == 0)
            throw new InvalidOperationException("DEK was not loaded.");

        ArgonParameters targetParameters = EncryptionFunctions.GetArgonParameters(GetPlatformType());
        byte[] newSalt = EncryptionFunctions.GenerateSalt(16);
        byte[] newKek = EncryptionFunctions.GenerateKEKwArgon2id(newPassword, newSalt, targetParameters);
        byte[] newEncryptedDek;
        try
        {
            newEncryptedDek = EncryptionFunctions.EncryptDEKwithGCM(_dek, newKek, out _, out _);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(newKek);
        }

        byte[] keyFileBytes = PackKeyFile(newSalt, newEncryptedDek, targetParameters);
        FileWorker.writeFile(keyFileBytes, Path.GetFileName(_keyFilePath));

        _salt = newSalt;
        _encryptedDek = newEncryptedDek;
        KeyVersion++;
    }

    public void replaceMasterPassword(string oldPassword, string newPassword)
    {
        LoadKeyFile(oldPassword);
        ChangePassword(newPassword);
    }

    private void SaveKeyFile()
    {
        if (_salt is null || _encryptedDek is null)
            throw new InvalidOperationException("Key data was not initialized.");

        byte[] keyFileBytes = PackKeyFile(
            _salt,
            _encryptedDek,
            EncryptionFunctions.GetArgonParameters(GetPlatformType()));
        FileWorker.writeFile(keyFileBytes, Path.GetFileName(_keyFilePath));
    }

    private void UpgradeKdfIfNeeded(ArgonParameters loadedArgonParameters, string password)
    {
        if (!EncryptionFunctions.NeedsKdfUpgrade(loadedArgonParameters))
            return;

        if (_dek is null || _dek.Length == 0)
            return;

        ArgonParameters targetParameters = EncryptionFunctions.GetArgonParameters(GetPlatformType());
        byte[] newSalt = EncryptionFunctions.GenerateSalt(16);
        byte[] newKek = EncryptionFunctions.GenerateKEKwArgon2id(password, newSalt, targetParameters);
        byte[] newEncryptedDek;
        try
        {
            newEncryptedDek = EncryptionFunctions.EncryptDEKwithGCM(_dek, newKek, out _, out _);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(newKek);
        }

        byte[] keyFileBytes = PackKeyFile(newSalt, newEncryptedDek, targetParameters);
        FileWorker.writeFile(keyFileBytes, Path.GetFileName(_keyFilePath));

        _salt = newSalt;
        _encryptedDek = newEncryptedDek;
    }

    public static ArgonParameters GetKeyFileParameters(byte[] keyFileBytes)
    {
        ReadKeyFile(keyFileBytes, out _, out _, out var parameters);
        return parameters;
    }

    public static byte[] CreatePackedKeyFile(byte[] dek, string password, ArgonParameters argonParameters)
    {
        byte[] salt = EncryptionFunctions.GenerateSalt(16);
        byte[] kek = EncryptionFunctions.GenerateKEKwArgon2id(password, salt, argonParameters);
        byte[] encryptedDek;
        try
        {
            encryptedDek = EncryptionFunctions.EncryptDEKwithGCM(dek, kek, out _, out _);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(kek);
        }
        return PackKeyFile(salt, encryptedDek, argonParameters);
    }

    public static byte[] PackKeyFile(byte[] salt, byte[] encryptedDek, ArgonParameters argonParameters)
    {
        using var memoryStream = new MemoryStream();
        using var writer = new BinaryWriter(memoryStream);

        writer.Write(KeyFileMagic);
        writer.Write(argonParameters.MemorySize);
        writer.Write(argonParameters.Iterations);
        writer.Write(argonParameters.ParallelismDegree);
        writer.Write(salt.Length);
        writer.Write(encryptedDek.Length);
        writer.Write(salt);
        writer.Write(encryptedDek);

        return memoryStream.ToArray();
    }

    public static void ReadKeyFile(byte[] keyFileBytes, out byte[] salt, out byte[] encryptedDek, out ArgonParameters argonParameters)
    {
        if (!HasPortableHeader(keyFileBytes))
        {
            ReadLegacyKeyFile(keyFileBytes, out salt, out encryptedDek);
            argonParameters = new ArgonParameters(2048, 2, 1);
            return;
        }

        using var memoryStream = new MemoryStream(keyFileBytes);
        using var reader = new BinaryReader(memoryStream);

        byte[] magic = reader.ReadBytes(KeyFileMagic.Length);
        if (!magic.SequenceEqual(KeyFileMagic))
            throw new InvalidDataException("Unsupported key file format.");

        int memorySize = reader.ReadInt32();
        int iterations = reader.ReadInt32();
        int parallelismDegree = reader.ReadInt32();
        int saltLength = reader.ReadInt32();
        int encryptedDekLength = reader.ReadInt32();

        if (saltLength <= 0 || encryptedDekLength <= 0 || memorySize <= 0 || iterations <= 0 || parallelismDegree <= 0)
            throw new InvalidDataException("Key file metadata is corrupted.");

        salt = reader.ReadBytes(saltLength);
        encryptedDek = reader.ReadBytes(encryptedDekLength);

        if (salt.Length != saltLength || encryptedDek.Length != encryptedDekLength)
            throw new InvalidDataException("Key file is truncated.");

        argonParameters = new ArgonParameters(memorySize, iterations, parallelismDegree);
    }

    private static void ReadLegacyKeyFile(byte[] keyFileBytes, out byte[] salt, out byte[] encryptedDek)
    {
        if (keyFileBytes.Length <= 16)
            throw new InvalidDataException("Key file is corrupted.");

        using var memoryStream = new MemoryStream(keyFileBytes);
        using var reader = new BinaryReader(memoryStream);

        salt = reader.ReadBytes(16);
        encryptedDek = reader.ReadBytes((int)(memoryStream.Length - memoryStream.Position));
    }

    public static bool HasPortableHeader(byte[] keyFileBytes)
    {
        return keyFileBytes.Length >= KeyFileMagic.Length &&
            keyFileBytes.Take(KeyFileMagic.Length).SequenceEqual(KeyFileMagic);
    }

    private static OSType GetPlatformType()
    {
#if ANDROID
        return OSType.Android;
#else
        return OSType.Windows;
#endif
    }
}
