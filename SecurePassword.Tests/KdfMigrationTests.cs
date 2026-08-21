using System.Security.Cryptography;
using System.Text;
using SecurePassword;
using Xunit;

namespace SecurePassword.Tests;

public class KdfMigrationTests : IDisposable
{
    private readonly string _testKeyFileName;
    private readonly string _testKeyFilePath;

    public KdfMigrationTests()
    {
        _testKeyFileName = $"test_keys_{Guid.NewGuid():N}.dat";
        _testKeyFilePath = Path.Combine(FileWorker.GetAppDataDirectory(), _testKeyFileName);
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(_testKeyFilePath))
                File.Delete(_testKeyFilePath);
        }
        catch
        {
        }
    }

    [Fact]
    public void Test01_CreateKeyFile_UsesTargetPlatformParameters()
    {
        var manager = new keyManager(_testKeyFilePath);
        string password = "StrongMasterPassword123!";

        manager.CreateKeyFile(password);

        Assert.True(manager.IsDekLoaded());
        byte[] dek = manager.GetDEK();
        Assert.NotNull(dek);
        Assert.Equal(32, dek.Length);

        byte[] keyFileBytes = FileWorker.readFile(_testKeyFileName);
        ArgonParameters parameters = keyManager.GetKeyFileParameters(keyFileBytes);

        // Parameters must be at least the safe baseline (64 MB, 3 iterations)
        Assert.True(parameters.MemorySize >= 65536, $"MemorySize was {parameters.MemorySize}");
        Assert.True(parameters.Iterations >= 3, $"Iterations was {parameters.Iterations}");
        Assert.True(parameters.ParallelismDegree >= 2, $"Parallelism was {parameters.ParallelismDegree}");
    }

    [Fact]
    public void Test02_NeedsKdfUpgrade_CorrectlyIdentifiesWeakAndStrongParameters()
    {
        // Weak parameters (should need upgrade)
        var oldAndroid = new ArgonParameters(2048, 2, 1);
        var lowMemory = new ArgonParameters(32768, 3, 2);
        var lowIterations = new ArgonParameters(65536, 2, 2);

        Assert.True(EncryptionFunctions.NeedsKdfUpgrade(oldAndroid));
        Assert.True(EncryptionFunctions.NeedsKdfUpgrade(lowMemory));
        Assert.True(EncryptionFunctions.NeedsKdfUpgrade(lowIterations));

        // Strong parameters (should NOT need upgrade)
        var newAndroid = new ArgonParameters(65536, 3, 2);
        var windows = new ArgonParameters(262144, 3, 3);
        var extraStrong = new ArgonParameters(524288, 4, 4);

        Assert.False(EncryptionFunctions.NeedsKdfUpgrade(newAndroid));
        Assert.False(EncryptionFunctions.NeedsKdfUpgrade(windows));
        Assert.False(EncryptionFunctions.NeedsKdfUpgrade(extraStrong));
    }

    [Fact]
    public void Test03_LoadKeyFile_OldAndroidKeyFile_SuccessfullyDecryptedAndUpgraded()
    {
        string password = "MySecretMasterPassword_2026";
        byte[] originalDek = EncryptionFunctions.GenerateDEK(32);

        // Create an old Android key file with weak params: 2048 KB, 2 iter, 1 lane
        var oldParams = new ArgonParameters(2048, 2, 1);
        byte[] oldPackedKeyFile = keyManager.CreatePackedKeyFile(originalDek, password, oldParams);
        FileWorker.writeFile(oldPackedKeyFile, _testKeyFileName);

        // Encrypt test payload with original DEK to verify user data stays decryptable
        byte[] plaintextData = Encoding.UTF8.GetBytes("Secret user credentials created before migration");
        byte[] encryptedData = EncryptionFunctions.EncryptData(originalDek, plaintextData);

        // Verify file currently has old weak parameters
        ArgonParameters beforeLoadParams = keyManager.GetKeyFileParameters(FileWorker.readFile(_testKeyFileName));
        Assert.Equal(2048, beforeLoadParams.MemorySize);
        Assert.Equal(2, beforeLoadParams.Iterations);

        // Now load the key file with keyManager
        var manager = new keyManager(_testKeyFilePath);
        manager.LoadKeyFile(password);

        // Verify DEK was preserved exactly
        Assert.True(manager.IsDekLoaded());
        byte[] loadedDek = manager.GetDEK();
        Assert.Equal(originalDek, loadedDek);

        // Verify test payload decrypts with loaded DEK
        byte[] decryptedData = EncryptionFunctions.DecryptData(loadedDek, encryptedData);
        Assert.Equal(plaintextData, decryptedData);

        // Verify the file on disk was automatically upgraded to strong parameters
        byte[] upgradedKeyFileBytes = FileWorker.readFile(_testKeyFileName);
        ArgonParameters afterLoadParams = keyManager.GetKeyFileParameters(upgradedKeyFileBytes);
        Assert.True(afterLoadParams.MemorySize >= 65536, $"Upgraded memory size was {afterLoadParams.MemorySize}");
        Assert.True(afterLoadParams.Iterations >= 3, $"Upgraded iterations was {afterLoadParams.Iterations}");
    }

    [Fact]
    public void Test04_LoadKeyFile_UpgradedKeyFile_IsIdempotent()
    {
        string password = "IdempotentPassword_789";
        var manager = new keyManager(_testKeyFilePath);
        manager.CreateKeyFile(password);

        byte[] firstPassBytes = FileWorker.readFile(_testKeyFileName);
        ArgonParameters firstParams = keyManager.GetKeyFileParameters(firstPassBytes);

        // Clear loaded session
        manager.ClearLoadedKey();

        // Load again
        manager.LoadKeyFile(password);

        byte[] secondPassBytes = FileWorker.readFile(_testKeyFileName);
        ArgonParameters secondParams = keyManager.GetKeyFileParameters(secondPassBytes);

        Assert.Equal(firstParams, secondParams);
        Assert.False(EncryptionFunctions.NeedsKdfUpgrade(secondParams));
    }

    [Fact]
    public void Test05_LoadKeyFile_WindowsStrongParameters_NotDowngraded()
    {
        string password = "WindowsUserPassword_456";
        byte[] originalDek = EncryptionFunctions.GenerateDEK(32);

        // Create key file with strong Windows parameters: 256 MB (262144 KB), 3 iter, 3 lanes
        var windowsParams = new ArgonParameters(262144, 3, 3);
        byte[] windowsKeyFile = keyManager.CreatePackedKeyFile(originalDek, password, windowsParams);
        FileWorker.writeFile(windowsKeyFile, _testKeyFileName);

        var manager = new keyManager(_testKeyFilePath);
        manager.LoadKeyFile(password);

        Assert.True(manager.IsDekLoaded());
        Assert.Equal(originalDek, manager.GetDEK());

        // File on disk must retain its strong parameters and not be downgraded
        byte[] savedKeyFileBytes = FileWorker.readFile(_testKeyFileName);
        ArgonParameters savedParams = keyManager.GetKeyFileParameters(savedKeyFileBytes);
        Assert.Equal(262144, savedParams.MemorySize);
        Assert.Equal(3, savedParams.Iterations);
        Assert.Equal(3, savedParams.ParallelismDegree);
    }

    [Fact]
    public void Test06_LoadKeyFile_WrongPassword_ThrowsExceptionAndDoesNotModifyFile()
    {
        string correctPassword = "CorrectMasterPassword123";
        string wrongPassword = "WrongMasterPassword456";

        var manager = new keyManager(_testKeyFilePath);
        manager.CreateKeyFile(correctPassword);

        byte[] bytesBeforeAttempt = FileWorker.readFile(_testKeyFileName);

        manager.ClearLoadedKey();

        Assert.ThrowsAny<Exception>(() =>
        {
            manager.LoadKeyFile(wrongPassword);
        });

        Assert.False(manager.IsDekLoaded());

        // File on disk must be completely untouched
        byte[] bytesAfterAttempt = FileWorker.readFile(_testKeyFileName);
        Assert.Equal(bytesBeforeAttempt, bytesAfterAttempt);
    }

    [Fact]
    public void Test07_LoadKeyFile_CorruptedKeyFile_ThrowsAndDoesNotMigrate()
    {
        string password = "CorruptedTestPassword";
        var manager = new keyManager(_testKeyFilePath);
        manager.CreateKeyFile(password);

        byte[] validBytes = FileWorker.readFile(_testKeyFileName);

        // Corrupt the encrypted ciphertext portion
        byte[] corruptedBytes = (byte[])validBytes.Clone();
        corruptedBytes[^5] ^= 0xFF;
        corruptedBytes[^6] ^= 0xAA;
        FileWorker.writeFile(corruptedBytes, _testKeyFileName);

        manager.ClearLoadedKey();

        Assert.ThrowsAny<Exception>(() =>
        {
            manager.LoadKeyFile(password);
        });

        Assert.False(manager.IsDekLoaded());

        // File should not be overwritten
        byte[] afterAttemptBytes = FileWorker.readFile(_testKeyFileName);
        Assert.Equal(corruptedBytes, afterAttemptBytes);
    }

    [Fact]
    public void Test08_LoadKeyFile_LegacyHeaderlessFile_SuccessfullyMigrated()
    {
        string password = "LegacyVaultPassword_999";
        byte[] originalDek = EncryptionFunctions.GenerateDEK(32);
        byte[] salt = EncryptionFunctions.GenerateSalt(16);

        // Legacy format: 16 bytes salt + AES-GCM encrypted DEK (no SPK1 header)
        var legacyParams = new ArgonParameters(2048, 2, 1);
        byte[] kek = EncryptionFunctions.GenerateKEKwArgon2id(password, salt, legacyParams);
        byte[] encryptedDek = EncryptionFunctions.EncryptDEKwithGCM(originalDek, kek, out _, out _);
        CryptographicOperations.ZeroMemory(kek);

        byte[] legacyFileBytes = new byte[salt.Length + encryptedDek.Length];
        Buffer.BlockCopy(salt, 0, legacyFileBytes, 0, salt.Length);
        Buffer.BlockCopy(encryptedDek, 0, legacyFileBytes, salt.Length, encryptedDek.Length);

        FileWorker.writeFile(legacyFileBytes, _testKeyFileName);

        Assert.False(keyManager.HasPortableHeader(legacyFileBytes));

        // Load legacy file
        var manager = new keyManager(_testKeyFilePath);
        manager.LoadKeyFile(password);

        Assert.True(manager.IsDekLoaded());
        Assert.Equal(originalDek, manager.GetDEK());

        // Verify it was upgraded with SPK1 header and strong parameters
        byte[] upgradedBytes = FileWorker.readFile(_testKeyFileName);
        Assert.True(keyManager.HasPortableHeader(upgradedBytes));

        ArgonParameters upgradedParams = keyManager.GetKeyFileParameters(upgradedBytes);
        Assert.True(upgradedParams.MemorySize >= 65536);
        Assert.True(upgradedParams.Iterations >= 3);
    }

    [Fact]
    public void Test09_ChangePassword_UsesTargetParametersAndPreservesDEK()
    {
        string oldPassword = "OldPassword_111";
        string newPassword = "NewPassword_222";

        var manager = new keyManager(_testKeyFilePath);
        manager.CreateKeyFile(oldPassword);
        byte[] initialDek = (byte[])manager.GetDEK().Clone();

        // Encrypt test data with initial DEK
        byte[] testData = Encoding.UTF8.GetBytes("Critical user note data");
        byte[] encryptedData = EncryptionFunctions.EncryptData(initialDek, testData);

        // Change password
        manager.ChangePassword(newPassword);

        // Verify DEK in memory is unchanged
        Assert.Equal(initialDek, manager.GetDEK());

        // Clear and reopen with new password
        manager.ClearLoadedKey();
        manager.LoadKeyFile(newPassword);

        Assert.True(manager.IsDekLoaded());
        Assert.Equal(initialDek, manager.GetDEK());

        // Verify existing data decrypts seamlessly
        byte[] decrypted = EncryptionFunctions.DecryptData(manager.GetDEK(), encryptedData);
        Assert.Equal(testData, decrypted);

        // Verify old password fails
        manager.ClearLoadedKey();
        Assert.ThrowsAny<Exception>(() => manager.LoadKeyFile(oldPassword));
    }

    [Fact]
    public void Test10_ClearLoadedKey_ZeroesDEKAndLocksVault()
    {
        string password = "ClearKeyPassword_333";
        var manager = new keyManager(_testKeyFilePath);
        manager.CreateKeyFile(password);

        byte[] dekRef = manager.GetDEK();
        Assert.NotNull(dekRef);
        Assert.Contains(dekRef, b => b != 0);

        manager.ClearLoadedKey();

        Assert.False(manager.IsDekLoaded());
        Assert.Throws<InvalidOperationException>(() => manager.GetDEK());

        // Verify DEK array in memory was zeroed
        Assert.All(dekRef, b => Assert.Equal(0, b));
    }
}
