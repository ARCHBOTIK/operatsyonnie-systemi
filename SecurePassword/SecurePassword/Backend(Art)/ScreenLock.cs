public class ScreenLock
{
    private bool _isLocked;
    private string _password;

    public ScreenLock(string password)
    {
        _password = password;
        _isLocked = false;
    }

    public void Lock()
    {
        _isLocked = true;
    }

    public bool Unlock(string input)
    {
        if (input == _password)
        {
            _isLocked = false;
            return true;
        }
        return false;
    }

    public bool GetStatus()
    {
        return _isLocked;
    }
}
