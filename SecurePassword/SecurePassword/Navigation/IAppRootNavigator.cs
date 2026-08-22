namespace SecurePassword.Navigation;

public enum RootNavigationState
{
    Locked,
    Unlocked
}

/// <summary>
/// Central coordinator for switching root application pages between
/// locked state (MasterPasswordPage) and unlocked state (AppShell).
/// </summary>
public interface IAppRootNavigator : IDisposable
{
    Page GetInitialRoot();
    void AttachWindow(Window window);
    void ShowLockedRoot();
    void ShowUnlockedRoot();
    RootNavigationState CurrentState { get; }
    int NavigationCount { get; }
}
