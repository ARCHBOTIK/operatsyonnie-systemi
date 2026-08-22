using System.Text.RegularExpressions;
using System.Windows.Input;
using SecurePassword.ViewModels.Base;


namespace SecurePassword.ViewModels.Vault;

/// <summary>
/// ViewModel for creating (Add) or editing (Edit) vault entries.
/// Sanitizes and zeroes sensitive form inputs upon Save, Cancel, or Lock.
/// </summary>
public sealed class ItemEditViewModel : BaseViewModel, ISensitiveViewModel
{
    private readonly SecureRepository<PasswordEntry> _passwordRepo;
    private readonly SecureRepository<CardEntry> _cardRepo;
    private readonly SecureRepository<NoteEntry> _noteRepo;
    private readonly VaultSessionService _vaultSession;

    private bool _isEditMode;
    private int? _itemId;
    private VaultItemType _selectedType = VaultItemType.Password;

    // Password form fields
    private string _passwordTitle = string.Empty;
    private string _login = string.Empty;
    private string _password = string.Empty;
    private string _serviceName = string.Empty;
    private bool _showPassword;

    // Card form fields
    private string _cardTitle = string.Empty;
    private string _cardNumber = string.Empty;
    private string _cardHolder = string.Empty;
    private string _expiryDate = string.Empty;
    private string _cvv = string.Empty;
    private string _bankName = string.Empty;
    private bool _showCvv;

    // Note form fields
    private string _noteTitle = string.Empty;
    private string _noteContent = string.Empty;

    // Validation & busy
    private string _validationError = string.Empty;
    private bool _isSaving;
    private bool _disposed;

    // Navigation and orchestration callbacks
    public Action? ItemSavedAction { get; set; }
    public Action? CloseAction { get; set; }
    public Action? RequestLockAction { get; set; }

    public ItemEditViewModel(
        SecureRepository<PasswordEntry> passwordRepo,
        SecureRepository<CardEntry> cardRepo,
        SecureRepository<NoteEntry> noteRepo,
        VaultSessionService vaultSession)
    {
        _passwordRepo = passwordRepo ?? throw new ArgumentNullException(nameof(passwordRepo));
        _cardRepo = cardRepo ?? throw new ArgumentNullException(nameof(cardRepo));
        _noteRepo = noteRepo ?? throw new ArgumentNullException(nameof(noteRepo));
        _vaultSession = vaultSession ?? throw new ArgumentNullException(nameof(vaultSession));

        _vaultSession.StateChanged += OnSessionStateChanged;

        SelectTypeCommand = new RelayCommand(p =>
        {
            if (p is VaultItemType type) SelectType(type);
            else if (Enum.TryParse<VaultItemType>(p?.ToString(), true, out var parsed)) SelectType(parsed);
        });

        TogglePasswordVisibilityCommand = new RelayCommand(() => ShowPassword = !ShowPassword);
        ToggleCvvVisibilityCommand = new RelayCommand(() => ShowCvv = !ShowCvv);
        GeneratePasswordCommand = new RelayCommand(GeneratePassword);
        SaveCommand = new AsyncRelayCommand(SaveAsync, () => !IsBusy);
        CancelCommand = new RelayCommand(Cancel);
    }

    // ─── Commands ──────────────────────────────────────────────────────────────

    public ICommand SelectTypeCommand { get; }
    public ICommand TogglePasswordVisibilityCommand { get; }
    public ICommand ToggleCvvVisibilityCommand { get; }
    public ICommand GeneratePasswordCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand CancelCommand { get; }

    // ─── Properties ────────────────────────────────────────────────────────────

    public bool IsEditMode
    {
        get => _isEditMode;
        private set
        {
            if (SetProperty(ref _isEditMode, value))
            {
                OnPropertyChanged(nameof(PageTitle));
                OnPropertyChanged(nameof(SaveButtonText));
                OnPropertyChanged(nameof(CanChangeType));
            }
        }
    }

    public int? ItemId
    {
        get => _itemId;
        private set => SetProperty(ref _itemId, value);
    }

    public VaultItemType SelectedType
    {
        get => _selectedType;
        set
        {
            if (SetProperty(ref _selectedType, value))
            {
                OnPropertyChanged(nameof(IsPasswordType));
                OnPropertyChanged(nameof(IsCardType));
                OnPropertyChanged(nameof(IsNoteType));
            }
        }
    }

