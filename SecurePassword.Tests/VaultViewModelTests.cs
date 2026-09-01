using System.IO;
using SecurePassword.ViewModels.Vault;
using Xunit;

namespace SecurePassword.Tests;

/// <summary>
/// Unit tests for VaultViewModel, covering:
/// - Loading items across all 3 repositories (Password, Card, Note)
/// - Empty state handling
/// - In-memory search & type filter combinations
/// - Ascending and descending sorting by title & type
/// - Security guarantees (clearing data on session lock, no sensitive data restoration)
/// - Race condition and concurrent load resilience
/// - Data formatting (card masking, note preview truncation)
/// </summary>
public class VaultViewModelTests : IDisposable
{
    private readonly string _testDir;
    private readonly string _keyFilePath;
    private readonly string _passwordsFilePath;
    private readonly string _cardsFilePath;
    private readonly string _notesFilePath;
    private readonly string _originalAppDataDir;

    private readonly keyManager _km;
    private readonly VaultSessionService _session;
    private readonly SecureRepository<PasswordEntry> _passwordRepo;
    private readonly SecureRepository<CardEntry> _cardRepo;
    private readonly SecureRepository<NoteEntry> _noteRepo;

    public VaultViewModelTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "VaultVmTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);

        _keyFilePath = Path.Combine(_testDir, "keys.dat");
        _passwordsFilePath = Path.Combine(_testDir, "passwords.dat");
        _cardsFilePath = Path.Combine(_testDir, "cards.dat");
        _notesFilePath = Path.Combine(_testDir, "notes.dat");

        _originalAppDataDir = FileWorker.TestingAppDataDirectory ?? string.Empty;
        FileWorker.TestingAppDataDirectory = _testDir;

        _km = new keyManager(_keyFilePath);
        _km.CreateKeyFile("MasterPassword123!");

        _passwordRepo = new SecureRepository<PasswordEntry>(_passwordsFilePath, _km);
        _cardRepo = new SecureRepository<CardEntry>(_cardsFilePath, _km);
        _noteRepo = new SecureRepository<NoteEntry>(_notesFilePath, _km);

        _session = new VaultSessionService();
        _session.MarkAuthenticated();
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

    private void SeedSampleVault()
    {
        _passwordRepo.Add(new PasswordEntry
        {
            Id = 1,
            Title = "GitHub Account",
            Login = "octocat@github.com",
            Password = "secret_password_1",
            ServiceName = "GitHub"
        });
        _passwordRepo.Add(new PasswordEntry
        {
            Id = 2,
            Title = "Apple ID",
            Login = "steve@apple.com",
            Password = "secret_password_2",
            ServiceName = "Apple"
        });
        _passwordRepo.Save();

        _cardRepo.Add(new CardEntry
        {
            Id = 1,
            Title = "Tinkoff Black",
            CardNumber = "2200700011112222",
            CardHolder = "IVAN IVANOV",
            ExpiryDate = "12/28",
            Cvv = "123",
            BankName = "Tinkoff"
        });
        _cardRepo.Save();

        _noteRepo.Add(new NoteEntry
        {
            Id = 1,
            Title = "Wi-Fi Passwords",
            Content = "Home: SuperSecretRouterPass123\nOffice: WorkOfficeGuest2026"
        });
        _noteRepo.Save();
    }

    // ─── Loading Tests ─────────────────────────────────────────────────────────

    [Fact]
    public async Task LoadVault_LoadsPasswordsCardsAndNotes()
    {
        SeedSampleVault();

        using var vm = new VaultViewModel(_passwordRepo, _cardRepo, _noteRepo, _session);
        await vm.LoadVaultAsync();

        Assert.Equal(4, vm.TotalCount);
        Assert.Equal(2, vm.PasswordsCount);
        Assert.Equal(1, vm.CardsCount);
        Assert.Equal(1, vm.NotesCount);
        Assert.Equal(4, vm.DisplayedItems.Count);
        Assert.False(vm.IsEmpty);
        Assert.False(vm.HasNoSearchResults);
    }

    [Fact]
    public async Task LoadVault_EmptyVault_ShowsEmptyState()
    {
        using var vm = new VaultViewModel(_passwordRepo, _cardRepo, _noteRepo, _session);
        await vm.LoadVaultAsync();

        Assert.Equal(0, vm.TotalCount);
        Assert.Equal(0, vm.PasswordsCount);
        Assert.Equal(0, vm.CardsCount);
        Assert.Equal(0, vm.NotesCount);
        Assert.Empty(vm.DisplayedItems);
        Assert.True(vm.IsEmpty);
        Assert.False(vm.HasNoSearchResults);
    }

