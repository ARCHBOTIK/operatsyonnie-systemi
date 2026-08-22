using System.IO;
using SecurePassword.Navigation;
#if WINDOWS
using System.Runtime.InteropServices;
#endif

namespace SecurePassword;

public enum MasterPasswordMode
{
    Login,
    Create,
    Reset
}

public partial class MasterPasswordPage : ContentPage
{
    private const string KeyFileName = "keys.dat";
    private const int MinPasswordLength = 8;

    private readonly keyManager _keyManager;
    private readonly VaultSessionService _vaultSession;
    private readonly TcpBridge _tcpBridge;
    private readonly IAppRootNavigator _rootNavigator;

    private MasterPasswordMode _mode;
    private CancellationTokenSource? _syncCancellation;
    private PairingSecret? _activeReceiverSecret;

    private bool _loginPasswordVisible;
    private bool _createPasswordVisible;
    private bool _createConfirmVisible;
    private bool _resetPasswordVisible;
    private bool _resetConfirmVisible;
    private bool _statusTimerStarted;

    public MasterPasswordPage(
        keyManager keyManager,
        VaultSessionService vaultSession,
        TcpBridge tcpBridge,
        IAppRootNavigator rootNavigator)
    {
        InitializeComponent();
        _keyManager = keyManager ?? throw new ArgumentNullException(nameof(keyManager));
        _vaultSession = vaultSession ?? throw new ArgumentNullException(nameof(vaultSession));
        _tcpBridge = tcpBridge ?? throw new ArgumentNullException(nameof(tcpBridge));
        _rootNavigator = rootNavigator ?? throw new ArgumentNullException(nameof(rootNavigator));

        UpdateImportHint();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        _vaultSession.StateChanged += OnSessionStateChanged;

        if (!_statusTimerStarted)
        {
            _statusTimerStarted = true;
            Dispatcher.StartTimer(TimeSpan.FromMilliseconds(300), OnStatusTimerTick);
        }

        if (_vaultSession.IsAuthenticated)
        {
            _rootNavigator.ShowUnlockedRoot();
            return;
        }

        SetInitialMode();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _vaultSession.StateChanged -= OnSessionStateChanged;
        _statusTimerStarted = false;
        _syncCancellation?.Cancel();
        _activeReceiverSecret?.Dispose();
        _activeReceiverSecret = null;
        ClearPasswordFields();
    }

    private string KeyFilePath => Path.Combine(FileSystem.AppDataDirectory, KeyFileName);

    private bool OnStatusTimerTick()
    {
        if (!_statusTimerStarted)
            return false;

        UpdateCapsLockState();

        if (_vaultSession.ShouldLockForInactivity())
            PrepareForLock();

        return _statusTimerStarted;
    }

    private void OnSessionStateChanged()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (_vaultSession.IsAuthenticated)
            {
                _rootNavigator.ShowUnlockedRoot();
                return;
            }

