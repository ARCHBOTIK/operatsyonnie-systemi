using System.Collections.ObjectModel;
using System.Windows.Input;
using SecurePassword.ViewModels.Base;

namespace SecurePassword.ViewModels.Vault;

/// <summary>
/// ViewModel for the main native XAML Vault screen (VaultPage.xaml).
/// Manages vault items loading, local in-memory filtering, category tabs,
/// sorting, search, selection, and sensitive session lifecycle.
/// </summary>
public sealed class VaultViewModel : BaseViewModel, ISensitiveViewModel
{
    private readonly SecureRepository<PasswordEntry> _passwordRepository;
    private readonly SecureRepository<CardEntry> _cardRepository;
    private readonly SecureRepository<NoteEntry> _noteRepository;
    private readonly VaultSessionService _vaultSession;

    // In-memory cache of decrypted display representations
    private readonly List<VaultListItemViewModel> _allItems = [];
    private readonly object _syncLock = new();

    private string _searchQuery = string.Empty;
    private string _activeFilter = "all";
    private string _sortBy = "title";
    private bool _sortDescending;
    private bool _isLoading;
    private bool _isRefreshing;
    private VaultListItemViewModel? _selectedItem;

    private int _passwordsCount;
    private int _cardsCount;
    private int _notesCount;
    private int _totalCount;
    private int _filteredCount;

    // Generation token for late-load race protection
    private int _operationGeneration;
    private CancellationTokenSource? _loadCts;
    private bool _disposed;

    // Navigation callbacks
    public Action<VaultListItemViewModel>? NavigateToDetailAction { get; set; }
    public Action? NavigateToAddItemAction { get; set; }
    public Action? RequestLockAction { get; set; }

    public ObservableCollection<VaultListItemViewModel> DisplayedItems { get; } = [];
    public ObservableCollection<VaultGroupViewModel> GroupedItems { get; } = [];

    public VaultViewModel(
        SecureRepository<PasswordEntry> passwordRepository,
        SecureRepository<CardEntry> cardRepository,
        SecureRepository<NoteEntry> noteRepository,
        VaultSessionService vaultSession)
    {
        _passwordRepository = passwordRepository ?? throw new ArgumentNullException(nameof(passwordRepository));
        _cardRepository = cardRepository ?? throw new ArgumentNullException(nameof(cardRepository));
        _noteRepository = noteRepository ?? throw new ArgumentNullException(nameof(noteRepository));
        _vaultSession = vaultSession ?? throw new ArgumentNullException(nameof(vaultSession));

        _vaultSession.StateChanged += OnSessionStateChanged;

        LoadVaultCommand = new AsyncRelayCommand(LoadVaultAsync);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        SetFilterCommand = new RelayCommand(p => SetFilter(p?.ToString()));
        ToggleSortCommand = new RelayCommand(ToggleSort);
        SelectItemCommand = new RelayCommand(p => SelectItem(p as VaultListItemViewModel));
        AddNewItemCommand = new RelayCommand(AddNewItem);
        ClearSearchCommand = new RelayCommand(ClearSearch);

        if (_vaultSession.IsAuthenticated)
        {
            _ = LoadVaultAsync();
        }
    }

    // ─── Commands ──────────────────────────────────────────────────────────────
    public ICommand LoadVaultCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand SetFilterCommand { get; }
    public ICommand ToggleSortCommand { get; }
    public ICommand SelectItemCommand { get; }
    public ICommand AddNewItemCommand { get; }
    public ICommand ClearSearchCommand { get; }

