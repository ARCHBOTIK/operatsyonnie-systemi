using System.Security.Cryptography;
using System.Text;
using SecurePassword;
using Xunit;

namespace SecurePassword.Tests;

public class AtomicWriteTests : IDisposable
{
    private readonly List<string> _testFiles = [];

    private string CreateTestFilePath(string? fileName = null)
    {
        string name = fileName ?? $"atomic_test_{Guid.NewGuid():N}.dat";
        string path = Path.Combine(FileWorker.GetAppDataDirectory(), name);
        _testFiles.Add(path);
        return path;
    }

    private static string ComputeSha256(byte[] data)
    {
        return Convert.ToHexString(SHA256.HashData(data));
    }

    private static string ComputeFileSha256(string filePath)
    {
        return ComputeSha256(File.ReadAllBytes(filePath));
    }

    public void Dispose()
    {
        FileWorker.TestingFailPointHook = null;
        foreach (string file in _testFiles)
        {
            try
            {
                if (File.Exists(file))
                    File.Delete(file);
            }
            catch
            {
            }
        }
        FileWorker.CleanupLeftoverTempFiles();
    }

    [Fact]
    public void Test01_AtomicWrite_NewFile_WritesSuccessfully()
    {
        string filePath = CreateTestFilePath();
        byte[] payload = Encoding.UTF8.GetBytes("Brand new atomic file payload");

        FileWorker.WriteFileAtomically(payload, filePath);

        Assert.True(File.Exists(filePath));
        byte[] readBytes = FileWorker.readFile(filePath);
        Assert.Equal(payload, readBytes);
    }

    [Fact]
    public void Test02_AtomicWrite_ExistingFile_ReplacesSuccessfully()
    {
        string filePath = CreateTestFilePath();
        byte[] initialPayload = Encoding.UTF8.GetBytes("Initial payload version 1");
        byte[] updatedPayload = Encoding.UTF8.GetBytes("Updated payload version 2 with different content and size");

        FileWorker.WriteFileAtomically(initialPayload, filePath);
        Assert.Equal(initialPayload, FileWorker.readFile(filePath));

        FileWorker.WriteFileAtomically(updatedPayload, filePath);
        Assert.Equal(updatedPayload, FileWorker.readFile(filePath));
    }

    [Fact]
    public void Test03_AtomicWrite_WriteFails_OldFileRemainsUnchanged()
    {
        string filePath = CreateTestFilePath();
        byte[] initialPayload = Encoding.UTF8.GetBytes("Initial valid data before crash");
        FileWorker.WriteFileAtomically(initialPayload, filePath);

        string hashBefore = ComputeFileSha256(filePath);

        // Inject simulated failure during writing
        FileWorker.TestingFailPointHook = (target, stage) =>
        {
            if (stage == AtomicWriteStage.DuringWrite)
                throw new IOException("Simulated disk write failure / OutOfSpace");
        };

        byte[] badPayload = Encoding.UTF8.GetBytes("Corrupted incomplete data");
        Assert.Throws<IOException>(() =>
        {
            FileWorker.WriteFileAtomically(badPayload, filePath);
        });

        // The original file must remain 100% byte-identical
        string hashAfter = ComputeFileSha256(filePath);
        Assert.Equal(hashBefore, hashAfter);
        Assert.Equal(initialPayload, FileWorker.readFile(filePath));
    }

    [Fact]
    public void Test04_AtomicWrite_ExceptionBeforeCommit_OldFileRemainsUnchanged()
    {
        string filePath = CreateTestFilePath();
        byte[] initialPayload = Encoding.UTF8.GetBytes("Stable production state");
        FileWorker.WriteFileAtomically(initialPayload, filePath);

        string hashBefore = ComputeFileSha256(filePath);

        // Inject simulated failure right before commit
        FileWorker.TestingFailPointHook = (target, stage) =>
        {
            if (stage == AtomicWriteStage.BeforeCommit)
                throw new InvalidOperationException("Simulated process termination before commit");
        };

        byte[] pendingPayload = Encoding.UTF8.GetBytes("Uncommitted state");
        Assert.Throws<InvalidOperationException>(() =>
        {
            FileWorker.WriteFileAtomically(pendingPayload, filePath);
        });

        // Target file must be byte-identical to pre-failure state
        string hashAfter = ComputeFileSha256(filePath);
        Assert.Equal(hashBefore, hashAfter);
        Assert.Equal(initialPayload, FileWorker.readFile(filePath));
    }

