using SecurePassword.Navigation;
using Xunit;

namespace SecurePassword.Tests;

public class AppRootNavigatorTests : IDisposable
{
    private readonly VaultSessionService _session;
    private readonly TestServiceProvider _services;
    private readonly AppRootNavigator _navigator;

    public AppRootNavigatorTests()
    {
        _session = new VaultSessionService();
        _services = new TestServiceProvider();
        _navigator = new AppRootNavigator(_services, _session);
    }

    public void Dispose()
    {
        _navigator.Dispose();
    }

    private sealed class TestServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    [Fact]
    public void InitialState_ShowsLockedRoot()
    {
        _navigator.GetInitialRoot();
        Assert.Equal(RootNavigationState.Locked, _navigator.CurrentState);
        Assert.Equal(1, _navigator.NavigationCount);
    }

    [Fact]
    public void SuccessfulUnlock_ShowsAppShell()
    {
        _navigator.GetInitialRoot();
        _session.MarkAuthenticated();
        _navigator.ShowUnlockedRoot();

        Assert.Equal(RootNavigationState.Unlocked, _navigator.CurrentState);
        Assert.Equal(2, _navigator.NavigationCount);
    }

    [Fact]
    public void Lock_ReplacesAppShellWithMasterPasswordPage()
    {
        _navigator.GetInitialRoot();
        _session.MarkAuthenticated();
        _navigator.ShowUnlockedRoot();
        Assert.Equal(RootNavigationState.Unlocked, _navigator.CurrentState);

        _session.Lock();

        Assert.Equal(RootNavigationState.Locked, _navigator.CurrentState);
        Assert.Equal(3, _navigator.NavigationCount);
    }

    [Fact]
    public void SecondUnlock_CreatesFreshAuthenticatedRoot()
    {
        _navigator.GetInitialRoot();

        // 1st Unlock
        _session.MarkAuthenticated();
        _navigator.ShowUnlockedRoot();
        Assert.Equal(RootNavigationState.Unlocked, _navigator.CurrentState);

        // Lock
        _session.Lock();
        Assert.Equal(RootNavigationState.Locked, _navigator.CurrentState);

        // 2nd Unlock
        _session.MarkAuthenticated();
        _navigator.ShowUnlockedRoot();
        Assert.Equal(RootNavigationState.Unlocked, _navigator.CurrentState);

        Assert.Equal(4, _navigator.NavigationCount);
    }

    [Fact]
    public void Lock_DoesNotReuseOldShell()
    {
        _navigator.GetInitialRoot();
        _session.MarkAuthenticated();
        _navigator.ShowUnlockedRoot();

        _session.Lock();

        Assert.Equal(RootNavigationState.Locked, _navigator.CurrentState);
    }

    [Fact]
    public void Reset_ShowsVaultCreationRoot()
    {
        _navigator.GetInitialRoot();
        _session.MarkAuthenticated();
        _navigator.ShowUnlockedRoot();

        // When reset is performed, session is locked
        _session.Lock();

        Assert.Equal(RootNavigationState.Locked, _navigator.CurrentState);
    }

    [Fact]
    public void P2PImportLock_ReturnsToAuthentication()
    {
        _navigator.GetInitialRoot();
        _session.MarkAuthenticated();
        _navigator.ShowUnlockedRoot();
        Assert.Equal(RootNavigationState.Unlocked, _navigator.CurrentState);

        // P2P import triggers session lock requiring re-authentication
        _session.Lock();

        Assert.Equal(RootNavigationState.Locked, _navigator.CurrentState);
    }

    [Fact]
    public void RepeatedLock_IsIdempotent()
    {
        _navigator.GetInitialRoot();
        int countBefore = _navigator.NavigationCount;

        _navigator.ShowLockedRoot();
        _navigator.ShowLockedRoot();
        _navigator.ShowLockedRoot();

        Assert.Equal(RootNavigationState.Locked, _navigator.CurrentState);
        Assert.Equal(countBefore, _navigator.NavigationCount);
    }

    [Fact]
    public void RepeatedUnlock_DoesNotCreateDuplicateNavigation()
    {
        _navigator.GetInitialRoot();
        _session.MarkAuthenticated();
        _navigator.ShowUnlockedRoot();
        int countAfterFirstUnlock = _navigator.NavigationCount;

        _navigator.ShowUnlockedRoot();
        _navigator.ShowUnlockedRoot();

        Assert.Equal(RootNavigationState.Unlocked, _navigator.CurrentState);
        Assert.Equal(countAfterFirstUnlock, _navigator.NavigationCount);
    }

    [Fact]
    public void SessionEvent_AfterDisposedShell_DoesNotRetainOldUi()
    {
        _navigator.GetInitialRoot();
        _session.MarkAuthenticated();
        _navigator.ShowUnlockedRoot();

        _navigator.Dispose();

        int countBefore = _navigator.NavigationCount;
        // Since navigator is disposed, it should no longer listen to session state changes
        _session.Lock();

        Assert.Equal(countBefore, _navigator.NavigationCount);
    }
}
