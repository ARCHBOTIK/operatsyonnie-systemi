using System.IO;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using SecurePassword.ViewModels.Base;

namespace SecurePassword.ViewModels.Settings;

/// <summary>
/// ViewModel for the Settings screen and security modal dialogs
/// (Master Password replacement and Vault deletion).
/// </summary>
public class SettingsViewModel : BaseViewModel, ISensitiveViewModel
{
    public static IReadOnlyList<string> VaultFiles => VaultImportTransaction.VaultFiles;

    private readonly VaultSessionService _vaultSession;
    private readonly MasterPasswordService _masterPasswordService;
    private readonly keyManager _keyManager;
    private readonly ILogger<SettingsViewModel>? _logger;

    private bool _isChangePasswordModalVisible;
    private bool _isDeleteModalVisible;

    private string _currentPassword = string.Empty;
    private string _newPassword = string.Empty;
    private string _confirmPassword = string.Empty;
    private string _changePasswordError = string.Empty;
    private string _deleteDatabaseError = string.Empty;

    private bool _isCurrentPasswordVisible;
    private bool _isNewPasswordVisible;
    private bool _isConfirmPasswordVisible;

    public Func<Task>? RequestLockAction { get; set; }
    public Func<Task>? NavigateToSyncAction { get; set; }
    public Func<Task>? NavigateToImportAction { get; set; }

    public SettingsViewModel(
        VaultSessionService vaultSession,
        MasterPasswordService masterPasswordService,
        keyManager keyManager,
        ILogger<SettingsViewModel>? logger = null)
    {
        _vaultSession = vaultSession ?? throw new ArgumentNullException(nameof(vaultSession));
        _masterPasswordService = masterPasswordService ?? throw new ArgumentNullException(nameof(masterPasswordService));
        _keyManager = keyManager ?? throw new ArgumentNullException(nameof(keyManager));
        _logger = logger;

        _vaultSession.StateChanged += OnSessionStateChanged;

        OpenChangePasswordCommand = new RelayCommand(OpenChangePasswordModal);
        CloseChangePasswordCommand = new RelayCommand(CloseChangePasswordModal);
        ChangePasswordCommand = new AsyncRelayCommand(ChangePasswordAsync, () => !IsBusy);

        ToggleCurrentPasswordVisibilityCommand = new RelayCommand(() => IsCurrentPasswordVisible = !IsCurrentPasswordVisible);
        ToggleNewPasswordVisibilityCommand = new RelayCommand(() => IsNewPasswordVisible = !IsNewPasswordVisible);
        ToggleConfirmPasswordVisibilityCommand = new RelayCommand(() => IsConfirmPasswordVisible = !IsConfirmPasswordVisible);

        OpenDeleteModalCommand = new RelayCommand(OpenDeleteModal);
        CloseDeleteModalCommand = new RelayCommand(CloseDeleteModal);
        DeleteDatabaseCommand = new AsyncRelayCommand(DeleteDatabaseAsync, () => !IsBusy);

        LockNowCommand = new AsyncRelayCommand(LockApplicationAsync);
        NavigateToSyncCommand = new AsyncRelayCommand(NavigateToSyncAsync);
        NavigateToImportCommand = new AsyncRelayCommand(NavigateToImportAsync);
    }

    public ICommand OpenChangePasswordCommand { get; }
    public ICommand CloseChangePasswordCommand { get; }
    public ICommand ChangePasswordCommand { get; }
    public ICommand ToggleCurrentPasswordVisibilityCommand { get; }
    public ICommand ToggleNewPasswordVisibilityCommand { get; }
    public ICommand ToggleConfirmPasswordVisibilityCommand { get; }
    public ICommand OpenDeleteModalCommand { get; }
    public ICommand CloseDeleteModalCommand { get; }
    public ICommand DeleteDatabaseCommand { get; }
    public ICommand LockNowCommand { get; }
    public ICommand NavigateToSyncCommand { get; }
    public ICommand NavigateToImportCommand { get; }

