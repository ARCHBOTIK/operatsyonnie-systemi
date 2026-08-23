using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using SecurePassword;
using SecurePassword.Navigation;
using SecurePassword.ViewModels.Generator;
using SecurePassword.ViewModels.Settings;
using SecurePassword.ViewModels.Sync;
using SecurePassword.ViewModels.Vault;
using Xunit;

namespace SecurePassword.Tests;

public class Stage8ARegressionTests : IDisposable
{
    private readonly string _testDir;
    private readonly string _keyFilePath;
    private readonly keyManager _km;
    private readonly VaultSessionService _session;
    private readonly SecureRepository<PasswordEntry> _passRepo;
    private readonly SecureRepository<CardEntry> _cardRepo;
    private readonly SecureRepository<NoteEntry> _noteRepo;
    private readonly ISecureClipboardService _clipboard;
    private readonly MockClipboardBackend _clipboardBackend;

    public Stage8ARegressionTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "Stage8ATests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);

        _keyFilePath = Path.Combine(_testDir, "keys.dat");
        _km = new keyManager(_keyFilePath);
        _km.CreateKeyFile("MasterPassword123!");

        _passRepo = new SecureRepository<PasswordEntry>(Path.Combine(_testDir, "passwords.dat"), _km);
        _cardRepo = new SecureRepository<CardEntry>(Path.Combine(_testDir, "cards.dat"), _km);
        _noteRepo = new SecureRepository<NoteEntry>(Path.Combine(_testDir, "notes.dat"), _km);

        _session = new VaultSessionService();
        _session.MarkAuthenticated();