    [Fact]
    public async Task SelectItemAsync_AwaitsDetailNavigation()
    {
        using var vm = new VaultViewModel(_passwordRepo, _cardRepo, _noteRepo, _session);
        var item = new VaultListItemViewModel
        {
            Id = 42,
            Type = VaultItemType.Password,
            Title = "Test"
        };
        bool navigationCompleted = false;

        vm.NavigateToDetailAction = async selectedItem =>
        {
            Assert.Same(item, selectedItem);
            await Task.Yield();
            navigationCompleted = true;
        };

        await vm.SelectItemAsync(item);

        Assert.True(navigationCompleted);
        Assert.Same(item, vm.SelectedItem);
    }

    [Fact]
    public async Task SelectItemAsync_ContainsNavigationFailure()
    {
        using var vm = new VaultViewModel(_passwordRepo, _cardRepo, _noteRepo, _session);
        var item = new VaultListItemViewModel
        {
            Id = 42,
            Type = VaultItemType.Note,
            Title = "Test"
        };
        vm.NavigateToDetailAction = _ => Task.FromException(new InvalidOperationException("Navigation failed"));

        Exception? exception = await Record.ExceptionAsync(() => vm.SelectItemAsync(item));

        Assert.Null(exception);
        Assert.Same(item, vm.SelectedItem);
    }

    // ─── Filtering Tests ───────────────────────────────────────────────────────

    [Fact]
    public async Task Search_FiltersExpectedItems()
    {
        SeedSampleVault();

        using var vm = new VaultViewModel(_passwordRepo, _cardRepo, _noteRepo, _session);
        await vm.LoadVaultAsync();

        vm.SearchQuery = "GitHub";

        Assert.Single(vm.DisplayedItems);
        Assert.Equal("GitHub Account", vm.DisplayedItems[0].Title);
        Assert.Equal(1, vm.FilteredCount);
        Assert.False(vm.HasNoSearchResults);
    }

    [Fact]
    public async Task TypeFilter_FiltersExpectedType()
    {
        SeedSampleVault();

        using var vm = new VaultViewModel(_passwordRepo, _cardRepo, _noteRepo, _session);
        await vm.LoadVaultAsync();

        vm.ActiveFilter = "card";

        Assert.Single(vm.DisplayedItems);
        Assert.Equal(VaultItemType.Card, vm.DisplayedItems[0].Type);
        Assert.Equal("Tinkoff Black", vm.DisplayedItems[0].Title);

        vm.ActiveFilter = "note";
        Assert.Single(vm.DisplayedItems);
        Assert.Equal(VaultItemType.Note, vm.DisplayedItems[0].Type);
        Assert.Equal("Wi-Fi Passwords", vm.DisplayedItems[0].Title);

        vm.ActiveFilter = "password";
        Assert.Equal(2, vm.DisplayedItems.Count);
        Assert.All(vm.DisplayedItems, item => Assert.Equal(VaultItemType.Password, item.Type));
    }

    [Fact]
    public async Task SearchAndTypeFilter_CombineCorrectly()
    {
        SeedSampleVault();

        using var vm = new VaultViewModel(_passwordRepo, _cardRepo, _noteRepo, _session);
        await vm.LoadVaultAsync();

        // Search for "Apple" while filter is "card" -> 0 items
        vm.ActiveFilter = "card";
        vm.SearchQuery = "Apple";

        Assert.Empty(vm.DisplayedItems);
        Assert.Equal(0, vm.FilteredCount);
        Assert.True(vm.HasNoSearchResults);

        // Switch filter to "password" -> matches Apple ID
        vm.ActiveFilter = "password";
        Assert.Single(vm.DisplayedItems);
        Assert.Equal("Apple ID", vm.DisplayedItems[0].Title);
    }

    // ─── Sorting Tests ─────────────────────────────────────────────────────────

    [Fact]
    public async Task SortAscending_OrdersCorrectly()
    {
        SeedSampleVault();

        using var vm = new VaultViewModel(_passwordRepo, _cardRepo, _noteRepo, _session);
        await vm.LoadVaultAsync();

        vm.SortBy = "title";
        vm.SortDescending = false;

        Assert.Equal(4, vm.DisplayedItems.Count);
        Assert.Equal("Apple ID", vm.DisplayedItems[0].Title);
        Assert.Equal("GitHub Account", vm.DisplayedItems[1].Title);
        Assert.Equal("Tinkoff Black", vm.DisplayedItems[2].Title);
        Assert.Equal("Wi-Fi Passwords", vm.DisplayedItems[3].Title);
    }

    [Fact]
    public async Task SortDescending_OrdersCorrectly()
    {
        SeedSampleVault();

        using var vm = new VaultViewModel(_passwordRepo, _cardRepo, _noteRepo, _session);
        await vm.LoadVaultAsync();

        vm.SortBy = "title";
        vm.SortDescending = true;

        Assert.Equal(4, vm.DisplayedItems.Count);
        Assert.Equal("Wi-Fi Passwords", vm.DisplayedItems[0].Title);
        Assert.Equal("Tinkoff Black", vm.DisplayedItems[1].Title);
        Assert.Equal("GitHub Account", vm.DisplayedItems[2].Title);
        Assert.Equal("Apple ID", vm.DisplayedItems[3].Title);
    }

