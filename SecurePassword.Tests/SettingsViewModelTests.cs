using System.IO;
using SecurePassword.ViewModels.Settings;
using Xunit;

namespace SecurePassword.Tests;

public class SettingsViewModelTests : IDisposable
{
    private readonly string _testDir;
    private readonly string _keyFilePath;
    private readonly string _originalAppDataDir;
    private readonly keyManager _km;
    private readonly MasterPasswordService _mps;
    private readonly VaultSessionService _session;

    public SettingsViewModelTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "SettingsVmTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
        _keyFilePath = Path.Combine(_testDir, "keys.dat");

        _originalAppDataDir = FileWorker.TestingAppDataDirectory ?? string.Empty;
        FileWorker.TestingAppDataDirectory = _testDir;

        _km = new keyManager(_keyFilePath);
        _mps = new MasterPasswordService(_km, _keyFilePath);
        _session = new VaultSessionService();
    }

    public void Dispose()
    {
        FileWorker.TestingAppDataDirectory = _originalAppDataDir;
        _km.ClearLoadedKey();
        try
        {
            if (Directory.Exists(_testDir))
                Directory.Delete(_testDir, true);
        }
        catch { }
    }

    [Fact]
    public void Defaults_ShouldLoadSettingsFromSession()
    {
        using var vm = new SettingsViewModel(_session, _mps, _km);

        Assert.Equal(_session.LockOnExit, vm.LockOnExit);
        Assert.Equal(_session.LockOnMinimize, vm.LockOnMinimize);
        Assert.Equal(_session.LockOnTimer, vm.LockOnTimer);
        Assert.False(vm.IsChangePasswordModalVisible);
        Assert.False(vm.IsDeleteModalVisible);
        Assert.Equal(string.Empty, vm.CurrentPassword);
        Assert.Equal(string.Empty, vm.NewPassword);
        Assert.Equal(string.Empty, vm.ConfirmPassword);
        Assert.Equal(string.Empty, vm.ChangePasswordError);
        Assert.False(vm.HasChangePasswordError);
        Assert.False(vm.IsBusy);
        Assert.True(vm.IsNotBusy);
    }

    [Fact]
    public void Toggles_ShouldUpdateSessionProperties()
    {
        using var vm = new SettingsViewModel(_session, _mps, _km);

        vm.LockOnExit = false;
        Assert.False(_session.LockOnExit);
        vm.LockOnExit = true;
        Assert.True(_session.LockOnExit);

        vm.LockOnMinimize = false;
        Assert.False(_session.LockOnMinimize);
        vm.LockOnMinimize = true;
        Assert.True(_session.LockOnMinimize);

        vm.LockOnTimer = false;
        Assert.False(_session.LockOnTimer);
        vm.LockOnTimer = true;
        Assert.True(_session.LockOnTimer);
    }

    [Fact]
    public void OpenClose_ChangePasswordModal_ShouldManageVisibilityAndClearSensitiveData()
    {
        using var vm = new SettingsViewModel(_session, _mps, _km);

        vm.CurrentPassword = "temp";
        vm.OpenChangePasswordCommand.Execute(null);

        Assert.True(vm.IsChangePasswordModalVisible);
        Assert.Equal(string.Empty, vm.CurrentPassword);

        vm.CurrentPassword = "secret";
        vm.NewPassword = "newsecret123";
        vm.ConfirmPassword = "newsecret123";
        vm.CloseChangePasswordCommand.Execute(null);

        Assert.False(vm.IsChangePasswordModalVisible);
        Assert.Equal(string.Empty, vm.CurrentPassword);
        Assert.Equal(string.Empty, vm.NewPassword);
        Assert.Equal(string.Empty, vm.ConfirmPassword);
    }

    [Theory]
    [InlineData("", "newpass123", "newpass123")]
    [InlineData("oldpass", "", "newpass123")]
    [InlineData("oldpass", "newpass123", "")]
    [InlineData("   ", "newpass123", "newpass123")]
    public async Task ChangePassword_EmptyFields_ShouldSetValidationError(string oldP, string newP, string confP)
    {
        using var vm = new SettingsViewModel(_session, _mps, _km)
        {
            CurrentPassword = oldP,
            NewPassword = newP,
            ConfirmPassword = confP
        };

        await vm.ChangePasswordAsync();

        Assert.Equal("Заполните все поля.", vm.ChangePasswordError);
        Assert.True(vm.HasChangePasswordError);
    }

    [Fact]
    public async Task ChangePassword_PasswordMismatch_ShouldSetErrorMessage()
    {
        using var vm = new SettingsViewModel(_session, _mps, _km)
        {
            CurrentPassword = "current_valid",
            NewPassword = "new_password_1",
            ConfirmPassword = "new_password_2"
        };

        await vm.ChangePasswordAsync();

        Assert.Equal("Новый пароль и подтверждение не совпадают.", vm.ChangePasswordError);
        Assert.True(vm.HasChangePasswordError);
    }

    [Fact]
    public async Task ChangePassword_TooShort_ShouldSetErrorMessage()
    {
        using var vm = new SettingsViewModel(_session, _mps, _km)
        {
            CurrentPassword = "current_valid",
            NewPassword = "short",
            ConfirmPassword = "short"
        };

        await vm.ChangePasswordAsync();

        Assert.Equal("Минимальная длина пароля — 8 символов.", vm.ChangePasswordError);
        Assert.True(vm.HasChangePasswordError);
    }

    [Fact]
    public async Task ChangePassword_SuccessfulExecution_ShouldCallServiceAndCloseModal()
    {
        string oldPassword = "Initial_Master_1234!";
        string newPassword = "New_Master_Password_5678!";

        _km.CreateKeyFile(oldPassword);
        _km.LoadKeyFile(oldPassword);
        byte[] originalDek = _km.GetDEK().ToArray();

        using var vm = new SettingsViewModel(_session, _mps, _km);
        vm.OpenChangePasswordModal();

        vm.CurrentPassword = oldPassword;
        vm.NewPassword = newPassword;
        vm.ConfirmPassword = newPassword;

        await vm.ChangePasswordAsync();

        Assert.False(vm.IsChangePasswordModalVisible);
        Assert.Equal(string.Empty, vm.CurrentPassword);
        Assert.Equal(string.Empty, vm.NewPassword);
        Assert.Equal(string.Empty, vm.ConfirmPassword);
        Assert.False(vm.HasChangePasswordError);

        // Verify keyfile can now be opened with newPassword
        _km.ClearLoadedKey();
        _km.LoadKeyFile(newPassword);
        Assert.True(_km.IsDekLoaded());
        Assert.Equal(originalDek, _km.GetDEK());
    }

    [Fact]
    public async Task ChangePassword_WrongCurrentPassword_ShouldSetErrorMessage()
    {
        string correctPassword = "Initial_Master_1234!";
        string wrongPassword = "Wrong_Password_9999!";
        string newPassword = "New_Master_Password_5678!";

        _km.CreateKeyFile(correctPassword);
        _session.MarkAuthenticated();

        using var vm = new SettingsViewModel(_session, _mps, _km);
        vm.OpenChangePasswordModal();

        vm.CurrentPassword = wrongPassword;
        vm.NewPassword = newPassword;
        vm.ConfirmPassword = newPassword;

        await vm.ChangePasswordAsync();

        Assert.True(vm.IsChangePasswordModalVisible);
        Assert.Equal("Не удалось сменить мастер-пароль. Проверьте текущий пароль.", vm.ChangePasswordError);
        Assert.True(vm.HasChangePasswordError);
    }

    [Fact]
    public void OpenClose_DeleteModal_ShouldManageVisibility()
    {
        using var vm = new SettingsViewModel(_session, _mps, _km);

        vm.OpenDeleteModalCommand.Execute(null);
        Assert.True(vm.IsDeleteModalVisible);

        vm.CloseDeleteModalCommand.Execute(null);
        Assert.False(vm.IsDeleteModalVisible);
    }

    [Fact]
    public async Task DeleteDatabase_ShouldDeleteVaultFilesAndLock()
    {
        string[] vaultFiles = ["keys.dat", "passwords.dat", "cards.dat", "notes.dat"];
        foreach (string file in vaultFiles)
        {
            string path = FileWorker.ResolvePath(file);
            File.WriteAllText(path, "test data");
        }

        bool lockRequested = false;
        _session.MarkAuthenticated();

        using var vm = new SettingsViewModel(_session, _mps, _km)
        {
            RequestLockAction = () =>
            {
                lockRequested = true;
                return Task.CompletedTask;
            }
        };

        vm.OpenDeleteModal();
        Assert.True(vm.IsDeleteModalVisible);

        await vm.DeleteDatabaseAsync();

        Assert.False(vm.IsDeleteModalVisible);
        Assert.False(_session.IsAuthenticated);
        Assert.True(lockRequested);

        foreach (string file in vaultFiles)
        {
            string path = FileWorker.ResolvePath(file);
            Assert.False(File.Exists(path));
        }
    }

    [Fact]
    public void LockNow_ShouldClearLoadedKeyAndLockSession()
    {
        _km.CreateKeyFile("MasterPassword123!");
        _session.MarkAuthenticated();
        Assert.True(_km.IsDekLoaded());
        Assert.True(_session.IsAuthenticated);

        bool lockRequested = false;
        using var vm = new SettingsViewModel(_session, _mps, _km)
        {
            RequestLockAction = () =>
            {
                lockRequested = true;
                return Task.CompletedTask;
            }
        };

        vm.LockNowCommand.Execute(null);

        Assert.False(_km.IsDekLoaded());
        Assert.False(_session.IsAuthenticated);
        Assert.True(lockRequested);
    }

    [Fact]
    public void SessionLock_ShouldClearSensitiveDataAndCloseModals()
    {
        _session.MarkAuthenticated();
        using var vm = new SettingsViewModel(_session, _mps, _km);

        vm.OpenChangePasswordModal();
        vm.CurrentPassword = "secret_pass";
        vm.NewPassword = "new_secret_pass";
        vm.ConfirmPassword = "new_secret_pass";
        vm.OpenDeleteModal();

        Assert.True(vm.IsDeleteModalVisible);
        Assert.True(vm.IsChangePasswordModalVisible);

        _session.Lock();

        Assert.False(vm.IsDeleteModalVisible);
        Assert.False(vm.IsChangePasswordModalVisible);
        Assert.Equal(string.Empty, vm.CurrentPassword);
        Assert.Equal(string.Empty, vm.NewPassword);
        Assert.Equal(string.Empty, vm.ConfirmPassword);
    }

    [Fact]
    public void ToggleVisibility_ShouldFlipFlags()
    {
        using var vm = new SettingsViewModel(_session, _mps, _km);

        Assert.False(vm.IsCurrentPasswordVisible);
        vm.ToggleCurrentPasswordVisibilityCommand.Execute(null);
        Assert.True(vm.IsCurrentPasswordVisible);

        Assert.False(vm.IsNewPasswordVisible);
        vm.ToggleNewPasswordVisibilityCommand.Execute(null);
        Assert.True(vm.IsNewPasswordVisible);

        Assert.False(vm.IsConfirmPasswordVisible);
        vm.ToggleConfirmPasswordVisibilityCommand.Execute(null);
        Assert.True(vm.IsConfirmPasswordVisible);
    }

    [Fact]
    public void NavigateToSync_ShouldInvokeAction()
    {
        bool syncNavigated = false;
        using var vm = new SettingsViewModel(_session, _mps, _km)
        {
            NavigateToSyncAction = () =>
            {
                syncNavigated = true;
                return Task.CompletedTask;
            }
        };

        vm.NavigateToSyncCommand.Execute(null);
        Assert.True(syncNavigated);
    }

    [Fact]
    public void MasterPasswordService_Regression_ShouldInitializeAndCheckKeyFileExistence()
    {
        // Tests the bug fix in MasterPasswordService constructor using FileWorker.ResolvePath
        var service = new MasterPasswordService(_km);
        Assert.False(service.KeyFileExists());

        service.CreateMasterPassword("TestPassword_12345!");
        Assert.True(service.KeyFileExists());

        _km.ClearLoadedKey();
        service.Login("TestPassword_12345!");
        Assert.True(_km.IsDekLoaded());
    }

    [Fact]
    public async Task DeleteVault_ShouldRemoveAllPersistentVaultArtifacts()
    {
        string[] persistentFiles = ["keys.dat", "passwords.dat", "cards.dat", "notes.dat"];
        foreach (string file in persistentFiles)
        {
            string path = FileWorker.ResolvePath(file);
            File.WriteAllText(path, "vault data content");
        }

        // Add leftover temp files and import tx directory
        string tmpFile = FileWorker.ResolvePath(".passwords.dat." + Guid.NewGuid().ToString("N") + ".tmp");
        File.WriteAllText(tmpFile, "temp content");

        string importTxDir = Path.Combine(_testDir, ".vault-import-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(importTxDir);
        File.WriteAllText(Path.Combine(importTxDir, "staged.dat"), "staged content");

        _km.CreateKeyFile("TestPassword123!");
        _session.MarkAuthenticated();
        Assert.True(_km.IsDekLoaded());

        using var vm = new SettingsViewModel(_session, _mps, _km);
        vm.OpenDeleteModal();
        Assert.True(vm.IsDeleteModalVisible);

        await vm.DeleteDatabaseAsync();

        // 1. All main vault files removed
        foreach (string file in persistentFiles)
        {
            Assert.False(File.Exists(FileWorker.ResolvePath(file)));
        }

        // 2. All temp files and leftover transaction directories cleaned
        Assert.False(File.Exists(tmpFile));
        Assert.False(Directory.Exists(importTxDir));

        // 3. Security state: DEK cleared, session locked
        Assert.False(_km.IsDekLoaded());
        Assert.False(_session.IsAuthenticated);
        Assert.False(vm.IsDeleteModalVisible);
    }

    [Fact]
    public async Task DeleteDatabase_WhenAFileIsLocked_ShouldReportFailureAndKeepSessionOpen()
    {
        _km.CreateKeyFile("TestPassword123!");
        _session.MarkAuthenticated();
        string passwordsPath = FileWorker.ResolvePath("passwords.dat");
        File.WriteAllText(passwordsPath, "vault data content");

        using var lockedFile = new FileStream(
            passwordsPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.None);
        using var vm = new SettingsViewModel(_session, _mps, _km);
        vm.OpenDeleteModal();

        await vm.DeleteDatabaseAsync();

        Assert.True(vm.IsDeleteModalVisible);
        Assert.True(vm.HasDeleteDatabaseError);
        Assert.True(_session.IsAuthenticated);
        Assert.True(_km.IsDekLoaded());
        Assert.True(File.Exists(FileWorker.ResolvePath("keys.dat")));
    }

    [Fact]
    public async Task ChangePassword_SessionLockDuringAsyncOperation_ShouldKeepModalClosedAndClearSensitiveData()
    {
        string oldPassword = "Initial_Master_1234!";
        string newPassword = "New_Master_Password_5678!";

        _km.CreateKeyFile(oldPassword);
        _session.MarkAuthenticated();

        using var vm = new SettingsViewModel(_session, _mps, _km);
        vm.OpenChangePasswordModal();

        vm.CurrentPassword = oldPassword;
        vm.NewPassword = newPassword;
        vm.ConfirmPassword = newPassword;

        // Start change password task
        var changeTask = vm.ChangePasswordAsync();

        // Simulate session lock during execution
        _session.Lock();

        await changeTask;

        // After lock during operation: modal remains closed, sensitive fields cleared
        Assert.False(vm.IsChangePasswordModalVisible);
        Assert.Equal(string.Empty, vm.CurrentPassword);
        Assert.Equal(string.Empty, vm.NewPassword);
        Assert.Equal(string.Empty, vm.ConfirmPassword);
        Assert.Equal(string.Empty, vm.ChangePasswordError);
    }

    [Fact]
    public void MultipleInstances_LifecycleAndDispose_ShouldCleanlyUnsubscribe()
    {
        _session.MarkAuthenticated();

        var vms = Enumerable.Range(0, 10)
            .Select(_ => new SettingsViewModel(_session, _mps, _km))
            .ToList();

        foreach (var vm in vms)
        {
            vm.OpenChangePasswordModal();
            vm.CurrentPassword = "secret_pass";
            Assert.True(vm.IsChangePasswordModalVisible);
        }

        // Dispose first 5
        for (int i = 0; i < 5; i++)
        {
            vms[i].Dispose();
        }

        // Lock session
        _session.Lock();

        // Active 5 have modals closed and sensitive data wiped
        for (int i = 5; i < 10; i++)
        {
            Assert.False(vms[i].IsChangePasswordModalVisible);
            Assert.Equal(string.Empty, vms[i].CurrentPassword);
            vms[i].Dispose();
        }
    }
}