    public bool IsPasswordType => _selectedType == VaultItemType.Password;
    public bool IsCardType => _selectedType == VaultItemType.Card;
    public bool IsNoteType => _selectedType == VaultItemType.Note;
    public bool CanChangeType => !_isEditMode;

    public string PageTitle => _isEditMode ? "Редактирование записи" : "Новая запись";
    public string SaveButtonText => _isEditMode ? "Сохранить изменения" : "Добавить в хранилище";

    // Password Form
    public string PasswordTitle
    {
        get => _passwordTitle;
        set => SetProperty(ref _passwordTitle, value);
    }

    public string Login
    {
        get => _login;
        set => SetProperty(ref _login, value);
    }

    public string Password
    {
        get => _password;
        set => SetProperty(ref _password, value);
    }

    public string ServiceName
    {
        get => _serviceName;
        set => SetProperty(ref _serviceName, value);
    }

    public bool ShowPassword
    {
        get => _showPassword;
        set => SetProperty(ref _showPassword, value);
    }

    // Card Form
    public string CardTitle
    {
        get => _cardTitle;
        set => SetProperty(ref _cardTitle, value);
    }

    public string CardNumber
    {
        get => _cardNumber;
        set => SetProperty(ref _cardNumber, value);
    }

    public string CardHolder
    {
        get => _cardHolder;
        set => SetProperty(ref _cardHolder, value);
    }

    public string ExpiryDate
    {
        get => _expiryDate;
        set => SetProperty(ref _expiryDate, value);
    }

    public string Cvv
    {
        get => _cvv;
        set => SetProperty(ref _cvv, value);
    }

    public string BankName
    {
        get => _bankName;
        set => SetProperty(ref _bankName, value);
    }

    public bool ShowCvv
    {
        get => _showCvv;
        set => SetProperty(ref _showCvv, value);
    }

    // Note Form
    public string NoteTitle
    {
        get => _noteTitle;
        set => SetProperty(ref _noteTitle, value);
    }

    public string NoteContent
    {
        get => _noteContent;
        set => SetProperty(ref _noteContent, value);
    }

    public string ValidationError
    {
        get => _validationError;
        set
        {
            if (SetProperty(ref _validationError, value))
            {
                OnPropertyChanged(nameof(HasValidationError));
            }
        }
    }

    public bool HasValidationError => !string.IsNullOrEmpty(_validationError);

    // ─── Initialisation ────────────────────────────────────────────────────────

    public void InitializeForAdd(VaultItemType defaultType = VaultItemType.Password)
    {
        ClearSensitiveData();
        IsEditMode = false;
        ItemId = null;
        SelectedType = defaultType;
    }

    public void InitializeForEdit(int id, VaultItemType type)
    {
        ClearSensitiveData();
        IsEditMode = true;
        ItemId = id;
        SelectedType = type;

        if (!_vaultSession.IsAuthenticated) return;

        switch (type)
        {
            case VaultItemType.Password:
                var p = _passwordRepo.GetItemById(id);
                if (p is not null)
                {
                    PasswordTitle = p.Title;
                    Login = p.Login;
                    Password = p.Password;
                    ServiceName = p.ServiceName;
                }
                break;

            case VaultItemType.Card:
                var c = _cardRepo.GetItemById(id);
                if (c is not null)
                {
                    CardTitle = c.Title;
                    CardNumber = FormatCardNumberForInput(c.CardNumber);
                    CardHolder = c.CardHolder;
                    ExpiryDate = c.ExpiryDate;
                    Cvv = c.Cvv;
                    BankName = c.BankName;
                }
                break;

            case VaultItemType.Note:
                var n = _noteRepo.GetItemById(id);
                if (n is not null)
                {
                    NoteTitle = n.Title;
                    NoteContent = n.Content;
                }
                break;
        }
    }

    public void SelectType(VaultItemType newType)
    {
        if (_isEditMode || _selectedType == newType)
            return;

        _vaultSession.RecordActivity();

        // Clear sensitive fields of previous type
        switch (_selectedType)
        {
            case VaultItemType.Password:
                PasswordTitle = string.Empty;
                Login = string.Empty;
                Password = string.Empty;
                ServiceName = string.Empty;
                ShowPassword = false;
                break;

            case VaultItemType.Card:
                CardTitle = string.Empty;
                CardNumber = string.Empty;
                CardHolder = string.Empty;
                ExpiryDate = string.Empty;
                Cvv = string.Empty;
                BankName = string.Empty;
                ShowCvv = false;
                break;

            case VaultItemType.Note:
                NoteTitle = string.Empty;
                NoteContent = string.Empty;
                break;
        }

        ValidationError = string.Empty;
        SelectedType = newType;
    }