    // ─── Security Tests ────────────────────────────────────────────────────────

    [Fact]
    public async Task Lock_ClearsVaultItems()
    {
        SeedSampleVault();

        using var vm = new VaultViewModel(_passwordRepo, _cardRepo, _noteRepo, _session);
        await vm.LoadVaultAsync();

        Assert.NotEmpty(vm.DisplayedItems);
        Assert.Equal(4, vm.TotalCount);

        _session.Lock();

        Assert.Empty(vm.DisplayedItems);
        Assert.Empty(vm.GroupedItems);
        Assert.Equal(0, vm.TotalCount);
        Assert.Equal(0, vm.PasswordsCount);
        Assert.Equal(0, vm.CardsCount);
        Assert.Equal(0, vm.NotesCount);
    }

    [Fact]
    public async Task Lock_ClearsSelectedItem()
    {
        SeedSampleVault();

        using var vm = new VaultViewModel(_passwordRepo, _cardRepo, _noteRepo, _session);
        await vm.LoadVaultAsync();

        vm.SelectedItem = vm.DisplayedItems[0];
        Assert.NotNull(vm.SelectedItem);

        _session.Lock();

        Assert.Null(vm.SelectedItem);
    }

    [Fact]
    public async Task LateLoadAfterLock_DoesNotRestoreSensitiveData()
    {
        SeedSampleVault();

        using var vm = new VaultViewModel(_passwordRepo, _cardRepo, _noteRepo, _session);

        // Start async load, immediately lock session
        var loadTask = vm.LoadVaultAsync();
        _session.Lock();

        await loadTask;

        // Even after load completes, items must remain cleared
        Assert.Empty(vm.DisplayedItems);
        Assert.Equal(0, vm.TotalCount);
    }

    [Fact]
    public async Task CommandsAfterLock_AreRejected()
    {
        SeedSampleVault();

        using var vm = new VaultViewModel(_passwordRepo, _cardRepo, _noteRepo, _session);
        await vm.LoadVaultAsync();

        _session.Lock();

        // Attempting to load while locked must not populate items
        await vm.LoadVaultAsync();

        Assert.Empty(vm.DisplayedItems);
        Assert.Equal(0, vm.TotalCount);
    }

    // ─── Lifecycle & Concurrency Tests ─────────────────────────────────────────

    [Fact]
    public async Task RepeatedLoad_DoesNotDuplicateItems()
    {
        SeedSampleVault();

        using var vm = new VaultViewModel(_passwordRepo, _cardRepo, _noteRepo, _session);

        await vm.LoadVaultAsync();
        Assert.Equal(4, vm.TotalCount);

        await vm.LoadVaultAsync();
        Assert.Equal(4, vm.TotalCount);
        Assert.Equal(4, vm.DisplayedItems.Count);
    }

    [Fact]
    public void Dispose_UnsubscribesSessionEvents()
    {
        var vm = new VaultViewModel(_passwordRepo, _cardRepo, _noteRepo, _session);
        vm.Dispose();

        int changeCount = 0;
        vm.PropertyChanged += (_, _) => changeCount++;

        _session.Lock();
        _session.MarkAuthenticated();

        Assert.Equal(0, changeCount);
    }

    [Fact]
    public async Task ConcurrentLoads_DoNotCorruptState()
    {
        SeedSampleVault();

        using var vm = new VaultViewModel(_passwordRepo, _cardRepo, _noteRepo, _session);

        // Run 5 simultaneous loads
        var tasks = Enumerable.Range(0, 5).Select(_ => vm.LoadVaultAsync()).ToArray();
        await Task.WhenAll(tasks);

        Assert.Equal(4, vm.TotalCount);
        Assert.Equal(4, vm.DisplayedItems.Count);
    }

    // ─── Formatting Tests ──────────────────────────────────────────────────────

    [Fact]
    public void CardPreview_IsMasked()
    {
        var card = new CardEntry
        {
            Id = 1,
            Title = "Test Card",
            CardNumber = "4111222233334444"
        };

        var itemVm = VaultListItemViewModel.FromCard(card);

        Assert.Equal("•••• •••• •••• 4444", itemVm.Subtitle);
        Assert.DoesNotContain("4111", itemVm.Subtitle);
        Assert.DoesNotContain("2222", itemVm.Subtitle);
        Assert.DoesNotContain("3333", itemVm.Subtitle);
    }

    [Fact]
    public void NotePreview_IsBounded()
    {
        string longContent = new string('A', 150);
        var note = new NoteEntry
        {
            Id = 1,
            Title = "Long Note",
            Content = longContent
        };

        var itemVm = VaultListItemViewModel.FromNote(note);

        Assert.True(itemVm.Subtitle.Length <= 53); // 50 chars + "..."
        Assert.EndsWith("...", itemVm.Subtitle);
    }
}
