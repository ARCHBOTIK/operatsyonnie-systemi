namespace SecurePassword;

public partial class AppShell : Shell
{
    private readonly VaultSessionService? _vaultSession;

    public AppShell(VaultSessionService? vaultSession = null)
    {
        InitializeComponent();
        _vaultSession = vaultSession;
    }

    protected override void OnNavigated(ShellNavigatedEventArgs args)
    {
        base.OnNavigated(args);
        _vaultSession?.RecordActivity();
    }
}
