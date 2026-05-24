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
    private string? _loadedPassword;

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
        _loadedPassword = password;
        KeyVersion++;

        SaveKeyFile();
    }

    public void LoadKeyFile(string password)
    {
        byte[] keyFileBytes = FileWorker.readFile(Path.GetFileName(_keyFilePath));
        ReadKeyFile(keyFileBytes, out _salt, out _encryptedDek, out var argonParameters);

        byte[] kek = EncryptionFunctions.GenerateKEKwArgon2id(password, _salt, argonParameters);
        _dek = EncryptionFunctions.DecryptDEK(kek, _encryptedDek);
        _loadedPassword = password;
        NormalizeKeyFileForCurrentPlatform(argonParameters, password);
        KeyVersion++;
    }

    public bool IsDekLoaded()
    {
        return _dek is { Length: > 0 };
    }

    public void ClearLoadedKey()
    {
        _dek = null;
        _loadedPassword = null;
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
        if (_dek is null || _dek.Length == 0 || _loadedPassword is null)
            throw new InvalidOperationException("DEK was not loaded. Call LoadKeyFile first.");

        return CreatePackedKeyFile(_dek, _loadedPassword, TransferArgonParameters);
    }

    public void ChangePassword(string newPassword)
    {
        if (_dek is null)
            throw new InvalidOperationException("DEK was not loaded.");

        _salt = EncryptionFunctions.GenerateSalt();
        byte[] kek = EncryptionFunctions.GenerateKEKwArgon2id(newPassword, _salt, GetPlatformType());
        _encryptedDek = EncryptionFunctions.EncryptDEKwithGCM(_dek, kek, out _, out _);
        _loadedPassword = newPassword;
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

    private void NormalizeKeyFileForCurrentPlatform(ArgonParameters loadedArgonParameters, string password)
    {
        ArgonParameters currentArgonParameters = EncryptionFunctions.GetArgonParameters(GetPlatformType());
        if (AreSameArgonParameters(loadedArgonParameters, currentArgonParameters))
            return;

        if (_dek is null || _dek.Length == 0)
            return;

        byte[] keyFileBytes = CreatePackedKeyFile(_dek, password, currentArgonParameters);
        FileWorker.writeFile(keyFileBytes, Path.GetFileName(_keyFilePath));
    }

    private static byte[] CreatePackedKeyFile(byte[] dek, string password, ArgonParameters argonParameters)
    {
        byte[] salt = EncryptionFunctions.GenerateSalt(16);
        byte[] kek = EncryptionFunctions.GenerateKEKwArgon2id(password, salt, argonParameters);
        byte[] encryptedDek = EncryptionFunctions.EncryptDEKwithGCM(dek, kek, out _, out _);
        return PackKeyFile(salt, encryptedDek, argonParameters);
    }

    private static bool AreSameArgonParameters(ArgonParameters left, ArgonParameters right)
    {
        return left.MemorySize == right.MemorySize &&
            left.Iterations == right.Iterations &&
            left.ParallelismDegree == right.ParallelismDegree;
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