    // ─── Properties ────────────────────────────────────────────────────────────

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (SetProperty(ref _searchQuery, value ?? string.Empty))
            {
                RecordUserActivity();
                OnPropertyChanged(nameof(IsSearchActive));
                ApplyFilterAndSort();
            }
        }
    }

    public bool IsSearchActive => !string.IsNullOrWhiteSpace(_searchQuery);

    public string ActiveFilter
    {
        get => _activeFilter;
        set
        {
            if (SetProperty(ref _activeFilter, value ?? "all"))
            {
                RecordUserActivity();
                OnPropertyChanged(nameof(IsFilterAll));
                OnPropertyChanged(nameof(IsFilterPassword));
                OnPropertyChanged(nameof(IsFilterCard));
                OnPropertyChanged(nameof(IsFilterNote));
                ApplyFilterAndSort();
            }
        }
    }

    public bool IsFilterAll => _activeFilter == "all";
    public bool IsFilterPassword => _activeFilter == "password";
    public bool IsFilterCard => _activeFilter == "card";
    public bool IsFilterNote => _activeFilter == "note";

    public string SortBy
    {
        get => _sortBy;
        set
        {
            if (SetProperty(ref _sortBy, value))
            {
                OnPropertyChanged(nameof(SortIndicatorText));
                OnPropertyChanged(nameof(SortIconSource));
                ApplyFilterAndSort();
            }
        }
    }

    public bool SortDescending
    {
        get => _sortDescending;
        set
        {
            if (SetProperty(ref _sortDescending, value))
            {
                OnPropertyChanged(nameof(SortIndicatorText));
                OnPropertyChanged(nameof(SortIconSource));
                ApplyFilterAndSort();
            }
        }
    }

    public string SortIndicatorText => _sortBy == "title"
        ? (_sortDescending ? "по названию ▼" : "по названию ▲")
        : (_sortDescending ? "по типу ▼" : "по типу ▲");

    public string SortIconSource => _sortBy == "title"
        ? (_sortDescending ? "icon_sort_desc.svg" : "icon_sort_asc.svg")
        : "icon_sort_type.svg";

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (SetProperty(ref _isLoading, value))
            {
                OnPropertyChanged(nameof(IsEmpty));
            }
        }
    }

    public bool IsRefreshing
    {
        get => _isRefreshing;
        set => SetProperty(ref _isRefreshing, value);
    }

    public VaultListItemViewModel? SelectedItem
    {
        get => _selectedItem;
        set => SetProperty(ref _selectedItem, value);
    }

    public int TotalCount
    {
        get => _totalCount;
        private set
        {
            if (SetProperty(ref _totalCount, value))
            {
                OnPropertyChanged(nameof(IsEmpty));
                OnPropertyChanged(nameof(HasNoSearchResults));
            }
        }
    }

    public int PasswordsCount
    {
        get => _passwordsCount;
        private set => SetProperty(ref _passwordsCount, value);
    }

    public int CardsCount
    {
        get => _cardsCount;
        private set => SetProperty(ref _cardsCount, value);
    }

    public int NotesCount
    {
        get => _notesCount;
        private set => SetProperty(ref _notesCount, value);
    }

    public int FilteredCount
    {
        get => _filteredCount;
        private set
        {
            if (SetProperty(ref _filteredCount, value))
            {
                OnPropertyChanged(nameof(HasNoSearchResults));
            }
        }
    }

    public bool IsEmpty => _totalCount == 0 && !_isLoading;
    public bool HasNoSearchResults => _filteredCount == 0 && _totalCount > 0;

    // ─── Data Loading ──────────────────────────────────────────────────────────

    public async Task LoadVaultAsync()
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

        List<VaultListItemViewModel> loadedItems = [];
        int passwordsC = 0;
        int cardsC = 0;
        int notesC = 0;

        try
        {
            await Task.Run(() =>
            {
                if (token.IsCancellationRequested) return;

                var rawPasswords = _passwordRepository.getAll().OrderBy(p => p.Title).ToList();
                var rawCards = _cardRepository.getAll().OrderBy(c => c.Title).ToList();
                var rawNotes = _noteRepository.getAll().OrderBy(n => n.Title).ToList();

                passwordsC = rawPasswords.Count;
                cardsC = rawCards.Count;
                notesC = rawNotes.Count;

                foreach (var p in rawPasswords)
                    loadedItems.Add(VaultListItemViewModel.FromPassword(p));

                foreach (var c in rawCards)
                    loadedItems.Add(VaultListItemViewModel.FromCard(c));

                foreach (var n in rawNotes)
                    loadedItems.Add(VaultListItemViewModel.FromNote(n));
            }, token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception)
        {
            // Vault locked or decrypt error
            loadedItems.Clear();
        }
        finally
        {
            IsLoading = false;
        }

        // Late callback guard: ensure no session lock or newer load took place
        if (currentGen != _operationGeneration || !_vaultSession.IsAuthenticated)
        {
            loadedItems.Clear();
            return;
        }

        lock (_syncLock)
        {
            _allItems.Clear();
            _allItems.AddRange(loadedItems);
        }

        PasswordsCount = passwordsC;
        CardsCount = cardsC;
        NotesCount = notesC;
        TotalCount = _allItems.Count;

        ApplyFilterAndSort();
    }

    public async Task RefreshAsync()
    {
        RecordUserActivity();
        IsRefreshing = true;
        try
        {
            await LoadVaultAsync();
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    // ─── Filtering & Sorting ───────────────────────────────────────────────────

    public void ApplyFilterAndSort()
    {
        List<VaultListItemViewModel> source;
        lock (_syncLock)
        {
            source = new List<VaultListItemViewModel>(_allItems);
        }

        IEnumerable<VaultListItemViewModel> filtered = source;

        // 1. Search Query
        if (!string.IsNullOrWhiteSpace(_searchQuery))
        {
            string query = _searchQuery.Trim().ToLowerInvariant();
            filtered = filtered.Where(item =>
                item.SearchText.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                item.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                item.Subtitle.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        // 2. Type Filter
        if (_activeFilter != "all")
        {
            var targetType = _activeFilter switch
            {
                "password" => VaultItemType.Password,
                "card" => VaultItemType.Card,
                "note" => VaultItemType.Note,
                _ => (VaultItemType?)null
            };

            if (targetType.HasValue)
            {
                filtered = filtered.Where(item => item.Type == targetType.Value);
            }
        }

        // 3. Sorting
        List<VaultListItemViewModel> sortedList = _sortBy switch
        {
            "type" => _sortDescending
                ? filtered.OrderByDescending(item => item.Type).ThenBy(item => item.Title).ToList()
                : filtered.OrderBy(item => item.Type).ThenBy(item => item.Title).ToList(),
            _ => _sortDescending
                ? filtered.OrderByDescending(item => item.Title).ToList()
                : filtered.OrderBy(item => item.Title).ToList()
        };

        // 4. Update collections safely on UI thread (or directly in test runners)
        UpdateObservableCollections(sortedList);
    }

    private void UpdateObservableCollections(List<VaultListItemViewModel> items)
    {
        void Update()
        {
            FilteredCount = items.Count;

            DisplayedItems.Clear();
            foreach (var item in items)
                DisplayedItems.Add(item);

            // Grouping for category views
            GroupedItems.Clear();

            var passwordGroupItems = items.Where(i => i.Type == VaultItemType.Password).ToList();
            var cardGroupItems = items.Where(i => i.Type == VaultItemType.Card).ToList();
            var noteGroupItems = items.Where(i => i.Type == VaultItemType.Note).ToList();

            if (passwordGroupItems.Count > 0 || _activeFilter is "all" or "password")
            {
                GroupedItems.Add(new VaultGroupViewModel(
                    "passwords", "Логины", "🔑", "icon_login.svg", passwordGroupItems));
            }

            if (cardGroupItems.Count > 0 || _activeFilter is "all" or "card")
            {
                GroupedItems.Add(new VaultGroupViewModel(
                    "cards", "Банковские карты", "💳", "icon_card.svg", cardGroupItems));
            }

            if (noteGroupItems.Count > 0 || _activeFilter is "all" or "note")
            {
                GroupedItems.Add(new VaultGroupViewModel(
                    "notes", "Защищённые заметки", "📝", "icon_note.svg", noteGroupItems));
            }
        }

        try
        {
            if (MainThread.IsMainThread)
            {
                Update();
                return;
            }

            MainThread.BeginInvokeOnMainThread(Update);
        }
        catch
        {
            Update();
        }
    }

    public void SetFilter(string? filter)
    {
        ActiveFilter = string.IsNullOrWhiteSpace(filter) ? "all" : filter;
    }

    public void ToggleSort()
    {
        RecordUserActivity();
        if (_sortBy == "title")
        {
            _sortBy = "type";
        }
        else
        {
            _sortBy = "title";
            _sortDescending = !_sortDescending;
        }

        OnPropertyChanged(nameof(SortBy));
        OnPropertyChanged(nameof(SortDescending));
        OnPropertyChanged(nameof(SortIndicatorText));
        OnPropertyChanged(nameof(SortIconSource));
        ApplyFilterAndSort();
    }

    public void ClearSearch()
    {
        SearchQuery = string.Empty;
    }

    public void SelectItem(VaultListItemViewModel? item)
    {
        if (item is null) return;
        RecordUserActivity();
        SelectedItem = item;
        NavigateToDetailAction?.Invoke(item);
    }

    public void AddNewItem()
    {
        RecordUserActivity();
        NavigateToAddItemAction?.Invoke();
    }

    private void RecordUserActivity()
    {
        _vaultSession.RecordActivity();
    }

    // ─── Sensitive Data & Session Lifecycle ────────────────────────────────────

    public void ClearSensitiveData()
    {
        Interlocked.Increment(ref _operationGeneration);
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = null;

        lock (_syncLock)
        {
            _allItems.Clear();
        }

        void ClearCollections()
        {
            DisplayedItems.Clear();
            GroupedItems.Clear();
            SelectedItem = null;
            SearchQuery = string.Empty;
            TotalCount = 0;
            PasswordsCount = 0;
            CardsCount = 0;
            NotesCount = 0;
            FilteredCount = 0;
        }

        try
        {
            if (MainThread.IsMainThread) { ClearCollections(); return; }
            MainThread.BeginInvokeOnMainThread(ClearCollections);
        }
        catch
        {
            ClearCollections();
        }
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
        else
        {
            _ = LoadVaultAsync();
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