    [Fact]
    public void Test05_AtomicWrite_TemporaryFileDoesNotBecomeVault()
    {
        string filePath = CreateTestFilePath();
        byte[] validData = Encoding.UTF8.GetBytes("Valid vault content");
        FileWorker.WriteFileAtomically(validData, filePath);

        // Inject failure after temp creation
        FileWorker.TestingFailPointHook = (target, stage) =>
        {
            if (stage == AtomicWriteStage.TempCreated)
                throw new IOException("Simulated crash right after creating temp file");
        };

        Assert.Throws<IOException>(() =>
        {
            FileWorker.WriteFileAtomically(Encoding.UTF8.GetBytes("Failed payload"), filePath);
        });

        // Ensure temp file was cleaned up and original file intact
        string baseDir = Path.GetDirectoryName(filePath)!;
        string fileName = Path.GetFileName(filePath);
        var matchingTemps = Directory.GetFiles(baseDir, $".{fileName}.*.tmp");
        Assert.Empty(matchingTemps);

        Assert.Equal(validData, FileWorker.readFile(filePath));
    }

    [Fact]
    public async Task Test06_AtomicWrite_ConcurrentWrites_DoNotProduceCorruption()
    {
        string filePath = CreateTestFilePath();
        byte[] initial = Encoding.UTF8.GetBytes("Base");
        FileWorker.WriteFileAtomically(initial, filePath);

        const int threadCount = 10;
        var tasks = new List<Task>();

        for (int i = 0; i < threadCount; i++)
        {
            int index = i;
            tasks.Add(Task.Run(() =>
            {
                byte[] data = Encoding.UTF8.GetBytes($"Payload from worker thread #{index} with some padding {new string('X', 500)}");
                FileWorker.WriteFileAtomically(data, filePath);
                byte[] read = FileWorker.readFile(filePath);
                Assert.NotNull(read);
                Assert.True(read.Length > 0);
            }));
        }

        await Task.WhenAll(tasks);

        byte[] finalBytes = FileWorker.readFile(filePath);
        string content = Encoding.UTF8.GetString(finalBytes);
        Assert.StartsWith("Payload from worker thread #", content);
    }

    [Fact]
    public void Test07_KdfMigration_WriteFailure_OldKeysFileStillWorks()
    {
        string keyFileName = $"keys_migr_fail_{Guid.NewGuid():N}.dat";
        string keyFilePath = CreateTestFilePath(keyFileName);
        string masterPassword = "TestMasterPassword_123";

        byte[] originalDek = EncryptionFunctions.GenerateDEK(32);
        var oldParams = new ArgonParameters(2048, 2, 1);
        byte[] oldPackedKey = keyManager.CreatePackedKeyFile(originalDek, masterPassword, oldParams);
        FileWorker.writeFile(oldPackedKey, keyFileName);

        string hashBefore = ComputeFileSha256(keyFilePath);

        // Inject failure during migration commit of keys.dat
        FileWorker.TestingFailPointHook = (target, stage) =>
        {
            if (target.Contains(keyFileName) && stage == AtomicWriteStage.BeforeCommit)
                throw new IOException("Simulated disk failure during KDF migration commit");
        };

        var manager = new keyManager(keyFilePath);
        Assert.Throws<IOException>(() =>
        {
            manager.LoadKeyFile(masterPassword);
        });

        FileWorker.TestingFailPointHook = null;

        // SHA-256 of keys.dat on disk must be 100% byte-identical
        string hashAfter = ComputeFileSha256(keyFilePath);
        Assert.Equal(hashBefore, hashAfter);

        // Old keys.dat must still open properly with master password and decrypt DEK
        var recoveryManager = new keyManager(keyFilePath);
        recoveryManager.LoadKeyFile(masterPassword);
        Assert.True(recoveryManager.IsDekLoaded());
        Assert.Equal(originalDek, recoveryManager.GetDEK());
    }