            SetInitialMode();
        });
    }

    private void SetInitialMode()
    {
        bool hasKeyFile = File.Exists(KeyFilePath);
        SetMode(hasKeyFile ? MasterPasswordMode.Login : MasterPasswordMode.Create);
    }

    private void SetMode(MasterPasswordMode mode, bool preserveFields = false)
    {
        _mode = mode;

        LoginBlock.IsVisible = mode == MasterPasswordMode.Login;
        CreateBlock.IsVisible = mode == MasterPasswordMode.Create;
        ResetBlock.IsVisible = mode == MasterPasswordMode.Reset;
        ImportPanel.IsVisible = false;
        HideSyncStatus();

        if (!preserveFields)
            ClearPasswordFields();

        ClearStatus();

        switch (mode)
        {
            case MasterPasswordMode.Login:
                TitleLabel.Text = "Вход в хранилище";
                SubtitleLabel.Text = "Введите мастер-пароль для расшифровки данных";
                break;
            case MasterPasswordMode.Create:
                TitleLabel.Text = "Создание хранилища";
                SubtitleLabel.Text = "Придумайте надёжный мастер-пароль (от 8 символов)";
                break;
            case MasterPasswordMode.Reset:
                TitleLabel.Text = "Сброс хранилища";
                SubtitleLabel.Text = "Внимание: все текущие данные будут удалены";
                break;
        }

        UpdateImportHint();
    }

    private void UpdateImportHint()
    {
        bool hasKeyFile = File.Exists(KeyFilePath);
        ImportHintLabel.Text = hasKeyFile
            ? "Импорт перезапишет существующую базу данных и ключ."
            : "Если у вас есть база на другом устройстве, можно перенести её по локальной сети.";
    }

    private void OnToggleImportClicked(object sender, EventArgs e)
    {
        ImportPanel.IsVisible = !ImportPanel.IsVisible;
        if (!ImportPanel.IsVisible)
            HideSyncStatus();
    }

    private async void OnReceiveSyncClicked(object sender, EventArgs e)
    {
        SyncButton.IsEnabled = false;
        SyncCancelButton.IsVisible = true;
        _activeReceiverSecret?.Dispose();
        _activeReceiverSecret = PairingSecret.Generate();


        _syncCancellation?.Cancel();
        _syncCancellation?.Dispose();
        _syncCancellation = new CancellationTokenSource();
        ShowSyncStatus($"Код: {_activeReceiverSecret.FormattedCode}. Ожидание подключения...");

        try
        {
            var result = await _tcpBridge.ReceiveVaultFromPeerAsync(_activeReceiverSecret, _syncCancellation.Token);
            if (result.Success)
            {
                SetMode(MasterPasswordMode.Login);
                ShowSuccess("База данных получена. Теперь введите мастер-пароль для входа.");
                ShowSyncStatus(result.Message, isSuccess: true);
            }
            else
            {
                ShowSyncStatus(result.Message, isError: !result.Cancelled);
            }
        }
        finally
        {
            _activeReceiverSecret?.Dispose();
            _activeReceiverSecret = null;
            _syncCancellation?.Dispose();
            _syncCancellation = null;
            SyncButton.IsEnabled = true;
            SyncCancelButton.IsVisible = false;
        }
    }

    private void OnCancelSyncClicked(object sender, EventArgs e)
    {
        _activeReceiverSecret?.Dispose();
        _activeReceiverSecret = null;
        _syncCancellation?.Cancel();
        ShowSyncStatus("Приём базы отменён.");
    }

    private async void OnLoginClicked(object sender, EventArgs e)
    {
        await TryLoginAsync();
    }

    private async void OnLoginCompleted(object sender, EventArgs e)
    {
        await TryLoginAsync();
    }

    private async Task TryLoginAsync()
    {
        ClearStatus();

        string password = LoginPasswordEntry.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(password))
        {
            ShowError("Введите мастер-пароль.");
            return;
        }

        try
        {
            _keyManager.LoadKeyFile(password);
            OpenVault();
        }
        catch
        {
            LoginPasswordEntry.Text = string.Empty;
            ShowError("Неверный пароль.");
            await Task.CompletedTask;
        }
    }

    private void OnGoResetClicked(object sender, EventArgs e)
    {
        SetMode(MasterPasswordMode.Reset);
    }

    private void OnBackToLoginClicked(object sender, EventArgs e)
    {
        SetMode(MasterPasswordMode.Login);
    }

    private async void OnCreateClicked(object sender, EventArgs e)
    {
        await TryCreateAsync();
    }

    private void OnCreatePasswordCompleted(object sender, EventArgs e)
    {
        CreateConfirmEntry.Focus();
    }

    private async void OnCreateCompleted(object sender, EventArgs e)
    {
        await TryCreateAsync();
    }

    private async Task TryCreateAsync()
    {
        ClearStatus();

        string password = CreatePasswordEntry.Text ?? string.Empty;
        string confirm = CreateConfirmEntry.Text ?? string.Empty;

        if (!ValidateNewPassword(password, confirm))
            return;

        try
        {
            _keyManager.CreateKeyFile(password);
            OpenVault();
        }
        catch
        {
            ShowError("Не удалось создать хранилище.");
            await Task.CompletedTask;
        }
    }

    private void OnResetPasswordCompleted(object sender, EventArgs e)
    {
        ResetConfirmEntry.Focus();
    }

    private async void OnResetCompleted(object sender, EventArgs e)
    {
        await ConfirmAndResetAsync();
    }

    private async void OnResetClicked(object sender, EventArgs e)
    {
        await ConfirmAndResetAsync();
    }

    private async Task ConfirmAndResetAsync()
    {
        ClearStatus();

        string newPassword = ResetPasswordEntry.Text ?? string.Empty;
        string confirm = ResetConfirmEntry.Text ?? string.Empty;

        if (!ValidateNewPassword(newPassword, confirm))
            return;

        bool confirmed = await DisplayAlertAsync(
            "Подтверждение сброса",
            "Сброс удалит сохранённые пароли, карты, заметки и старый ключ. Продолжить?",
            "Удалить данные",
            "Отмена");

        if (!confirmed)
            return;

        DeleteVaultFiles();
        _keyManager.ClearLoadedKey();
        _vaultSession.Lock();
        ClearPasswordFields();

        SetMode(MasterPasswordMode.Create, preserveFields: false);
        ShowSuccess("Старые данные удалены. Теперь создайте новое хранилище или импортируйте базу.");
    }

    private void DeleteVaultFiles()
    {
        string[] files = ["keys.dat", "passwords.dat", "cards.dat", "notes.dat"];

        foreach (string file in files)
        {
            string path = Path.Combine(FileSystem.AppDataDirectory, file);
            if (File.Exists(path))
            {
                try { File.Delete(path); } catch { }
            }
        }

        FileWorker.CleanupLeftoverTempFiles();
        try
        {
            var txDirs = Directory.GetDirectories(FileSystem.AppDataDirectory, ".vault-import-*");
            foreach (var dir in txDirs)
            {
                try { Directory.Delete(dir, recursive: true); } catch { }
            }
        }
        catch { }
    }

    private void OnToggleLoginPasswordClicked(object sender, EventArgs e)
    {
        SetPasswordVisibility(LoginPasswordEntry, LoginToggleButton, ref _loginPasswordVisible);
    }

    private void OnToggleCreatePasswordClicked(object sender, EventArgs e)
    {
        SetPasswordVisibility(CreatePasswordEntry, CreatePasswordToggleButton, ref _createPasswordVisible);
    }

    private void OnToggleCreateConfirmClicked(object sender, EventArgs e)
    {
        SetPasswordVisibility(CreateConfirmEntry, CreateConfirmToggleButton, ref _createConfirmVisible);
    }

    private void OnToggleResetPasswordClicked(object sender, EventArgs e)
    {
        SetPasswordVisibility(ResetPasswordEntry, ResetPasswordToggleButton, ref _resetPasswordVisible);
    }

    private void OnToggleResetConfirmClicked(object sender, EventArgs e)
    {
        SetPasswordVisibility(ResetConfirmEntry, ResetConfirmToggleButton, ref _resetConfirmVisible);
    }

    private void UpdateCapsLockState()
    {
#if WINDOWS
        CapsLockContainer.IsVisible = IsCapsLockOnWindows();
#else
        CapsLockContainer.IsVisible = false;
#endif
    }

