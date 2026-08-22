using System.Windows.Input;
using SecurePassword.ViewModels.Base;

namespace SecurePassword.ViewModels.Generator;

/// <summary>
/// ViewModel for the Password Generator screen.
/// Orchestrates password generation, entropy/strength calculation,
/// secure clipboard export, and sensitive data lifetime management.
/// </summary>
public class GeneratorViewModel : BaseViewModel, ISensitiveViewModel
{
    public const string InitialMessage = "Нажмите кнопку для генерации";
    public const string ValidationMessage = "Должен быть выбран хотя бы один тип символов";

    private readonly ISecureClipboardService _secureClipboard;
    private readonly VaultSessionService? _vaultSession;

    private int _passwordLength = 12;
    private bool _includeDigits = true;
    private bool _includeLowercase = true;
    private bool _includeUppercase = true;
    private bool _includeSpecial;
    private string _generatedPassword = InitialMessage;
    private bool _copied;
    private CancellationTokenSource? _copyToastCts;

    public GeneratorViewModel(ISecureClipboardService secureClipboard, VaultSessionService? vaultSession = null)
    {
        _secureClipboard = secureClipboard ?? throw new ArgumentNullException(nameof(secureClipboard));
        _vaultSession = vaultSession;

        if (_vaultSession is not null)
        {
            _vaultSession.StateChanged += OnSessionStateChanged;
        }

        GenerateCommand = new RelayCommand(GeneratePassword);
        CopyCommand = new AsyncRelayCommand(CopyPasswordAsync);
        ResetPreviewCommand = new RelayCommand(ResetPasswordPreview);
    }

    public ICommand GenerateCommand { get; }
    public ICommand CopyCommand { get; }
    public ICommand ResetPreviewCommand { get; }

    public int PasswordLength
    {
        get => _passwordLength;
        set
        {
            int clamped = Math.Clamp(value, 4, 255);
            if (SetProperty(ref _passwordLength, clamped))
            {
                OnPropertyChanged(nameof(PasswordLengthDouble));
            }
        }
    }

    /// <summary>
    /// Double representation for MAUI Slider binding.
    /// </summary>
    public double PasswordLengthDouble
    {
        get => _passwordLength;
        set => PasswordLength = (int)Math.Round(value);
    }

    public bool IncludeDigits
    {
        get => _includeDigits;
        set
        {
            if (SetProperty(ref _includeDigits, value))
            {
                ResetPasswordPreview();
                OnPropertyChanged(nameof(ActiveOptionsCount));
            }
        }
    }

    public bool IncludeLowercase
    {
        get => _includeLowercase;
        set
        {
            if (SetProperty(ref _includeLowercase, value))
            {
                ResetPasswordPreview();
                OnPropertyChanged(nameof(ActiveOptionsCount));
            }
        }
    }

    public bool IncludeUppercase
    {
        get => _includeUppercase;
        set
        {
            if (SetProperty(ref _includeUppercase, value))
            {
                ResetPasswordPreview();
                OnPropertyChanged(nameof(ActiveOptionsCount));
            }
        }
    }

    public bool IncludeSpecial
    {
        get => _includeSpecial;
        set
        {
            if (SetProperty(ref _includeSpecial, value))
            {
                ResetPasswordPreview();
                OnPropertyChanged(nameof(ActiveOptionsCount));
            }
        }
    }

    public string GeneratedPassword
    {
        get => _generatedPassword;
        private set
        {
            if (SetProperty(ref _generatedPassword, value))
            {
                OnPropertyChanged(nameof(HasEvaluatedPassword));
                OnPropertyChanged(nameof(EntropyBits));
                OnPropertyChanged(nameof(EntropyText));
                OnPropertyChanged(nameof(StrengthPercentage));
                OnPropertyChanged(nameof(StrengthProgress));
                OnPropertyChanged(nameof(StrengthText));
                OnPropertyChanged(nameof(StrengthColorHex));
            }
        }
    }

    public bool Copied
    {
        get => _copied;
        set => SetProperty(ref _copied, value);
    }