    private void GeneratePassword()
    {
        _vaultSession.RecordActivity();
        Password = PasswordGenerator.GeneratePassword(useLowercase: true, useUppercase: true, useDigits: true, useSpecial: true, passwordLength: 16);
        ShowPassword = true;
    }


    // ─── Save & Cancel ─────────────────────────────────────────────────────────

    public async Task SaveAsync()
    {
        if (_isSaving || !_vaultSession.IsAuthenticated) return;

        ValidationError = string.Empty;
        _vaultSession.RecordActivity();

        // 1. Validation
        if (!ValidateForm())
            return;

        _isSaving = true;
        IsBusy = true;

        try
        {
            await Task.Run(() =>
            {
                switch (SelectedType)
                {
                    case VaultItemType.Password:
                        SavePassword();
                        break;
                    case VaultItemType.Card:
                        SaveCard();
                        break;
                    case VaultItemType.Note:
                        SaveNote();
                        break;
                }
            });

            if (!_vaultSession.IsAuthenticated)
            {
                ClearSensitiveData();
                return;
            }

            ClearSensitiveData();
            ItemSavedAction?.Invoke();
        }
        catch (Exception ex)
        {
            ValidationError = $"Ошибка при сохранении: {ex.Message}";
        }
        finally
        {
            _isSaving = false;
            IsBusy = false;
        }
    }

    private bool ValidateForm()
    {
        switch (SelectedType)
        {
            case VaultItemType.Password:
                if (string.IsNullOrWhiteSpace(PasswordTitle))
                {
                    ValidationError = "Укажите название записи.";
                    return false;
                }
                if (string.IsNullOrWhiteSpace(Login))
                {
                    ValidationError = "Укажите логин или email.";
                    return false;
                }
                if (string.IsNullOrEmpty(Password))
                {
                    ValidationError = "Укажите пароль.";
                    return false;
                }
                return true;

            case VaultItemType.Card:
                if (string.IsNullOrWhiteSpace(CardTitle))
                {
                    ValidationError = "Укажите название карты.";
                    return false;
                }
                string rawDigits = new(CardNumber.Where(char.IsDigit).ToArray());
                if (string.IsNullOrWhiteSpace(rawDigits) || rawDigits.Length < 12)
                {
                    ValidationError = "Номер карты должен содержать минимум 12 цифр.";
                    return false;
                }
                if (!string.IsNullOrWhiteSpace(ExpiryDate) && !Regex.IsMatch(ExpiryDate.Trim(), @"^\d{2}/\d{2}$|^\d{4}$"))
                {
                    ValidationError = "Срок действия карты должен быть в формате MM/YY.";
                    return false;
                }
                return true;

            case VaultItemType.Note:
                if (string.IsNullOrWhiteSpace(NoteTitle) && string.IsNullOrWhiteSpace(NoteContent))
                {
                    ValidationError = "Укажите название или текст заметки.";
                    return false;
                }
                return true;

            default:
                return false;
        }
    }

