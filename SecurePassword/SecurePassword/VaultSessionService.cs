using Microsoft.Maui.Storage;

namespace SecurePassword;

public sealed class VaultSessionService
{
    private const string LockOnExitPreferenceKey = "lock_on_exit";
    private const string LockOnMinimizePreferenceKey = "lock_on_minimize";
    private const string LockOnTimerPreferenceKey = "lock_on_timer";

    private DateTimeOffset _lastActivityUtc = DateTimeOffset.UtcNow;

    public event Action? StateChanged;

    public bool IsAuthenticated { get; private set; }

    public bool LockOnExit
    {
        get => Preferences.Default.Get(LockOnExitPreferenceKey, true);
        set
        {
            Preferences.Default.Set(LockOnExitPreferenceKey, value);
            NotifyStateChanged();
        }
    }

    public bool LockOnMinimize
    {
        get => Preferences.Default.Get(LockOnMinimizePreferenceKey, true);
        set
        {
            Preferences.Default.Set(LockOnMinimizePreferenceKey, value);
            NotifyStateChanged();
        }
    }

    public bool LockOnTimer
    {
        get => Preferences.Default.Get(LockOnTimerPreferenceKey, false);
        set
        {
            Preferences.Default.Set(LockOnTimerPreferenceKey, value);
            NotifyStateChanged();
        }
    }

    public TimeSpan InactivityTimeout { get; } = TimeSpan.FromMinutes(2);

    public void MarkAuthenticated()
    {
        IsAuthenticated = true;
        RecordActivity();
        NotifyStateChanged();
    }

    public void Lock()
    {
        if (!IsAuthenticated)
            return;

        IsAuthenticated = false;
        NotifyStateChanged();
    }

    public void RecordActivity()
    {
        _lastActivityUtc = DateTimeOffset.UtcNow;
    }

    public bool ShouldLockForInactivity()
    {
        return IsAuthenticated &&
            LockOnTimer &&
            DateTimeOffset.UtcNow - _lastActivityUtc >= InactivityTimeout;
    }

    private void NotifyStateChanged()
    {
        StateChanged?.Invoke();
    }
}