        _clipboardBackend = new MockClipboardBackend();
        _clipboard = new SecureClipboardService(_clipboardBackend);
    }

    public void Dispose()
    {
        _km.ClearLoadedKey();
        try
        {
            if (Directory.Exists(_testDir))
                Directory.Delete(_testDir, true);
        }
        catch { }
    }

    private static string GetApplicationSourceFile(string relativePath)
    {
        string repositoryRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", ".."));
        string sourcePath = Path.Combine(repositoryRoot, "SecurePassword", "SecurePassword", relativePath);

        Assert.True(File.Exists(sourcePath), $"Expected application source file was not found: {sourcePath}");
        return sourcePath;
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // 1. BLOCKER 1 — Generator Regression Tests
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Generator_AppProgressBarStyle_IsDefinedInStylesResourceDictionary()
    {
        // GeneratorPage resolves this static resource during XAML construction.
        string generatorPage = File.ReadAllText(GetApplicationSourceFile(Path.Combine("Views", "Generator", "GeneratorPage.xaml")));
        string styles = File.ReadAllText(GetApplicationSourceFile(Path.Combine("Resources", "Styles", "Styles.xaml")));

        Assert.Contains("Style=\"{StaticResource AppProgressBarStyle}\"", generatorPage);
        Assert.Contains("<Style x:Key=\"AppProgressBarStyle\" TargetType=\"ProgressBar\">", styles);
    }

    [Fact]
    public void GeneratorPage_ViewModelResolution_DoesNotThrow()
    {
        using var vm = new GeneratorViewModel(_clipboard, _session);
        Assert.NotNull(vm);
        Assert.NotNull(vm.GenerateCommand);
        Assert.NotNull(vm.CopyCommand);
        Assert.NotNull(vm.ResetPreviewCommand);
    }

    [Fact]
    public async Task Generator_FullFunctionalParity_LifecycleAndGeneration()
    {
        using var vm = new GeneratorViewModel(_clipboard, _session);

        // 1. Open Generator & verify default state
        Assert.Equal(12, vm.PasswordLength);
        Assert.True(vm.IncludeDigits);
        Assert.True(vm.IncludeLowercase);
        Assert.True(vm.IncludeUppercase);
        Assert.False(vm.IncludeSpecial);
        Assert.False(vm.HasEvaluatedPassword);

        // 2. Change length
        vm.PasswordLength = 20;
        Assert.Equal(20, vm.PasswordLength);
        Assert.Equal(20.0, vm.PasswordLengthDouble);

        // 3. Toggle character sets
        vm.IncludeSpecial = true;
        Assert.True(vm.IncludeSpecial);
        Assert.Equal(4, vm.ActiveOptionsCount);

        // 4. Generate
        vm.GenerateCommand.Execute(null);
        Assert.True(vm.HasEvaluatedPassword);
        Assert.Equal(20, vm.GeneratedPassword.Length);
        Assert.True(vm.EntropyBits > 0);
        Assert.False(string.IsNullOrEmpty(vm.StrengthText));
        Assert.False(string.IsNullOrEmpty(vm.StrengthColorHex));

        // 5. Copy
        vm.CopyCommand.Execute(null);
        await Task.Delay(50);
        Assert.True(vm.Copied);
        Assert.Equal(vm.GeneratedPassword, _clipboardBackend.Content);
        Assert.True(_clipboardBackend.LastIsSensitive);

        // 6. Multiple generate in sequence
        string firstPass = vm.GeneratedPassword;
        vm.GenerateCommand.Execute(null);
        string secondPass = vm.GeneratedPassword;
        Assert.NotEqual(firstPass, secondPass);

        // 7. Clear sensitive on tab leave
        vm.ClearSensitiveData();
        Assert.False(vm.HasEvaluatedPassword);
        Assert.False(vm.Copied);

        // 8. Re-generate & Lock/Unlock parity
        vm.GenerateCommand.Execute(null);
        Assert.True(vm.HasEvaluatedPassword);

        _session.Lock();
        Assert.False(vm.HasEvaluatedPassword);

        _session.MarkAuthenticated();
        vm.GenerateCommand.Execute(null);
        Assert.True(vm.HasEvaluatedPassword);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // 2. BLOCKER 2 — Add Item Type Selection Regression Tests
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void AddMode_ExposesPasswordCardAndNoteTypes()
    {
        using var vm = new ItemEditViewModel(_passRepo, _cardRepo, _noteRepo, _session);
        vm.InitializeForAdd();

        Assert.True(vm.CanChangeType);
        Assert.False(vm.IsEditMode);
        Assert.Equal("Новая запись", vm.PageTitle);
        Assert.Equal(VaultItemType.Password, vm.SelectedType);
        Assert.True(vm.IsPasswordType);
        Assert.False(vm.IsCardType);
        Assert.False(vm.IsNoteType);
    }

    [Fact]
    public void AddMode_TypeSelectorPlacesEveryTypeInItsOwnColumn()
    {
        string xaml = File.ReadAllText(GetApplicationSourceFile(Path.Combine("Views", "Vault", "ItemEditPage.xaml")));

        Assert.Contains("<Grid ColumnDefinitions=\"*,*,*\"", xaml);
        Assert.Matches(new Regex("<Button\\s+Grid.Column=\"0\"[\\s\\S]*?CommandParameter=\"Password\""), xaml);
        Assert.Matches(new Regex("<Button\\s+Grid.Column=\"1\"[\\s\\S]*?CommandParameter=\"Card\""), xaml);
        Assert.Matches(new Regex("<Button\\s+Grid.Column=\"2\"[\\s\\S]*?CommandParameter=\"Note\""), xaml);
    }

    [Fact]
    public void SelectPassword_ShowsPasswordState()
    {
        using var vm = new ItemEditViewModel(_passRepo, _cardRepo, _noteRepo, _session);
        vm.InitializeForAdd(VaultItemType.Note);
        Assert.True(vm.IsNoteType);

        vm.SelectTypeCommand.Execute("Password");

        Assert.Equal(VaultItemType.Password, vm.SelectedType);
        Assert.True(vm.IsPasswordType);
        Assert.False(vm.IsCardType);
        Assert.False(vm.IsNoteType);
    }

    [Fact]
    public void SelectCard_ShowsCardState()
    {
        using var vm = new ItemEditViewModel(_passRepo, _cardRepo, _noteRepo, _session);
        vm.InitializeForAdd(VaultItemType.Password);

        vm.SelectTypeCommand.Execute("Card");

        Assert.Equal(VaultItemType.Card, vm.SelectedType);
        Assert.True(vm.IsCardType);
        Assert.False(vm.IsPasswordType);
        Assert.False(vm.IsNoteType);
    }

    [Fact]
    public void SelectNote_ShowsNoteState()
    {
        using var vm = new ItemEditViewModel(_passRepo, _cardRepo, _noteRepo, _session);
        vm.InitializeForAdd(VaultItemType.Password);

        vm.SelectTypeCommand.Execute("Note");

        Assert.Equal(VaultItemType.Note, vm.SelectedType);
        Assert.True(vm.IsNoteType);
        Assert.False(vm.IsPasswordType);
        Assert.False(vm.IsCardType);
    }

    [Fact]
    public void TypeSwitch_ClearsPreviousSensitiveState()
    {
        using var vm = new ItemEditViewModel(_passRepo, _cardRepo, _noteRepo, _session);
        vm.InitializeForAdd(VaultItemType.Password);

        vm.PasswordTitle = "GitHub Account";
        vm.Login = "octocat";
        vm.Password = "SuperSecretPassword123";
        vm.ServiceName = "GitHub";

        // Password -> Card
        vm.SelectType(VaultItemType.Card);
        Assert.True(vm.IsCardType);
        Assert.Empty(vm.PasswordTitle);
        Assert.Empty(vm.Login);
        Assert.Empty(vm.Password);
        Assert.Empty(vm.ServiceName);

        vm.CardTitle = "T-Bank Black";
        vm.CardNumber = "2200111122223333";
        vm.CardHolder = "IVAN IVANOV";
        vm.ExpiryDate = "12/28";
        vm.Cvv = "999";
        vm.BankName = "T-Bank";

        // Card -> Note
        vm.SelectType(VaultItemType.Note);
        Assert.True(vm.IsNoteType);
        Assert.Empty(vm.CardTitle);
        Assert.Empty(vm.CardNumber);
        Assert.Empty(vm.CardHolder);
        Assert.Empty(vm.ExpiryDate);
        Assert.Empty(vm.Cvv);
        Assert.Empty(vm.BankName);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // 3. BLOCKER 3 — Settings -> Sync Navigation Regression Tests
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Settings_SyncCommand_RequestsNativeSyncNavigation()
    {
        var masterPassService = new MasterPasswordService(_km);
        using var vm = new SettingsViewModel(_session, masterPassService, _km);

        bool navigationInvoked = false;
        vm.NavigateToSyncAction = () =>
        {
            navigationInvoked = true;
        };

        Assert.NotNull(vm.NavigateToSyncCommand);
        vm.NavigateToSyncCommand.Execute(null);

        Assert.True(navigationInvoked);
    }

    [Fact]
    public void SettingsPage_WiresSyncCommandToNativeSyncRoute()
    {
        string codeBehind = File.ReadAllText(GetApplicationSourceFile(Path.Combine("Views", "Settings", "SettingsPage.xaml.cs")));

        Assert.Contains("_viewModel.NavigateToSyncAction =", codeBehind);
        Assert.Contains("Shell.Current.GoToAsync(\"//sync\")", codeBehind);
    }

    [Fact]
    public void Settings_SyncNavigation_AfterRepeatedOpen_RemainsValid()
    {
        var masterPassService = new MasterPasswordService(_km);
        using var vm = new SettingsViewModel(_session, masterPassService, _km);

        int invocationCount = 0;
        vm.NavigateToSyncAction = () =>
        {
            invocationCount++;
        };

        vm.NavigateToSyncCommand.Execute(null);
        vm.NavigateToSyncCommand.Execute(null);
        vm.NavigateToSyncCommand.Execute(null);

        Assert.Equal(3, invocationCount);
    }

    [Fact]
    public void LockFromSync_ReturnsToLockedRoot()
    {
        var session = new VaultSessionService();
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection().BuildServiceProvider();
        using var navigator = new AppRootNavigator(services, session);

        // 1. Initial lock state
        navigator.GetInitialRoot();
        Assert.Equal(RootNavigationState.Locked, navigator.CurrentState);
        Assert.Equal(1, navigator.NavigationCount);

        // 2. Unlock to AppShell (contains Vault, Sync, Generator, Settings)
        session.MarkAuthenticated();
        navigator.ShowUnlockedRoot();
        Assert.Equal(RootNavigationState.Unlocked, navigator.CurrentState);
        Assert.Equal(2, navigator.NavigationCount);

        // 3. User is in Sync tab, and then session Lock is triggered
        session.Lock();

        // 4. AppRootNavigator must transition back to locked root immediately
        Assert.Equal(RootNavigationState.Locked, navigator.CurrentState);
        Assert.Equal(3, navigator.NavigationCount);
    }
}
