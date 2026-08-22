namespace SecurePassword.Navigation;

public sealed class AppRootNavigator : IAppRootNavigator
{
    private readonly IServiceProvider _services;
    private readonly VaultSessionService _vaultSession;
    private Window? _window;
    private bool _disposed;

    public RootNavigationState CurrentState { get; private set; } = RootNavigationState.Locked;
    public int NavigationCount { get; private set; }

    public Func<Page>? LockedPageFactory { get; set; }
    public Func<Page>? UnlockedPageFactory { get; set; }

    public AppRootNavigator(IServiceProvider services, VaultSessionService vaultSession)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _vaultSession = vaultSession ?? throw new ArgumentNullException(nameof(vaultSession));

        _vaultSession.StateChanged += OnSessionStateChanged;
    }

    public Page GetInitialRoot()
    {
        if (_vaultSession.IsAuthenticated)
        {
            CurrentState = RootNavigationState.Unlocked;
            NavigationCount++;
            return CreateFreshShell();
        }

        CurrentState = RootNavigationState.Locked;
        NavigationCount++;
        return CreateFreshMasterPasswordPage();
    }

    public void AttachWindow(Window window)
    {
        _window = window;
    }

    public void ShowLockedRoot()
    {
        if (CurrentState == RootNavigationState.Locked && NavigationCount > 0)
            return;

        CurrentState = RootNavigationState.Locked;
        NavigationCount++;

        RunOnMainThread(() =>
        {
            var targetWindow = GetTargetWindow();
            if (targetWindow is null) return;

            var page = CreateFreshMasterPasswordPage();
            targetWindow.Page = page;
        });
    }

    public void ShowUnlockedRoot()
    {
        if (CurrentState == RootNavigationState.Unlocked && NavigationCount > 0)
            return;

        CurrentState = RootNavigationState.Unlocked;
        NavigationCount++;

        RunOnMainThread(() =>
        {
            var targetWindow = GetTargetWindow();
            if (targetWindow is null) return;

            var shell = CreateFreshShell();
            targetWindow.Page = shell;
        });
    }

    private Window? GetTargetWindow()
    {
        if (_window is not null) return _window;
        return Application.Current?.Windows.FirstOrDefault();
    }

    private Page CreateFreshMasterPasswordPage()
    {
        if (LockedPageFactory is not null)
            return LockedPageFactory();

        var pageType = Type.GetType("SecurePassword.MasterPasswordPage, SecurePassword");
        if (pageType is not null)
        {
            var resolved = _services.GetService(pageType) as Page;
            if (resolved is not null) return resolved;
        }

        return null!;
    }

    private Page CreateFreshShell()
    {
        if (UnlockedPageFactory is not null)
            return UnlockedPageFactory();

        var shellType = Type.GetType("SecurePassword.AppShell, SecurePassword");
        if (shellType is not null)
        {
            var resolved = _services.GetService(shellType) as Page;
            if (resolved is not null) return resolved;
        }

        return null!;
    }

    private void OnSessionStateChanged()
    {
        if (!_vaultSession.IsAuthenticated)
        {
            ShowLockedRoot();
        }
        else if (CurrentState != RootNavigationState.Unlocked)
        {
            ShowUnlockedRoot();
        }
    }

    private static void RunOnMainThread(Action action)
    {
        try
        {
            if (MainThread.IsMainThread)
            {
                action();
            }
            else
            {
                MainThread.BeginInvokeOnMainThread(action);
            }
        }
        catch
        {
            // Fallback for test / headless environments where MainThread dispatcher is not initialized
            action();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _vaultSession.StateChanged -= OnSessionStateChanged;
    }
}
