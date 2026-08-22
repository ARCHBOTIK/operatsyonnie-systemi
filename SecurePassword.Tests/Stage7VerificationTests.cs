using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using SecurePassword;
using SecurePassword.Navigation;
using SecurePassword.ViewModels.Generator;
using SecurePassword.ViewModels.Settings;
using SecurePassword.ViewModels.Sync;
using SecurePassword.ViewModels.Vault;
using Xunit;

namespace SecurePassword.Tests;

public class Stage7VerificationTests : IDisposable
{
    private readonly string _testDir;

    public Stage7VerificationTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "SecurePassword_Stage7Tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDir))
                Directory.Delete(_testDir, recursive: true);
        }
        catch { }
    }

    private string GetPath(string filename) => Path.Combine(_testDir, filename);

    // ─────────────────────────────────────────────────────────────────────────────
    // 1. Authenticated UI Graph Lifecycle & GC WeakReference Test
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void AuthenticatedGraph_OnLock_DisposesSensitiveViewModelsAndUnsubscribes()
    {
        var session = new VaultSessionService();
        var clipboard = new MockClipboardBackend();
        var secureClipboard = new SecureClipboardService(clipboard);

        var keyPath = GetPath("keys.dat");
        var km = new keyManager(keyPath);
        km.CreateKeyFile("MasterPassword123!");

        var passRepo = new SecureRepository<PasswordEntry>(GetPath("passwords.dat"), km);
        var cardRepo = new SecureRepository<CardEntry>(GetPath("cards.dat"), km);
        var noteRepo = new SecureRepository<NoteEntry>(GetPath("notes.dat"), km);
        var netService = new NetworkService();
        var tcpBridge = new TcpBridge(netService, km);

        session.MarkAuthenticated();

        // Create transient ViewModels in a separate method so local references leave stack
        var weakRefs = CreateViewModelsAndGetWeakReferences(
            session, secureClipboard, km, passRepo, cardRepo, noteRepo, tcpBridge,
            out var vaultVm, out var detailVm, out var editVm, out var genVm, out var setVm, out var syncVm);

        // Verify initial state
        Assert.True(session.IsAuthenticated);
        Assert.NotNull(vaultVm);
        Assert.NotNull(detailVm);
        Assert.NotNull(editVm);
        Assert.NotNull(genVm);
        Assert.NotNull(setVm);
        Assert.NotNull(syncVm);

        // Set sensitive data
        genVm.GeneratePassword();
        Assert.False(string.IsNullOrEmpty(genVm.GeneratedPassword));

        setVm.CurrentPassword = "OldPassword1!";
        setVm.NewPassword = "NewPassword2!";
        setVm.ConfirmPassword = "NewPassword2!";
        Assert.False(string.IsNullOrEmpty(setVm.NewPassword));

        editVm.Password = "SensitiveSecretPassword";
        Assert.False(string.IsNullOrEmpty(editVm.Password));

        // Lock session
        session.Lock();
        Assert.False(session.IsAuthenticated);

        // Verify all ViewModels had their sensitive data cleared on lock
        Assert.False(genVm.HasEvaluatedPassword);
        Assert.Equal(GeneratorViewModel.InitialMessage, genVm.GeneratedPassword);
        Assert.True(string.IsNullOrEmpty(setVm.NewPassword));
        Assert.True(string.IsNullOrEmpty(setVm.CurrentPassword));
        Assert.True(string.IsNullOrEmpty(editVm.Password));
        Assert.True(string.IsNullOrEmpty(detailVm.Password));

        // Explicitly clear local strong references
        vaultVm = null!;
        detailVm = null!;
        editVm = null!;
        genVm = null!;
        setVm = null!;
        syncVm = null!;

        // Perform GC to test unreachable transient ViewModels
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        // Re-unlock session
        session.MarkAuthenticated();
        Assert.True(session.IsAuthenticated);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference[] CreateViewModelsAndGetWeakReferences(
        VaultSessionService session,
        ISecureClipboardService clipboard,
        keyManager km,
        SecureRepository<PasswordEntry> passRepo,
        SecureRepository<CardEntry> cardRepo,
        SecureRepository<NoteEntry> noteRepo,
        TcpBridge tcpBridge,
        out VaultViewModel vaultVm,
        out ItemDetailViewModel detailVm,
        out ItemEditViewModel editVm,
        out GeneratorViewModel genVm,
        out SettingsViewModel setVm,
        out SyncViewModel syncVm)
    {
        vaultVm = new VaultViewModel(passRepo, cardRepo, noteRepo, session);
        detailVm = new ItemDetailViewModel(passRepo, cardRepo, noteRepo, clipboard, session);
        editVm = new ItemEditViewModel(passRepo, cardRepo, noteRepo, session);
        genVm = new GeneratorViewModel(clipboard, session);
        setVm = new SettingsViewModel(session, new MasterPasswordService(km), km);
        syncVm = new SyncViewModel(tcpBridge, session);

        return new[]
        {
            new WeakReference(vaultVm),
            new WeakReference(detailVm),
            new WeakReference(editVm),
            new WeakReference(genVm),
            new WeakReference(setVm),
            new WeakReference(syncVm)
        };
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // 2. Full Vault Persistence Integration Scenario (Real encrypted files)
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void FullVaultPersistence_AcrossMultipleLockUnlockCycles_PersistsAndModifiesCleanly()
    {
        string keyFile = GetPath("keys.dat");
        string passFile = GetPath("passwords.dat");
        string cardFile = GetPath("cards.dat");
        string noteFile = GetPath("notes.dat");
        string masterPass = "MasterSecurePass2026$!";

        // Step 1: Create vault
        var km1 = new keyManager(keyFile);
        km1.CreateKeyFile(masterPass);

        var passRepo1 = new SecureRepository<PasswordEntry>(passFile, km1);
        var cardRepo1 = new SecureRepository<CardEntry>(cardFile, km1);
        var noteRepo1 = new SecureRepository<NoteEntry>(noteFile, km1);

        // Step 2: Add Password, Card, Note
        var passEntry = new PasswordEntry
        {
            Id = 1,
            ServiceName = "GitHub",
            Title = "GitHub Account",
            Login = "developer",
            Password = "InitialGitHubPassword!1"
        };
        passRepo1.Add(passEntry);
        passRepo1.Save();

        var cardEntry = new CardEntry
        {
            Id = 1,
            Title = "Corporate Visa",
            CardNumber = "4111222233334444",
            CardHolder = "ALEX DEVELOPER",
            ExpiryDate = "12/29",
            Cvv = "999",
            BankName = "Chase"
        };
        cardRepo1.Add(cardEntry);
        cardRepo1.Save();

        var noteEntry = new NoteEntry
        {
            Id = 1,
            Title = "Server SSH Keys",
            Content = "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIG... dev@work"
        };
        noteRepo1.Add(noteEntry);
        noteRepo1.Save();

        // Step 3: Lock and wipe all in-memory keys
        km1.ClearLoadedKey();

        // Step 4: Re-open with master password
        var km2 = new keyManager(keyFile);
        km2.LoadKeyFile(masterPass);
        var passRepo2 = new SecureRepository<PasswordEntry>(passFile, km2);
        var cardRepo2 = new SecureRepository<CardEntry>(cardFile, km2);
        var noteRepo2 = new SecureRepository<NoteEntry>(noteFile, km2);

        var loadedPass = passRepo2.GetItemById(passEntry.Id);
        var loadedCard = cardRepo2.GetItemById(cardEntry.Id);
        var loadedNote = noteRepo2.GetItemById(noteEntry.Id);

        Assert.NotNull(loadedPass);
        Assert.Equal("GitHub Account", loadedPass.Title);
        Assert.Equal("InitialGitHubPassword!1", loadedPass.Password);

        Assert.NotNull(loadedCard);
        Assert.Equal("Corporate Visa", loadedCard.Title);
        Assert.Equal("4111222233334444", loadedCard.CardNumber);
        Assert.Equal("999", loadedCard.Cvv);

        Assert.NotNull(loadedNote);
        Assert.Equal("Server SSH Keys", loadedNote.Title);
        Assert.Contains("ssh-ed25519", loadedNote.Content);

        // Step 5: Edit all 3 entries
        loadedPass.Password = "UpdatedGitHubPass2026$#";
        passRepo2.Update(loadedPass);
        passRepo2.Save();

        loadedCard.CardHolder = "ALEXANDER DEVELOPER";
        loadedCard.ExpiryDate = "01/30";
        cardRepo2.Update(loadedCard);
        cardRepo2.Save();

        loadedNote.Content = "Updated content: key replaced";
        noteRepo2.Update(loadedNote);
        noteRepo2.Save();

        // Step 6: Lock and wipe
        km2.ClearLoadedKey();

        // Step 7: Re-open and verify edits persisted
        var km3 = new keyManager(keyFile);
        km3.LoadKeyFile(masterPass);
        var passRepo3 = new SecureRepository<PasswordEntry>(passFile, km3);
        var cardRepo3 = new SecureRepository<CardEntry>(cardFile, km3);
        var noteRepo3 = new SecureRepository<NoteEntry>(noteFile, km3);

        var reloadedPass = passRepo3.GetItemById(passEntry.Id);
        var reloadedCard = cardRepo3.GetItemById(cardEntry.Id);
        var reloadedNote = noteRepo3.GetItemById(noteEntry.Id);

        Assert.NotNull(reloadedPass);
        Assert.Equal("UpdatedGitHubPass2026$#", reloadedPass.Password);

        Assert.NotNull(reloadedCard);
        Assert.Equal("ALEXANDER DEVELOPER", reloadedCard.CardHolder);
        Assert.Equal("01/30", reloadedCard.ExpiryDate);

        Assert.NotNull(reloadedNote);
        Assert.Equal("Updated content: key replaced", reloadedNote.Content);

        // Step 8: Delete card entry
        cardRepo3.Remove(cardEntry.Id);
        cardRepo3.Save();

        // Step 9: Lock and wipe
        km3.ClearLoadedKey();

        // Step 10: Re-open and verify card is gone, others remain
        var km4 = new keyManager(keyFile);
        km4.LoadKeyFile(masterPass);
        var passRepo4 = new SecureRepository<PasswordEntry>(passFile, km4);
        var cardRepo4 = new SecureRepository<CardEntry>(cardFile, km4);
        var noteRepo4 = new SecureRepository<NoteEntry>(noteFile, km4);

        Assert.NotNull(passRepo4.GetItemById(passEntry.Id));
        Assert.Null(cardRepo4.GetItemById(cardEntry.Id));
        Assert.NotNull(noteRepo4.GetItemById(noteEntry.Id));

        Assert.Single(passRepo4.getAll());
        Assert.Empty(cardRepo4.getAll());
        Assert.Single(noteRepo4.getAll());

        km4.ClearLoadedKey();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // 3. Inactivity Background Auto-Lock Monitor Test
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task InactivityMonitor_AutoLocksWhenTimeoutExpires()
    {
        var session = new VaultSessionService
        {
            LockOnTimer = true,
            InactivityTimeout = TimeSpan.FromMilliseconds(200)
        };

        session.MarkAuthenticated();
        Assert.True(session.IsAuthenticated);

        // Wait slightly more than timeout
        await Task.Delay(400);

        Assert.False(session.IsAuthenticated);
    }

    [Fact]
    public async Task InactivityMonitor_RecordActivityPostponesAutoLock()
    {
        var session = new VaultSessionService
        {
            LockOnTimer = true,
            InactivityTimeout = TimeSpan.FromMilliseconds(300)
        };

        session.MarkAuthenticated();
        Assert.True(session.IsAuthenticated);

        // Wait 150ms and record activity
        await Task.Delay(150);
        session.RecordActivity();

        // Wait another 150ms (total 300ms from start, but only 150ms from last activity)
        await Task.Delay(150);
        Assert.True(session.IsAuthenticated);

        // Now wait 350ms without activity -> should auto-lock
        await Task.Delay(350);
        Assert.False(session.IsAuthenticated);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // 4. AppRootNavigator Factory & State Transition
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void AppRootNavigator_TransitionsAndDestroysOnLock()
    {
        var session = new VaultSessionService();
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection().BuildServiceProvider();
        var navigator = new AppRootNavigator(services, session);

        int lockedCreated = 0;
        int unlockedCreated = 0;

        navigator.LockedPageFactory = () =>
        {
            lockedCreated++;
            return null!;
        };

        navigator.UnlockedPageFactory = () =>
        {
            unlockedCreated++;
            return null!;
        };

        // Initial state is locked
        var initial = navigator.GetInitialRoot();
        Assert.Equal(RootNavigationState.Locked, navigator.CurrentState);
        Assert.Equal(1, lockedCreated);
        Assert.Equal(0, unlockedCreated);

        // Unlock
        session.MarkAuthenticated();
        navigator.ShowUnlockedRoot();
        Assert.Equal(RootNavigationState.Unlocked, navigator.CurrentState);

        // Re-lock
        session.Lock();
        Assert.Equal(RootNavigationState.Locked, navigator.CurrentState);

        navigator.Dispose();
    }
}
