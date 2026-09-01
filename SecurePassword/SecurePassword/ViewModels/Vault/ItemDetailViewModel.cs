using System.Windows.Input;
using Microsoft.Extensions.Logging;
using SecurePassword.ViewModels.Base;


namespace SecurePassword.ViewModels.Vault;

/// <summary>
/// ViewModel for viewing details of a single vault entry (Password, Card, or Note).
/// Holds decrypted plaintext temporarily while the view is displayed.
/// Clears all secrets upon Lock, Disappear, Cancel or Dispose.
/// </summary>
public sealed class ItemDetailViewModel : BaseViewModel, ISensitiveViewModel
{
    private readonly SecureRepository<PasswordEntry> _passwordRepo;
    private readonly SecureRepository<CardEntry> _cardRepo;
    private readonly SecureRepository<NoteEntry> _noteRepo;
    private readonly ISecureClipboardService _secureClipboard;
    private readonly VaultSessionService _vaultSession;
    private readonly ILogger<ItemDetailViewModel>? _logger;

    private int _itemId;
    private VaultItemType _itemType;
    private string _title = string.Empty;
    private string _typeDisplayName = string.Empty;
    private string _iconSource = string.Empty;

    // Password fields
    private string _login = string.Empty;
    private string _password = string.Empty;
    private string _serviceName = string.Empty;
    private bool _showPassword;

    // Card fields
    private string _cardNumber = string.Empty;
    private string _cardHolder = string.Empty;
    private string _expiryDate = string.Empty;
    private string _cvv = string.Empty;
    private string _bankName = string.Empty;
    private bool _showCardNumber;
    private bool _showCvv;


    // Note fields
    private string _noteContent = string.Empty;

    private bool _isLoading;
    private bool _isItemNotFound;
    private int _operationGeneration;
    private CancellationTokenSource? _loadCts;
    private bool _disposed;
    private bool _isDeleting;

    // Navigation and orchestration callbacks
    public Func<string, Task<bool>>? ConfirmDeleteAction { get; set; }
    public Func<int, VaultItemType, Task>? NavigateToEditAction { get; set; }
    public Func<Task>? ItemDeletedAction { get; set; }
    public Func<Task>? CloseAction { get; set; }
    public Func<Task>? RequestLockAction { get; set; }

    public ItemDetailViewModel(
        SecureRepository<PasswordEntry> passwordRepo,
        SecureRepository<CardEntry> cardRepo,
        SecureRepository<NoteEntry> noteRepo,
        ISecureClipboardService secureClipboard,
        VaultSessionService vaultSession,
        ILogger<ItemDetailViewModel>? logger = null)
    {
        _passwordRepo = passwordRepo ?? throw new ArgumentNullException(nameof(passwordRepo));
        _cardRepo = cardRepo ?? throw new ArgumentNullException(nameof(cardRepo));
        _noteRepo = noteRepo ?? throw new ArgumentNullException(nameof(noteRepo));
        _secureClipboard = secureClipboard ?? throw new ArgumentNullException(nameof(secureClipboard));
        _vaultSession = vaultSession ?? throw new ArgumentNullException(nameof(vaultSession));
        _logger = logger;

        _vaultSession.StateChanged += OnSessionStateChanged;

        TogglePasswordVisibilityCommand = new RelayCommand(TogglePasswordVisibility);
        ToggleCardNumberVisibilityCommand = new RelayCommand(ToggleCardNumberVisibility);
        ToggleCvvVisibilityCommand = new RelayCommand(ToggleCvvVisibility);
        CopyLoginCommand = new AsyncRelayCommand(CopyLoginAsync);
        CopyPasswordCommand = new AsyncRelayCommand(CopyPasswordAsync);
        CopyCardNumberCommand = new AsyncRelayCommand(CopyCardNumberAsync);
        CopyCardHolderCommand = new AsyncRelayCommand(CopyCardHolderAsync);
        CopyCvvCommand = new AsyncRelayCommand(CopyCvvAsync);
        CopyNoteContentCommand = new AsyncRelayCommand(CopyNoteContentAsync);
        EditCommand = new AsyncRelayCommand(EditAsync);
        DeleteCommand = new AsyncRelayCommand(DeleteAsync);
        CloseCommand = new AsyncRelayCommand(CloseAsync);
    }

    // ─── Commands ──────────────────────────────────────────────────────────────

    public ICommand TogglePasswordVisibilityCommand { get; }
    public ICommand ToggleCardNumberVisibilityCommand { get; }
    public ICommand ToggleCvvVisibilityCommand { get; }

    public ICommand CopyLoginCommand { get; }
    public ICommand CopyPasswordCommand { get; }
    public ICommand CopyCardNumberCommand { get; }
    public ICommand CopyCardHolderCommand { get; }
    public ICommand CopyCvvCommand { get; }
    public ICommand CopyNoteContentCommand { get; }
    public ICommand EditCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand CloseCommand { get; }