    public bool IsNotBusy => !IsBusy;

    public bool LockOnExit
    {
        get => _vaultSession.LockOnExit;
        set
        {
            if (_vaultSession.LockOnExit != value)
            {
                _vaultSession.LockOnExit = value;
                OnPropertyChanged();
            }
        }
    }

    public bool LockOnMinimize
    {
        get => _vaultSession.LockOnMinimize;
        set
        {
            if (_vaultSession.LockOnMinimize != value)
            {
                _vaultSession.LockOnMinimize = value;
                OnPropertyChanged();
            }
        }
    }

    public bool LockOnTimer
    {
        get => _vaultSession.LockOnTimer;
        set
        {
            if (_vaultSession.LockOnTimer != value)
            {
                _vaultSession.LockOnTimer = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsChangePasswordModalVisible
    {
        get => _isChangePasswordModalVisible;
        set => SetProperty(ref _isChangePasswordModalVisible, value);
    }

    public bool IsDeleteModalVisible
    {
        get => _isDeleteModalVisible;
        set => SetProperty(ref _isDeleteModalVisible, value);
    }

    public string CurrentPassword
    {
        get => _currentPassword;
        set => SetProperty(ref _currentPassword, value);
    }

    public string NewPassword
    {
        get => _newPassword;
        set => SetProperty(ref _newPassword, value);
    }

    public string ConfirmPassword
    {
        get => _confirmPassword;
        set => SetProperty(ref _confirmPassword, value);
    }

    public string ChangePasswordError
    {
        get => _changePasswordError;
        set
        {
            if (SetProperty(ref _changePasswordError, value))
            {
                OnPropertyChanged(nameof(HasChangePasswordError));
            }
        }
    }

    public bool HasChangePasswordError => !string.IsNullOrWhiteSpace(ChangePasswordError);

    public string DeleteDatabaseError
    {
        get => _deleteDatabaseError;
        set
        {
            if (SetProperty(ref _deleteDatabaseError, value))
            {
                OnPropertyChanged(nameof(HasDeleteDatabaseError));
            }
        }
    }

    public bool HasDeleteDatabaseError => !string.IsNullOrWhiteSpace(DeleteDatabaseError);

    public bool IsCurrentPasswordVisible
    {
        get => _isCurrentPasswordVisible;
        set => SetProperty(ref _isCurrentPasswordVisible, value);
    }

    public bool IsNewPasswordVisible
    {
        get => _isNewPasswordVisible;
        set => SetProperty(ref _isNewPasswordVisible, value);
    }

    public bool IsConfirmPasswordVisible
    {
        get => _isConfirmPasswordVisible;
        set => SetProperty(ref _isConfirmPasswordVisible, value);
    }

    public void OpenChangePasswordModal()
    {
        _vaultSession.RecordActivity();
        ClearSensitiveData();
        IsChangePasswordModalVisible = true;
    }

    public void CloseChangePasswordModal()
    {
        IsChangePasswordModalVisible = false;
        ClearSensitiveData();
    }

    public async Task ChangePasswordAsync()
    {
        _vaultSession.RecordActivity();
        ChangePasswordError = string.Empty;

        if (string.IsNullOrWhiteSpace(CurrentPassword) ||
            string.IsNullOrWhiteSpace(NewPassword) ||
            string.IsNullOrWhiteSpace(ConfirmPassword))
        {
            ChangePasswordError = "Заполните все поля.";
            return;
        }

        if (NewPassword != ConfirmPassword)
        {
            ChangePasswordError = "Новый пароль и подтверждение не совпадают.";
            return;
        }

        if (NewPassword.Length < 8)
        {
            ChangePasswordError = "Минимальная длина пароля — 8 символов.";
            return;
        }

        try
        {
            IsBusy = true;
            OnPropertyChanged(nameof(IsNotBusy));

            string current = CurrentPassword;
            string next = NewPassword;

            await Task.Run(() =>
            {
                _masterPasswordService.ChangeMasterPassword(current, next);
            });

            if (!_vaultSession.IsAuthenticated)
            {
                ClearSensitiveData();
                IsChangePasswordModalVisible = false;
                return;
            }

            CloseChangePasswordModal();
        }
        catch (Exception exception)
        {
            _logger?.LogError(exception, "Failed to change the master password.");
            if (!_vaultSession.IsAuthenticated)
            {
                ClearSensitiveData();
                IsChangePasswordModalVisible = false;
                return;
            }

            ChangePasswordError = "Не удалось сменить мастер-пароль. Проверьте текущий пароль.";
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(IsNotBusy));
        }
    }

    public void OpenDeleteModal()
    {
        DeleteDatabaseError = string.Empty;
        IsDeleteModalVisible = true;
    }

    public void CloseDeleteModal()
    {
        IsDeleteModalVisible = false;
        DeleteDatabaseError = string.Empty;
    }

    public async Task DeleteDatabaseAsync()
    {
        try
        {
            IsBusy = true;
            OnPropertyChanged(nameof(IsNotBusy));

            await Task.Run(() =>
            {
                VaultDataDeletion.DeleteAll();
            });

            CloseDeleteModal();
            await LockApplicationAsync();
        }
        catch (Exception exception)
        {
            _logger?.LogError(exception, "Failed to delete vault data.");
            DeleteDatabaseError = "Не удалось удалить базу данных. Закройте другие процессы, использующие файлы, и повторите попытку.";
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(IsNotBusy));
        }
    }

    public async Task LockApplicationAsync()
    {
        _keyManager.ClearLoadedKey();
        _vaultSession.Lock();
        await InvokeNavigationCallbackAsync(RequestLockAction, "Failed to close settings after session lock.");
    }

    public async Task NavigateToSyncAsync()
    {
        _vaultSession.RecordActivity();
        await InvokeNavigationCallbackAsync(NavigateToSyncAction, "Failed to navigate from settings to sync.");
    }

    public async Task NavigateToImportAsync()
    {
        _vaultSession.RecordActivity();
        await InvokeNavigationCallbackAsync(NavigateToImportAction, "Failed to navigate from settings to import.");
    }

    private async Task InvokeNavigationCallbackAsync(Func<Task>? callback, string logMessage)
    {
        if (callback is null)
            return;

        try
        {
            await callback.Invoke();
        }
        catch (Exception exception)
        {
            _logger?.LogError(exception, logMessage);
        }
    }

    public void ClearSensitiveData()
    {
        CurrentPassword = string.Empty;
        NewPassword = string.Empty;
        ConfirmPassword = string.Empty;
        ChangePasswordError = string.Empty;
        DeleteDatabaseError = string.Empty;
        IsCurrentPasswordVisible = false;
        IsNewPasswordVisible = false;
        IsConfirmPasswordVisible = false;
    }

    private void OnSessionStateChanged()
    {
        try
        {
            if (MainThread.IsMainThread)
            {
                HandleSessionStateChanged();
                return;
            }

            MainThread.BeginInvokeOnMainThread(HandleSessionStateChanged);
        }
        catch
        {
            // Fallback for non-UI test runners
            HandleSessionStateChanged();
        }
    }

    private void HandleSessionStateChanged()
    {
        if (!_vaultSession.IsAuthenticated)
        {
            ClearSensitiveData();
            IsChangePasswordModalVisible = false;
            IsDeleteModalVisible = false;
        }
        else
        {
            OnPropertyChanged(nameof(LockOnExit));
            OnPropertyChanged(nameof(LockOnMinimize));
            OnPropertyChanged(nameof(LockOnTimer));
        }
    }

    public override void Dispose()
    {
        _vaultSession.StateChanged -= OnSessionStateChanged;
        ClearSensitiveData();
        base.Dispose();
    }
}
