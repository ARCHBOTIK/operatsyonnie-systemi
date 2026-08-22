using System.IO;
using SecurePassword.ViewModels.Vault;
using Xunit;

namespace SecurePassword.Tests;

public class ItemDetailAndEditTests : IDisposable
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
    private readonly ISecureClipboardService _clipboard;
    private readonly TestClipboardBackend _clipboardBackend;

    public ItemDetailAndEditTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "DetailEditTests_" + Guid.NewGuid().ToString("N"));
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

        _clipboardBackend = new TestClipboardBackend();
        _clipboard = new SecureClipboardService(_clipboardBackend);
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

    private sealed class TestClipboardBackend : IClipboardBackend
    {
        public string? Text { get; private set; }
        public bool IsSensitive { get; private set; }

        public Task SetTextAsync(string text, bool isSensitive)
        {
            Text = text;
            IsSensitive = isSensitive;
            return Task.CompletedTask;
        }

        public Task<string?> GetTextAsync() => Task.FromResult(Text);

        public Task ClearAsync()
        {
            Text = null;
            IsSensitive = false;
            return Task.CompletedTask;
        }
    }


    // ─── 44. Detail Tests ──────────────────────────────────────────────────────

    [Fact]
    public async Task Detail_LoadPassword_LoadsExpectedFields()
    {
        _passwordRepo.Add(new PasswordEntry
        {
            Id = 1,
            Title = "GitHub Account",
            Login = "octocat",
            Password = "MySecretPassword123",
            ServiceName = "GitHub"
        });
        _passwordRepo.Save();

        using var vm = new ItemDetailViewModel(_passwordRepo, _cardRepo, _noteRepo, _clipboard, _session);
        await vm.LoadItemAsync(1, VaultItemType.Password);

        Assert.Equal("GitHub Account", vm.Title);
        Assert.Equal("octocat", vm.Login);
        Assert.Equal("MySecretPassword123", vm.Password);
        Assert.Equal("GitHub", vm.ServiceName);
        Assert.True(vm.IsPasswordType);
        Assert.False(vm.IsItemNotFound);
    }

    [Fact]
    public async Task Detail_LoadCard_LoadsExpectedFields()
    {
        _cardRepo.Add(new CardEntry
        {
            Id = 1,
            Title = "Tinkoff Black",
            CardNumber = "2200700011112222",
            CardHolder = "IVAN IVANOV",
            ExpiryDate = "12/28",
            Cvv = "999",
            BankName = "T-Bank"
        });
        _cardRepo.Save();

        using var vm = new ItemDetailViewModel(_passwordRepo, _cardRepo, _noteRepo, _clipboard, _session);
        await vm.LoadItemAsync(1, VaultItemType.Card);

        Assert.Equal("Tinkoff Black", vm.Title);
        Assert.Equal("2200700011112222", vm.CardNumber);
        Assert.Equal("IVAN IVANOV", vm.CardHolder);
        Assert.Equal("12/28", vm.ExpiryDate);
        Assert.Equal("999", vm.Cvv);
        Assert.Equal("T-Bank", vm.BankName);
        Assert.True(vm.IsCardType);
    }

    [Fact]
    public async Task Detail_LoadNote_LoadsExpectedFields()
    {
        _noteRepo.Add(new NoteEntry
        {
            Id = 1,
            Title = "Secret Note",
            Content = "<script>alert('test')</script>\nVery important note content."
        });
        _noteRepo.Save();

        using var vm = new ItemDetailViewModel(_passwordRepo, _cardRepo, _noteRepo, _clipboard, _session);
        await vm.LoadItemAsync(1, VaultItemType.Note);

        Assert.Equal("Secret Note", vm.Title);
        Assert.Equal("<script>alert('test')</script>\nVery important note content.", vm.NoteContent);
        Assert.True(vm.IsNoteType);
    }

    [Fact]
    public async Task Detail_PasswordMaskedByDefault()
    {
        _passwordRepo.Add(new PasswordEntry
        {
            Id = 1,
            Title = "Test",
            Login = "user",
            Password = "SuperSecretPassword"
        });
        _passwordRepo.Save();

        using var vm = new ItemDetailViewModel(_passwordRepo, _cardRepo, _noteRepo, _clipboard, _session);
        await vm.LoadItemAsync(1, VaultItemType.Password);

        Assert.False(vm.ShowPassword);
        Assert.Equal("••••••••", vm.MaskedPassword);

        vm.TogglePasswordVisibilityCommand.Execute(null);
        Assert.True(vm.ShowPassword);
        Assert.Equal("SuperSecretPassword", vm.MaskedPassword);
    }

    [Fact]
    public async Task Detail_CardNumberMaskedByDefault()
    {
        _cardRepo.Add(new CardEntry
        {
            Id = 1,
            Title = "Card",
            CardNumber = "2200700011112222",
            Cvv = "123"
        });
        _cardRepo.Save();

        using var vm = new ItemDetailViewModel(_passwordRepo, _cardRepo, _noteRepo, _clipboard, _session);
        await vm.LoadItemAsync(1, VaultItemType.Card);

        Assert.False(vm.ShowCardNumber);
        Assert.Equal("•••• •••• •••• 2222", vm.CardNumberDisplay);

        vm.ToggleCardNumberVisibilityCommand.Execute(null);
        Assert.True(vm.ShowCardNumber);
        Assert.Equal("2200 7000 1111 2222", vm.CardNumberDisplay);
    }

    [Fact]
    public async Task Detail_CvvMaskedByDefault()
    {
        _cardRepo.Add(new CardEntry
        {
            Id = 1,
            Title = "Card",
            CardNumber = "1111222233334444",
            Cvv = "123"
        });
        _cardRepo.Save();

        using var vm = new ItemDetailViewModel(_passwordRepo, _cardRepo, _noteRepo, _clipboard, _session);
        await vm.LoadItemAsync(1, VaultItemType.Card);

        Assert.False(vm.ShowCvv);
        Assert.Equal("•••", vm.MaskedCvv);

        vm.ToggleCvvVisibilityCommand.Execute(null);
        Assert.True(vm.ShowCvv);
        Assert.Equal("123", vm.MaskedCvv);
    }


    [Fact]
    public async Task Detail_CopyPassword_UsesSecureClipboard()
    {
        _passwordRepo.Add(new PasswordEntry
        {
            Id = 1,
            Title = "Test",
            Login = "user",
            Password = "CopiedSecretPassword"
        });
        _passwordRepo.Save();

        using var vm = new ItemDetailViewModel(_passwordRepo, _cardRepo, _noteRepo, _clipboard, _session);
        await vm.LoadItemAsync(1, VaultItemType.Password);

        vm.CopyPasswordCommand.Execute(null);
        await Task.Delay(50);

        Assert.True(_clipboard.HasActiveSecret);
        Assert.True(_clipboardBackend.IsSensitive);
    }

    [Fact]
    public async Task Detail_CopyCardNumber_UsesSecureClipboard()
    {
        _cardRepo.Add(new CardEntry
        {
            Id = 1,
            Title = "Card",
            CardNumber = "2200111122223333"
        });
        _cardRepo.Save();

        using var vm = new ItemDetailViewModel(_passwordRepo, _cardRepo, _noteRepo, _clipboard, _session);
        await vm.LoadItemAsync(1, VaultItemType.Card);

        vm.CopyCardNumberCommand.Execute(null);
        await Task.Delay(50);

        Assert.True(_clipboard.HasActiveSecret);
        Assert.True(_clipboardBackend.IsSensitive);
    }

    [Fact]
    public async Task Detail_CopyCvv_UsesSecureClipboard()
    {
        _cardRepo.Add(new CardEntry
        {
            Id = 1,
            Title = "Card",
            CardNumber = "2200111122223333",
            Cvv = "777"
        });
        _cardRepo.Save();

        using var vm = new ItemDetailViewModel(_passwordRepo, _cardRepo, _noteRepo, _clipboard, _session);
        await vm.LoadItemAsync(1, VaultItemType.Card);

        vm.CopyCvvCommand.Execute(null);
        await Task.Delay(50);

        Assert.True(_clipboard.HasActiveSecret);
        Assert.True(_clipboardBackend.IsSensitive);
    }


    [Fact]
    public async Task Detail_Lock_ClearsSensitiveFields()
    {
        _passwordRepo.Add(new PasswordEntry
        {
            Id = 1,
            Title = "Test",
            Login = "user",
            Password = "SecretPassword"
        });
        _passwordRepo.Save();

        using var vm = new ItemDetailViewModel(_passwordRepo, _cardRepo, _noteRepo, _clipboard, _session);
        await vm.LoadItemAsync(1, VaultItemType.Password);

        Assert.Equal("SecretPassword", vm.Password);

        _session.Lock();

        Assert.Empty(vm.Password);
        Assert.Empty(vm.Login);
        Assert.Empty(vm.Title);
    }

    [Fact]
    public async Task Detail_LateLoadAfterLock_DoesNotRestoreData()
    {
        _passwordRepo.Add(new PasswordEntry
        {
            Id = 1,
            Title = "Test",
            Login = "user",
            Password = "SecretPassword"
        });
        _passwordRepo.Save();

        using var vm = new ItemDetailViewModel(_passwordRepo, _cardRepo, _noteRepo, _clipboard, _session);

        var task = vm.LoadItemAsync(1, VaultItemType.Password);
        _session.Lock();

        await task;

        Assert.Empty(vm.Password);
        Assert.Empty(vm.Title);
    }

    [Fact]
    public void Detail_Dispose_UnsubscribesEvents()
    {
        var vm = new ItemDetailViewModel(_passwordRepo, _cardRepo, _noteRepo, _clipboard, _session);
        vm.Dispose();

        int propertyChanges = 0;
        vm.PropertyChanged += (_, _) => propertyChanges++;

        _session.Lock();
        _session.MarkAuthenticated();

        Assert.Equal(0, propertyChanges);
    }

    [Fact]
    public async Task Detail_MissingItem_HandledGracefully()
    {
        using var vm = new ItemDetailViewModel(_passwordRepo, _cardRepo, _noteRepo, _clipboard, _session);
        await vm.LoadItemAsync(999, VaultItemType.Password);

        Assert.True(vm.IsItemNotFound);
        Assert.Empty(vm.Password);
        Assert.Empty(vm.Title);
    }

    // ─── 45. Add / Edit Tests ──────────────────────────────────────────────────

    [Fact]
    public async Task AddPassword_SavesExpectedRecord()
    {
        using var vm = new ItemEditViewModel(_passwordRepo, _cardRepo, _noteRepo, _session);
        vm.InitializeForAdd(VaultItemType.Password);

        vm.PasswordTitle = "New Site";
        vm.Login = "admin@site.com";
        vm.Password = "NewSecretPassword1!";
        vm.ServiceName = "Site";

        await vm.SaveAsync();

        var all = _passwordRepo.getAll().ToList();
        Assert.Single(all);
        Assert.Equal("New Site", all[0].Title);
        Assert.Equal("admin@site.com", all[0].Login);
        Assert.Equal("NewSecretPassword1!", all[0].Password);
        Assert.Equal("Site", all[0].ServiceName);
    }

    [Fact]
    public async Task AddCard_SavesExpectedRecord()
    {
        using var vm = new ItemEditViewModel(_passwordRepo, _cardRepo, _noteRepo, _session);
        vm.InitializeForAdd(VaultItemType.Card);

        vm.CardTitle = "My Visa";
        vm.CardNumber = "4111 2222 3333 4444";
        vm.CardHolder = "IVAN PETROV";
        vm.ExpiryDate = "05/29";
        vm.Cvv = "321";
        vm.BankName = "Sber";

        await vm.SaveAsync();

        var all = _cardRepo.getAll().ToList();
        Assert.Single(all);
        Assert.Equal("My Visa", all[0].Title);
        Assert.Equal("4111222233334444", all[0].CardNumber);
        Assert.Equal("IVAN PETROV", all[0].CardHolder);
        Assert.Equal("05/29", all[0].ExpiryDate);
        Assert.Equal("321", all[0].Cvv);
        Assert.Equal("Sber", all[0].BankName);
    }

    [Fact]
    public async Task AddNote_SavesExpectedRecord()
    {
        using var vm = new ItemEditViewModel(_passwordRepo, _cardRepo, _noteRepo, _session);
        vm.InitializeForAdd(VaultItemType.Note);

        vm.NoteTitle = "Important Memo";
        vm.NoteContent = "Buy milk\nPay taxes\nFinish Stage 5";

        await vm.SaveAsync();

        var all = _noteRepo.getAll().ToList();
        Assert.Single(all);
        Assert.Equal("Important Memo", all[0].Title);
        Assert.Equal("Buy milk\nPay taxes\nFinish Stage 5", all[0].Content);
    }

    [Fact]
    public async Task EditPassword_UpdatesExpectedRecord()
    {
        _passwordRepo.Add(new PasswordEntry
        {
            Id = 1,
            Title = "Old Title",
            Login = "old_login",
            Password = "old_password",
            ServiceName = "old_service"
        });
        _passwordRepo.Save();

        using var vm = new ItemEditViewModel(_passwordRepo, _cardRepo, _noteRepo, _session);
        vm.InitializeForEdit(1, VaultItemType.Password);

        vm.PasswordTitle = "Updated Title";
        vm.Password = "brand_new_pass";

        await vm.SaveAsync();

        var updated = _passwordRepo.GetItemById(1);
        Assert.NotNull(updated);
        Assert.Equal("Updated Title", updated.Title);
        Assert.Equal("brand_new_pass", updated.Password);
        Assert.Equal("old_login", updated.Login);
    }

    [Fact]
    public async Task EditCard_UpdatesExpectedRecord()
    {
        _cardRepo.Add(new CardEntry
        {
            Id = 1,
            Title = "Old Card",
            CardNumber = "1111222233334444",
            CardHolder = "OLD HOLDER",
            ExpiryDate = "01/25",
            Cvv = "111"
        });
        _cardRepo.Save();

        using var vm = new ItemEditViewModel(_passwordRepo, _cardRepo, _noteRepo, _session);
        vm.InitializeForEdit(1, VaultItemType.Card);

        vm.CardTitle = "New Card Title";
        vm.ExpiryDate = "02/30";

        await vm.SaveAsync();

        var updated = _cardRepo.GetItemById(1);
        Assert.NotNull(updated);
        Assert.Equal("New Card Title", updated.Title);
        Assert.Equal("02/30", updated.ExpiryDate);
    }

    [Fact]
    public async Task EditNote_UpdatesExpectedRecord()
    {
        _noteRepo.Add(new NoteEntry
        {
            Id = 1,
            Title = "Old Note",
            Content = "Old Content"
        });
        _noteRepo.Save();

        using var vm = new ItemEditViewModel(_passwordRepo, _cardRepo, _noteRepo, _session);
        vm.InitializeForEdit(1, VaultItemType.Note);

        vm.NoteContent = "Updated Content";

        await vm.SaveAsync();

        var updated = _noteRepo.GetItemById(1);
        Assert.NotNull(updated);
        Assert.Equal("Updated Content", updated.Content);
    }

    [Fact]
    public void Cancel_DoesNotSave()
    {
        using var vm = new ItemEditViewModel(_passwordRepo, _cardRepo, _noteRepo, _session);
        vm.InitializeForAdd(VaultItemType.Password);

        vm.PasswordTitle = "Unsaved Item";
        vm.Login = "unsaved";
        vm.Password = "unsaved";

        vm.CancelCommand.Execute(null);

        Assert.Empty(_passwordRepo.getAll());
        Assert.Empty(vm.Password);
    }

    [Fact]
    public void Lock_ClearsFormSecrets()
    {
        using var vm = new ItemEditViewModel(_passwordRepo, _cardRepo, _noteRepo, _session);
        vm.InitializeForAdd(VaultItemType.Password);

        vm.PasswordTitle = "Title";
        vm.Login = "Login";
        vm.Password = "TopSecretPassword";

        _session.Lock();

        Assert.Empty(vm.Password);
        Assert.Empty(vm.Login);
        Assert.Empty(vm.PasswordTitle);
    }

    [Fact]
    public async Task DoubleSave_CreatesSingleRecord()
    {
        using var vm = new ItemEditViewModel(_passwordRepo, _cardRepo, _noteRepo, _session);
        vm.InitializeForAdd(VaultItemType.Password);

        vm.PasswordTitle = "Single Record";
        vm.Login = "login";
        vm.Password = "password";

        var task1 = vm.SaveAsync();
        var task2 = vm.SaveAsync();

        await Task.WhenAll(task1, task2);

        Assert.Single(_passwordRepo.getAll());
    }

    [Fact]
    public void ChangeType_ClearsPreviousSensitiveFields()
    {
        using var vm = new ItemEditViewModel(_passwordRepo, _cardRepo, _noteRepo, _session);
        vm.InitializeForAdd(VaultItemType.Password);

        vm.PasswordTitle = "Password Title";
        vm.Password = "SecretPass";
        vm.Login = "my_login";

        vm.SelectType(VaultItemType.Card);

        Assert.Empty(vm.Password);
        Assert.Empty(vm.Login);
        Assert.Empty(vm.PasswordTitle);
        Assert.True(vm.IsCardType);

        vm.CardNumber = "123456789012";
        vm.Cvv = "999";

        vm.SelectType(VaultItemType.Note);

        Assert.Empty(vm.CardNumber);
        Assert.Empty(vm.Cvv);
        Assert.True(vm.IsNoteType);
    }

    [Fact]
    public async Task InvalidForm_DoesNotSave()
    {
        using var vm = new ItemEditViewModel(_passwordRepo, _cardRepo, _noteRepo, _session);
        vm.InitializeForAdd(VaultItemType.Password);

        // Empty title and login
        await vm.SaveAsync();

        Assert.True(vm.HasValidationError);
        Assert.Empty(_passwordRepo.getAll());
    }

    [Fact]
    public async Task SaveThenLock_DoesNotRestoreUnlockedUi()
    {
        using var vm = new ItemEditViewModel(_passwordRepo, _cardRepo, _noteRepo, _session);
        vm.InitializeForAdd(VaultItemType.Password);

        vm.PasswordTitle = "Title";
        vm.Login = "Login";
        vm.Password = "Password";

        var saveTask = vm.SaveAsync();
        _session.Lock();

        await saveTask;

        Assert.Empty(vm.Password);
        Assert.Empty(vm.Login);
    }

    // ─── 46. Delete Tests ──────────────────────────────────────────────────────

    [Fact]
    public async Task DeletePassword_RemovesRecord()
    {
        _passwordRepo.Add(new PasswordEntry { Id = 1, Title = "To Delete", Login = "u", Password = "p" });
        _passwordRepo.Save();

        using var vm = new ItemDetailViewModel(_passwordRepo, _cardRepo, _noteRepo, _clipboard, _session);
        await vm.LoadItemAsync(1, VaultItemType.Password);

        vm.ConfirmDeleteAction = _ => Task.FromResult(true);
        await vm.DeleteAsync();

        Assert.Empty(_passwordRepo.getAll());
    }

    [Fact]
    public async Task DeleteCard_RemovesRecord()
    {
        _cardRepo.Add(new CardEntry { Id = 1, Title = "Card To Delete", CardNumber = "1111222233334444" });
        _cardRepo.Save();

        using var vm = new ItemDetailViewModel(_passwordRepo, _cardRepo, _noteRepo, _clipboard, _session);
        await vm.LoadItemAsync(1, VaultItemType.Card);

        vm.ConfirmDeleteAction = _ => Task.FromResult(true);
        await vm.DeleteAsync();

        Assert.Empty(_cardRepo.getAll());
    }

    [Fact]
    public async Task DeleteNote_RemovesRecord()
    {
        _noteRepo.Add(new NoteEntry { Id = 1, Title = "Note To Delete", Content = "Content" });
        _noteRepo.Save();

        using var vm = new ItemDetailViewModel(_passwordRepo, _cardRepo, _noteRepo, _clipboard, _session);
        await vm.LoadItemAsync(1, VaultItemType.Note);

        vm.ConfirmDeleteAction = _ => Task.FromResult(true);
        await vm.DeleteAsync();

        Assert.Empty(_noteRepo.getAll());
    }

    [Fact]
    public async Task DeleteCancelled_DoesNotModifyRepository()
    {
        _passwordRepo.Add(new PasswordEntry { Id = 1, Title = "Preserved", Login = "u", Password = "p" });
        _passwordRepo.Save();

        using var vm = new ItemDetailViewModel(_passwordRepo, _cardRepo, _noteRepo, _clipboard, _session);
        await vm.LoadItemAsync(1, VaultItemType.Password);

        vm.ConfirmDeleteAction = _ => Task.FromResult(false);
        await vm.DeleteAsync();

        Assert.Single(_passwordRepo.getAll());
    }

    [Fact]
    public async Task DoubleDelete_DoesNotCrash()
    {
        _passwordRepo.Add(new PasswordEntry { Id = 1, Title = "Preserved", Login = "u", Password = "p" });
        _passwordRepo.Save();

        using var vm = new ItemDetailViewModel(_passwordRepo, _cardRepo, _noteRepo, _clipboard, _session);
        await vm.LoadItemAsync(1, VaultItemType.Password);

        vm.ConfirmDeleteAction = _ => Task.FromResult(true);
        var task1 = vm.DeleteAsync();
        var task2 = vm.DeleteAsync();

        await Task.WhenAll(task1, task2);

        Assert.Empty(_passwordRepo.getAll());
    }

    [Fact]
    public async Task LockDuringDelete_DoesNotRestoreSensitiveUi()
    {
        _passwordRepo.Add(new PasswordEntry { Id = 1, Title = "Preserved", Login = "u", Password = "p" });
        _passwordRepo.Save();

        using var vm = new ItemDetailViewModel(_passwordRepo, _cardRepo, _noteRepo, _clipboard, _session);
        await vm.LoadItemAsync(1, VaultItemType.Password);

        vm.ConfirmDeleteAction = _ => Task.FromResult(true);
        var deleteTask = vm.DeleteAsync();
        _session.Lock();

        await deleteTask;

        Assert.Empty(vm.Password);
        Assert.Empty(vm.Title);
    }

    // ─── 47. Stage 4 Regression Tests (SearchText & Minimization) ──────────────

    [Fact]
    public void VaultListItem_DoesNotRetainPassword()
    {
        var entry = new PasswordEntry
        {
            Id = 1,
            Title = "GitHub Account",
            Login = "octocat",
            Password = "SuperSecretPassword123",
            ServiceName = "GitHub"
        };

        var itemVm = VaultListItemViewModel.FromPassword(entry);

        Assert.DoesNotContain("SuperSecretPassword123", itemVm.SearchText);
        Assert.DoesNotContain("SuperSecretPassword123", itemVm.Subtitle);
    }

    [Fact]
    public void VaultListItem_DoesNotRetainCvv()
    {
        var entry = new CardEntry
        {
            Id = 1,
            Title = "My Card",
            CardNumber = "2200700011112222",
            CardHolder = "IVAN IVANOV",
            Cvv = "987"
        };

        var itemVm = VaultListItemViewModel.FromCard(entry);

        Assert.DoesNotContain("987", itemVm.SearchText);
        Assert.DoesNotContain("987", itemVm.Subtitle);
    }

    [Fact]
    public void VaultListItem_DoesNotRetainFullCardNumber()
    {
        var entry = new CardEntry
        {
            Id = 1,
            Title = "My Card",
            CardNumber = "2200700011112222",
            CardHolder = "IVAN IVANOV",
            Cvv = "987"
        };

        var itemVm = VaultListItemViewModel.FromCard(entry);

        // SearchText and Subtitle must NOT contain the full 16 digits
        Assert.DoesNotContain("2200700011112222", itemVm.SearchText);
        Assert.DoesNotContain("2200700011112222", itemVm.Subtitle);
        Assert.Contains("2222", itemVm.Subtitle); // Masked preview with last 4 digits
    }

    [Fact]
    public void VaultListItem_DoesNotRetainFullNoteContent()
    {
        string fullContent = new string('X', 500);
        var entry = new NoteEntry
        {
            Id = 1,
            Title = "Long Note",
            Content = fullContent
        };

        var itemVm = VaultListItemViewModel.FromNote(entry);

        Assert.True(itemVm.SearchText.Length < 100);
        Assert.True(itemVm.Subtitle.Length <= 53);
        Assert.DoesNotContain(fullContent, itemVm.SearchText);
    }
}
