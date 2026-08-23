using SecurePassword.Views.Import;

namespace SecurePassword;

public partial class AppShell : Shell
{
    private static readonly object RouteRegistrationLock = new();
    private static bool _importRouteRegistered;
    private readonly VaultSessionService? _vaultSession;

    public AppShell(VaultSessionService? vaultSession = null)
    {
        InitializeComponent();
        _vaultSession = vaultSession;

        lock (RouteRegistrationLock)
        {
            if (!_importRouteRegistered)
            {
                Routing.RegisterRoute("import", typeof(ImportPage));
                _importRouteRegistered = true;
            }
        }
    }

    protected override void OnNavigated(ShellNavigatedEventArgs args)
    {
        base.OnNavigated(args);
        _vaultSession?.RecordActivity();
    }
}