    private void SavePassword()
    {
        string normalizedTitle = PasswordTitle.Trim();
        string normalizedLogin = Login.Trim();
        string normalizedServiceName = string.IsNullOrWhiteSpace(ServiceName)
            ? normalizedTitle
            : ServiceName.Trim();

        if (_isEditMode && _itemId.HasValue)
        {
            var existing = _passwordRepo.GetItemById(_itemId.Value);
            var entry = new PasswordEntry
            {
                Id = _itemId.Value,
                Title = normalizedTitle,
                Login = normalizedLogin,
                Password = Password,
                ServiceName = normalizedServiceName,
                CreatedAt = existing?.CreatedAt ?? DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _passwordRepo.Update(entry);
        }
        else
        {
            var all = _passwordRepo.getAll().ToList();
            int newId = all.Count > 0 ? all.Max(x => x.Id) + 1 : 1;
            var entry = new PasswordEntry
            {
                Id = newId,
                Title = normalizedTitle,
                Login = normalizedLogin,
                Password = Password,
                ServiceName = normalizedServiceName,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _passwordRepo.Add(entry);
        }

        _passwordRepo.Save();
    }

    private void SaveCard()
    {
        string normalizedTitle = CardTitle.Trim();
        string rawDigits = new(CardNumber.Where(char.IsDigit).ToArray());
        string normalizedHolder = CardHolder.Trim();
        string normalizedExpiry = ExpiryDate.Trim();
        string normalizedCvv = new(Cvv.Where(char.IsDigit).ToArray());
        string normalizedBank = BankName.Trim();

        if (_isEditMode && _itemId.HasValue)
        {
            var existing = _cardRepo.GetItemById(_itemId.Value);
            var entry = new CardEntry
            {
                Id = _itemId.Value,
                Title = normalizedTitle,
                CardNumber = rawDigits,
                CardHolder = normalizedHolder,
                ExpiryDate = normalizedExpiry,
                Cvv = normalizedCvv,
                BankName = string.IsNullOrWhiteSpace(normalizedBank) ? (existing?.BankName ?? string.Empty) : normalizedBank,
                CreatedAt = existing?.CreatedAt ?? DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _cardRepo.Update(entry);
        }
        else
        {
            var all = _cardRepo.getAll().ToList();
            int newId = all.Count > 0 ? all.Max(x => x.Id) + 1 : 1;
            var entry = new CardEntry
            {
                Id = newId,
                Title = normalizedTitle,
                CardNumber = rawDigits,
                CardHolder = normalizedHolder,
                ExpiryDate = normalizedExpiry,
                Cvv = normalizedCvv,
                BankName = normalizedBank,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _cardRepo.Add(entry);
        }

        _cardRepo.Save();
    }

    private void SaveNote()
    {
        string normalizedTitle = string.IsNullOrWhiteSpace(NoteTitle)
            ? "Новая заметка"
            : NoteTitle.Trim();

        if (_isEditMode && _itemId.HasValue)
        {
            var existing = _noteRepo.GetItemById(_itemId.Value);
            var entry = new NoteEntry
            {
                Id = _itemId.Value,
                Title = normalizedTitle,
                Content = NoteContent,
                CreatedAt = existing?.CreatedAt ?? DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _noteRepo.Update(entry);
        }
        else
        {
            var all = _noteRepo.getAll().ToList();
            int newId = all.Count > 0 ? all.Max(x => x.Id) + 1 : 1;
            var entry = new NoteEntry
            {
                Id = newId,
                Title = normalizedTitle,
                Content = NoteContent,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _noteRepo.Add(entry);
        }

        _noteRepo.Save();
    }

    private void Cancel()
    {
        _vaultSession.RecordActivity();
        ClearSensitiveData();
        CloseAction?.Invoke();
    }

    private static string FormatCardNumberForInput(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var digits = new string(value.Where(char.IsDigit).ToArray());
        return string.Join(" ",
            Enumerable.Range(0, (digits.Length + 3) / 4)
                .Select(index => digits.Skip(index * 4).Take(4))
                .Where(chunk => chunk.Any())
                .Select(chunk => new string(chunk.ToArray())));
    }

    // ─── Sensitive Data & Session Lifecycle ────────────────────────────────────

    public void ClearSensitiveData()
    {
        PasswordTitle = string.Empty;
        Login = string.Empty;
        Password = string.Empty;
        ServiceName = string.Empty;
        ShowPassword = false;

        CardTitle = string.Empty;
        CardNumber = string.Empty;
        CardHolder = string.Empty;
        ExpiryDate = string.Empty;
        Cvv = string.Empty;
        BankName = string.Empty;
        ShowCvv = false;

        NoteTitle = string.Empty;
        NoteContent = string.Empty;

        ValidationError = string.Empty;
    }

    private void OnSessionStateChanged()
    {
        try
        {
            if (MainThread.IsMainThread) { HandleSessionStateChanged(); return; }
            MainThread.BeginInvokeOnMainThread(HandleSessionStateChanged);
        }
        catch
        {
            HandleSessionStateChanged();
        }
    }

    private void HandleSessionStateChanged()
    {
        if (!_vaultSession.IsAuthenticated)
        {
            ClearSensitiveData();
            RequestLockAction?.Invoke();
        }
    }

    public override void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _vaultSession.StateChanged -= OnSessionStateChanged;
        ClearSensitiveData();

        base.Dispose();
    }
}
