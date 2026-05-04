using Microsoft.AspNetCore.Components.WebView;
using Microsoft.AspNetCore.Components.WebView.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using SecurePassword.Components;

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

    private MasterPasswordMode _mode;
    private CancellationTokenSource? _syncCancellation;

    private bool _loginPasswordVisible;
    private bool _createPasswordVisible;
    private bool _createConfirmVisible;
    private bool _resetPasswordVisible;
    private bool _resetConfirmVisible;
    private bool _statusTimerStarted;
    private bool _appHostInitialized;

    public MasterPasswordPage(keyManager keyManager, VaultSessionService vaultSession, TcpBridge tcpBridge)
    {
        InitializeComponent();
        _keyManager = keyManager;
        _vaultSession = vaultSession;
        _tcpBridge = tcpBridge;
        _vaultSession.StateChanged += OnSessionStateChanged;
        UpdateImportHint();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        if (!_statusTimerStarted)
        {
            _statusTimerStarted = true;
            Dispatcher.StartTimer(TimeSpan.FromMilliseconds(300), OnStatusTimerTick);
        }

        if (_vaultSession.IsAuthenticated)
        {
            EnsureAppHostInitialized();
            AuthOverlay.IsVisible = false;
            return;
        }

        SetInitialMode();
        AuthOverlay.IsVisible = true;
    }

    private string KeyFilePath => Path.Combine(FileSystem.AppDataDirectory, KeyFileName);

    private bool OnStatusTimerTick()
    {
        UpdateCapsLockState();

        if (_vaultSession.ShouldLockForInactivity())
            PrepareForLock();

        return true;
    }

    private void OnSessionStateChanged()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            AuthOverlay.IsVisible = !_vaultSession.IsAuthenticated;

            if (_vaultSession.IsAuthenticated)
            {
                EnsureAppHostInitialized();
                return;
            }

            SetInitialMode();
        });
    }

    private void EnsureAppHostInitialized()
    {
        if (_appHostInitialized)
            return;

        void EnsureRootComponent()
        {
            bool hasRoutesRootComponent = AppHost.RootComponents.Any(component =>
                string.Equals(component.Selector, "#app", StringComparison.Ordinal) &&
                component.ComponentType == typeof(Routes));

            if (!hasRoutesRootComponent)
            {
                AppHost.RootComponents.Add(new RootComponent
                {
                    Selector = "#app",
                    ComponentType = typeof(Routes)
                });
            }

            _appHostInitialized = true;
        }

        if (MainThread.IsMainThread)
        {
            EnsureRootComponent();
            return;
        }

        MainThread.BeginInvokeOnMainThread(EnsureRootComponent);
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

        switch (mode)
        {
            case MasterPasswordMode.Login:
                TitleLabel.Text = "Вход в хранилище";
                SubtitleLabel.Text = "Введите мастер-пароль для доступа";
                if (!preserveFields)
                    LoginPasswordEntry.Text = string.Empty;
                break;

            case MasterPasswordMode.Create:
                TitleLabel.Text = "Создание хранилища";
                SubtitleLabel.Text = "Создайте новый мастер-пароль";
                if (!preserveFields)
                {
                    CreatePasswordEntry.Text = string.Empty;
                    CreateConfirmEntry.Text = string.Empty;
                }
                UpdateImportHint();
                break;

            case MasterPasswordMode.Reset:
                TitleLabel.Text = "Сброс пароля";
                SubtitleLabel.Text = "Старые данные будут удалены";
                if (!preserveFields)
                {
                    ResetPasswordEntry.Text = string.Empty;
                    ResetConfirmEntry.Text = string.Empty;
                }
                break;
        }

        ClearStatus();
        UpdateCapsLockState();
        FocusCurrentField();
    }

    private void FocusCurrentField()
    {
        Dispatcher.Dispatch(() =>
        {
            switch (_mode)
            {
                case MasterPasswordMode.Login:
                    LoginPasswordEntry.Focus();
                    break;
                case MasterPasswordMode.Create:
                    CreatePasswordEntry.Focus();
                    break;
                case MasterPasswordMode.Reset:
                    ResetPasswordEntry.Focus();
                    break;
            }
        });
    }

    private void ClearStatus()
    {
        StatusLabel.Text = string.Empty;
        StatusContainer.IsVisible = false;
    }

    private void ShowError(string message)
    {
        StatusContainer.BackgroundColor = Color.FromArgb("#FFF2F2");
        StatusLabel.TextColor = Color.FromArgb("#C62828");
        StatusLabel.Text = message;
        StatusContainer.IsVisible = true;
    }

    private void ShowSuccess(string message)
    {
        StatusContainer.BackgroundColor = Color.FromArgb("#ECFFF8");
        StatusLabel.TextColor = Color.FromArgb("#0F8B6D");
        StatusLabel.Text = message;
        StatusContainer.IsVisible = true;
    }

    private bool ValidateNewPassword(string password, string confirmPassword)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            ShowError("Введите пароль.");
            return false;
        }

        if (password.Length < MinPasswordLength)
        {
            ShowError($"Пароль должен содержать минимум {MinPasswordLength} символов.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(confirmPassword))
        {
            ShowError("Подтвердите пароль.");
            return false;
        }

        if (password != confirmPassword)
        {
            ShowError("Пароли не совпадают.");
            return false;
        }

        return true;
    }

    private void SetPasswordVisibility(Entry entry, Button button, ref bool fieldState)
    {
        fieldState = !fieldState;
        entry.IsPassword = !fieldState;
        button.Text = fieldState ? "Скрыть" : "Показать";
    }

    private void UpdateImportHint()
    {
        ImportAddressLabel.Text = _tcpBridge.GetPeerAddressHint();
    }

    private void OnImportClicked(object sender, EventArgs e)
    {
        ImportPanel.IsVisible = !ImportPanel.IsVisible;
        UpdateImportHint();

        if (!ImportPanel.IsVisible)
            HideSyncStatus();
    }

    private async void OnReceiveDatabaseClicked(object sender, EventArgs e)
    {
        HideSyncStatus();
        UpdateImportHint();

        SyncButton.IsEnabled = false;
        SyncCancelButton.IsVisible = true;
        _syncCancellation = new CancellationTokenSource();
        ShowSyncStatus("Ожидание второго устройства...");

        try
        {
            var result = await _tcpBridge.ReceiveVaultFromPeerAsync(_syncCancellation.Token);
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
            _syncCancellation?.Dispose();
            _syncCancellation = null;
            SyncButton.IsEnabled = true;
            SyncCancelButton.IsVisible = false;
        }
    }

    private void OnCancelSyncClicked(object sender, EventArgs e)
    {
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

        SetMode(MasterPasswordMode.Create, preserveFields: true);
        CreatePasswordEntry.Text = newPassword;
        CreateConfirmEntry.Text = confirm;

        ShowSuccess("Старые данные удалены. Теперь создайте новое хранилище или импортируйте базу.");
    }

    private void DeleteVaultFiles()
    {
        string[] files = ["keys.dat", "passwords.dat", "cards.dat", "notes.dat"];

        foreach (string file in files)
        {
            string path = Path.Combine(FileSystem.AppDataDirectory, file);
            if (File.Exists(path))
                File.Delete(path);
        }
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
        CapsLockContainer.IsVisible = IsCapsLockOnWindows() && AuthOverlay.IsVisible;
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
        AuthOverlay.IsVisible = true;
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

        EnsureAppHostInitialized();
        ClearStatus();
        ClearPasswordFields();
        ImportPanel.IsVisible = false;
        _vaultSession.MarkAuthenticated();
        AuthOverlay.IsVisible = false;
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

    private void blazorWebView_BlazorWebViewInitializing(object sender, BlazorWebViewInitializingEventArgs e)
    {
    }

    private void blazorWebView_BlazorWebViewInitialized(object sender, BlazorWebViewInitializedEventArgs e)
    {
#if WINDOWS
        if (e.WebView is Microsoft.UI.Xaml.Controls.WebView2 webView && webView.CoreWebView2 is not null)
            webView.CoreWebView2.Settings.IsZoomControlEnabled = false;
#endif
    }
}
