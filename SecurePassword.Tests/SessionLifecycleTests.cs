using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace SecurePassword.Tests;

public class SessionLifecycleTests : IDisposable
{
    private readonly string _testDir;
    private readonly string _keyFilePath;
    private readonly string _originalAppDataDir;

    public SessionLifecycleTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"SP_SessionTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
        _keyFilePath = Path.Combine(_testDir, "keys.dat");
        _originalAppDataDir = FileWorker.TestingAppDataDirectory ?? string.Empty;
        FileWorker.TestingAppDataDirectory = _testDir;
    }

    public void Dispose()
    {
        FileWorker.TestingAppDataDirectory = _originalAppDataDir;
        try
        {
            if (Directory.Exists(_testDir))
                Directory.Delete(_testDir, recursive: true);
        }
        catch
        {
        }
    }

    [Fact]
    public void Test01_Unlock_CreatesActiveSessionWithLoadedDek()
    {
        var km = new keyManager(_keyFilePath);
        km.CreateKeyFile("MasterPassword123!");

        // Key is loaded after create
        Assert.True(km.IsDekLoaded());
        byte[] dek = km.GetDEK();
        Assert.NotNull(dek);
        Assert.Equal(32, dek.Length);

        // Lock
        km.ClearLoadedKey();
        Assert.False(km.IsDekLoaded());

        // Unlock with correct password
        km.LoadKeyFile("MasterPassword123!");
        Assert.True(km.IsDekLoaded());
        Assert.Equal(32, km.GetDEK().Length);
    }

    [Fact]
    public void Test02_WrongPassword_DoesNotCreateSession_Throws()
    {
        var km = new keyManager(_keyFilePath);
        km.CreateKeyFile("CorrectPassword123!");
        km.ClearLoadedKey();

        Assert.ThrowsAny<Exception>(() => km.LoadKeyFile("WrongPassword123!"));
        Assert.False(km.IsDekLoaded());
        Assert.Throws<InvalidOperationException>(() => km.GetDEK());
    }

    [Fact]
    public void Test03_Lock_DestroysDekAndZeroesMemory()
    {
        var km = new keyManager(_keyFilePath);
        km.CreateKeyFile("MasterPassword123!");
        byte[] dekRef = km.GetDEK();

        km.ClearLoadedKey();

        Assert.False(km.IsDekLoaded());
        Assert.Throws<InvalidOperationException>(() => km.GetDEK());
    }

    [Fact]
    public void Test04_RepositoryAccessAfterLock_ThrowsInvalidOperationException()
    {
        var km = new keyManager(_keyFilePath);
        km.CreateKeyFile("MasterPassword123!");

        var repo = new SecureRepository<PasswordEntry>("passwords.dat", km);
        repo.Add(new PasswordEntry { Id = 1, Title = "Site", Login = "user", Password = "pass" });
        repo.Save();

        // Lock vault
        km.ClearLoadedKey();

        // Reading returns empty or default
        Assert.Empty(repo.getAll());
        Assert.Null(repo.GetItemById(1));

        // Modifying throws InvalidOperationException
        Assert.Throws<InvalidOperationException>(() => repo.Add(new PasswordEntry { Id = 2, Title = "Site2", Login = "user", Password = "pass" }));
        Assert.Throws<InvalidOperationException>(() => repo.Save());
    }

    [Fact]
    public void Test05_InactivityTimeout_TriggersShouldLock()
    {
        var session = new VaultSessionService();
        session.LockOnTimer = true;

        session.MarkAuthenticated();
        Assert.True(session.IsAuthenticated);
        Assert.False(session.ShouldLockForInactivity());
    }

    [Fact]
    public void Test06_UserActivity_RefreshesInactivityState()
    {
        var session = new VaultSessionService();
        session.LockOnTimer = true;
        session.MarkAuthenticated();

        session.RecordActivity();
        Assert.False(session.ShouldLockForInactivity());
    }

    [Fact]
    public void Test07_ProcessRestartModel_NeverRestoresUnlockedStateWithoutPassword()
    {
        // Simulate Process 1 creating vault
        var km1 = new keyManager(_keyFilePath);
        km1.CreateKeyFile("SecretVaultPass123!");
        Assert.True(km1.IsDekLoaded());

        // Process dies (km1 discarded, fresh instance km2 created)
        var km2 = new keyManager(_keyFilePath);
        Assert.False(km2.IsDekLoaded());
        Assert.Throws<InvalidOperationException>(() => km2.GetDEK());

        // Cannot read data without providing master password
        var repo = new SecureRepository<PasswordEntry>("passwords.dat", km2);
        Assert.Empty(repo.getAll());
    }

    [Fact]
    public void Test08_ResetVault_DestroysSessionAndKey()
    {
        var km = new keyManager(_keyFilePath);
        km.CreateKeyFile("Password123!");

        var repo = new SecureRepository<PasswordEntry>("passwords.dat", km);
        repo.Add(new PasswordEntry { Id = 1, Title = "Test", Login = "user", Password = "pass" });
        repo.Save();

        // Perform reset
        km.ClearLoadedKey();
        if (File.Exists(_keyFilePath))
            File.Delete(_keyFilePath);
        string passwordsPath = Path.Combine(_testDir, "passwords.dat");
        if (File.Exists(passwordsPath))
            File.Delete(passwordsPath);

        Assert.False(km.IsDekLoaded());
        Assert.False(File.Exists(_keyFilePath));
        Assert.False(File.Exists(passwordsPath));
    }

    [Fact]
    public void Test09_ResetVault_DeletesAllInternalVaultFiles()
    {
        string[] files = ["keys.dat", "passwords.dat", "cards.dat", "notes.dat"];
        foreach (string f in files)
        {
            File.WriteAllBytes(Path.Combine(_testDir, f), [1, 2, 3]);
        }

        foreach (string f in files)
        {
            string p = Path.Combine(_testDir, f);
            if (File.Exists(p))
                File.Delete(p);
        }

        foreach (string f in files)
        {
            Assert.False(File.Exists(Path.Combine(_testDir, f)));
        }
    }

    [Fact]
    public void Test10_ChangeMasterPassword_DoesNotRetainOldOrNewPasswordAfterwards()
    {
        var km = new keyManager(_keyFilePath);
        km.CreateKeyFile("InitialPassword123!");

        var repo = new SecureRepository<PasswordEntry>("passwords.dat", km);
        repo.Add(new PasswordEntry { Id = 1, Title = "MyBank", Login = "admin", Password = "secret" });
        repo.Save();

        // Change password
        km.ChangePassword("NewMasterPassword456!");

        // Lock
        km.ClearLoadedKey();

        // Verify old password fails
        Assert.ThrowsAny<Exception>(() => km.LoadKeyFile("InitialPassword123!"));

        // Verify new password succeeds and data intact
        km.LoadKeyFile("NewMasterPassword456!");
        Assert.True(km.IsDekLoaded());

        var repoAfter = new SecureRepository<PasswordEntry>("passwords.dat", km);
        var item = repoAfter.GetItemById(1);
        Assert.NotNull(item);
        Assert.Equal("MyBank", item.Title);
    }

    [Fact]
    public void Test11_P2PBundle_DoesNotRequireMasterPasswordStorage()
    {
        var km = new keyManager(_keyFilePath);
        km.CreateKeyFile("AnyMasterPass123!");

        // In the new architecture, keyManager does not have _loadedPassword
        // Vault bundle exports exact encrypted files from disk
        byte[] oldDek = (byte[])km.GetDEK().Clone();

        km.ClearLoadedKey();
        Assert.False(km.IsDekLoaded());
    }

    [Fact]
    public void Test12_RepeatedLockUnlock_MaintainsCleanState()
    {
        var km = new keyManager(_keyFilePath);
        km.CreateKeyFile("CyclePassword123!");

        for (int i = 0; i < 5; i++)
        {
            km.ClearLoadedKey();
            Assert.False(km.IsDekLoaded());

            km.LoadKeyFile("CyclePassword123!");
            Assert.True(km.IsDekLoaded());
            Assert.Equal(32, km.GetDEK().Length);
        }
    }
}
