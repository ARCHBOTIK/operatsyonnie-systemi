using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SecurePassword.ViewModels.Base;

/// <summary>
/// Lightweight base class for ViewModels providing INotifyPropertyChanged
/// and standard cleanup support.
/// </summary>
public abstract class BaseViewModel : INotifyPropertyChanged, IDisposable
{
    private bool _isBusy;
    private string _title = string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsBusy
    {
        get => _isBusy;
        set => SetProperty(ref _isBusy, value);
    }

    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(storage, value))
            return false;

        storage = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    public virtual void Dispose()
    {
        // Derived classes can override to detach event listeners or clean up timers.
    }
}
