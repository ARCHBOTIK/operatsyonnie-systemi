using System;
using System.IO;
using Microsoft.Maui.Graphics;

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
    private readonly MainPage _mainPage;

    private MasterPasswordMode _mode;

    private bool _loginPasswordVisible;
    private bool _createPasswordVisible;
    private bool _createConfirmVisible;
    private bool _resetPasswordVisible;
    private bool _resetConfirmVisible;

    private bool _capsTimerStarted;

    public MasterPasswordPage(keyManager keyManager, MainPage mainPage)
    {
        InitializeComponent();
        _keyManager = keyManager;
        _mainPage = mainPage;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        if (!_capsTimerStarted)
        {
            _capsTimerStarted = true;

            Dispatcher.StartTimer(TimeSpan.FromMilliseconds(300), () =>
            {
                UpdateCapsLockState();
                return true;
            });
        }

        SetInitialMode();
    }

    private string KeyFilePath => Path.Combine(FileSystem.AppDataDirectory, KeyFileName);

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
            ShowError("Введите пароль");
            return false;
        }

        if (password.Length < MinPasswordLength)
        {
            ShowError($"Пароль должен содержать минимум {MinPasswordLength} символов");
            return false;
        }

        if (string.IsNullOrWhiteSpace(confirmPassword))
        {
            ShowError("Подтвердите пароль");
            return false;
        }

        if (password != confirmPassword)
        {
            ShowError("Пароли не совпадают");
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

    private async void OnInfoClicked(object sender, EventArgs e)
    {
        string title;
        string message;

        switch (_mode)
        {
            case MasterPasswordMode.Login:
                title = "О входе";
                message =
                    "Это локальное хранилище.\n\n" +
                    "Если файл keys.dat уже существует, приложение показывает форму входа. " +
                    "Для доступа нужен мастер-пароль, который использовался при создании хранилища.";
                break;

            case MasterPasswordMode.Create:
                title = "О создании";
                message =
                    "Хранилище создаётся локально на устройстве.\n\n" +
                    "После создания будет сформирован файл keys.dat. " +
                    "Мастер-пароль восстановить невозможно, поэтому его нужно сохранить в надёжном месте.";
                break;

            default:
                title = "О сбросе";
                message =
                    "Сброс пароля не восстанавливает старый доступ.\n\n" +
                    "При подтверждении будут удалены keys.dat, passwords.dat, cards.dat и notes.dat. " +
                    "После этого можно создать новое пустое хранилище или выполнить импорт резервной копии.";
                break;
        }

        await DisplayAlert(title, message, "Понятно");
    }

    // ===== LOGIN =====

    private void OnLoginClicked(object sender, EventArgs e)
    {
        TryLogin();
    }

    private void OnLoginCompleted(object sender, EventArgs e)
    {
        TryLogin();
    }

    private void TryLogin()
    {
        ClearStatus();

        string password = LoginPasswordEntry.Text?.Trim() ?? string.Empty;

        if (string.IsNullOrEmpty(password))
        {
            ShowError("Введите мастер-пароль");
            return;
        }

        try
        {
            _keyManager.LoadKeyFile(password);
            OpenMain();
        }
        catch
        {
            ShowError("Неверный пароль");
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

    // ===== CREATE =====

    private void OnCreateClicked(object sender, EventArgs e)
    {
        TryCreate();
    }

    private void OnCreatePasswordCompleted(object sender, EventArgs e)
    {
        CreateConfirmEntry.Focus();
    }

    private void OnCreateCompleted(object sender, EventArgs e)
    {
        TryCreate();
    }

    private void TryCreate()
    {
        ClearStatus();

        string password = CreatePasswordEntry.Text ?? string.Empty;
        string confirm = CreateConfirmEntry.Text ?? string.Empty;

        if (!ValidateNewPassword(password, confirm))
            return;

        try
        {
            _keyManager.CreateKeyFile(password);
            OpenMain();
        }
        catch
        {
            ShowError("Не удалось создать хранилище");
        }
    }

    // ===== RESET =====

    private void OnResetPasswordCompleted(object sender, EventArgs e)
    {
        ResetConfirmEntry.Focus();
    }

    private void OnResetCompleted(object sender, EventArgs e)
    {
        _ = ConfirmAndResetAsync();
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

        bool confirmed = await DisplayAlert(
            "Подтверждение сброса",
            "Сброс пароля удалит все сохранённые данные: пароли, карты, заметки и старый ключ. Продолжить?",
            "Удалить данные",
            "Отмена");

        if (!confirmed)
            return;

        DeleteVaultFiles();

        // Переводим пользователя на стартовый экран создания/импорта.
        SetMode(MasterPasswordMode.Create, preserveFields: true);
        CreatePasswordEntry.Text = newPassword;
        CreateConfirmEntry.Text = confirm;

        ShowSuccess("Старые данные удалены. Теперь создайте новое хранилище или выполните импорт.");
    }

    private void DeleteVaultFiles()
    {
        string baseDir = FileSystem.AppDataDirectory;

        string[] files =
        {
            "keys.dat",
            "passwords.dat",
            "cards.dat",
            "notes.dat"
        };

        foreach (string file in files)
        {
            string path = Path.Combine(baseDir, file);
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    // ===== IMPORT =====

    private async void OnImportClicked(object sender, EventArgs e)
    {
        await DisplayAlert("Импорт", "Пока не реализовано", "OK");
    }

    // ===== PASSWORD VISIBILITY =====

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

    // ===== CAPS LOCK =====

    private void UpdateCapsLockState()
    {
#if WINDOWS
        bool isCapsLockOn = IsCapsLockOnWindows();
        CapsLockContainer.IsVisible = isCapsLockOn;
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

    // ===== NAVIGATION =====

    private void OpenMain()
    {
        Application.Current.Windows[0].Page = _mainPage;
    }
}