    // ─── Properties ────────────────────────────────────────────────────────────

    public int ItemId
    {
        get => _itemId;
        private set => SetProperty(ref _itemId, value);
    }

    public VaultItemType ItemType
    {
        get => _itemType;
        private set
        {
            if (SetProperty(ref _itemType, value))
            {
                OnPropertyChanged(nameof(IsPasswordType));
                OnPropertyChanged(nameof(IsCardType));
                OnPropertyChanged(nameof(IsNoteType));
            }
        }
    }

    public bool IsPasswordType => _itemType == VaultItemType.Password;
    public bool IsCardType => _itemType == VaultItemType.Card;
    public bool IsNoteType => _itemType == VaultItemType.Note;

    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }

    public string TypeDisplayName
    {
        get => _typeDisplayName;
        private set => SetProperty(ref _typeDisplayName, value);
    }

    public string IconSource
    {
        get => _iconSource;
        private set => SetProperty(ref _iconSource, value);
    }

    // Password properties
    public string Login
    {
        get => _login;
        set => SetProperty(ref _login, value);
    }

    public string Password
    {
        get => _password;
        set
        {
            if (SetProperty(ref _password, value))
            {
                OnPropertyChanged(nameof(MaskedPassword));
            }
        }
    }

    public string ServiceName
    {
        get => _serviceName;
        set
        {
            if (SetProperty(ref _serviceName, value))
                OnPropertyChanged(nameof(HasServiceName));
        }
    }

    public bool HasServiceName => !string.IsNullOrWhiteSpace(ServiceName);

    public bool ShowPassword
    {
        get => _showPassword;
        set
        {
            if (SetProperty(ref _showPassword, value))
            {
                OnPropertyChanged(nameof(MaskedPassword));
                OnPropertyChanged(nameof(PasswordToggleIcon));
                OnPropertyChanged(nameof(PasswordToggleAccessibleText));
            }
        }
    }

    public string MaskedPassword => _showPassword
        ? _password
        : (string.IsNullOrEmpty(_password) ? string.Empty : "••••••••");

    public string PasswordToggleIcon => _showPassword ? "icon_eye_off.png" : "icon_eye.png";
    public string PasswordToggleAccessibleText => _showPassword ? "Скрыть пароль" : "Показать пароль";

    // Card properties
    public string CardNumber
    {
        get => _cardNumber;
        set
        {
            if (SetProperty(ref _cardNumber, value))
            {
                OnPropertyChanged(nameof(FormattedCardNumber));
                OnPropertyChanged(nameof(MaskedCardNumber));
            }
        }
    }

    public string FormattedCardNumber => FormatCardNumber(_cardNumber);
    public string MaskedCardNumber => VaultListItemViewModel.MaskCardNumber(_cardNumber);

    public bool ShowCardNumber
    {
        get => _showCardNumber;
        set
        {
            if (SetProperty(ref _showCardNumber, value))
            {
                OnPropertyChanged(nameof(CardNumberDisplay));
                OnPropertyChanged(nameof(CardNumberToggleIcon));
                OnPropertyChanged(nameof(CardNumberToggleAccessibleText));
            }
        }
    }

    public string CardNumberDisplay => _showCardNumber ? FormattedCardNumber : MaskedCardNumber;
    public string CardNumberToggleIcon => _showCardNumber ? "icon_eye_off.png" : "icon_eye.png";
    public string CardNumberToggleAccessibleText => _showCardNumber ? "Скрыть номер карты" : "Показать номер карты";

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
        set
        {
            if (SetProperty(ref _cvv, value))
            {
                OnPropertyChanged(nameof(MaskedCvv));
                OnPropertyChanged(nameof(CvvToggleIcon));
                OnPropertyChanged(nameof(CvvToggleAccessibleText));
            }
        }
    }

    public bool ShowCvv
    {
        get => _showCvv;
        set
        {
            if (SetProperty(ref _showCvv, value))
            {
                OnPropertyChanged(nameof(MaskedCvv));
                OnPropertyChanged(nameof(CvvToggleIcon));
                OnPropertyChanged(nameof(CvvToggleAccessibleText));
            }
        }
    }

    public string MaskedCvv => _showCvv
        ? _cvv
        : (string.IsNullOrEmpty(_cvv) ? string.Empty : new string('•', _cvv.Length));

    public string CvvToggleIcon => _showCvv ? "icon_eye_off.png" : "icon_eye.png";
    public string CvvToggleAccessibleText => _showCvv ? "Скрыть CVV" : "Показать CVV";

    public string BankName
    {
        get => _bankName;
        set => SetProperty(ref _bankName, value);
    }

    // Note properties
    public string NoteContent
    {
        get => _noteContent;
        set => SetProperty(ref _noteContent, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    public bool IsItemNotFound
    {
        get => _isItemNotFound;
        private set => SetProperty(ref _isItemNotFound, value);
    }

    // ─── Loading ───────────────────────────────────────────────────────────────

    public async Task LoadItemAsync(int id, VaultItemType type)
    {
        if (!_vaultSession.IsAuthenticated)
        {
            ClearSensitiveData();
            return;
        }

        int currentGen = Interlocked.Increment(ref _operationGeneration);

        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = new CancellationTokenSource();
        var token = _loadCts.Token;

        IsLoading = true;
        IsItemNotFound = false;

        ItemId = id;
        ItemType = type;

        string loadedTitle = string.Empty;
        string loadedTypeDisplayName = string.Empty;
        string loadedIconSource = string.Empty;

        string loadedLogin = string.Empty;
        string loadedPassword = string.Empty;
        string loadedServiceName = string.Empty;

        string loadedCardNumber = string.Empty;
        string loadedCardHolder = string.Empty;
        string loadedExpiryDate = string.Empty;
        string loadedCvv = string.Empty;
        string loadedBankName = string.Empty;

        string loadedNoteContent = string.Empty;
        bool found = false;

        try
        {
            await Task.Run(() =>
            {
                if (token.IsCancellationRequested) return;

                switch (type)
                {
                    case VaultItemType.Password:
                        var p = _passwordRepo.GetItemById(id);
                        if (p is not null)
                        {
                            found = true;
                            loadedTitle = p.Title;
                            loadedLogin = p.Login;
                            loadedPassword = p.Password;
                            loadedServiceName = p.ServiceName;
                            loadedTypeDisplayName = "Пароль";
                            loadedIconSource = ServiceImageGenerator.GetServiceIconSource(p.ServiceName, p.Title);
                        }
                        break;

                    case VaultItemType.Card:
                        var c = _cardRepo.GetItemById(id);
                        if (c is not null)
                        {
                            found = true;
                            loadedTitle = c.Title;
                            loadedCardNumber = c.CardNumber;
                            loadedCardHolder = c.CardHolder;
                            loadedExpiryDate = c.ExpiryDate;
                            loadedCvv = c.Cvv;
                            loadedBankName = c.BankName;
                            loadedTypeDisplayName = "Банковская карта";
                            loadedIconSource = "icon_card.svg";
                        }
                        break;

                    case VaultItemType.Note:
                        var n = _noteRepo.GetItemById(id);
                        if (n is not null)
                        {
                            found = true;
                            loadedTitle = n.Title;
                            loadedNoteContent = n.Content;
                            loadedTypeDisplayName = "Защищённая заметка";
                            loadedIconSource = "icon_note.svg";
                        }
                        break;
                }
            }, token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception exception)
        {
            found = false;
            _logger?.LogError(
                exception,
                "Failed to load vault item details. ItemId={ItemId}, ItemType={ItemType}",
                id,
                type);
        }
        finally
        {
            IsLoading = false;
        }

        // Late callback guard: ensure session is authenticated and generation is current
        if (currentGen != _operationGeneration || !_vaultSession.IsAuthenticated)
        {
            ClearSensitiveData();
            return;
        }

        if (!found)
        {
            IsItemNotFound = true;
            return;
        }

        Title = loadedTitle;
        TypeDisplayName = loadedTypeDisplayName;
        IconSource = loadedIconSource;

        Login = loadedLogin;
        Password = loadedPassword;
        ServiceName = loadedServiceName;

        CardNumber = loadedCardNumber;
        CardHolder = loadedCardHolder;
        ExpiryDate = loadedExpiryDate;
        Cvv = loadedCvv;
        BankName = loadedBankName;

        NoteContent = loadedNoteContent;

        // Ensure defaults are masked
        ShowPassword = false;
        ShowCardNumber = false;
        ShowCvv = false;
    }


    // ─── Actions & Commands ────────────────────────────────────────────────────

    private void TogglePasswordVisibility()
    {
        _vaultSession.RecordActivity();
        ShowPassword = !ShowPassword;
    }

    private void ToggleCardNumberVisibility()
    {
        _vaultSession.RecordActivity();
        ShowCardNumber = !ShowCardNumber;
    }

    private void ToggleCvvVisibility()
    {
        _vaultSession.RecordActivity();
        ShowCvv = !ShowCvv;
    }


    private async Task CopyLoginAsync()
    {
        _vaultSession.RecordActivity();
        if (!string.IsNullOrEmpty(Login))
            await _secureClipboard.CopyToClipboardAsync(Login, isSensitive: false);
    }

    private async Task CopyPasswordAsync()
    {
        _vaultSession.RecordActivity();
        if (!string.IsNullOrEmpty(Password))
            await _secureClipboard.CopyToClipboardAsync(Password, isSensitive: true);
    }

    private async Task CopyCardNumberAsync()
    {
        _vaultSession.RecordActivity();
        if (!string.IsNullOrEmpty(CardNumber))
            await _secureClipboard.CopyToClipboardAsync(CardNumber, isSensitive: true);
    }

    private async Task CopyCardHolderAsync()
    {
        _vaultSession.RecordActivity();
        if (!string.IsNullOrEmpty(CardHolder))
            await _secureClipboard.CopyToClipboardAsync(CardHolder, isSensitive: false);
    }

    private async Task CopyCvvAsync()
    {
        _vaultSession.RecordActivity();
        if (!string.IsNullOrEmpty(Cvv))
            await _secureClipboard.CopyToClipboardAsync(Cvv, isSensitive: true);
    }

    private async Task CopyNoteContentAsync()
    {
        _vaultSession.RecordActivity();
        if (!string.IsNullOrEmpty(NoteContent))
            await _secureClipboard.CopyToClipboardAsync(NoteContent, isSensitive: true);
    }

    private async Task EditAsync()
    {
        _vaultSession.RecordActivity();
        if (NavigateToEditAction is null)
            return;

        try
        {
            await NavigateToEditAction.Invoke(ItemId, ItemType);
        }
        catch (Exception exception)
        {
            _logger?.LogError(
                exception,
                "Failed to navigate to item editing. ItemId={ItemId}, ItemType={ItemType}",
                ItemId,
                ItemType);
        }
    }

    public async Task DeleteAsync()
    {
        if (_isDeleting || !_vaultSession.IsAuthenticated) return;
        _isDeleting = true;

        _vaultSession.RecordActivity();

        try
        {
            bool confirmed = true;
            if (ConfirmDeleteAction is not null)
            {
                confirmed = await ConfirmDeleteAction.Invoke(Title);
            }

            if (!confirmed || !_vaultSession.IsAuthenticated)
                return;

            await Task.Run(() =>
            {
                switch (ItemType)
                {
                    case VaultItemType.Password:
                        _passwordRepo.Remove(ItemId);
                        _passwordRepo.Save();
                        break;
                    case VaultItemType.Card:
                        _cardRepo.Remove(ItemId);
                        _cardRepo.Save();
                        break;
                    case VaultItemType.Note:
                        _noteRepo.Remove(ItemId);
                        _noteRepo.Save();
                        break;
                }
            });

            ClearSensitiveData();
            if (ItemDeletedAction is not null)
                await ItemDeletedAction.Invoke();
        }
        catch (Exception exception)
        {
            _logger?.LogError(
                exception,
                "Failed to delete vault item. ItemId={ItemId}, ItemType={ItemType}",
                ItemId,
                ItemType);
        }
        finally
        {
            _isDeleting = false;
        }
    }

    private async Task CloseAsync()
    {
        _vaultSession.RecordActivity();
        ClearSensitiveData();
        if (CloseAction is null)
            return;

        try
        {
            await CloseAction.Invoke();
        }
        catch (Exception exception)
        {
            _logger?.LogError(
                exception,
                "Failed to close item details. ItemId={ItemId}, ItemType={ItemType}",
                ItemId,
                ItemType);
        }
    }

    private static string FormatCardNumber(string? number)
    {
        if (string.IsNullOrWhiteSpace(number))
            return "•••• •••• •••• ••••";

        var digits = new string(number.Where(char.IsDigit).ToArray());
        if (digits.Length < 16)
            return number;

        return string.Join(" ", Enumerable.Range(0, 4).Select(i => digits.Substring(i * 4, 4)));
    }

    // ─── Sensitive Data & Session Lifecycle ────────────────────────────────────

    public void ClearSensitiveData()
    {
        Interlocked.Increment(ref _operationGeneration);
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = null;

        Title = string.Empty;
        TypeDisplayName = string.Empty;
        IconSource = string.Empty;

        Login = string.Empty;
        Password = string.Empty;
        ServiceName = string.Empty;
        ShowPassword = false;

        CardNumber = string.Empty;
        CardHolder = string.Empty;
        ExpiryDate = string.Empty;
        Cvv = string.Empty;
        BankName = string.Empty;
        ShowCardNumber = false;
        ShowCvv = false;


        NoteContent = string.Empty;
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
            _ = InvokeRequestLockAsync();
        }
    }

    private async Task InvokeRequestLockAsync()
    {
        if (RequestLockAction is null)
            return;

        try
        {
            await RequestLockAction.Invoke();
        }
        catch (Exception exception)
        {
            _logger?.LogError(exception, "Failed to close item details after session lock.");
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
