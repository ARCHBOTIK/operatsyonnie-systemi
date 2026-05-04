namespace SecurePassword;

public class keyManager
{
    private static readonly byte[] KeyFileMagic = [0x53, 0x50, 0x4B, 0x31]; // SPK1

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

        byte[] kek = EncryptionFunctions.GenerateKEKwArgon2id(password, _salt, GetPlatformType());
        _encryptedDek = EncryptionFunctions.EncryptDEKwithGCM(_dek, kek, out _, out _);
        KeyVersion++;

        SaveKeyFile();
    }

    public void LoadKeyFile(string password)
    {
        byte[] keyFileBytes = FileWorker.readFile(Path.GetFileName(_keyFilePath));
        ReadKeyFile(keyFileBytes, out _salt, out _encryptedDek, out var argonParameters);

        byte[] kek = EncryptionFunctions.GenerateKEKwArgon2id(password, _salt, argonParameters);
        _dek = EncryptionFunctions.DecryptDEK(kek, _encryptedDek);
        KeyVersion++;
    }

    public bool IsDekLoaded()
    {
        return _dek is { Length: > 0 };
    }

    public void ClearLoadedKey()
    {
        _dek = null;
        KeyVersion++;
    }

    public byte[] GetDEK()
    {
        if (_dek is null || _dek.Length == 0)
            throw new InvalidOperationException("DEK was not loaded. Call LoadKeyFile first.");

        return _dek;
    }

    public byte[] ExportKeyFileForTransfer()
    {
        byte[] keyFileBytes = FileWorker.readFile(Path.GetFileName(_keyFilePath));
        if (HasPortableHeader(keyFileBytes))
            return keyFileBytes;

        ReadLegacyKeyFile(keyFileBytes, out var salt, out var encryptedDek);
        return PackKeyFile(salt, encryptedDek, EncryptionFunctions.GetArgonParameters(GetPlatformType()));
    }

    public void ChangePassword(string newPassword)
    {
        if (_dek is null)
            throw new InvalidOperationException("DEK was not loaded.");

        _salt = EncryptionFunctions.GenerateSalt();
        byte[] kek = EncryptionFunctions.GenerateKEKwArgon2id(newPassword, _salt, GetPlatformType());
        _encryptedDek = EncryptionFunctions.EncryptDEKwithGCM(_dek, kek, out _, out _);
        SaveKeyFile();
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

    private static void ReadKeyFile(byte[] keyFileBytes, out byte[] salt, out byte[] encryptedDek, out ArgonParameters argonParameters)
    {
        if (!HasPortableHeader(keyFileBytes))
        {
            ReadLegacyKeyFile(keyFileBytes, out salt, out encryptedDek);
            argonParameters = EncryptionFunctions.GetArgonParameters(GetPlatformType());
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

        if (saltLength <= 0 || encryptedDekLength <= 0)
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

    private static byte[] PackKeyFile(byte[] salt, byte[] encryptedDek, ArgonParameters argonParameters)
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

    private static bool HasPortableHeader(byte[] keyFileBytes)
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