    public bool HasEvaluatedPassword =>
        !string.IsNullOrWhiteSpace(GeneratedPassword) &&
        GeneratedPassword != InitialMessage &&
        GeneratedPassword != ValidationMessage;

    public int ActiveOptionsCount =>
        (IncludeDigits ? 1 : 0) +
        (IncludeLowercase ? 1 : 0) +
        (IncludeUppercase ? 1 : 0) +
        (IncludeSpecial ? 1 : 0);

    public double EntropyBits =>
        HasEvaluatedPassword ? PasswordValidator.CalculateEntropyBits(GeneratedPassword) : 0d;

    public string EntropyText =>
        HasEvaluatedPassword
            ? $"Энтропия: {EntropyBits:0.#} бит • Активных опций: {ActiveOptionsCount} из 4"
            : string.Empty;

    public int StrengthPercentage =>
        HasEvaluatedPassword ? PasswordValidator.CalculateStrengthPercentage(GeneratedPassword) : 0;

    public double StrengthProgress =>
        StrengthPercentage / 100.0;

    public string StrengthText
    {
        get
        {
            if (!HasEvaluatedPassword)
                return string.Empty;

            return PasswordValidator.CalculateCryptographicStrength(GeneratedPassword) switch
            {
                1 => "Слабый",
                2 => "Нормальный",
                3 => "Хороший",
                4 => "Отличный",
                _ => "Недостаточно данных"
            };
        }
    }

    public string StrengthColorHex
    {
        get
        {
            if (!HasEvaluatedPassword)
                return "#8A98A3";

            return PasswordValidator.CalculateCryptographicStrength(GeneratedPassword) switch
            {
                1 => "#E53935", // Weak Red
                2 => "#FB8C00", // Medium Orange
                3 => "#43A047", // Good Green
                4 => "#19A38C", // Excellent Emerald
                _ => "#8A98A3"
            };
        }
    }

    public void GeneratePassword()
    {
        _vaultSession?.RecordActivity();
        try
        {
            GeneratedPassword = PasswordGenerator.GeneratePassword(
                IncludeLowercase,
                IncludeUppercase,
                IncludeDigits,
                IncludeSpecial,
                (byte)PasswordLength);
        }
        catch (ArgumentException exception)
        {
            GeneratedPassword = exception.Message.Replace("\n", " ");
        }
    }

    public async Task CopyPasswordAsync()
    {
        _vaultSession?.RecordActivity();
        if (!HasEvaluatedPassword)
            return;

        await _secureClipboard.CopyToClipboardAsync(GeneratedPassword, isSensitive: true);

        _copyToastCts?.Cancel();
        _copyToastCts = new CancellationTokenSource();
        var token = _copyToastCts.Token;

        Copied = true;

        try
        {
            await Task.Delay(1500, token);
            Copied = false;
        }
        catch (TaskCanceledException)
        {
            // Reset occurred earlier or new copy started.
        }
    }

    public void ResetPasswordPreview()
    {
        GeneratedPassword = InitialMessage;
    }

    public void ClearSensitiveData()
    {
        _copyToastCts?.Cancel();
        _copyToastCts = null;
        Copied = false;
        GeneratedPassword = InitialMessage;
    }

    private void OnSessionStateChanged()
    {
        try
        {
            if (MainThread.IsMainThread)
            {
                HandleSessionStateChanged();
                return;
            }

            MainThread.BeginInvokeOnMainThread(HandleSessionStateChanged);
        }
        catch
        {
            // Fallback for non-UI test runners
            HandleSessionStateChanged();
        }
    }

    private void HandleSessionStateChanged()
    {
        if (_vaultSession is not null && !_vaultSession.IsAuthenticated)
        {
            ClearSensitiveData();
        }
    }

    public override void Dispose()
    {
        if (_vaultSession is not null)
        {
            _vaultSession.StateChanged -= OnSessionStateChanged;
        }

        _copyToastCts?.Cancel();
        _copyToastCts?.Dispose();
        _copyToastCts = null;

        base.Dispose();
    }
}