#if WINDOWS
    [DllImport("user32.dll")]
    private static extern short GetKeyState(int nVirtKey);

    private static bool IsCapsLockOnWindows()
    {
        const int VK_CAPITAL = 0x14;
        return (GetKeyState(VK_CAPITAL) & 0x0001) != 0;
    }
#endif

    public void PrepareForLock()
    {
        _syncCancellation?.Cancel();
        _keyManager.ClearLoadedKey();
        _vaultSession.Lock();
        ClearPasswordFields();
        SetInitialMode();
    }

    private void ClearPasswordFields()
    {
        LoginPasswordEntry.Text = string.Empty;
        CreatePasswordEntry.Text = string.Empty;
        CreateConfirmEntry.Text = string.Empty;
        ResetPasswordEntry.Text = string.Empty;
        ResetConfirmEntry.Text = string.Empty;
    }

    private void OpenVault()
    {
        if (!_keyManager.IsDekLoaded())
            throw new InvalidOperationException("DEK was not loaded. Call LoadKeyFile first.");

        ClearStatus();
        ClearPasswordFields();
        ImportPanel.IsVisible = false;
        _vaultSession.MarkAuthenticated();
        _rootNavigator.ShowUnlockedRoot();
    }

    private void HideSyncStatus()
    {
        SyncStatusLabel.Text = string.Empty;
        SyncStatusContainer.IsVisible = false;
    }

    private void ShowSyncStatus(string message, bool isError = false, bool isSuccess = false)
    {
        SyncStatusContainer.BackgroundColor = isError
            ? Color.FromArgb("#FFF2F2")
            : isSuccess
                ? Color.FromArgb("#ECFFF8")
                : Color.FromArgb("#EEF8FF");

        SyncStatusLabel.TextColor = isError
            ? Color.FromArgb("#C62828")
            : isSuccess
                ? Color.FromArgb("#0F8B6D")
                : Color.FromArgb("#1D4ED8");

        SyncStatusLabel.Text = message;
        SyncStatusContainer.IsVisible = true;
    }

    private static void SetPasswordVisibility(Entry entry, Button button, ref bool isVisible)
    {
        isVisible = !isVisible;
        entry.IsPassword = !isVisible;
        button.Text = isVisible ? "Скрыть" : "Показать";
    }

    private bool ValidateNewPassword(string password, string confirm)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            ShowError("Введите мастер-пароль.");
            return false;
        }

        if (password.Length < MinPasswordLength)
        {
            ShowError($"Мастер-пароль должен быть не короче {MinPasswordLength} символов.");
            return false;
        }

        if (password != confirm)
        {
            ShowError("Пароли не совпадают.");
            return false;
        }

        return true;
    }

    private void ShowError(string message)
    {
        ErrorLabel.Text = message;
        ErrorContainer.IsVisible = true;
        StatusContainer.IsVisible = false;
    }

    private void ShowSuccess(string message)
    {
        StatusLabel.Text = message;
        StatusContainer.IsVisible = true;
        ErrorContainer.IsVisible = false;
    }

    private void ClearStatus()
    {
        ErrorLabel.Text = string.Empty;
        ErrorContainer.IsVisible = false;
        StatusLabel.Text = string.Empty;
        StatusContainer.IsVisible = false;
    }

    private Task<bool> DisplayAlertAsync(string title, string message, string accept, string cancel)
    {
        return DisplayAlert(title, message, accept, cancel);
    }
}