    [Fact]
    public void Test08_ChangePassword_WriteFailure_OldKeysFileStillWorks()
    {
        string keyFileName = $"keys_pwd_fail_{Guid.NewGuid():N}.dat";
        string keyFilePath = CreateTestFilePath(keyFileName);
        string oldPassword = "InitialOldPassword_111";
        string newPassword = "IntendedNewPassword_222";

        var manager = new keyManager(keyFilePath);
        manager.CreateKeyFile(oldPassword);
        byte[] originalDek = (byte[])manager.GetDEK().Clone();

        string hashBefore = ComputeFileSha256(keyFilePath);

        // Inject failure during password change write
        FileWorker.TestingFailPointHook = (target, stage) =>
        {
            if (target.Contains(keyFileName) && stage == AtomicWriteStage.DuringWrite)
                throw new IOException("Simulated disk error during ChangePassword");
        };

        Assert.Throws<IOException>(() =>
        {
            manager.ChangePassword(newPassword);
        });

        FileWorker.TestingFailPointHook = null;

        // Verify keys.dat SHA-256 is unchanged
        string hashAfter = ComputeFileSha256(keyFilePath);
        Assert.Equal(hashBefore, hashAfter);

        // Verify old password opens the vault and new password fails
        var reopenManager = new keyManager(keyFilePath);
        Assert.ThrowsAny<Exception>(() => reopenManager.LoadKeyFile(newPassword));

        reopenManager.LoadKeyFile(oldPassword);
        Assert.True(reopenManager.IsDekLoaded());
        Assert.Equal(originalDek, reopenManager.GetDEK());
    }

    [Fact]
    public void Test09_RepositoryWriteFailure_PreviousVaultDataStillDecrypts()
    {
        string keyFileName = $"keys_repo_fail_{Guid.NewGuid():N}.dat";
        string keyFilePath = CreateTestFilePath(keyFileName);
        string dataFileName = $"passwords_repo_fail_{Guid.NewGuid():N}.dat";
        string dataFilePath = CreateTestFilePath(dataFileName);
        string password = "VaultPassword_777";

        var keyMgr = new keyManager(keyFilePath);
        keyMgr.CreateKeyFile(password);

        var repo = new SecureRepository<PasswordEntry>(dataFilePath, keyMgr);
        repo.Add(new PasswordEntry
        {
            Id = 1,
            Title = "Initial Item",
            Login = "user1",
            Password = "pass1"
        });
        repo.Save();

        string dataHashBefore = ComputeFileSha256(dataFilePath);

        // Inject failure on subsequent save
        FileWorker.TestingFailPointHook = (target, stage) =>
        {
            if (target.Contains(dataFileName) && stage == AtomicWriteStage.AfterFlush)
                throw new IOException("Simulated power loss after flush");
        };

        repo.Add(new PasswordEntry
        {
            Id = 2,
            Title = "Second Item",
            Login = "user2",
            Password = "pass2"
        });

        Assert.Throws<IOException>(() =>
        {
            repo.Save();
        });

        FileWorker.TestingFailPointHook = null;

        // Data file SHA-256 must be identical to pre-failure state
        string dataHashAfter = ComputeFileSha256(dataFilePath);
        Assert.Equal(dataHashBefore, dataHashAfter);

        // Reopening repository must decrypt original data without corruption
        var freshRepo = new SecureRepository<PasswordEntry>(dataFilePath, keyMgr);
        var items = freshRepo.getAll().ToList();
        Assert.Single(items);
        Assert.Equal("Initial Item", items[0].Title);
    }

    [Fact]
    public void Test10_SuccessfulAtomicWrite_ResultDecryptsNormally()
    {
        string keyFilePath = CreateTestFilePath();
        string dataFilePath = CreateTestFilePath();
        string password = "HappyPathPassword_999";

        var keyMgr = new keyManager(keyFilePath);
        keyMgr.CreateKeyFile(password);

        var repo = new SecureRepository<PasswordEntry>(dataFilePath, keyMgr);
        repo.Add(new PasswordEntry
        {
            Id = 10,
            Title = "GitHub Account",
            Login = "octocat",
            Password = "SuperSecretToken#1"
        });
        repo.Save();

        keyMgr.ClearLoadedKey();

        // Reopen vault from scratch
        var reopenKeyMgr = new keyManager(keyFilePath);
        reopenKeyMgr.LoadKeyFile(password);

        var reopenRepo = new SecureRepository<PasswordEntry>(dataFilePath, reopenKeyMgr);
        var entry = reopenRepo.GetItemById(10);
        Assert.NotNull(entry);
        Assert.Equal("GitHub Account", entry.Title);
        Assert.Equal("octocat", entry.Login);
        Assert.Equal("SuperSecretToken#1", entry.Password);
    }
}
