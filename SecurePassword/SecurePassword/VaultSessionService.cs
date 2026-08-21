using Microsoft.Maui.Storage;

namespace SecurePassword;

public sealed class VaultSessionService
{
    private const string LockOnExitPreferenceKey = "lock_on_exit";
    private const string LockOnMinimizePreferenceKey = "lock_on_minimize";
    private const string LockOnTimerPreferenceKey = "lock_on_timer";

    private bool _lockOnExit = true;
    private bool _lockOnMinimize = true;
    private bool _lockOnTimer = true;
    private DateTimeOffset _lastActivityUtc = DateTimeOffset.UtcNow;

    public event Action? StateChanged;

    public bool IsAuthenticated { get; private set; }

    public bool LockOnExit
    {
        get
        {
            try { return Preferences.Default.Get(LockOnExitPreferenceKey, _lockOnExit); }
            catch { return _lockOnExit; }
        }
        set
        {
            _lockOnExit = value;
            try { Preferences.Default.Set(LockOnExitPreferenceKey, value); } catch { }
            NotifyStateChanged();
        }
    }

    public bool LockOnMinimize
    {
        get
        {
            try { return Preferences.Default.Get(LockOnMinimizePreferenceKey, _lockOnMinimize); }
            catch { return _lockOnMinimize; }
        }
        set
        {
            _lockOnMinimize = value;
            try { Preferences.Default.Set(LockOnMinimizePreferenceKey, value); } catch { }
            NotifyStateChanged();
        }
    }

    public bool LockOnTimer
    {
        get
        {
            try { return Preferences.Default.Get(LockOnTimerPreferenceKey, _lockOnTimer); }
            catch { return _lockOnTimer; }
        }
        set
        {
            _lockOnTimer = value;
            try { Preferences.Default.Set(LockOnTimerPreferenceKey, value); } catch { }
            NotifyStateChanged();
        }
    }

    public TimeSpan InactivityTimeout { get; set; } = TimeSpan.FromMinutes(5);

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
